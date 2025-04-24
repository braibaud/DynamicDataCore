using DynamicDataCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class CoreAdminConfigurationExtensions
    {
        [Obsolete("Use app.UseCoreAdminCustomAuth()")]
        public static void AddCoreAdmin(this IServiceCollection services, Func<Task<bool>> customAuthorisationMethod)
        {
            CoreAdminOptions coreAdminOptions = new CoreAdminOptions();

            FindDbContexts(services, coreAdminOptions);

            if (customAuthorisationMethod != null)
            {
                coreAdminOptions.CustomAuthorisationMethod = customAuthorisationMethod;
            }

            services.AddSingleton(coreAdminOptions);

            AddLocalisation(services);

            services.AddControllersWithViews();
        }

        private static void AddLocalisation(IServiceCollection services)
        {
            services.AddSingleton<IStringLocalizer<JsonLocalizer>, JsonLocalizer>();
            services.AddTransient<IHttpContextAccessor, HttpContextAccessor>();
        }

        public static void AddCoreAdmin(this IServiceCollection services, CoreAdminOptions options)
        {
            FindDbContexts(services, options);

            services.AddSingleton(options);

            services.AddControllersWithViews();

            AddLocalisation(services);
        }

        public static void AddCoreAdmin(this IServiceCollection services, params string[] restrictToRoles)
        {

            CoreAdminOptions coreAdminOptions = new CoreAdminOptions();

            FindDbContexts(services, coreAdminOptions);

            if (restrictToRoles != null && restrictToRoles.Any())
            {
                coreAdminOptions.RestrictToRoles = restrictToRoles;
            }
            
            services.AddSingleton(coreAdminOptions);

            AddLocalisation(services);

            services.AddControllersWithViews();

        }

        public static void UseCoreAdminCustomUrl(this IApplicationBuilder app, string customUrlPrefix)
        {
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "coreadminroute",
                    pattern: customUrlPrefix + "/{controller=CoreAdmin}/{action=Index}/{id?}");

            });
        }

        public static void UseCoreAdminCdn(this IApplicationBuilder app, string cdnPath)
        {
            IEnumerable<CoreAdminOptions> options = app.ApplicationServices.GetServices<CoreAdminOptions>();
            foreach(CoreAdminOptions option in options)
            {
                option.CdnPath = cdnPath;
            }
        }

        public static void UseCoreAdminCustomTitle(this IApplicationBuilder app, string customTitle)
        {
            IEnumerable<CoreAdminOptions> options = app.ApplicationServices.GetServices<CoreAdminOptions>();
            foreach (CoreAdminOptions option in options)
            {
                option.Title = customTitle;
            }
        }

        public static void UseCoreAdminCustomAuth(this IApplicationBuilder app, Func<IServiceProvider, Task<bool>> customAuthFunction)
        {
            IEnumerable<CoreAdminOptions> options = app.ApplicationServices.GetServices<CoreAdminOptions>();
            foreach (CoreAdminOptions option in options)
            {
                option.CustomAuthorisationMethodWithServiceProvider = customAuthFunction;
            }
        }

        private static void FindDbContexts(IServiceCollection services, CoreAdminOptions options)
        {
            // remove previously discovered db contexts
            List<ServiceDescriptor> servicesToRemove = services.Where(s => s.ServiceType == typeof(DiscoveredDbSetEntityType)).ToList();
            foreach(ServiceDescriptor serviceToRemove in servicesToRemove)
            {
                services.Remove(serviceToRemove);
            }

            List<DiscoveredDbSetEntityType> discoveredServices = new List<DiscoveredDbSetEntityType>();
            foreach (ServiceDescriptor service in services.ToList())
            {
                if (service.ImplementationType == null)
                    continue;
                if (service.ImplementationType.IsSubclassOf(typeof(DbContext)) && 
                    !discoveredServices.Any(x => x.DbContextType == service.ImplementationType))
                {
                    foreach (System.Reflection.PropertyInfo dbSetProperty in service.ImplementationType.GetProperties())
                    {
                        // looking for DbSet<Entity>
                        if (dbSetProperty.PropertyType.IsGenericType && dbSetProperty.PropertyType.Name.StartsWith("DbSet"))
                        {
                            if (!options.IgnoreEntityTypes.Contains(dbSetProperty.PropertyType.GenericTypeArguments.First()))
                            {
                                discoveredServices.Add(new DiscoveredDbSetEntityType() { 
                                    DbContextType = service.ImplementationType, 
                                    DbSetType = dbSetProperty.PropertyType, 
                                    UnderlyingType = dbSetProperty.PropertyType.GenericTypeArguments.First(), Name = dbSetProperty.Name });
                            }
                        }
                    }

                    
                }
            }

            foreach (DiscoveredDbSetEntityType service in discoveredServices)
            {
                services.AddTransient(_ => service);
            }
        }
    }
}
