using DynamicDataCore.ViewModels;
using DynamicDataCore.Utilities;
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
using Microsoft.EntityFrameworkCore.Metadata;

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
            DataListViewModel viewModel = new DataListViewModel();

            if (id == null)
            {
                // Get the first DbSetEntityType
                DiscoveredDbSetEntityType firstDbSetEntity = _dbSetEntities.FirstOrDefault();

                if (firstDbSetEntity != null)
                {
                    id = firstDbSetEntity.Name;
                }
            }

            foreach (DiscoveredDbSetEntityType dbSetEntity in _dbSetEntities.Where(db => string.Compare(db.Name, id, StringComparison.InvariantCultureIgnoreCase) == 0))
            {
                foreach (PropertyInfo dbSetProperty in dbSetEntity.DbContextType.GetProperties())
                {
                    if (dbSetProperty.PropertyType.IsGenericType && dbSetProperty.PropertyType.Name.StartsWith("DbSet") && dbSetProperty.Name.ToLowerInvariant() == id.ToLowerInvariant())
                    {
                        viewModel.EntityType = dbSetProperty.PropertyType.GetGenericArguments().First();
                        viewModel.DbSetProperty = dbSetProperty;

                        DbContext dbContextObject = (DbContext)this.HttpContext.RequestServices.GetRequiredService(dbSetEntity.DbContextType);
                        IQueryable<object> query = dbContextObject.Set(viewModel.EntityType);

                        object dbSetValue = dbSetProperty.GetValue(dbContextObject);

                        IEnumerable<INavigation> navProperties = dbContextObject.Model.FindEntityType(viewModel.EntityType).GetNavigations();
                        foreach (INavigation property in navProperties)
                        {
                            // Only display One to One relationships on the Grid
                            if (property.GetCollectionAccessor() == null)
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
            DiscoveredDbSetEntityType dbSetEntity = _dbSetEntities
                .FirstOrDefault(db => TypeConverterUtility.EqualsIgnoringCase(db.Name, dbSetName));

            if (dbSetEntity != null)
            {
                PropertyInfo dbSetProperty = dbSetEntity
                    .DbContextType
                    .GetProperties()
                    .FirstOrDefault(p => p.PropertyType == dbSetEntity.DbSetType);
                
                if (dbSetProperty != null)
                {
                    // Get the database context for the DbSet
                    dbContextObject = (DbContext)HttpContext.RequestServices.GetRequiredService(dbSetEntity.DbContextType);

                    // Get the type of the entity for the DbSet
                    typeOfEntity = dbSetProperty.PropertyType.GetGenericArguments().First();

                    // Get the entity type from the DbContext
                    IEntityType entityType = dbContextObject.Model.FindEntityType(typeOfEntity);

                    // Get the foreign keys for the entity type
                    IEnumerable<IForeignKey> foreignKeys = entityType.GetForeignKeys();

                    Dictionary<string, Dictionary<object, string>> relationshipDictionary = new Dictionary<string, Dictionary<object, string>>();

                    foreach (IForeignKey fk in foreignKeys)
                    {
                        Dictionary<object, string> childValues = new Dictionary<object, string>();

                        IEntityType principalEntityType = fk.PrincipalEntityType;

                        PropertyInfo principalDbSet = dbContextObject
                            .GetType()
                            .GetProperties()
                            .FirstOrDefault(
                                p => 
                                {
                                    var __GetGenericTypeDefinition = p.PropertyType.GetGenericTypeDefinition();
                                    var __GetGenericArguments = p.PropertyType.GetGenericArguments().FirstOrDefault();

                                    bool __b1 = (p.PropertyType.IsGenericType);
                                    bool __b2 = (__GetGenericTypeDefinition == typeof(DbSet<>));
                                    bool __b3 = (__GetGenericArguments == principalEntityType.ClrType);

                                    return p.PropertyType.IsGenericType &&
                                           p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>) &&
                                           p.PropertyType.GetGenericArguments().First() == principalEntityType.ClrType;
                                });

                        if (principalDbSet != null)
                        {
                            IKey primaryKey = principalEntityType.FindPrimaryKey();
                            IEnumerable<object> allChildren = (IEnumerable<object>)principalDbSet.GetValue(dbContextObject);

                            foreach (object child in allChildren)
                            {
                                object childPkValue = primaryKey.Properties.First().PropertyInfo.GetValue(child);
                                childValues.Add(childPkValue, child.ToString());
                            }
                        }

                        relationshipDictionary.Add(fk.Properties.First().Name, childValues);
                    }

                    relationships = relationshipDictionary;

                    return dbSetProperty.GetValue(dbContextObject);
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
            object dbSetValue = GetDbSetValueOrNull(dbSetName, out dbContextObject, out typeOfEntity, out relationships);

            if (dbSetValue == null || dbContextObject == null || typeOfEntity == null)
            {
                dbContextObject = null;
                typeOfEntity = null;
                relationships = null;
                return null;
            }

            IKey primaryKey = dbContextObject.Model.FindEntityType(typeOfEntity).FindPrimaryKey();
            if (primaryKey == null)
            {
                return null;
            }

            // Build the composite key values
            object[] keyValues = primaryKey
                .Properties
                .Select(
                    pk =>
                    {
                        string keyName = pk.Name;

                        if (!primaryKeys.TryGetValue(keyName, out string keyValue))
                        {
                            return null; // Missing key value
                        }

                        // Convert the key value to the appropriate CLR type
                        return TypeConverterUtility.ConvertToType(keyValue, pk.ClrType);
                    })
                .ToArray();

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
        public async Task<IActionResult> CreateEntityPost(string dbSetName, [FromForm] object formData)
        {
            object dbSetValue = GetDbSetValueOrNull(dbSetName, out DbContext dbContextObject, out Type entityType, out Dictionary<string, Dictionary<object, string>> relationships);

            object newEntity = Activator.CreateInstance(entityType);

            IEnumerable<string> databaseGeneratedProperties = newEntity.GetType()
                .GetProperties()
                .Where(p => p.GetCustomAttributes().Any(a => a.GetType().Name.Contains("DatabaseGenerated")))
                .Select(p => p.Name);

            await AddByteArrayFiles(newEntity);

            await TryUpdateModelAsync(newEntity, entityType, string.Empty,
                await CompositeValueProvider.CreateAsync(this.ControllerContext, this.ControllerContext.ValueProviderFactories),
                (ModelMetadata meta) => !databaseGeneratedProperties.Contains(meta.PropertyName));

            // Remove any errors from foreign key properties - EF will handle this validation
            foreach (string fkProperty in newEntity.GetType().GetProperties()
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
            ViewBag.GeneratedProperties = databaseGeneratedProperties;
            ViewBag.Relationships = relationships;

            return View("Create", newEntity);
        }

        [HttpGet]
        public IActionResult CreateEntity(string dbSetName, [FromQuery] IDictionary<string, string> primaryKeys)
        {
            return RenderView(dbSetName, "Create", primaryKeys);
        }

        [HttpGet]
        public IActionResult EditEntity(string dbSetName, [FromQuery] IDictionary<string, string> primaryKeys)
        {
            return RenderView(dbSetName, "Edit", primaryKeys);
        }

        /// <summary>
        /// Prepares a view for a specific entity by resolving the entity from the database context
        /// using the provided DbSet name, view name, and primary key values.
        /// </summary>
        /// <param name="dbSetName">The name of the DbSet containing the entity.</param>
        /// <param name="viewName">The name of the view to render.</param>
        /// <param name="primaryKeys">
        /// A dictionary containing the primary key names and their corresponding values as strings.
        /// </param>
        /// <returns>
        /// An <see cref="IActionResult"/> that represents the result of the operation:
        /// <list type="bullet">
        /// <item><description><see cref="NotFound"/> if the DbSet, entity type, or entity cannot be found.</description></item>
        /// <item><description><see cref="BadRequest"/> if any primary key values are missing or invalid.</description></item>
        /// <item><description>A rendered view with the entity if successful.</description></item>
        /// </list>
        /// </returns>
        private IActionResult RenderView(string dbSetName, string viewName, IDictionary<string, string> primaryKeys)
        {
            object dbSetValue = GetDbSetValueOrNull(
                dbSetName,
                out DbContext dbContextObject,
                out Type entityType,
                out Dictionary<string, Dictionary<object, string>> relationships);

            if (dbSetValue == null || dbContextObject == null || entityType == null)
            {
                return NotFound();
            }

            IKey primaryKey = dbContextObject
                .Model
                .FindEntityType(entityType)
                .FindPrimaryKey();

            if (primaryKey == null)
            {
                return NotFound();
            }

            // Build the composite key values
            object[] keyValues = primaryKey
                .Properties
                .Select(
                    pk =>
                    {
                        string keyName = pk.Name;

                        if (!primaryKeys.TryGetValue(keyName, out string keyValue))
                        {
                            return null; // Missing key value
                        }

                        // Convert the key value to the appropriate CLR type
                        return TypeConverterUtility.ConvertToType(keyValue, pk.ClrType);
                    })
                .ToArray();

            if (keyValues.Contains(null))
            {
                return BadRequest("Missing or invalid primary key values.");
            }

            // Find the entity using the composite key
            object entity = dbSetValue
                .GetType()
                .InvokeMember(
                    "Find",
                    BindingFlags.InvokeMethod,
                    null,
                    dbSetValue,
                    args: keyValues);

            if (entity == null)
            {
                return NotFound();
            }

            // Get database-generated properties
            IEnumerable<string> databaseGeneratedProperties = entity
                .GetType()
                .GetProperties()
                .Where(p => p.GetCustomAttributes().Any(a => a.GetType().Name.Contains("DatabaseGenerated")))
                .Select(p => p.Name);

            ViewBag.DbSetName = dbSetName;
            ViewBag.PrimaryKeys = primaryKeys;
            ViewBag.Relationships = relationships;
            ViewBag.GeneratedProperties = databaseGeneratedProperties;

            return View(viewName, entity);
        }

        [HttpPost]
        public async Task<IActionResult> EditEntityPost([FromForm] DataEditViewModel viewModel)
        {
            object dbSetValue = GetDbSetValueOrNull(viewModel.DbSetName, out DbContext dbContextObject, out Type entityType, out Dictionary<string, Dictionary<object, string>> relationships);

            if (dbSetValue == null || dbContextObject == null || entityType == null)
            {
                return NotFound();
            }

            // Build the composite key values
            IKey primaryKey = dbContextObject.Model.FindEntityType(entityType).FindPrimaryKey();
            object[] keyValues = primaryKey.Properties.Select(pk =>
            {
                KeyValuePair<string, object> keyValuePair = viewModel.PrimaryKeys.FirstOrDefault(kvp => kvp.Key == pk.Name);
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
            object entityToEdit = dbSetValue.GetType().InvokeMember(
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
            foreach (KeyValuePair<string, string> formField in viewModel.FormData)
            {
                PropertyInfo property = entityType.GetProperty(formField.Key);
                if (property != null)
                {
                    object convertedValue = Convert.ChangeType(formField.Value, property.PropertyType);
                    property.SetValue(entityToEdit, convertedValue);
                }
            }

            IEnumerable<string> databaseGeneratedProperties = entityToEdit.GetType().GetProperties()
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
            ViewBag.GeneratedProperties = databaseGeneratedProperties;

            return View("Edit", entityToEdit);
        }

        [HttpGet]
        public IActionResult ViewEntity(string dbSetName, [FromQuery] IDictionary<string, string> primaryKeys)
        {
            return RenderView(dbSetName, "View", primaryKeys);
        }

        private async Task AddByteArrayFiles(object entityToEdit)
        {
            foreach (Microsoft.AspNetCore.Http.IFormFile file in Request.Form.Files)
            {
                PropertyInfo matchingProperty = entityToEdit.GetType().GetProperties()
                    .FirstOrDefault(prop => prop.Name == file.Name && prop.PropertyType == typeof(byte[]));
                if (matchingProperty != null)
                {
                    MemoryStream memoryStream = new MemoryStream();
                    await file.CopyToAsync(memoryStream);
                    matchingProperty.SetValue(entityToEdit, memoryStream.ToArray());
                }
            }
        }

        [HttpGet]
        public IActionResult DeleteEntity(string dbSetName, [FromQuery] IDictionary<string, string> primaryKeys)
        {
            IEntityType entityType = _dbContext.Model.FindEntityType(dbSetName);
            if (entityType == null)
            {
                return NotFound();
            }

            IKey primaryKey = entityType.FindPrimaryKey();
            if (primaryKey == null)
            {
                return NotFound();
            }

            // Build the composite key values
            object[] keyValues = primaryKey.Properties.Select(pk =>
            {
                string keyName = pk.Name;
                if (!primaryKeys.TryGetValue(keyName, out string keyValue))
                {
                    return null; // Missing key value
                }

                // Convert the key value to the appropriate CLR type
                return TypeConverterUtility.ConvertToType(keyValue, pk.ClrType);
            }).ToArray();

            if (keyValues.Contains(null))
            {
                return BadRequest("Missing or invalid primary key values.");
            }

            // Find the entity using the composite key
            object entity = _dbContext.Find(entityType.ClrType, keyValues);
            if (entity == null)
            {
                return NotFound();
            }

            // Prepare the view model
            List<KeyValuePair<string, object>> primaryKeysList = primaryKey.Properties
                .Select(pk => new KeyValuePair<string, object>(pk.Name, pk.PropertyInfo.GetValue(entity)))
                .ToList();

            DataDeleteViewModel model = new DataDeleteViewModel
            {
                DbSetName = dbSetName,
                Object = entity,
                PrimaryKeys = primaryKeysList
            };

            return View(model);
        }

        [HttpPost]
        [ActionName("Delete")]
        public async Task<IActionResult> DeleteEntityPost([FromForm] DataDeleteViewModel viewModel)
        {
            foreach (DiscoveredDbSetEntityType dbSetEntity in _dbSetEntities.Where(db => db.Name.ToLowerInvariant() == viewModel.DbSetName.ToLowerInvariant()))
            {
                foreach (PropertyInfo dbSetProperty in dbSetEntity.DbContextType.GetProperties())
                {
                    if (dbSetProperty.PropertyType.IsGenericType && dbSetProperty.PropertyType.Name.StartsWith("DbSet") && dbSetProperty.Name.ToLowerInvariant() == viewModel.DbSetName.ToLowerInvariant())
                    {
                        DbContext dbContextObject = (DbContext)this.HttpContext.RequestServices.GetRequiredService(dbSetEntity.DbContextType);
                        object dbSetValue = dbSetProperty.GetValue(dbContextObject);

                        Type entityType = dbSetProperty.PropertyType.GetGenericArguments()[0];
                        IKey primaryKey = dbContextObject.Model.FindEntityType(entityType).FindPrimaryKey();

                        // Build the composite key values
                        object[] keyValues = primaryKey
                            .Properties
                            .Select(
                                pk =>
                                {
                                    string keyName = pk.Name;

                                    object keyValue = viewModel
                                        .PrimaryKeys
                                        .FirstOrDefault(k => k.Key == keyName)
                                        .Value;

                                    // Convert the key value to the appropriate CLR type
                                    return TypeConverterUtility.ConvertObjectToType(keyValue, pk.ClrType);
                                })
                            .ToArray();

                        // Find the entity using the composite key
                        object entityToDelete = dbSetValue.GetType().InvokeMember(
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
