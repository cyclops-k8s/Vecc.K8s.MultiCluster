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
    //[EntityRbac(typeof(V1Ingress), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Watch)]
    //public class K8sIngressController : IResourceController<V1Ingress>
    //{
    //    private readonly ILogger<K8sIngressController> _logger;
    //    private readonly KubernetesClient _kubernetesClient;
    //    private readonly ICache _cache;
    //    private readonly IHostStateChangeNotifier _hostStateChangeNotifier;

    //    public K8sIngressController(ILogger<K8sIngressController> logger, KubernetesClient kubernetesClient, ICache cache, IHostStateChangeNotifier hostStateChangeNotifier)
    //    {
    //        _logger = logger;
    //        _kubernetesClient = kubernetesClient;
    //        _cache = cache;
    //        _hostStateChangeNotifier = hostStateChangeNotifier;
    //    }

    //    public async Task<ResourceControllerResult?> ReconcileAsync(V1Ingress ingress)
    //    {
    //        using var logScope = _logger.BeginScope("{@namespace}:{@ingress}", ingress.Namespace(), ingress.Name());
    //        if (!_leaderElection.IsLeader())
    //        {
    //            _logger.LogTrace("Not leader, not processing ingress");
    //            return null;
    //        }

    //        //we're the leader, do our thing during the reconcile phase.
    //        var serviceNames = GetServiceNames(ingress);
    //        var services = await GetServicesAsync(ingress, serviceNames);
    //        var hostnames = GetHostnames(ingress);

    //        if (serviceNames.Length != services.Length)
    //        {
    //            _logger.LogInformation("Ingress is missing service, marking failed. {@hostnames}", hostnames);
    //            foreach (var hostname in hostnames)
    //            {
    //                //TODO: cluster identifier configurable
    //                await _cache.SetHostStateAsync(hostname, "local", false, ingress);
    //            }
    //            return null;
    //        }

    //        var endpoints = await GetEndpointsAsync(services);

    //        if (endpoints.Length != serviceNames.Length)
    //        {
    //            _logger.LogInformation("At least one ingress services endpoints is down, marking failed. {@hostnames}", hostnames);
    //            foreach (var hostname in hostnames)
    //            {
    //                //TODO: cluster identifier configurable
    //                await _cache.SetHostStateAsync(hostname, "local", false, ingress);
    //            }
    //            return null;
    //        }

    //        //TODO: handle a valid ingress
    //        foreach (var hostname in hostnames)
    //        {
    //            //TODO: cluster identifier configurable
    //            await _cache.SetHostStateAsync(hostname, "local", true, ingress);
    //        }
    //        return null;
    //    }

    //    private string[] GetServiceNames(V1Ingress ingress)
    //    {
    //        if (ingress.Status?.LoadBalancer?.Ingress == null ||
    //            ingress.Status.LoadBalancer.Ingress.Count == 0)
    //        {
    //            // ingress isn't enabled by the ingress controller, bail
    //            return Array.Empty<string>();
    //        }

    //        if (ingress.Spec?.Rules == null ||
    //            ingress.Spec.Rules.Count == 0)
    //        {
    //            // no rules, means no services
    //            return Array.Empty<string>();
    //        }

    //        var serviceNames = ingress.Spec.Rules
    //            .Where((rule) => rule != null)
    //            .SelectMany((rule) =>
    //            {
    //                return rule.Http.Paths.Select((path) => path?.Backend?.Service?.Name).ToList();
    //            })
    //            .Distinct()
    //            .Where((name) => !string.IsNullOrWhiteSpace(name))
    //            .Select((name) => name!)!;

    //        var hostNames = GetHostnames(ingress);
    //        foreach (var hostname in hostNames)
    //        {
    //            //TODO: handle hostname being up
    //        }

    //        return serviceNames.ToArray();
    //    }

    //    private async Task<V1Service[]> GetServicesAsync(V1Ingress ingress, string[] serviceNames)
    //    {
    //        var services = new List<V1Service>();
    //        var ingressNamespace = ingress.Namespace();
    //        foreach (var serviceName in serviceNames)
    //        {
    //            var service = await _kubernetesClient.Get<V1Service>(serviceName, ingressNamespace);
    //            services.Add(service);
    //        }

    //        return services.ToArray();
    //    }

    //    private async Task<V1Endpoints[]> GetEndpointsAsync(V1Service[] services)
    //    {
    //        var endpoints = new List<V1Endpoints>();

    //        foreach (var service in services)
    //        {
    //            var endpointObject = await _kubernetesClient.Get<V1Endpoints>(service.Name(), service.Namespace());
    //            if (endpointObject?.Subsets == null ||
    //                endpointObject.Subsets.Count == 0 ||
    //                    endpointObject.Subsets.Any((subset) => subset.Addresses.Count == 0 || subset.Addresses.Any((address) => string.IsNullOrWhiteSpace(address.Ip))))
    //            {
    //                //no valid endpoints, don't add it
    //                continue;
    //            }
    //            endpoints.Add(endpointObject);
    //        }

    //        return endpoints.ToArray();
    //    }

    //    private string[] GetHostnames(V1Ingress ingress)
    //    {
    //        if (ingress.Status?.LoadBalancer?.Ingress == null ||
    //            ingress.Status.LoadBalancer.Ingress.Count == 0)
    //        {
    //            // ingress isn't enabled by the ingress controller, bail
    //            return Array.Empty<string>();
    //        }

    //        if (ingress.Spec?.Rules == null ||
    //            ingress.Spec.Rules.Count == 0)
    //        {
    //            // no rules, means no services
    //            return Array.Empty<string>();
    //        }

    //        var hostNames = ingress.Spec.Rules
    //            .Select((rule) => rule.Host)
    //            .Distinct();

    //        return hostNames!.ToArray();
    //    }
    //}
}
