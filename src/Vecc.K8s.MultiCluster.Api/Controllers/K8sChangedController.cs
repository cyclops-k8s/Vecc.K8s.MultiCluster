using k8s.Models;
using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
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
    [EntityRbac(typeof(V1EndpointSlice), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Watch)]
    [EntityRbac(typeof(V1Namespace), Verbs = RbacVerb.List | RbacVerb.Get)]
    public class K8sChangedController : IEntityController<V1Ingress>, IEntityController<V1Service>, IEntityController<V1EndpointSlice>, IEntityController<V1Gslb>
    {
        private const string _serviceNameLabel = "kubernetes.io/service-name";
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
        public async Task<ReconciliationResult<V1Ingress>> DeletedAsync(V1Ingress ingress, CancellationToken cancellationToken)
        {
            using var _scope = _logger.BeginScope(new {@object = "ingress", state="deleted", @namespace = ingress.Namespace(), ingress = ingress.Name() });

            _logger.LogInformation("Ingress deleted");
            await SyncIngressIfRequired(ingress);

            return ReconciliationResult<V1Ingress>.Success(ingress);
        }

        /// <summary>
        /// </summary>
        /// <param name="ingress"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ReconciliationResult<V1Ingress>> ReconcileAsync(V1Ingress ingress, CancellationToken cancellationToken)
        {
            using var _scope = _logger.BeginScope(new {@object = "ingress", state="reconcile", @namespace = ingress.Namespace(), ingress = ingress.Name() });

            _logger.LogInformation("Ingress reconcile requested");
            await SyncIngressIfRequired(ingress);

            return ReconciliationResult<V1Ingress>.Success(ingress);
        }

        /// <summary>
        /// </summary>
        /// <param name="service"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ReconciliationResult<V1Service>> DeletedAsync(V1Service service, CancellationToken cancellationToken)
        {
            using var _scope = _logger.BeginScope(new {@object = "service", state="deleted", @namespace = service.Namespace(), service = service.Name() });

            _logger.LogInformation("Service deleted");
            await SyncServiceIfRequiredAsync(service);

            return ReconciliationResult<V1Service>.Success(service);
        }

        /// <summary>
        /// </summary>
        /// <param name="service"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ReconciliationResult<V1Service>> ReconcileAsync(V1Service service, CancellationToken cancellationToken)
        {
            using var _scope = _logger.BeginScope(new {@object = "service", state= "reconcile", @namespace = service.Namespace(), service = service.Name() });

            _logger.LogInformation("Service reconcile requested");
            await SyncServiceIfRequiredAsync(service);

            return ReconciliationResult<V1Service>.Success(service);
        }

        /// <summary>
        /// </summary>
        /// <param name="endpointSlice"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ReconciliationResult<V1EndpointSlice>> DeletedAsync(V1EndpointSlice endpointSlice, CancellationToken cancellationToken)
        {
            var serviceName = endpointSlice.GetLabel(_serviceNameLabel) ?? endpointSlice.Name();
            using var _scope = _logger.BeginScope(new {@object = "endpointslice", state="deleted", @namespace = endpointSlice.Namespace(), endpointSlice = endpointSlice.Name(), service = serviceName });

            _logger.LogInformation("EndpointSlice deleted");
            await SyncEndpointSliceIfRequiredAsync(endpointSlice, serviceName);

            return ReconciliationResult<V1EndpointSlice>.Success(endpointSlice);
        }

        /// <summary>
        /// </summary>
        /// <param name="endpointSlice"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ReconciliationResult<V1EndpointSlice>> ReconcileAsync(V1EndpointSlice endpointSlice, CancellationToken cancellationToken)
        {
            var serviceName = endpointSlice.GetLabel(_serviceNameLabel) ?? endpointSlice.Name();
            using var _scope = _logger.BeginScope(new {@object = "endpointslice", state= "reconcile", @namespace = endpointSlice.Namespace(), endpointSlice = endpointSlice.Name(), service = serviceName });

            _logger.LogInformation("EndpointSlice reconcile requested");
            await SyncEndpointSliceIfRequiredAsync(endpointSlice, serviceName);

            return ReconciliationResult<V1EndpointSlice>.Success(endpointSlice);
        }

        /// <summary>
        /// </summary>
        /// <param name="gslb"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ReconciliationResult<V1Gslb>> ReconcileAsync(V1Gslb gslb, CancellationToken cancellationToken)
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

            return ReconciliationResult<V1Gslb>.Success(gslb);
        }

        /// <summary>
        /// </summary>
        /// <param name="gslb"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ReconciliationResult<V1Gslb>> DeletedAsync(V1Gslb gslb, CancellationToken cancellationToken)
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

            return ReconciliationResult<V1Gslb>.Success(gslb);
        }

        private async Task SyncEndpointSliceIfRequiredAsync(V1EndpointSlice endpointSlice, string serviceName)
        {
            _logger.LogTrace("Syncing endpoint slice if required");
            if (await _cache.IsServiceMonitoredAsync(endpointSlice.Namespace(), serviceName))
            {
                _logger.LogTrace("Endpoint slice service is monitored, checking");
                var oldResourceVersion = await _cache.GetLastResourceVersionAsync(endpointSlice.Metadata.Uid);
                if (oldResourceVersion != endpointSlice.Metadata.ResourceVersion)
                {
                    _logger.LogTrace("Endpoint slice resource version is not the same, checking endpoint count.");
                    var oldCount = await _cache.GetEndpointsCountAsync(endpointSlice.Namespace(), serviceName);
                    var doSync = false;

                    // Count ready endpoints in this slice
                    var readyEndpointCount = endpointSlice.Endpoints?
                        .Count(e => e.Conditions?.Ready == true) ?? 0;

                    // resource version will change as pods start/stop/delete/create, check to see if we need to resync
                    // we only need to resync if pod count is 0 and now we have pods, or if pod count was not 0 and now we don't
                    if (oldCount != 0 && readyEndpointCount == 0)
                    {
                        _logger.LogInformation("We had ready endpoints and now we do not, resyncing");
                        doSync = true;
                    }
                    else if (oldCount == 0 && readyEndpointCount != 0)
                    {
                        _logger.LogInformation("We did not have ready endpoints and now we do, resyncing");
                        doSync = true;
                    }
                    else if (oldCount == 0 && readyEndpointCount == 0)
                    {
                        _logger.LogInformation("We did not have ready endpoints and we still do not, not resyncing");
                    }
                    else
                    {
                        _logger.LogDebug("We had ready endpoints and we still do, not resyncing");
                    }

                    if (doSync)
                    {
                        _logger.LogInformation("EndpointSlice {@oldCount}->{@newCount} change requires resync",
                            oldCount, readyEndpointCount);

                        await _synchronizer.SynchronizeLocalEndpointSliceAsync(endpointSlice);
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
