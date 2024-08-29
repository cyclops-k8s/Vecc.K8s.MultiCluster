using k8s.Models;
using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;
using Vecc.K8s.MultiCluster.Api.Services;

namespace Vecc.K8s.MultiCluster.Api.Controllers
{
    [EntityRbac(typeof(V1Ingress), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Watch)]
    [EntityRbac(typeof(V1Service), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Watch)]
    [EntityRbac(typeof(V1Endpoints), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Watch)]
    [EntityRbac(typeof(V1Namespace), Verbs = RbacVerb.List | RbacVerb.Get)]
    public class K8sChangedController : IEntityController<V1Ingress>, IEntityController<V1Service>, IEntityController<V1Endpoints>, IEntityController<V1Gslb>
    {
        private readonly ILogger<K8sChangedController> _logger;
        private readonly ICache _cache;
        private readonly IHostnameSynchronizer _synchronizer;
        private readonly IKubernetesClient _client;

        public K8sChangedController(ILogger<K8sChangedController> logger, ICache cache, IHostnameSynchronizer synchronizer, IKubernetesClient client)
        {
            _logger = logger;
            _cache = cache;
            _synchronizer = synchronizer;
            _client = client;
        }

        public async Task DeletedAsync(V1Ingress ingress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Ingress {@namespace}/{@ingress} deleted", ingress.Namespace(), ingress.Name());
            await SyncIngressIfRequired(ingress);
        }

        public async Task ReconcileAsync(V1Ingress ingress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Ingress {@namespace}/{@ingress} reconcile requested", ingress.Namespace(), ingress.Name());
            await SyncIngressIfRequired(ingress);
        }

        public async Task DeletedAsync(V1Service service, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Service {@namespace}/{@service} deleted", service.Namespace(), service.Name());
            await SyncServiceIfRequiredAsync(service);
        }

        public async Task ReconcileAsync(V1Service service, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Service {@namespace}/{@service} reconcile requested", service.Namespace(), service.Name());
            await SyncServiceIfRequiredAsync(service);
        }

        public async Task DeletedAsync(V1Endpoints endpoints, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Endpoints {@namespace}/{@endpoints} deleted", endpoints.Namespace(), endpoints.Name());
            await SyncEndpointsIfRequiredAsync(endpoints);
        }

        public async Task ReconcileAsync(V1Endpoints endpoints, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Endpoints {@namespace}/{@endpoints} reconcile requested", endpoints.Namespace(), endpoints.Name());
            await SyncEndpointsIfRequiredAsync(endpoints);
        }

        public async Task ReconcileAsync(V1Gslb entity, CancellationToken cancellationToken)
        {
            _logger.LogInformation("GSLB {@namespace}/{@name} reconcile requested", entity.Namespace(), entity.Name());

            if (entity.ObjectReference.Kind == V1Gslb.V1ObjectReference.ReferenceType.Ingress)
            {
                _logger.LogInformation("GSLB {@namespace}/{@name} has ingress references, syncing", entity.Namespace(), entity.Name());
                var ingress = await _client.GetAsync<V1Ingress>(entity.ObjectReference.Name, entity.Namespace());
                if (ingress == null)
                {
                    _logger.LogError("Ingress {@namespace}/{@name} not found", entity.Namespace(), entity.Name());
                }
                else
                {
                    await _synchronizer.SynchronizeLocalIngressAsync(ingress);
                }
            }
            else
            {
                _logger.LogInformation("GSLB {@namespace}/{@name} has service references, syncing", entity.Namespace(), entity.Name());
                var service = await _client.GetAsync<V1Service>(entity.ObjectReference.Name, entity.Namespace());
                if (service == null)
                {
                    _logger.LogError("Service {@namespace}/{@name} not found", entity.Namespace(), entity.Name());
                }
                else
                {
                    await _synchronizer.SynchronizeLocalServiceAsync(service);
                }
            }
        }

        public async Task DeletedAsync(V1Gslb entity, CancellationToken cancellationToken)
        {
            _logger.LogInformation("GSLB {@namespace}/{@name} delete requested", entity.Namespace(), entity.Name());

            if (entity.ObjectReference.Kind == V1Gslb.V1ObjectReference.ReferenceType.Ingress)
            {
                _logger.LogInformation("GSLB {@namespace}/{@name} has ingress references, syncing", entity.Namespace(), entity.Name());
                var ingress = await _client.GetAsync<V1Ingress>(entity.ObjectReference.Name, entity.Namespace());
                if (ingress == null)
                {
                    _logger.LogInformation("Ingress {@namespace}/{@name} not found, resyncing cluster", entity.Namespace(), entity.Name());
                    await _synchronizer.SynchronizeLocalClusterAsync();
                }
                else
                {
                    await _synchronizer.SynchronizeLocalIngressAsync(ingress);
                }
            }
            else
            {
                _logger.LogInformation("GSLB {@namespace}/{@name} has service references, syncing", entity.Namespace(), entity.Name());
                var service = await _client.GetAsync<V1Service>(entity.ObjectReference.Name, entity.Namespace());
                if (service == null)
                {
                    _logger.LogInformation("Service {@namespace}/{@name} not found, resyncing cluster", entity.Namespace(), entity.Name());
                    await _synchronizer.SynchronizeLocalClusterAsync();
                }
                else
                {
                    await _synchronizer.SynchronizeLocalServiceAsync(service);
                }
            }
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
