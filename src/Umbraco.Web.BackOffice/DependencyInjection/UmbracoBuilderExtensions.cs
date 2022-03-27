using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Implement;
using Umbraco.Cms.Infrastructure.DependencyInjection;
using Umbraco.Cms.Infrastructure.Examine.DependencyInjection;
using Umbraco.Cms.Infrastructure.WebAssets;
using Umbraco.Cms.Web.BackOffice.Controllers;
using Umbraco.Cms.Web.BackOffice.Filters;
using Umbraco.Cms.Web.BackOffice.Install;
using Umbraco.Cms.Web.BackOffice.Middleware;
using Umbraco.Cms.Web.BackOffice.ModelsBuilder;
using Umbraco.Cms.Web.BackOffice.Routing;
using Umbraco.Cms.Web.BackOffice.Security;
using Umbraco.Cms.Web.BackOffice.Services;
using Umbraco.Cms.Web.BackOffice.SignalR;
using Umbraco.Cms.Web.BackOffice.Trees;
using IHostingEnvironment = Umbraco.Cms.Core.Hosting.IHostingEnvironment;

namespace Umbraco.Extensions
{
    /// <summary>
    /// Extension methods for <see cref="IUmbracoBuilder"/> for the Umbraco back office
    /// </summary>
    public static partial class UmbracoBuilderExtensions
    {
        /// <summary>
        /// Adds all required components to run the Umbraco back office
        /// </summary>
        public static IUmbracoBuilder AddBackOffice(this IUmbracoBuilder builder,
            Action<IMvcBuilder> configureMvc = null) => builder
            .AddConfiguration()
            .AddUmbracoCore()
            .AddWebComponents()
            .AddRuntimeMinifier()
            .AddBackOfficeCore()
            .AddBackOfficeAuthentication()
            .AddBackOfficeIdentity()
            .AddMembersIdentity()
            .AddBackOfficeAuthorizationPolicies()
            .AddUmbracoProfiler()
            .AddMvcAndRazor(configureMvc)
            .AddWebServer()
            .AddPreviewSupport()
            .AddHostedServices()
            .AddNuCache()
            .AddDistributedCache()
            .AddModelsBuilderDashboard()
            .AddUnattendedInstallInstallCreateUser()
            .AddCoreNotifications()
            .AddLogViewer()
            .AddExamine()
            .AddExamineIndexes()
            .AddControllersWithAmbiguousConstructors()
            .AddSupplemenataryLocalizedTextFileSources();

        public static IUmbracoBuilder AddUnattendedInstallInstallCreateUser(this IUmbracoBuilder builder)
        {
            builder.AddNotificationAsyncHandler<UnattendedInstallNotification, CreateUnattendedUserNotificationHandler>();
            return builder;
        }

        /// <summary>
        /// Adds Umbraco preview support
        /// </summary>
        public static IUmbracoBuilder AddPreviewSupport(this IUmbracoBuilder builder)
        {
            builder.Services.AddSignalR();

            return builder;
        }

        /// <summary>
        /// Gets the back office tree collection builder
        /// </summary>
        public static TreeCollectionBuilder Trees(this IUmbracoBuilder builder)
            => builder.WithCollectionBuilder<TreeCollectionBuilder>();

