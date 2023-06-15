using k8s;
using k8s.Models;
using KubeOps.KubernetesClient;

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

        public async Task<List<V1Service>> GetServicesAsync(string? ns)
        {
            if (string.IsNullOrWhiteSpace(ns))
            {
                _logger.LogInformation("{@namespace} is empty. We will get service objects from all namespaces", ns);
                return await GetServicesAsync();
            }

            var result = new List<V1Service>();

            
            _logger.LogInformation("Getting services in {@namespace}", ns);
            var k8sServices = await _kubernetesClient.List<V1Service>(ns);
            _logger.LogInformation("Done getting services in {@namespace}", ns);

            result.AddRange(k8sServices);

            return result;
        }

        public Task<List<V1Service>> GetLoadBalancerEndpointsAsync(IList<V1Service> services)
        {
            _logger.LogInformation("Finding load balancer services");
            var result = services.Where(service => service.Spec.Type == "LoadBalancer").ToList();

            _logger.LogInformation("Done");
            return Task.FromResult(result);
        }

        public async Task<Dictionary<string, IList<V1Service>>> GetAvailableHostnamesAsync(IList<V1Service> allServices, IList<V1Endpoints> allEndpoints)
        {
            var result = new Dictionary<string, IList<V1Service>>();

            var namespacedEndpoints = allEndpoints.GroupBy(x => x.Metadata.NamespaceProperty).ToDictionary(x => x.Key, x => x.ToArray())!;
            var namespacedServices = allServices.GroupBy(x => x.Metadata.NamespaceProperty).ToDictionary(x => x.Key, x => x.ToArray())!;

            foreach (var serviceEntry in namespacedServices)
            {
                _logger.LogInformation("Checking load balancer service namespace: {@namespace}", serviceEntry.Key);

                if (!namespacedEndpoints.TryGetValue(serviceEntry.Key, out var endpoints))
                {
                    foreach (var service in serviceEntry.Value)
                    {
                        _logger.LogInformation("Missing endpoints in namespace {@namespace} for {@service}", serviceEntry.Key, service.Name());
                    }
                }
                else
                {
                    foreach (var service in serviceEntry.Value)
                    {
                        using var scope = _logger.BeginScope("{@namespace}/{@service}", service.Namespace(), service.Name());
                        var hostname = service.GetAnnotation("multicluster.veccsolutions.com/hostname");
                        if (string.IsNullOrWhiteSpace(hostname))
                        {
                            _logger.LogInformation("Service {@service} does not have the multicluster.veccsolutions.com/hostname annotation, skipping.", service.Name());
                            continue;
                        }

                        var serviceEndpoint = endpoints.SingleOrDefault(endpoint => endpoint.Name() == service.Name());
                        if (serviceEndpoint == null)
                        {
                            _logger.LogWarning("Missing endpoint in namespace {@namespace} for {@service}", service.Namespace(), service.Name());
                            continue;
                        }

                        if (serviceEndpoint.Subsets == null)
                        {
                            _logger.LogWarning("Subsets missing in service endpoint {@namespace}/{@service}, skipping {@hostname}", service.Namespace(), service.Name(), hostname);
                            continue;
                        }

                        if (serviceEndpoint.Subsets.Count == 0)
                        {
                            _logger.LogWarning("Service has no available backend {@namespace}/{@service}, skipping {@hostname}", service.Namespace(), service.Name(), hostname);
                            continue;
                        }

                        if (!result.ContainsKey(hostname))
                        {
                            result.Add(hostname, new List<V1Service>());
                        }

                        _logger.LogInformation("Adding service to {@hostname}", hostname);
                        result[hostname].Add(service);
                    }
                }
            }

            return result;
        }

        public async Task<List<V1Endpoints>> GetEndpointsAsync(string? ns)
        {
            if (string.IsNullOrWhiteSpace(ns))
            {
                _logger.LogInformation("{@namespace} is empty, getting endpoint objects from all namespaces", ns);
                return await GetEndpointsAsync();
            }

            var result = new List<V1Endpoints>();

            _logger.LogInformation("Getting endpoints in {@namespace}", ns);
            var endpoints = await _kubernetesClient.List<V1Endpoints>(ns);
            _logger.LogInformation("Done getting endpoints in {@namespace}", ns);

            result.AddRange(endpoints);

            return result;
        }

        private async Task<List<V1Service>> GetServicesAsync()
        {
            var namespaces = await _namespaceManager.GetNamsepacesAsync();
            var result = new List<V1Service>();

            var tasks = namespaces.Select(space => Task.Run(async () =>
            {
                var services = await GetServicesAsync(space.Name());
                return services;
            }));

            await Task.WhenAll(tasks);

            foreach (var task in tasks)
            {
                result.AddRange(task.Result);
            }

            return result;
        }

        private async Task<List<V1Endpoints>> GetEndpointsAsync()
        {
            var namespaces = await _namespaceManager.GetNamsepacesAsync();
            var result = new List<V1Endpoints>();

            var tasks = namespaces.Select(space => Task.Run(async () =>
            {
                var endpoints = await GetEndpointsAsync(space.Name());
                return endpoints;
            }));

            await Task.WhenAll(tasks);

            foreach (var task in tasks)
            {
                result.AddRange(task.Result);
            }

            return result;
        }

    }
}
