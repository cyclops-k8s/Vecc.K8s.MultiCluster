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
            _logger.LogInformation("Ingress {@namespace}/{@ingress} deleted", ingress.Namespace(), ingress.Name());
            await SyncIngressIfRequired(ingress);
        }

        public async Task<ResourceControllerResult?> ReconcileAsync(V1Ingress ingress)
        {
            _logger.LogInformation("Ingress {@namespace}/{@ingress} reconcile requested", ingress.Namespace(), ingress.Name());
            await SyncIngressIfRequired(ingress);

            return null;
        }

        public async Task StatusModifiedAsync(V1Ingress ingress)
        {
            _logger.LogInformation("Ingress {@namespace}/{@ingress} state changed", ingress.Namespace(), ingress.Name());
            await SyncIngressIfRequired(ingress);
        }

        public async Task DeletedAsync(V1Service service)
        {
            _logger.LogInformation("Service {@namespace}/{@service} deleted", service.Namespace(), service.Name());
            await SyncServiceIfRequiredAsync(service);
        }

        public async Task<ResourceControllerResult?> ReconcileAsync(V1Service service)
        {
            _logger.LogInformation("Service {@namespace}/{@service} reconcile requested", service.Namespace(), service.Name());
            await SyncServiceIfRequiredAsync(service);

            return null;
        }

        public async Task StatusModifiedAsync(V1Service service)
        {
            _logger.LogInformation("Service {@namespace}/{@service} status modified", service.Namespace(), service.Name());
            await SyncServiceIfRequiredAsync(service);
        }

        public async Task DeletedAsync(V1Endpoints endpoints)
        {
            _logger.LogInformation("Endpoints {@namespace}/{@endpoints} deleted", endpoints.Namespace(), endpoints.Name());
            await SyncEndpointsIfRequiredAsync(endpoints);
        }

        public async Task<ResourceControllerResult?> ReconcileAsync(V1Endpoints endpoints)
        {
            _logger.LogInformation("Endpoints {@namespace}/{@endpoints} reconcile requested", endpoints.Namespace(), endpoints.Name());
            await SyncEndpointsIfRequiredAsync(endpoints);

            return null;
        }

        public async Task StatusModifiedAsync(V1Endpoints endpoints)
        {
            _logger.LogInformation("Endpoints {@namespace}/{@endpoints} status changed", endpoints.Namespace(), endpoints.Name());
            await SyncEndpointsIfRequiredAsync(endpoints);
        }

        private async Task SyncEndpointsIfRequiredAsync(V1Endpoints endpoints)
        {
            _logger.LogTrace("Syncing endpoints if required {namespace}/{name}", endpoints.Namespace(), endpoints.Name());
            if (await _cache.IsServiceMonitoredAsync(endpoints.Namespace(), endpoints.Name()))
            {
                _logger.LogTrace("Endpoint service is monitored, checking");
                var oldResourceVersion = await _cache.GetLastResourceVersionAsync(endpoints.Metadata.Uid);
                if (oldResourceVersion != endpoints.Metadata.ResourceVersion)
                {
                    _logger.LogTrace("Endpoint resource version is not the same, checking endpoint count.");
                    var oldCount = await _cache.GetEndpointsCountAsync(endpoints.Namespace(), endpoints.Name());
                    var doSync = false;

                    // if there are no subsets then subsets is null, check that here
                    var subsetCount = endpoints.Subsets?.Count ?? 0;

                    // resource version will change as pods start/stop/delete/create, check to see if we need to resync
                    // we only need to resync if pod count is 0 and now we have pods, or if pod count was not 0 and now we don't
                    if (oldCount != 0 && subsetCount == 0)
                    {
                        _logger.LogInformation("We had endpoints and now we do not, resyncing");
                        doSync = true;
                    }
                    else if (oldCount == 0 && subsetCount != 0)
                    {
                        _logger.LogInformation("We did not have endpoints and now we do, resyncing");
                        doSync = true;
                    }
                    else if (oldCount == 0 && subsetCount == 0)
                    {
                        _logger.LogInformation("We did not have endpoints and we still do not, not resyncing");
                    }
                    else
                    {
                        _logger.LogInformation("We had endpoints and we still do, not resyncing");
                    }

                    if (doSync)
                    {
                        _logger.LogInformation("Endpoints {@namespace}/{@endpoints} {@oldCount}->{@newCount} change requires resync",
                            endpoints.Namespace(), endpoints.Name(), oldCount, subsetCount);

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