        public static IUmbracoBuilder AddBackOfficeCore(this IUmbracoBuilder builder)
        {
            builder.Services.AddSingleton<KeepAliveMiddleware>();
            builder.Services.ConfigureOptions<ConfigureGlobalOptionsForKeepAliveMiddlware>();
            builder.Services.AddSingleton<ServerVariablesParser>();
            builder.Services.AddSingleton<InstallAreaRoutes>();
            builder.Services.AddSingleton<BackOfficeAreaRoutes>();
            builder.Services.AddSingleton<PreviewRoutes>();
            builder.AddNotificationAsyncHandler<ContentCacheRefresherNotification, PreviewHubUpdater>();
            builder.Services.AddSingleton<BackOfficeServerVariables>();
            builder.Services.AddScoped<BackOfficeSessionIdValidator>();
            builder.Services.AddScoped<BackOfficeSecurityStampValidator>();

            // register back office trees
            // the collection builder only accepts types inheriting from TreeControllerBase
            // and will filter out those that are not attributed with TreeAttribute
            var umbracoApiControllerTypes = builder.TypeLoader.GetUmbracoApiControllers().ToList();
            builder.Trees()
                .AddTreeControllers(umbracoApiControllerTypes.Where(x => typeof(TreeControllerBase).IsAssignableFrom(x)));

            builder.AddWebMappingProfiles();

            builder.Services.AddUnique<IPhysicalFileSystem>(factory =>
            {
                var path = "~/";
                var hostingEnvironment = factory.GetRequiredService<IHostingEnvironment>();
                return new PhysicalFileSystem(
                    factory.GetRequiredService<IIOHelper>(),
                    hostingEnvironment,
                    factory.GetRequiredService<ILogger<PhysicalFileSystem>>(),
                    hostingEnvironment.MapPathContentRoot(path),
                    hostingEnvironment.ToAbsolute(path)
                );
            });

            builder.Services.AddUnique<IIconService, IconService>();
            builder.Services.AddUnique<IConflictingRouteService, ConflictingRouteService>();
            builder.Services.AddSingleton<UnhandledExceptionLoggerMiddleware>();

            return builder;
        }

        /// <summary>
        /// Adds explicit registrations for controllers with ambiguous constructors to prevent downstream issues for
        /// users who wish to use <see cref="Microsoft.AspNetCore.Mvc.Controllers.ServiceBasedControllerActivator"/>
        /// </summary>
        public static IUmbracoBuilder AddControllersWithAmbiguousConstructors(
            this IUmbracoBuilder builder)
        {
            builder.Services.TryAddTransient(sp =>
                ActivatorUtilities.CreateInstance<CurrentUserController>(sp));

            return builder;
        }

        public static IUmbracoBuilder AddSupplemenataryLocalizedTextFileSources(this IUmbracoBuilder builder)
        {
            builder.Services.AddTransient(sp =>
            {
                return GetSupplementaryFileSources(
                    sp.GetRequiredService<IHostingEnvironment>(),
                    sp.GetRequiredService<IWebHostEnvironment>());
            });

            return builder;
        }

        private static IEnumerable<LocalizedTextServiceSupplementaryFileSource> GetSupplementaryFileSources(
            IHostingEnvironment hostingEnvironment,
            IWebHostEnvironment webHostEnvironment)
        {
            var appPlugins = new DirectoryInfo(hostingEnvironment.MapPathContentRoot(Cms.Core.Constants.SystemDirectories.AppPlugins));
            var configLangFolder = new DirectoryInfo(hostingEnvironment.MapPathContentRoot(WebPath.Combine(Cms.Core.Constants.SystemDirectories.Config, "lang")));

            var pluginLangFolders = appPlugins.Exists == false
                ? Enumerable.Empty<LocalizedTextServiceSupplementaryFileSource>()
                : appPlugins.GetDirectories()
                    // Check for both Lang & lang to support case sensitive file systems.
                    .SelectMany(x => x.GetDirectories("?ang", SearchOption.AllDirectories).Where(x => x.Name.InvariantEquals("lang")))
                    .SelectMany(x => x.GetFiles("*.xml", SearchOption.TopDirectoryOnly))
                    .Select(x => new LocalizedTextServiceSupplementaryFileSource(x, false));


            // user defined langs that overwrite the default, these should not be used by plugin creators
            var userLangFolders = configLangFolder.Exists == false
                ? Enumerable.Empty<LocalizedTextServiceSupplementaryFileSource>()
                : configLangFolder
                    .GetFiles("*.user.xml", SearchOption.TopDirectoryOnly)
                    .Select(x => new LocalizedTextServiceSupplementaryFileSource(x, true));

            foreach (LocalizedTextServiceSupplementaryFileSource fileSource in userLangFolders)
            {
                yield return fileSource;
            }

            foreach (LocalizedTextServiceSupplementaryFileSource fileSource in pluginLangFolders)
            {
                yield return fileSource;
            }

            // TODO: Get more from WebHostEnvironment.WebRootFileProvider
        }
    }
}
