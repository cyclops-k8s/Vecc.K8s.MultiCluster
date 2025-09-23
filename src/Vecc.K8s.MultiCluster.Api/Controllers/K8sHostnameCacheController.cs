using k8s.Models;
using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;
using Vecc.K8s.MultiCluster.Api.Services.Default;

namespace Vecc.K8s.MultiCluster.Api.Controllers
{
    /// <summary>
    /// </summary>
    [EntityRbac(typeof(V1HostnameCache), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1ClusterCache), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1ResourceCache), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1ServiceCache), Verbs = RbacVerb.All)]
    public class K8sHostnameCacheController : IEntityController<V1HostnameCache>
    {
        private readonly ILogger<K8sHostnameCacheController> _logger;
        private readonly KubernetesQueue _queue;

        /// <summary>
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="queue"></param>
        public K8sHostnameCacheController(ILogger<K8sHostnameCacheController> logger, KubernetesQueue queue)
        {
            _logger = logger;
            _queue = queue;
        }

        /// <summary>
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task DeletedAsync(V1HostnameCache entity, CancellationToken cancellationToken)
        {

            var hostname = entity.Hostname ?? entity.GetLabel("hostname");
            using var _scope = _logger.BeginScope(new {@object = "hostnamecache", state="deleted", @namespace = entity.Namespace(), cluster = entity.Name(), hostname });
            _logger.LogInformation("Deleting hostname cache");
            _queue.OnHostChangedAsync(hostname);
            _logger.LogInformation("Hostname cache deleted");
            return Task.CompletedTask;
        }

        /// <summary>
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task ReconcileAsync(V1HostnameCache entity, CancellationToken cancellationToken)
        {
            var hostname = entity.Hostname ?? entity.GetLabel("hostname");
            using var _scope = _logger.BeginScope(new {@object = "hostnamecache", state="deleted", @namespace = entity.Namespace(), cluster = entity.Name(), hostname });
            _logger.LogInformation("Reconciling hostname cache");
            _queue.OnHostChangedAsync(hostname);
            _logger.LogInformation("Hostname cache reconciled");
            return Task.CompletedTask;
        }
    }
}
