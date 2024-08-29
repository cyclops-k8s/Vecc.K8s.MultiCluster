using k8s;
using k8s.Models;
using KubeOps.KubernetesClient;
using NewRelic.Api.Agent;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultServiceManager : IServiceManager
    {
        private readonly ILogger<DefaultServiceManager> _logger;
        private readonly IKubernetesClient _kubernetesClient;
        private readonly INamespaceManager _namespaceManager;

        public DefaultServiceManager(ILogger<DefaultServiceManager> logger, IKubernetesClient kubernetesClient, INamespaceManager namespaceManager)
        {
            _logger = logger;
            _kubernetesClient = kubernetesClient;
            _namespaceManager = namespaceManager;
        }

        [Trace]
        public async Task<IList<V1Service>> GetServicesAsync()
        {
            _logger.LogDebug("Getting services in the cluster");

            var result = await _kubernetesClient.ListAsync<V1Service>();
            _logger.LogDebug("Done getting services in {count}", result.Count);

            return result;
        }

        [Trace]
        public Task<List<V1Service>> GetLoadBalancerServicesAsync(IList<V1Service> services, IList<V1Endpoints> endpoints)
        {
            _logger.LogDebug("Finding valid load balancer services");
            var result = new List<V1Service>();
            var loadBalancerServices = services.Where(service => service.Spec.Type == "LoadBalancer").ToList();

            var namespacedEndpoints = endpoints.GroupBy(x => x.Metadata.NamespaceProperty).ToDictionary(x => x.Key, x => x.ToArray())!;
            var namespacedServices = loadBalancerServices.GroupBy(x => x.Metadata.NamespaceProperty).ToDictionary(x => x.Key, x => x.ToArray())!;

            foreach (var serviceEntry in namespacedServices)
            {
                _logger.LogDebug("Checking load balancer service namespace: {@namespace}", serviceEntry.Key);

                if (!namespacedEndpoints.TryGetValue(serviceEntry.Key, out var serviceEndpoints))
                {
                    foreach (var service in serviceEntry.Value)
                    {
                        _logger.LogDebug("Missing endpoints in namespace {@namespace} for {@service}", serviceEntry.Key, service.Name());
                    }
                }
                else
                {
                    foreach (var service in serviceEntry.Value)
                    {
                        using var scope = _logger.BeginScope("{@namespace}/{@service}", service.Namespace(), service.Name());

                        var serviceEndpoint = serviceEndpoints.SingleOrDefault(endpoint => endpoint.Name() == service.Name());
                        if (serviceEndpoint == null)
                        {
                            _logger.LogWarning("Missing endpoint in namespace {@namespace} for {@service}, skipping.", service.Namespace(), service.Name());
                            continue;
                        }

                        if (serviceEndpoint.Subsets == null)
                        {
                            _logger.LogWarning("Subsets missing in service endpoint {@namespace}/{@service}, skipping.", service.Namespace(), service.Name());
                            continue;
                        }

                        if (serviceEndpoint.Subsets.Count == 0)
                        {
                            _logger.LogWarning("Service has no available backend {@namespace}/{@service}, skipping.", service.Namespace(), service.Name());
                            continue;
                        }

                        _logger.LogDebug("Service has {@count} available backends", serviceEndpoint.Subsets.Count);
                        result.Add(service);
                    }
                }
            }

            return Task.FromResult(result);
        }

        [Trace]
        public async Task<IList<V1Endpoints>> GetEndpointsAsync()
        {
            _logger.LogDebug("Getting endpoints in the cluster");

            var result = await _kubernetesClient.ListAsync<V1Endpoints>();
            _logger.LogDebug("Done getting endpoints {count}", result.Count);

            return result;
        }
    }
}
