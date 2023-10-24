using IdentityModel;
using k8s.LeaderElection;
using k8s.Models;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Controller;
using KubeOps.Operator.Controller.Results;
using KubeOps.Operator.Rbac;
using Vecc.K8s.MultiCluster.Api.Services;

namespace Vecc.K8s.MultiCluster.Api.Controllers
{
    [EntityRbac(typeof(V1Ingress), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Watch)]
    [EntityRbac(typeof(V1Service), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Watch)]
    [EntityRbac(typeof(V1Endpoints), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Watch)]
    [EntityRbac(typeof(V1Namespace), Verbs = RbacVerb.List | RbacVerb.Get)]
    public class K8sChangedController : IResourceController<V1Ingress>, IResourceController<V1Service>, IResourceController<V1Endpoints>
    {
        private readonly ILogger<K8sChangedController> _logger;
        private readonly ICache _cache;
        private readonly IHostnameSynchronizer _synchronizer;

        public K8sChangedController(ILogger<K8sChangedController> logger, ICache cache, IHostnameSynchronizer synchronizer)
        {
            _logger = logger;
            _cache = cache;
            _synchronizer = synchronizer;
        }

        public async Task DeletedAsync(V1Ingress ingress)
        {
            _logger.LogDebug("Ingress {@namespace}/{@ingress} deleted", ingress.Namespace(), ingress.Name());
            await SyncIngressIfRequired(ingress);
        }

        public async Task<ResourceControllerResult?> ReconcileAsync(V1Ingress ingress)
        {
            _logger.LogDebug("Ingress {@namespace}/{@ingress} reconcile requested", ingress.Namespace(), ingress.Name());
            await SyncIngressIfRequired(ingress);

            return null;
        }

        public async Task StatusModifiedAsync(V1Ingress ingress)
        {
            _logger.LogDebug("Ingress {@namespace}/{@ingress} state changed", ingress.Namespace(), ingress.Name());
            await SyncIngressIfRequired(ingress);
        }

        public async Task DeletedAsync(V1Service service)
        {
            _logger.LogDebug("Service {@namespace}/{@service} deleted", service.Namespace(), service.Name());
            await SyncServiceIfRequiredAsync(service);
        }

        public async Task<ResourceControllerResult?> ReconcileAsync(V1Service service)
        {
            _logger.LogDebug("Service {@namespace}/{@service} reconcile requested", service.Namespace(), service.Name());
            await SyncServiceIfRequiredAsync(service);

            return null;
        }

        public async Task StatusModifiedAsync(V1Service service)
        {
            _logger.LogDebug("Service {@namespace}/{@service} status modified", service.Namespace(), service.Name());
            await SyncServiceIfRequiredAsync(service);
        }

        public async Task DeletedAsync(V1Endpoints endpoints)
        {
            _logger.LogDebug("Endpoints {@namespace}/{@endpoints} deleted", endpoints.Namespace(), endpoints.Name());
            await SyncEndpointsIfRequiredAsync(endpoints);
        }

        public async Task<ResourceControllerResult?> ReconcileAsync(V1Endpoints endpoints)
        {
            _logger.LogDebug("Endpoints {@namespace}/{@endpoints} reconcile requested", endpoints.Namespace(), endpoints.Name());
            await SyncEndpointsIfRequiredAsync(endpoints);

            return null;
        }

        public async Task StatusModifiedAsync(V1Endpoints endpoints)
        {
            _logger.LogDebug("Endpoints {@namespace}/{@endpoints} status changed", endpoints.Namespace(), endpoints.Name());
            await SyncEndpointsIfRequiredAsync(endpoints);
        }

        private async Task SyncEndpointsIfRequiredAsync(V1Endpoints endpoints)
        {
            if (await _cache.IsServiceMonitoredAsync(endpoints.Namespace(), endpoints.Name()))
            {
                var oldResourceVersion = await _cache.GetLastResourceVersionAsync(endpoints.Metadata.Uid);
                if (oldResourceVersion != endpoints.Metadata.ResourceVersion)
                {
                    var oldCount = await _cache.GetEndpointsCountAsync(endpoints.Namespace(), endpoints.Name());
                    var doSync = false;

                    // check to see if we had endpoints, and now we don't
                    if (oldCount != 0 && endpoints.Subsets.Count == 0)
                    {
                        doSync = true;
                    }
                    // check to see if we didn't have endpoints and now we do
                    else if (oldCount == 0 && endpoints.Subsets.Count != 0)
                    {
                        doSync = true;
                    }

                    if (doSync)
                    {
                        _logger.LogInformation("Endpoints {@namespace}/{@endpoints} {@oldCount}->{@newCount} change requires resync",
                            endpoints.Namespace(), endpoints.Name(), oldCount, endpoints.Subsets.Count);

                        await _synchronizer.SynchronizeLocalEndpointsAsync(endpoints);
                    }
                }
            }
        }

        private async Task SyncIngressIfRequired(V1Ingress ingress)
        {
            var oldResourceVersion = await _cache.GetLastResourceVersionAsync(ingress.Metadata.Uid);
            if (oldResourceVersion != ingress.Metadata.ResourceVersion)
            {
                _logger.LogInformation("Ingress {@namespace}/{@ingress} change requires resync", ingress.Namespace(), ingress.Name());
                await _synchronizer.SynchronizeLocalIngressAsync(ingress);
            }
        }

        private async Task SyncServiceIfRequiredAsync(V1Service service)
        {
            if (await _cache.IsServiceMonitoredAsync(service.Namespace(), service.Name()))
            {
                var oldResourceVersion = await _cache.GetLastResourceVersionAsync(service.Metadata.Uid);
                if (oldResourceVersion != service.Metadata.ResourceVersion)
                {
                    _logger.LogInformation("Service {@namespace}/{@service} change requires resync", service.Namespace(), service.Name());
                    await _synchronizer.SynchronizeLocalServiceAsync(service);
                }
            }
        }
    }
}
