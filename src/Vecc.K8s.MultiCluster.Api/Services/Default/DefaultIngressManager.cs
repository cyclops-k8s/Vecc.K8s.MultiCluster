using IdentityModel.OidcClient;
using k8s.Models;
using KubeOps.KubernetesClient;
using Namotion.Reflection;
using System.Data;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultIngressManager : IIngressManager
    {
        private readonly ILogger<DefaultIngressManager> _logger;
        private readonly IKubernetesClient _kubernetesClient;
        private readonly INamespaceManager _namespaceManager;
        private readonly IServiceManager _serviceManager;

        public DefaultIngressManager(ILogger<DefaultIngressManager> logger, IKubernetesClient kubernetesClient, INamespaceManager namespaceManager, IServiceManager serviceManager)
        {
            _logger = logger;
            _kubernetesClient = kubernetesClient;
            _namespaceManager = namespaceManager;
            _serviceManager = serviceManager;
        }

        public async Task<IList<V1Ingress>> GetAllIngressesAsync(string? ns)
        {
            var result = new List<V1Ingress>();

            if (string.IsNullOrWhiteSpace(ns))
            {
                _logger.LogInformation("{@namespace} is empty, getting all ingress objects from all namespaces", ns);
                return await GetAllIngressesAsync();
            }

            _logger.LogInformation("Getting ingress objects in {@namespace}", ns);
            var ingresses = await _kubernetesClient.List<V1Ingress>(ns);
            _logger.LogInformation("Done getting ingress objects in {@namespace}", ns);

            result.AddRange(ingresses);

            return result;
        }

        public async Task<IList<V1Ingress>> GetValidIngressesAsync(string? ns)
        {
            var result = new List<V1Ingress>();

            if (string.IsNullOrWhiteSpace(ns))
            {
                _logger.LogInformation("{@namespace} is empty, getting all valid ingress objects from all namespaces", ns);
                return await GetAllValidIngressesAsync();
            }

            _logger.LogInformation("Getting ingress objects in {@namespace}", ns);
            var k8sIngresses = await _kubernetesClient.List<V1Ingress>(ns);

            _logger.LogInformation("Getting service objects in {@namespace}", ns);
            var k8sServices = await _serviceManager.GetServicesAsync(ns);

            _logger.LogInformation("Getting endpoints in {@namespace}", ns);
            var k8sServiceEndpoints = await _serviceManager.GetEndpointsAsync(ns);
            _logger.LogInformation("Done getting Kubernetes objects");

            _logger.LogInformation("Finding ingresses with the loadbalancer set");
            foreach (var ingress in k8sIngresses)
            {
                using var ingressScope = _logger.BeginScope("{@namespace}/{@ingress}", ingress.Namespace(), ingress.Name());
                _logger.LogInformation("Got ingress");

                if (IsIngressValid(ingress, k8sServices, k8sServiceEndpoints))
                {
                    result.Add(ingress);
                }
                else
                {
                    _logger.LogWarning("Ingress is invalid");
                }
            }

            return result;
        }

        public async Task<Dictionary<string, IList<V1Ingress>>> GetAvailableHostnamesAsync()
        {
            var result = new Dictionary<string, IList<V1Ingress>>();
            var hostnameValidIngresses = new Dictionary<string, IList<V1Ingress>>();
            var hostnameInvalidIngresses = new Dictionary<string, IList<V1Ingress>>();

            _logger.LogInformation("Getting all ingresses");
            var allIngresses = await GetAllIngressesAsync();

            _logger.LogInformation("Getting all endpoints");
            var allEndpoints = await _serviceManager.GetEndpointsAsync(null);

            _logger.LogInformation("Getting all services");
            var allServices = await _serviceManager.GetServicesAsync(null);
            _logger.LogInformation("Done getting Kubernetes objects");

            var namespacedIngresses = allIngresses.GroupBy(x => x.Metadata.NamespaceProperty).ToDictionary(x => x.Key, x => x.ToArray())!;
            var namespacedEndpoints = allEndpoints.GroupBy(x => x.Metadata.NamespaceProperty).ToDictionary(x => x.Key, x => x.ToArray())!;
            var namespacedServices = allServices.GroupBy(x => x.Metadata.NamespaceProperty).ToDictionary(x => x.Key, x => x.ToArray())!;

            foreach (var ingressNamespace in namespacedIngresses)
            {
                var valid = true;
                if (!namespacedEndpoints.TryGetValue(ingressNamespace.Key, out var endpoints))
                {
                    valid = false;
                    _logger.LogInformation("No endpoints in namespace {@namespace}", ingressNamespace.Key);
                }

                if (!namespacedServices.TryGetValue(ingressNamespace.Key, out var services))
                {
                    valid = false;
                    _logger.LogInformation("No services in namespace {@namespace}", ingressNamespace.Key);
                }

                foreach (var ingress in ingressNamespace.Value)
                {
                    if (valid && IsIngressValid(ingress, services!, endpoints!))
                    {
                        _logger.LogInformation("Ingress is valid, marking all hostnames as good");
                        foreach (var rule in ingress.Spec.Rules)
                        {
                            hostnameValidIngresses.TryAdd(rule.Host, new List<V1Ingress>());
                            hostnameValidIngresses[rule.Host].Add(ingress);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Ingress is invalid, marking all hostnames as bad");
                        foreach (var rule in ingress.Spec.Rules)
                        {
                            if (string.IsNullOrWhiteSpace(rule.Host))
                            {
                                _logger.LogInformation("Ignoring empty host rule {@namespace}/{@ingress}", ingress.Namespace(), ingress.Name());
                                continue;
                            }

                            hostnameInvalidIngresses.TryAdd(rule.Host, new List<V1Ingress>());
                            hostnameInvalidIngresses[rule.Host].Add(ingress);
                        }
                    }
                }
            }

            foreach (var ingress in hostnameValidIngresses)
            {
                if (hostnameInvalidIngresses.ContainsKey(ingress.Key))
                {
                    _logger.LogWarning("Hostname was marked as invalid {@hostname}", ingress.Key);
                    continue;
                }

                _logger.LogInformation("Hostname is marked as valid {@hostname}", ingress.Key);
                result.Add(ingress.Key, ingress.Value);
            }

            foreach (var ingress in hostnameInvalidIngresses)
            {
                if (!hostnameValidIngresses.ContainsKey(ingress.Key))
                {
                    _logger.LogWarning("Hostname was marked as invalid {@hostname}", ingress.Key);
                }
            }

            return result;
        }

        public bool IsIngressValid(V1Ingress ingress, IList<V1Service> services, IList<V1Endpoints> endpoints)
        {
            if (ingress.Status == null)
            {
                _logger.LogWarning("Ingress status is null");
                return false;
            }

            if (ingress.Status.LoadBalancer == null)
            {
                _logger.LogWarning("Ingress status.loadbalancer is null");
                return false;
            }

            if (ingress.Status.LoadBalancer.Ingress == null)
            {
                _logger.LogWarning("Ingress status.loadbalancer.ingress is null");
                return false;
            }

            if (ingress.Spec == null)
            {
                _logger.LogWarning("Ingress spec is null");
                return false;
            }

            if (ingress.Spec.Rules == null)
            {
                _logger.LogWarning("Ingress spec.rules is null");
                return false;
            }

            if (ingress.Spec.Rules.Count == 0)
            {
                _logger.LogWarning("Ingress spec.rules is empty");
                return false;
            }

            if (!ingress.Status.LoadBalancer.Ingress.Any())
            {
                _logger.LogWarning("Ingress is not exposed by an ingress controller");
                return false;
            }

            _logger.LogInformation("Ingress is exposed");
            var validRules = GetValidIngressRules(ingress.Spec.Rules);
            var hasValidRule = validRules.Count > 0;

            if (!hasValidRule)
            {
                _logger.LogInformation("Ingress doesn't have a valid rule");
                return false;
            }

            if (!hasValidRule)
            {
                _logger.LogWarning("Does not have a valid rule");
                return false;
            }

            var result = false;
            _logger.LogDebug("Ingress is valid");

            var rules = GetValidIngressRules(ingress.Spec.Rules);

            if (rules.Count != ingress.Spec.Rules.Count)
            {
                _logger.LogWarning("At least one rule is invalid");
                return false;
            }

            foreach (var rule in ingress.Spec.Rules)
            {
                foreach (var path in rule.Http.Paths)
                {
                    var service = services.SingleOrDefault(x => x.Metadata.Name == path.Backend.Service.Name);
                    if (service == null)
                    {
                        _logger.LogWarning("Missing service {@service}", path.Backend.Service.Name);
                        continue;
                    }

                    var endpoint = endpoints.SingleOrDefault(x => x.Metadata.Name == service.Metadata.Name);
                    if (endpoint == null)
                    {
                        _logger.LogWarning("Missing endpoints {@service}", service.Name());
                        continue;
                    }

                    if (endpoint.Subsets == null)
                    {
                        _logger.LogWarning("Missing subsets in endpoint {@endpoint}", endpoint.Name());
                        continue;
                    }

                    if (endpoint.Subsets.Count > 0)
                    {
                        _logger.LogInformation("Service {@service} is available", service.Name());
                        result = true;
                    }
                }
            }

            return result;
        }

        private bool IsBackendValid(V1HTTPIngressPath path)
        {
            if (path.Backend == null)
            {
                _logger.LogWarning("Ingress has a null rule.http.paths.backend");
                return false;
            }

            if (path.Backend.Service == null)
            {
                if (path.Backend.Resource == null)
                {
                    _logger.LogWarning("Ingress has a null service and resource");
                    return false;
                }

                _logger.LogWarning("Ingress has a null service but has a resource defined. I can't support that right now.");
                return false;
            }

            if (path.Backend.Service.Name == null)
            {
                _logger.LogWarning("Ingress has a null backend service name");
                return false;
            }

            return true;
        }

        private List<V1IngressRule> GetValidIngressRules(IList<V1IngressRule> rules)
        {
            var result = new List<V1IngressRule>();

            foreach (var rule in rules)
            {
                var hasValidPath = false;

                if (rule == null)
                {
                    _logger.LogWarning("Ingress has a null rule");
                    continue;
                }

                if (rule.Http == null)
                {
                    _logger.LogWarning("Ingress has a null rule.http");
                    continue;
                }

                if (rule.Http.Paths == null)
                {
                    _logger.LogWarning("Ingress has null rule.http.paths");
                    continue;
                }

                if (rule.Http.Paths.Count == 0)
                {
                    _logger.LogWarning("Ingress has an empty rule.http.paths");
                    continue;
                }

                foreach (var path in rule.Http.Paths)
                {
                    hasValidPath = hasValidPath || IsBackendValid(path);
                }

                if (hasValidPath)
                {
                    result.Add(rule);
                }
            }

            return result;

        }

        private async Task<IList<V1Ingress>> GetAllIngressesAsync()
        {
            var namespaces = await _namespaceManager.GetNamsepacesAsync();
            var result = new List<V1Ingress>();

            var tasks = namespaces.Select(space => Task.Run(async () =>
            {
                var ingresses = await GetAllIngressesAsync(space.Name());
                return ingresses;
            }));

            await Task.WhenAll(tasks);

            foreach (var task in tasks)
            {
                result.AddRange(task.Result);
            }

            return result;
        }

        private async Task<IList<V1Ingress>> GetAllValidIngressesAsync()
        {
            var namespaces = await _namespaceManager.GetNamsepacesAsync();
            var result = new List<V1Ingress>();

            var tasks = namespaces.Select(space => Task.Run(async () =>
            {
                var ingresses = await GetValidIngressesAsync(space.Name());
                return ingresses;
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
