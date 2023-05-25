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
    public class K8sIngressController : IResourceController<V1Ingress>
    {
        private readonly ILogger<K8sIngressController> _logger;
        private readonly LeaderElector _leaderElection;
        private readonly KubernetesClient _kubernetesClient;
        private readonly ICache _cache;
        private readonly IHostStateChangeNotifier _hostStateChangeNotifier;

        public K8sIngressController(ILogger<K8sIngressController> logger, LeaderElector leaderElection, KubernetesClient kubernetesClient, ICache cache, IHostStateChangeNotifier hostStateChangeNotifier)
        {
            _logger = logger;
            _leaderElection = leaderElection;
            _kubernetesClient = kubernetesClient;
            _cache = cache;
            _hostStateChangeNotifier = hostStateChangeNotifier;
        }

        public async Task<ResourceControllerResult?> ReconcileAsync(V1Ingress ingress)
        {
            using var logScope = _logger.BeginScope("{@namespace}:{@ingress}", ingress.Namespace(), ingress.Name());
            if (!_leaderElection.IsLeader())
            {
                _logger.LogTrace("Not leader, not processing ingress");
                return null;
            }

            //we're the leader, do our thing during the reconcile phase.
            var serviceNames = GetServiceNames(ingress);
            var services = await GetServicesAsync(ingress, serviceNames);
            var hostnames = GetHostnames(ingress);

            if (serviceNames.Length != services.Length)
            {
                _logger.LogInformation("Ingress is missing service, marking failed. {@hostnames}", hostnames);
                foreach (var hostname in hostnames)
                {
                    //TODO: cluster identifier configurable
                    await _cache.SetHostStateAsync(hostname, "local", false, ingress);
                }
                return null;
            }

            var endpoints = await GetEndpointsAsync(services);

            if (endpoints.Length != serviceNames.Length)
            {
                _logger.LogInformation("At least one ingress services endpoints is down, marking failed. {@hostnames}", hostnames);
                foreach (var hostname in hostnames)
                {
                    //TODO: cluster identifier configurable
                    await _cache.SetHostStateAsync(hostname, "local", false, ingress);
                }
                return null;
            }

            //TODO: handle a valid ingress
            foreach (var hostname in hostnames)
            {
                //TODO: cluster identifier configurable
                await _cache.SetHostStateAsync(hostname, "local", true, ingress);
            }
            return null;
        }

        private string[] GetServiceNames(V1Ingress ingress)
        {
            if (ingress.Status?.LoadBalancer?.Ingress == null ||
                ingress.Status.LoadBalancer.Ingress.Count == 0)
            {
                // ingress isn't enabled by the ingress controller, bail
                return Array.Empty<string>();
            }

            if (ingress.Spec?.Rules == null ||
                ingress.Spec.Rules.Count == 0)
            {
                // no rules, means no services
                return Array.Empty<string>();
            }

            var serviceNames = ingress.Spec.Rules
                .Where((rule) => rule != null)
                .SelectMany((rule) =>
                {
                    return rule.Http.Paths.Select((path) => path?.Backend?.Service?.Name).ToList();
                })
                .Distinct()
                .Where((name) => !string.IsNullOrWhiteSpace(name))
                .Select((name) => name!)!;

            var hostNames = GetHostnames(ingress);
            foreach (var hostname in hostNames)
            {
                //TODO: handle hostname being up
            }

            return serviceNames.ToArray();
        }

        private async Task<V1Service[]> GetServicesAsync(V1Ingress ingress, string[] serviceNames)
        {
            var services = new List<V1Service>();
            var ingressNamespace = ingress.Namespace();
            foreach (var serviceName in serviceNames)
            {
                var service = await _kubernetesClient.Get<V1Service>(serviceName, ingressNamespace);
                services.Add(service);
            }

            return services.ToArray();
        }

        private async Task<V1Endpoints[]> GetEndpointsAsync(V1Service[] services)
        {
            var endpoints = new List<V1Endpoints>();

            foreach (var service in services)
            {
                var endpointObject = await _kubernetesClient.Get<V1Endpoints>(service.Name(), service.Namespace());
                if (endpointObject?.Subsets == null ||
                    endpointObject.Subsets.Count == 0 ||
                        endpointObject.Subsets.Any((subset) => subset.Addresses.Count == 0 || subset.Addresses.Any((address) => string.IsNullOrWhiteSpace(address.Ip))))
                {
                    //no valid endpoints, don't add it
                    continue;
                }
                endpoints.Add(endpointObject);
            }

            return endpoints.ToArray();
        }

        private string[] GetHostnames(V1Ingress ingress)
        {
            if (ingress.Status?.LoadBalancer?.Ingress == null ||
                ingress.Status.LoadBalancer.Ingress.Count == 0)
            {
                // ingress isn't enabled by the ingress controller, bail
                return Array.Empty<string>();
            }

            if (ingress.Spec?.Rules == null ||
                ingress.Spec.Rules.Count == 0)
            {
                // no rules, means no services
                return Array.Empty<string>();
            }

            var hostNames = ingress.Spec.Rules
                .Select((rule) => rule.Host)
                .Distinct();

            return hostNames!.ToArray();
        }
    }
}
