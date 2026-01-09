using k8s.Models;
using KubeOps.KubernetesClient;
using NewRelic.Api.Agent;
using System.Data;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultIngressManager : IIngressManager
    {
        private const string ServiceNameLabel = "kubernetes.io/service-name";
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

        [Trace]
        public async Task<IList<V1Ingress>> GetIngressesAsync()
        {   _logger.LogDebug("Getting ingress objects in the cluster");

            var result = await _kubernetesClient.ListAsync<V1Ingress>();
            _logger.LogDebug("Done getting ingress objects: {count}", result.Count);

            return result;
        }

        [Trace]
        public Task<Dictionary<string, IList<V1Ingress>>> GetAvailableHostnamesAsync(
            IList<V1Ingress> allIngresses,
            IList<V1Service> allServices,
            IList<V1EndpointSlice> allEndpointSlices)
        {
            var result = new Dictionary<string, IList<V1Ingress>>();
            var hostnameValidIngresses = new Dictionary<string, IList<V1Ingress>>();
            var hostnameInvalidIngresses = new Dictionary<string, IList<V1Ingress>>();

            var namespacedIngresses = allIngresses.GroupBy(x => x.Metadata.NamespaceProperty).ToDictionary(x => x.Key!, x => x.ToArray());
            // Group endpoint slices by namespace and service name
            var namespacedEndpointSlices = allEndpointSlices
                .GroupBy(x => x.Metadata.NamespaceProperty)
                .ToDictionary(x => x.Key!, x => x
                    .GroupBy(s => s.GetLabel(ServiceNameLabel) ?? "")
                    .ToDictionary(s => s.Key, s => s.ToList()));
            var namespacedServices = allServices.GroupBy(x => x.Metadata.NamespaceProperty).ToDictionary(x => x.Key!, x => x.ToArray());

            foreach (var ingressNamespace in namespacedIngresses)
            {
                using var namespaceScope = _logger.BeginScope("{@namespace}", ingressNamespace.Key);
                var valid = true;
                if (!namespacedEndpointSlices.TryGetValue(ingressNamespace.Key, out var endpointSlicesByService))
                {
                    valid = false;
                    _logger.LogDebug("No endpoint slices in namespace");
                }

                if (!namespacedServices.TryGetValue(ingressNamespace.Key, out var services))
                {
                    valid = false;
                    _logger.LogDebug("No services in namespace");
                }

                foreach (var ingress in ingressNamespace.Value)
                {
                    using var ingressScope = _logger.BeginScope("{ingress}", ingress.Name());
                    if (valid && IsIngressValid(ingress, services!, endpointSlicesByService!))
                    {
                        _logger.LogDebug("Ingress is valid, marking all hostnames as good");
                        foreach (var rule in ingress.Spec.Rules)
                        {
                            hostnameValidIngresses.TryAdd(rule.Host, new List<V1Ingress>());
                            hostnameValidIngresses[rule.Host].Add(ingress);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Ingress is invalid, marking all hostnames as bad");
                        foreach (var rule in ingress.Spec.Rules)
                        {
                            if (string.IsNullOrWhiteSpace(rule.Host))
                            {
                                _logger.LogWarning("Ignoring empty host rule {@namespace}/{@ingress}", ingress.Namespace(), ingress.Name());
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
                using var ingressScope = _logger.BeginScope("{@hostname}", ingress.Key);
                if (hostnameInvalidIngresses.ContainsKey(ingress.Key))
                {
                    _logger.LogWarning("Hostname was marked as invalid");
                    continue;
                }

                _logger.LogDebug("Hostname is marked as valid");
                result.Add(ingress.Key, ingress.Value);
            }

            return Task.FromResult(result);
        }

        [Trace]
        public bool IsIngressValid(V1Ingress ingress, IList<V1Service> services, IList<V1EndpointSlice> endpointSlices)
        {
            // Convert list to dictionary by service name for efficient lookup
            var endpointSlicesByService = endpointSlices
                .GroupBy(s => s.GetLabel(ServiceNameLabel) ?? "")
                .ToDictionary(s => s.Key, s => s.ToList());
            return IsIngressValid(ingress, services, endpointSlicesByService);
        }

        private bool IsIngressValid(V1Ingress ingress, IList<V1Service> services, Dictionary<string, List<V1EndpointSlice>> endpointSlicesByService)
        {
            using var _ingressScope = _logger.BeginScope("{namespace}/{ingress}", ingress.Namespace(), ingress.Name());

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

            _logger.LogDebug("Ingress is exposed");
            var validRules = GetValidIngressRules(ingress.Spec.Rules);
            var hasValidRule = validRules.Count > 0;

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

                    if (!endpointSlicesByService.TryGetValue(service.Metadata.Name, out var serviceEndpointSlices) || serviceEndpointSlices.Count == 0)
                    {
                        _logger.LogWarning("Missing endpoint slices for {@service}", service.Name());
                        continue;
                    }

                    var readyEndpointCount = _serviceManager.GetReadyEndpointCount(serviceEndpointSlices);
                    if (readyEndpointCount == 0)
                    {
                        _logger.LogWarning("No ready endpoints in endpoint slices for {@service}", service.Name());
                        continue;
                    }

                    _logger.LogDebug("Service {@service} is available with {@count} ready endpoints", service.Name(), readyEndpointCount);
                    result = true;
                }
            }

            return result;
        }

        [Trace]
        public Task<IList<string>> GetRelatedServiceNamesAsync(V1Ingress ingress)
        {
            var ns = ingress.Namespace();
            var rules = ingress.Spec.Rules?.Where(rule => rule?.Http != null).ToList() ?? new List<V1IngressRule>();
            var paths = rules.SelectMany(rule => rule.Http.Paths).Where(path => path?.Backend.Service != null);
            var result = paths.Select(path => path.Backend.Service.Name).ToList();
            return Task.FromResult<IList<string>>(result);
        }

        [Trace]
        private bool IsBackendValid(V1HTTPIngressPath path)
        {
            using var pathScope = _logger.BeginScope("{path}", path.Path);

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

        [Trace]
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
    }
}
