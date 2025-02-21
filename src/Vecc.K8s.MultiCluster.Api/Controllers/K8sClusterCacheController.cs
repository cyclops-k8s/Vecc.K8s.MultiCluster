using k8s.Models;
using KubeOps.Abstractions.Controller;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;
using Vecc.K8s.MultiCluster.Api.Services;

namespace Vecc.K8s.MultiCluster.Api.Controllers
{
    /// <summary>
    /// </summary>
    /// <param name="_logger"></param>
    /// <param name="_cache"></param>
    public class K8sClusterCacheController(ILogger<K8sClusterCacheController> _logger, ICache _cache) : IEntityController<V1ClusterCache>
    {
        /// <summary>
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task DeletedAsync(V1ClusterCache entity, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Cluster cache deleted: {namespace}/{name}", entity.Namespace(), entity.Name());
            try
            {
                await _cache.SynchronizeCachesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error synchronizing remote clusters");
            }
            _logger.LogInformation("Cluster cache deleted: {namespace}/{name}", entity.Namespace(), entity.Name());
        }

        /// <summary>
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task ReconcileAsync(V1ClusterCache entity, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Reconciling cluster cache: {namespace}/{name}", entity.Namespace(), entity.Name());
            try
            {
                var clusterIdentifier = entity.Name();
                var currentHostnames = await _cache.GetHostsAsync(clusterIdentifier) ?? Array.Empty<Models.Core.Host>();

                await _cache.SynchronizeCachesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error synchronizing remote clusters");
            }
            _logger.LogInformation("Reconciled cluster cache: {namespace}/{name}", entity.Namespace(), entity.Name());
        }
    }
}
