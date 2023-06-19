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
    public class K8sIngressController : IResourceController<V1Ingress>, IResourceController<V1Service>, IResourceController<V1Endpoints>
    {
        private readonly ILogger<K8sIngressController> _logger;
        private readonly ICache _cache;
        private readonly IHostnameSynchronizer _synchronizer;

        public K8sIngressController(ILogger<K8sIngressController> logger, ICache cache, IHostnameSynchronizer synchronizer)
        {
            _logger = logger;
            _cache = cache;
            _synchronizer = synchronizer;
        }

        public async Task DeletedAsync(V1Ingress ingress)
        {
            _logger.LogInformation("Ingress {@namespace}/{@ingress} deleted", ingress.Namespace(), ingress.Name());
            await _synchronizer.SynchronizeLocalIngressAsync(ingress);
        }

        public async Task<ResourceControllerResult?> ReconcileAsync(V1Ingress ingress)
        {
            if (await _cache.IsServiceMonitoredAsync(ingress.Namespace(), ingress.Name()))
            {
                var oldResourceVersion = await _cache.GetLastResourceVersionAsync(ingress.Metadata.Uid);
                if (oldResourceVersion != ingress.Metadata.ResourceVersion)
                {
                    _logger.LogInformation("Ingress {@namespace}/{@ingress} updated", ingress.Namespace(), ingress.Name());
                    await _synchronizer.SynchronizeLocalIngressAsync(ingress);
                }
            }

            return null;
        }

        public async Task StatusModifiedAsync(V1Ingress ingress)
        {
            var oldResourceVersion = await _cache.GetLastResourceVersionAsync(ingress.Metadata.Uid);
            if (oldResourceVersion != ingress.Metadata.ResourceVersion)
            {
                _logger.LogInformation("Ingress {@namespace}/{@ingress} state changed", ingress.Namespace(), ingress.Name());
                await _synchronizer.SynchronizeLocalIngressAsync(ingress);
            }
        }

        public async Task DeletedAsync(V1Service service)
        {
            if (await _cache.IsServiceMonitoredAsync(service.Namespace(), service.Name()))
            {
                _logger.LogInformation("Service {@namespace}/{@service} deleted", service.Namespace(), service.Name());
                await _synchronizer.SynchronizeLocalServiceAsync(service);
            }
            else
            {
                _logger.LogInformation("Service {@namepace}/{@service} is not tracked, ignoring.", service.Namespace(), service.Name());
            }
        }

        public async Task<ResourceControllerResult?> ReconcileAsync(V1Service service)
        {
            if (await _cache.IsServiceMonitoredAsync(service.Namespace(), service.Name()))
            {
                var oldResourceVersion = await _cache.GetLastResourceVersionAsync(service.Metadata.Uid);
                if (oldResourceVersion != service.Metadata.ResourceVersion)
                {
                    _logger.LogInformation("Service {@namespace}/{@service} updated", service.Namespace(), service.Name());
                    await _synchronizer.SynchronizeLocalServiceAsync(service);
                }
            }

            return null;
        }

        public async Task StatusModifiedAsync(V1Service service)
        {
            if (await _cache.IsServiceMonitoredAsync(service.Namespace(), service.Name()))
            {
                var oldResourceVersion = await _cache.GetLastResourceVersionAsync(service.Metadata.Uid);
                if (oldResourceVersion != service.Metadata.ResourceVersion)
                {
                    _logger.LogInformation("Service {@namespace}/{@service} state changed", service.Namespace(), service.Name());
                    await _synchronizer.SynchronizeLocalServiceAsync(service);
                }
            }
            else
            {
                _logger.LogInformation("Service {@namepace}/{@service} is not tracked, ignoring.", service.Namespace(), service.Name());
            }
        }


        public async Task DeletedAsync(V1Endpoints endpoints)
        {
            if (await _cache.IsServiceMonitoredAsync(endpoints.Namespace(), endpoints.Name()))
            {
                _logger.LogInformation("Endpoints {@namespace}/{@endpoints} deleted", endpoints.Namespace(), endpoints.Name());
                await _synchronizer.SynchronizeLocalEndpointsAsync(endpoints);
            }
            else
            {
                _logger.LogInformation("Endpoints {@namepace}/{@endpoints} is not tracked, ignoring.", endpoints.Namespace(), endpoints.Name());
            }
        }

        public async Task<ResourceControllerResult?> ReconcileAsync(V1Endpoints endpoints)
        {
            if (await _cache.IsServiceMonitoredAsync(endpoints.Namespace(), endpoints.Name()))
            {
                var oldResourceVersion = await _cache.GetLastResourceVersionAsync(endpoints.Metadata.Uid);
                if (oldResourceVersion != endpoints.Metadata.ResourceVersion)
                {
                    _logger.LogInformation("Endpoints {@namespace}/{@endpoints} updated", endpoints.Namespace(), endpoints.Name());
                    await _synchronizer.SynchronizeLocalEndpointsAsync(endpoints);
                }
            }

            return null;
        }

        public async Task StatusModifiedAsync(V1Endpoints endpoints)
        {
            if (await _cache.IsServiceMonitoredAsync(endpoints.Namespace(), endpoints.Name()))
            {
                var oldResourceVersion = await _cache.GetLastResourceVersionAsync(endpoints.Metadata.Uid);
                if (oldResourceVersion != endpoints.Metadata.ResourceVersion)
                {
                    _logger.LogInformation("Endpoints {@namespace}/{@endpoints} state changed", endpoints.Namespace(), endpoints.Name());
                    await _synchronizer.SynchronizeLocalEndpointsAsync(endpoints);
                }
            }
            else
            {
                _logger.LogInformation("Endpoints {@namepace}/{@endpoints} is not tracked, ignoring.", endpoints.Namespace(), endpoints.Name());
            }
        }
    }
}
