using k8s;
using k8s.Models;
using KubeOps.KubernetesClient;
using NewRelic.Api.Agent;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultServiceManager : IServiceManager
    {
        private const string _serviceNameLabel = "kubernetes.io/service-name";
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
        public Task<List<V1Service>> GetLoadBalancerServicesAsync(IList<V1Service> services, IList<V1EndpointSlice> endpointSlices)
        {
            _logger.LogDebug("Finding valid load balancer services");
            var result = new List<V1Service>();
            var loadBalancerServices = services.Where(service => service.Spec.Type == "LoadBalancer").ToList();

            // Group endpoint slices by namespace and service name (from label)
            var namespacedEndpointSlices = endpointSlices
                .GroupBy(x => x.Metadata.NamespaceProperty)
                .ToDictionary(x => x.Key!, x => x
                    .GroupBy(s => s.GetLabel(_serviceNameLabel) ?? "")
                    .ToDictionary(s => s.Key, s => s.ToList()));
            var namespacedServices = loadBalancerServices.GroupBy(x => x.Metadata.NamespaceProperty).ToDictionary(x => x.Key!, x => x.ToArray());

            foreach (var serviceEntry in namespacedServices)
            {
                _logger.LogDebug("Checking load balancer service namespace: {@namespace}", serviceEntry.Key);

                if (!namespacedEndpointSlices.TryGetValue(serviceEntry.Key, out var namespaceSlices))
                {
                    foreach (var service in serviceEntry.Value)
                    {
                        _logger.LogDebug("Missing endpoint slices in namespace {@namespace} for {@service}", serviceEntry.Key, service.Name());
                    }
                }
                else
                {
                    foreach (var service in serviceEntry.Value)
                    {
                        using var scope = _logger.BeginScope("{@namespace}/{@service}", service.Namespace(), service.Name());

                        if (!namespaceSlices.TryGetValue(service.Name(), out var serviceSlices) || serviceSlices.Count == 0)
                        {
                            _logger.LogWarning("Missing endpoint slice in namespace {@namespace} for {@service}, skipping.", service.Namespace(), service.Name());
                            continue;
                        }

                        var readyEndpointCount = GetReadyEndpointCount(serviceSlices);
                        if (readyEndpointCount == 0)
                        {
                            _logger.LogWarning("Service has no available backend {@namespace}/{@service}, skipping.", service.Namespace(), service.Name());
                            continue;
                        }

                        _logger.LogDebug("Service has {@count} available backends", readyEndpointCount);
                        result.Add(service);
                    }
                }
            }

            return Task.FromResult(result);
        }

        [Trace]
        public async Task<IList<V1EndpointSlice>> GetEndpointSlicesAsync()
        {
            _logger.LogDebug("Getting endpoint slices in the cluster");

            var result = await _kubernetesClient.ListAsync<V1EndpointSlice>();
            _logger.LogDebug("Done getting endpoint slices {count}", result.Count);

            return result;
        }

        /// <inheritdoc/>
        public int GetReadyEndpointCount(IEnumerable<V1EndpointSlice> slices)
        {
            return slices
                .SelectMany(s => s.Endpoints ?? Enumerable.Empty<V1Endpoint>())
                .Count(e => e.Conditions?.Ready == true);
        }
    }
}
