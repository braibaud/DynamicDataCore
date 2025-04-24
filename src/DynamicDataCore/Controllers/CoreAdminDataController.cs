using DynamicDataCore.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace DynamicDataCore.Controllers
{
    [CoreAdminAuth]
    public class CoreAdminDataController : Controller
    {
        private readonly IEnumerable<DiscoveredDbSetEntityType> _dbSetEntities;
        private readonly DbContext _dbContext;

        public CoreAdminDataController(IEnumerable<DiscoveredDbSetEntityType> dbSetEntities, DbContext dbContext)
        {
            this._dbSetEntities = dbSetEntities;
            this._dbContext = dbContext;
        }

        [HttpGet]
        public IActionResult Index(string id)
        {
            var viewModel = new DataListViewModel();

            if (id == null)
            {
                // Get the first DbSetEntityType
                var firstDbSetEntity = _dbSetEntities.FirstOrDefault();

                if (firstDbSetEntity != null)
                {
                    id = firstDbSetEntity.Name;
                }
            }

            foreach (var dbSetEntity in _dbSetEntities.Where(db => string.Compare(db.Name, id, StringComparison.InvariantCultureIgnoreCase) == 0))
            {
                foreach (var dbSetProperty in dbSetEntity.DbContextType.GetProperties())
                {
                    if (dbSetProperty.PropertyType.IsGenericType && dbSetProperty.PropertyType.Name.StartsWith("DbSet") && dbSetProperty.Name.ToLowerInvariant() == id.ToLowerInvariant())
                    {
                        viewModel.EntityType = dbSetProperty.PropertyType.GetGenericArguments().First();
                        viewModel.DbSetProperty = dbSetProperty;

                        var dbContextObject = (DbContext)this.HttpContext.RequestServices.GetRequiredService(dbSetEntity.DbContextType);
                        var query = dbContextObject.Set(viewModel.EntityType);

                        var dbSetValue = dbSetProperty.GetValue(dbContextObject);

                        var navProperties = dbContextObject.Model.FindEntityType(viewModel.EntityType).GetNavigations();
                        foreach (var property in navProperties)
                        {
                            // Only display One to One relationships on the Grid
                            if(property.GetCollectionAccessor() == null)    
                                query = query.Include(property.Name);
                        }

                        viewModel.Data = query.ToArray();
                        viewModel.DbContext = dbContextObject;
                    }
                }
            }

            if (viewModel.DbContext == null)
            {
                return NotFound();
            }

            return View(viewModel);
        }

        private object GetDbSetValueOrNull(
            string dbSetName, 
            out DbContext dbContextObject,
            out Type typeOfEntity,
            out Dictionary<string, Dictionary<object, string>> relationships)
        {
            foreach (var dbSetEntity in _dbSetEntities.Where(db => db.Name.ToLowerInvariant() == dbSetName.ToLowerInvariant()))
            {
                foreach (var dbSetProperty in dbSetEntity.DbContextType.GetProperties())
                {
                    if (dbSetProperty.PropertyType.IsGenericType && dbSetProperty.PropertyType.Name.StartsWith("DbSet") && dbSetProperty.Name.ToLowerInvariant() == dbSetName.ToLowerInvariant())
                    {
                        dbContextObject = (DbContext)this.HttpContext.RequestServices.GetRequiredService(dbSetEntity.DbContextType);
                        typeOfEntity = dbSetProperty.PropertyType.GetGenericArguments()[0];

                        var entityType = dbContextObject.Model.FindEntityType(typeOfEntity);
                        var foreignKeys = entityType.GetForeignKeys();

                        var relationshipDictionary = new Dictionary<string, Dictionary<object, string>>();
                        foreach (var fk in foreignKeys)
                        {
                            var childValues = new Dictionary<object, string>();

                            var principalEntityType = fk.PrincipalEntityType;
                            var principalDbSet = dbContextObject.GetType().GetProperties()
                                .FirstOrDefault(p => p.PropertyType.IsGenericType &&
                                                    p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>) &&
                                                    p.PropertyType.GetGenericArguments().First() == principalEntityType.ClrType);

                            if (principalDbSet != null)
                            {
                                var primaryKey = principalEntityType.FindPrimaryKey();
                                var allChildren = (IEnumerable<object>)principalDbSet.GetValue(dbContextObject);

                                foreach (var child in allChildren)
                                {
                                    var childPkValue = primaryKey.Properties.First().PropertyInfo.GetValue(child);
                                    childValues.Add(childPkValue, child.ToString());
                                }
                            }

                            relationshipDictionary.Add(fk.Properties.First().Name, childValues);
                        }

                        relationships = relationshipDictionary;

                        return dbSetProperty.GetValue(dbContextObject);
                    }
                }
            }

            dbContextObject = null;
            typeOfEntity = null;
            relationships = null;
            return null;
        }

        private object GetEntityFromDbSet(
            string dbSetName,
            IDictionary<string, string> primaryKeys,
            out DbContext dbContextObject,
            out Type typeOfEntity,
            out Dictionary<string, Dictionary<object, string>> relationships)
        {
            var dbSetValue = GetDbSetValueOrNull(dbSetName, out dbContextObject, out typeOfEntity, out relationships);

            if (dbSetValue == null || dbContextObject == null || typeOfEntity == null)
            {
                dbContextObject = null;
                typeOfEntity = null;
                relationships = null;
                return null;
            }

            var primaryKey = dbContextObject.Model.FindEntityType(typeOfEntity).FindPrimaryKey();
            if (primaryKey == null)
            {
                return null;
            }

            // Build the composite key values
            var keyValues = primaryKey.Properties.Select(pk =>
            {
                var keyName = pk.Name;
                if (!primaryKeys.TryGetValue(keyName, out var keyValue))
                {
                    return null; // Missing key value
                }

                // Convert the key value to the appropriate CLR type
                return Convert.ChangeType(keyValue, pk.ClrType);
            }).ToArray();

            if (keyValues.Contains(null))
            {
                return null; // Missing or invalid key values
            }

            // Find the entity using the composite key
            return dbSetValue.GetType().InvokeMember(
                "Find",
                BindingFlags.InvokeMethod,
                null,
                dbSetValue,
                args: keyValues
            );
        }

        [HttpPost]
        //[IgnoreAntiforgeryToken]
        [ActionName("Create")]
        public async Task<IActionResult> CreateEntityPost(string dbSetName, [FromForm] object formData)
        {
            var dbSetValue = GetDbSetValueOrNull(dbSetName, out var dbContextObject, out var entityType, out var relationships);

            var newEntity = Activator.CreateInstance(entityType);

            var databaseGeneratedProperties = newEntity.GetType()
                .GetProperties()
                .Where(p => p.GetCustomAttributes().Any(a => a.GetType().Name.Contains("DatabaseGenerated")))
                .Select(p => p.Name);

            await AddByteArrayFiles(newEntity);

            await TryUpdateModelAsync(newEntity, entityType, string.Empty,
                await CompositeValueProvider.CreateAsync(this.ControllerContext, this.ControllerContext.ValueProviderFactories),
                (ModelMetadata meta) => !databaseGeneratedProperties.Contains(meta.PropertyName));

            // Remove any errors from foreign key properties - EF will handle this validation
            foreach (var fkProperty in newEntity.GetType().GetProperties()
                .Where(p => p.GetCustomAttributes().Any(a => a.GetType().Name.Contains("ForeignKey"))).Select(p => p.Name))
            {
                if (ModelState.ContainsKey(fkProperty))
                {
                    ModelState[fkProperty].Errors.Clear();
                    ModelState[fkProperty].ValidationState = ModelValidationState.Skipped;
                }
            }

            if (ModelState.ValidationState == ModelValidationState.Valid)
            {
                dbContextObject.Add(newEntity);
                await dbContextObject.SaveChangesAsync();
                return RedirectToAction("Index", new { id = dbSetName });
            }

            ViewBag.DbSetName = dbSetName;
            ViewBag.IgnoreFromForm = databaseGeneratedProperties;
            ViewBag.Relationships = relationships;

            return View("Create", newEntity);
        }

        [HttpGet]
        //[IgnoreAntiforgeryToken]
        public IActionResult CreateEntity(string dbSetName, [FromQuery] IDictionary<string, string> primaryKeys)
        {
            var dbSetValue = GetDbSetValueOrNull(dbSetName, out var dbContextObject, out var entityType, out var relationships);

            if (dbSetValue == null || dbContextObject == null || entityType == null)
            {
                return NotFound();
            }

            var newEntity = Activator.CreateInstance(entityType);

            // Pre-populate primary key values if provided
            foreach (var pk in primaryKeys)
            {
                var property = entityType.GetProperty(pk.Key);
                if (property != null)
                {
                    var convertedValue = Convert.ChangeType(pk.Value, property.PropertyType);
                    property.SetValue(newEntity, convertedValue);
                }
            }

            var autoGeneratedPropertyNames = newEntity.GetType()
                .GetProperties()
                .Where(p => p.GetCustomAttributes().Any(a => a.GetType().Name.Contains("DatabaseGenerated")))
                .Select(p => p.Name);

            ViewBag.DbSetName = dbSetName;
            ViewBag.IgnoreFromForm = autoGeneratedPropertyNames;
            ViewBag.Relationships = relationships;

            return View(newEntity);
        }

        [HttpGet]
        //[IgnoreAntiforgeryToken]
        public IActionResult EditEntity(string dbSetName, [FromQuery] IDictionary<string, string> primaryKeys)
        {
            var dbSetValue = GetDbSetValueOrNull(dbSetName, out var dbContextObject, out var entityType, out var relationships);

            if (dbSetValue == null || dbContextObject == null || entityType == null)
            {
                return NotFound();
            }

            var primaryKey = dbContextObject.Model.FindEntityType(entityType).FindPrimaryKey();
            if (primaryKey == null)
            {
                return NotFound();
            }

            // Build the composite key values
            var keyValues = primaryKey.Properties.Select(pk =>
            {
                var keyName = pk.Name;
                if (!primaryKeys.TryGetValue(keyName, out var keyValue))
                {
                    return null; // Missing key value
                }

                // Convert the key value to the appropriate CLR type
                return Convert.ChangeType(keyValue, pk.ClrType);
            }).ToArray();

            if (keyValues.Contains(null))
            {
                return BadRequest("Missing or invalid primary key values.");
            }

            // Find the entity using the composite key
            var entityToEdit = dbSetValue.GetType().InvokeMember(
                "Find",
                BindingFlags.InvokeMethod,
                null,
                dbSetValue,
                args: keyValues
            );

            if (entityToEdit == null)
            {
                return NotFound();
            }

            // Get database-generated properties
            var databaseGeneratedProperties = entityToEdit.GetType()
                .GetProperties()
                .Where(p => p.GetCustomAttributes().Any(a => a.GetType().Name.Contains("DatabaseGenerated")))
                .Select(p => p.Name);

            ViewBag.DbSetName = dbSetName;
            ViewBag.PrimaryKeys = primaryKeys;
            ViewBag.Relationships = relationships;
            ViewBag.IgnoreFromForm = databaseGeneratedProperties;

            return View("Edit", entityToEdit);
        }

        [HttpPost]
        [ActionName("Edit")]
        public async Task<IActionResult> EditEntityPost([FromForm] DataEditViewModel viewModel)
        {
            var dbSetValue = GetDbSetValueOrNull(viewModel.DbSetName, out var dbContextObject, out var entityType, out var relationships);

            if (dbSetValue == null || dbContextObject == null || entityType == null)
            {
                return NotFound();
            }

            // Build the composite key values
            var primaryKey = dbContextObject.Model.FindEntityType(entityType).FindPrimaryKey();
            var keyValues = primaryKey.Properties.Select(pk =>
            {
                var keyValuePair = viewModel.PrimaryKeys.FirstOrDefault(kvp => kvp.Key == pk.Name);
                if (keyValuePair.Equals(default(KeyValuePair<string, object>)))
                {
                    return null; // Missing key value
                }

                // Convert the key value to the appropriate CLR type
                return Convert.ChangeType(keyValuePair.Value, pk.ClrType);
            }).ToArray();

            if (keyValues.Contains(null))
            {
                return BadRequest("Missing or invalid primary key values.");
            }

            // Find the entity using the composite key
            var entityToEdit = dbSetValue.GetType().InvokeMember(
                "Find",
                BindingFlags.InvokeMethod,
                null,
                dbSetValue,
                args: keyValues
            );

            if (entityToEdit == null)
            {
                return NotFound();
            }

            dbContextObject.Attach(entityToEdit);

            // Update the entity with form data
            foreach (var formField in viewModel.FormData)
            {
                var property = entityType.GetProperty(formField.Key);
                if (property != null)
                {
                    var convertedValue = Convert.ChangeType(formField.Value, property.PropertyType);
                    property.SetValue(entityToEdit, convertedValue);
                }
            }

            var databaseGeneratedProperties = entityToEdit.GetType().GetProperties()
                .Where(p => p.GetCustomAttributes().Any(a => a.GetType().Name.Contains("DatabaseGenerated")))
                .Select(p => p.Name);

            await TryUpdateModelAsync(entityToEdit, entityType, string.Empty,
                await CompositeValueProvider.CreateAsync(this.ControllerContext, this.ControllerContext.ValueProviderFactories),
                (ModelMetadata meta) => !databaseGeneratedProperties.Contains(meta.PropertyName));

            if (ModelState.ValidationState == ModelValidationState.Valid)
            {
                await dbContextObject.SaveChangesAsync();
                return RedirectToAction("Index", new { id = viewModel.DbSetName });
            }

            ViewBag.DbSetName = viewModel.DbSetName;
            ViewBag.Relationships = relationships;
            ViewBag.IgnoreFromForm = databaseGeneratedProperties;

            return View("Edit", entityToEdit);
        }

        [HttpGet]
        public IActionResult ViewEntity(string dbSetName, [FromQuery] IDictionary<string, string> primaryKeys)
        {
            var dbSetValue = GetDbSetValueOrNull(dbSetName, out var dbContextObject, out var entityType, out _);

            var primaryKey = dbContextObject.Model.FindEntityType(entityType).FindPrimaryKey();

            // Build the composite key values
            var keyValues = primaryKey.Properties.Select(pk =>
            {
                var keyName = pk.Name;
                var keyValue = primaryKeys.ContainsKey(keyName) ? primaryKeys[keyName] : null;

                // Convert the key value to the appropriate CLR type
                return Convert.ChangeType(keyValue, pk.ClrType);
            }).ToArray();

            // Find the entity using the composite key
            var entityToView = dbSetValue.GetType().InvokeMember(
                "Find",
                BindingFlags.InvokeMethod,
                null,
                dbSetValue,
                args: keyValues
            );

            if (entityToView == null)
            {
                return NotFound();
            }

            ViewBag.DbSetName = dbSetName;
            return View("View", entityToView);
        }

        private async Task AddByteArrayFiles(object entityToEdit)
        {
            foreach (var file in Request.Form.Files)
            {
                var matchingProperty = entityToEdit.GetType().GetProperties()
                    .FirstOrDefault(prop => prop.Name == file.Name && prop.PropertyType == typeof(byte[]));
                if (matchingProperty != null)
                {
                    var memoryStream = new MemoryStream();
                    await file.CopyToAsync(memoryStream);
                    matchingProperty.SetValue(entityToEdit, memoryStream.ToArray());
                }
            }
        }

        [HttpGet]
        public IActionResult DeleteEntity(string dbSetName, [FromQuery] IDictionary<string, string> primaryKeys)
        {
            var entityType = _dbContext.Model.FindEntityType(dbSetName);
            if (entityType == null)
            {
                return NotFound();
            }

            var primaryKey = entityType.FindPrimaryKey();
            if (primaryKey == null)
            {
                return NotFound();
            }

            // Build the composite key values
            var keyValues = primaryKey.Properties.Select(pk =>
            {
                var keyName = pk.Name;
                if (!primaryKeys.TryGetValue(keyName, out var keyValue))
                {
                    return null; // Missing key value
                }

                // Convert the key value to the appropriate CLR type
                return Convert.ChangeType(keyValue, pk.ClrType);
            }).ToArray();

            if (keyValues.Contains(null))
            {
                return BadRequest("Missing or invalid primary key values.");
            }

            // Find the entity using the composite key
            var entity = _dbContext.Find(entityType.ClrType, keyValues);
            if (entity == null)
            {
                return NotFound();
            }

            // Prepare the view model
            var primaryKeysList = primaryKey.Properties
                .Select(pk => new KeyValuePair<string, object>(pk.Name, pk.PropertyInfo.GetValue(entity)))
                .ToList();

            var model = new DataDeleteViewModel
            {
                DbSetName = dbSetName,
                Object = entity,
                PrimaryKeys = primaryKeysList
            };

            return View(model);
        }

        [HttpPost]
        //[IgnoreAntiforgeryToken]
        [ActionName("Delete")]
        public async Task<IActionResult> DeleteEntityPost([FromForm] DataDeleteViewModel viewModel)
        {
            foreach (var dbSetEntity in _dbSetEntities.Where(db => db.Name.ToLowerInvariant() == viewModel.DbSetName.ToLowerInvariant()))
            {
                foreach (var dbSetProperty in dbSetEntity.DbContextType.GetProperties())
                {
                    if (dbSetProperty.PropertyType.IsGenericType && dbSetProperty.PropertyType.Name.StartsWith("DbSet") && dbSetProperty.Name.ToLowerInvariant() == viewModel.DbSetName.ToLowerInvariant())
                    {
                        var dbContextObject = (DbContext)this.HttpContext.RequestServices.GetRequiredService(dbSetEntity.DbContextType);
                        var dbSetValue = dbSetProperty.GetValue(dbContextObject);

                        var entityType = dbSetProperty.PropertyType.GetGenericArguments()[0];
                        var primaryKey = dbContextObject.Model.FindEntityType(entityType).FindPrimaryKey();

                        // Build the composite key values
                        var keyValues = primaryKey.Properties.Select(pk =>
                        {
                            var keyName = pk.Name;
                            var keyValue = viewModel.PrimaryKeys.FirstOrDefault(k => k.Key == keyName).Value;

                            // Convert the key value to the appropriate CLR type
                            return Convert.ChangeType(keyValue, pk.ClrType);
                        }).ToArray();

                        // Find the entity using the composite key
                        var entityToDelete = dbSetValue.GetType().InvokeMember(
                            "Find",
                            BindingFlags.InvokeMethod,
                            null,
                            dbSetValue,
                            args: keyValues
                        );

                        if (entityToDelete != null)
                        {
                            // Remove the entity
                            dbSetValue.GetType().InvokeMember(
                                "Remove",
                                BindingFlags.InvokeMethod,
                                null,
                                dbSetValue,
                                args: new object[] { entityToDelete }
                            );

                            await dbContextObject.SaveChangesAsync();
                        }
                    }
                }
            }

            return RedirectToAction("Index", new { Id = viewModel.DbSetName });
        }
    }
}
