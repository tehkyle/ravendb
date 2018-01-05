using System;
using System.Linq;
using System.Net.Http;
using System.Web;
using System.Web.Hosting;
using System.Web.Http;
using Raven.Abstractions.Data;
using Raven.Abstractions.FileSystem;
using Raven.Database.Extensions;
using Raven.Database.Server.WebApi.Attributes;

namespace Raven.Database.FileSystem.Controllers
{
    public class FileSystemsStatsController : BaseFileSystemApiController
    {
        [HttpGet]
        [RavenRoute("fs/{fileSystemName}/stats")]
        public HttpResponseMessage Get()
        {
            var count = 0;
            Etag lastEtag = null;

            Storage.Batch(accessor =>
            {
                count = accessor.GetFileCount();
                lastEtag = accessor.GetLastEtag();
            });
            
            var stats = new FileSystemStats
            {
                Name = FileSystemName,
                FileSystemId = FileSystem.Storage.Id,
                ServerUrl = FileSystem.Configuration.ServerUrl,
                FileCount = count,
                LastFileEtag = lastEtag,
                Metrics = FileSystem.CreateMetrics(),
                ActiveSyncs = FileSystem.SynchronizationTask.Queue.Active.ToList(),
                PendingSyncs = FileSystem.SynchronizationTask.Queue.Pending.ToList(),
            };

            return GetMessageWithObject(stats).WithNoCache();
        }
    }
}
