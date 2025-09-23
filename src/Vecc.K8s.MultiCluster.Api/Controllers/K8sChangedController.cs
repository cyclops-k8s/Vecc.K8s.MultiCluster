using k8s.Models;
using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;
using Vecc.K8s.MultiCluster.Api.Services;

namespace Vecc.K8s.MultiCluster.Api.Controllers
{
    /// <summary>
    /// Inbound cluster operations
    /// </summary>
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

        /// <summary>
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="cache"></param>
        /// <param name="synchronizer"></param>
        /// <param name="client"></param>
        public K8sChangedController(ILogger<K8sChangedController> logger, ICache cache, IHostnameSynchronizer synchronizer, IKubernetesClient client)
        {
            _logger = logger;
            _cache = cache;
            _synchronizer = synchronizer;
            _client = client;
        }

        /// <summary>
        /// </summary>
        /// <param name="ingress"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task DeletedAsync(V1Ingress ingress, CancellationToken cancellationToken)
        {
            using var _scope = _logger.BeginScope(new {@object = "ingress", state="deleted", @namespace = ingress.Namespace(), ingress = ingress.Name() });

            _logger.LogInformation("Ingress deleted");
            await SyncIngressIfRequired(ingress);
        }

        /// <summary>
        /// </summary>
        /// <param name="ingress"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task ReconcileAsync(V1Ingress ingress, CancellationToken cancellationToken)
        {
            using var _scope = _logger.BeginScope(new {@object = "ingress", state="reconcile", @namespace = ingress.Namespace(), ingress = ingress.Name() });

            _logger.LogInformation("Ingress reconcile requested");
            await SyncIngressIfRequired(ingress);
        }

        /// <summary>
        /// </summary>
        /// <param name="service"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task DeletedAsync(V1Service service, CancellationToken cancellationToken)
        {
            using var _scope = _logger.BeginScope(new {@object = "service", state="deleted", @namespace = service.Namespace(), service = service.Name() });

            _logger.LogInformation("Service deleted");
            await SyncServiceIfRequiredAsync(service);
        }

        /// <summary>
        /// </summary>
        /// <param name="service"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task ReconcileAsync(V1Service service, CancellationToken cancellationToken)
        {
            using var _scope = _logger.BeginScope(new {@object = "service", state= "reconcile", @namespace = service.Namespace(), service = service.Name() });

            _logger.LogInformation("Service reconcile requested");
            await SyncServiceIfRequiredAsync(service);
        }

        /// <summary>
        /// </summary>
        /// <param name="endpoints"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task DeletedAsync(V1Endpoints endpoints, CancellationToken cancellationToken)
        {
            using var _scope = _logger.BeginScope(new {@object = "endpoints", state="deleted", @namespace = endpoints.Namespace(), endpoints = endpoints.Name() });

            _logger.LogInformation("Endpoints deleted");
            await SyncEndpointsIfRequiredAsync(endpoints);
        }

        /// <summary>
        /// </summary>
        /// <param name="endpoints"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task ReconcileAsync(V1Endpoints endpoints, CancellationToken cancellationToken)
        {
            using var _scope = _logger.BeginScope(new {@object = "endpoints", state= "reconcile", @namespace = endpoints.Namespace(), endpoints = endpoints.Name() });

            _logger.LogInformation("Endpoints reconcile requested");
            await SyncEndpointsIfRequiredAsync(endpoints);
        }

        /// <summary>
        /// </summary>
        /// <param name="gslb"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task ReconcileAsync(V1Gslb gslb, CancellationToken cancellationToken)
        {
            using var _scope = _logger.BeginScope(new {@object = "gslb", state= "reconcile", @namespace = gslb.Namespace(), gslb = gslb.Name() });
            _logger.LogInformation("GSLB reconcile requested");

            if (gslb.ObjectReference.Kind == V1Gslb.V1ObjectReference.ReferenceType.Ingress)
            {
                _logger.LogDebug("GSLB has ingress references, syncing");
                var ingress = await _client.GetAsync<V1Ingress>(gslb.ObjectReference.Name, gslb.Namespace());
                if (ingress == null)
                {
                    _logger.LogInformation("Ingress not found, resyncing entire cluster");
                    await _synchronizer.SynchronizeLocalClusterAsync();
                }
                else
                {
                    await _synchronizer.SynchronizeLocalIngressAsync(ingress);
                }
            }
            else
            {
                _logger.LogInformation("GSLB has service references, syncing");
                var service = await _client.GetAsync<V1Service>(gslb.ObjectReference.Name, gslb.Namespace());
                if (service == null)
                {
                    _logger.LogError("Service not found");
                }
                else
                {
                    await _synchronizer.SynchronizeLocalServiceAsync(service);
                }
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="gslb"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task DeletedAsync(V1Gslb gslb, CancellationToken cancellationToken)
        {
            using var _scope = _logger.BeginScope(new {@object = "gslb", state= "reconcile", @namespace = gslb.Namespace(), gslb = gslb.Name() });
            _logger.LogInformation("GSLB deleted");

            if (gslb.ObjectReference.Kind == V1Gslb.V1ObjectReference.ReferenceType.Ingress)
            {
                _logger.LogInformation("GSLB has ingress references, syncing");
                var ingress = await _client.GetAsync<V1Ingress>(gslb.ObjectReference.Name, gslb.Namespace());
                if (ingress == null)
                {
                    _logger.LogInformation("Ingress not found, resyncing entire cluster");
                    await _synchronizer.SynchronizeLocalClusterAsync();
                }
                else
                {
                    await _synchronizer.SynchronizeLocalIngressAsync(ingress);
                }
            }
            else
            {
                _logger.LogInformation("GSLB has service references, syncing");
                var service = await _client.GetAsync<V1Service>(gslb.ObjectReference.Name, gslb.Namespace());
                if (service == null)
                {
                    _logger.LogInformation("Service not found, resyncing cluster");
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
            _logger.LogTrace("Syncing endpoints if required");
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
                        _logger.LogDebug("We had endpoints and we still do, not resyncing");
                    }

                    if (doSync)
                    {
                        _logger.LogInformation("Endpoints {@oldCount}->{@newCount} change requires resync",
                            oldCount, subsetCount);

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
                _logger.LogInformation("Ingress change requires resync");
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
                    _logger.LogInformation("Service change requires resync");
                    await _synchronizer.SynchronizeLocalServiceAsync(service);
                }
            }
        }
    }
}
