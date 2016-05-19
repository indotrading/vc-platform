﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Description;
using Hangfire;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Modularity;
using VirtoCommerce.Platform.Core.Packaging;
using VirtoCommerce.Platform.Core.PushNotifications;
using VirtoCommerce.Platform.Core.Security;
using VirtoCommerce.Platform.Core.Web.Assets;
using VirtoCommerce.Platform.Core.Web.Security;
using VirtoCommerce.Platform.Data.Common;
using VirtoCommerce.Platform.Web.Converters.Packaging;
using webModel = VirtoCommerce.Platform.Web.Model.Packaging;

namespace VirtoCommerce.Platform.Web.Controllers.Api
{
    [RoutePrefix("api/platform/modules")]
    [CheckPermission(Permission = PredefinedPermissions.ModuleQuery)]
    public class ModulesController : ApiController
    {
        private readonly IModuleCatalog _moduleCatalog;
        private readonly IModuleInstaller _moduleInstaller;
        private readonly string _uploadsPath;
        private readonly IPushNotificationManager _pushNotifier;
        private readonly IUserNameResolver _userNameResolver;

        public ModulesController(IModuleCatalog moduleCatalog, IModuleInstaller moduleInstaller, string uploadsPath, IPushNotificationManager pushNotifier, IUserNameResolver userNameResolver)
        {
            _moduleCatalog = moduleCatalog;
            _moduleInstaller = moduleInstaller;
            _uploadsPath = uploadsPath;
            _pushNotifier = pushNotifier;
            _userNameResolver = userNameResolver;
        }

        /// <summary>
        /// Get installed modules
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(ManifestModuleInfo[]))]
        public IHttpActionResult GetModules()
        {
            var retVal = _moduleCatalog.Modules.OfType<ManifestModuleInfo>().ToArray();
            return Ok(retVal);
        }

        /// <summary>
        /// Get all dependent modules for module
        /// </summary>
        /// <param name="module">module</param>
        /// <returns></returns>
        [HttpGet]
        [Route("dependent")]
        [ResponseType(typeof(ManifestModuleInfo[]))]
        public IHttpActionResult GetDependentModules(ManifestModuleInfo module)
        {
            return Ok(_moduleCatalog.GetDependentModules(module).OfType<ManifestModuleInfo>().ToArray());
        }

        /// <summary>
        /// Returns a flat expanded  list of modules that depend on passed modules
        /// </summary>
        /// <param name="modules">modules</param>
        /// <returns></returns>
        [HttpPost]
        [Route("getmissingdependencies")]
        [ResponseType(typeof(ManifestModuleInfo[]))]
        public IHttpActionResult GetMissingDependencies(ManifestModuleInfo[] modules)
        {
            return Ok(_moduleCatalog.CompleteListWithDependencies(modules).OfType<ManifestModuleInfo>().Where(x=>!x.IsInstalled).ToArray());
        }

        /// <summary>
        /// Upload module package for installation or update
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [Route("")]
        [ResponseType(typeof(ManifestModuleInfo))]
        [CheckPermission(Permission = PredefinedPermissions.ModuleManage)]
        public async Task<IHttpActionResult> Upload()
        {
            // Check if the request contains multipart/form-data.
            if (!Request.Content.IsMimeMultipartContent())
            {
                throw new HttpResponseException(HttpStatusCode.UnsupportedMediaType);
            }

            if (!Directory.Exists(_uploadsPath))
            {
                Directory.CreateDirectory(_uploadsPath);
            }

            var streamProvider = new CustomMultipartFormDataStreamProvider(_uploadsPath);
            await Request.Content.ReadAsMultipartAsync(streamProvider);

            var file = streamProvider.FileData.FirstOrDefault();
            if (file != null)
            {
                //var moduleInfo = _moduleInstaller.LoadModule(file.LocalFileName);
                //if (moduleInfo != null)
                //{
                //    var retVal = moduleInfo;

                //    //var dependencyErrors = _moduleInstaller.GetDependencyErrors(moduleInfo);
                //    //retVal.ValidationErrors.AddRange(dependencyErrors);

                //    return Ok(retVal);
                //}
            }

            return NotFound();
        }

        /// <summary>
        /// Install modules 
        /// </summary>
        /// <param name="modules">modules for install</param>
        /// <returns></returns>
        [HttpPost]
        [Route("install")]
        [ResponseType(typeof(webModel.ModulePushNotification))]
        [CheckPermission(Permission = PredefinedPermissions.ModuleManage)]
        public IHttpActionResult InstallModules(ManifestModuleInfo[] modules)
        {
            var notInstalledModules = _moduleCatalog.CompleteListWithDependencies(modules).OfType<ManifestModuleInfo>().Where(x => !x.IsInstalled).ToArray();
            var options = new webModel.ModuleBackgroundJobOptions
            {
                Action = webModel.ModuleAction.Install,
                Modules = notInstalledModules
            };
            var result = ScheduleJob(options);
            return Ok();
        }

        /// <summary>
        /// Uninstall module
        /// </summary>
        /// <param name="modules">modules</param>
        /// <returns></returns>
        [HttpGet]
        [Route("install")]
        [ResponseType(typeof(webModel.ModulePushNotification))]
        [CheckPermission(Permission = PredefinedPermissions.ModuleManage)]
        public IHttpActionResult UninstallModule(ManifestModuleInfo[] modules)
        {
            var options = new webModel.ModuleBackgroundJobOptions
            {
                Action = webModel.ModuleAction.Uninstall,
                Modules = modules
            };
            var result = ScheduleJob(options);
            return Ok(result);
        }

        /// <summary>
        /// Restart web application
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("restart")]
        [ResponseType(typeof(void))]
        [CheckPermission(Permission = PredefinedPermissions.ModuleManage)]
        public IHttpActionResult Restart()
        {
            HttpRuntime.UnloadAppDomain();
            return StatusCode(HttpStatusCode.NoContent);
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        public void ModuleBackgroundJob(webModel.ModuleBackgroundJobOptions options, webModel.ModulePushNotification notification)
        {
            try
            {
                notification.Started = DateTime.UtcNow;

                var reportProgress = new Progress<ProgressMessage>(m =>
                {
                    notification.ProgressLog.Add(m.ToWebModel());
                    _pushNotifier.Upsert(notification);
                });

                switch (options.Action)
                {
                    case webModel.ModuleAction.Install:
                        _moduleInstaller.Install(options.Modules, reportProgress);
                        break;           
                    case webModel.ModuleAction.Uninstall:
                        _moduleInstaller.Uninstall(options.Modules, reportProgress);
                        break;
                }
            }
            catch (Exception ex)
            {
                notification.ProgressLog.Add(new webModel.ProgressMessage
                {
                    Level = ProgressMessageLevel.Error.ToString(),
                    Message = ex.ExpandExceptionMessage(),
                });
            }
            finally
            {
                notification.Finished = DateTime.UtcNow;
                _pushNotifier.Upsert(notification);
            }
        }


        private webModel.ModulePushNotification ScheduleJob(webModel.ModuleBackgroundJobOptions options)
        {
            var notification = new webModel.ModulePushNotification(_userNameResolver.GetCurrentUserName());

            switch (options.Action)
            {
                case webModel.ModuleAction.Install:
                    notification.Title = "Install Module";
                    break;           
                case webModel.ModuleAction.Uninstall:
                    notification.Title = "Uninstall Module";
                    break;
            }

            _pushNotifier.Upsert(notification);

            BackgroundJob.Enqueue(() => ModuleBackgroundJob(options, notification));

            return notification;
        }
    }
}