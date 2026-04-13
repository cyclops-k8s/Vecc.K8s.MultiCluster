using KubeOps.KubernetesClient;
using Cyclops.MultiCluster.Models.K8sEntities;
using k8s.Models;

namespace Cyclops.MultiCluster.Services.Default
{
    public class DefaultGslbManager : IGslbManager
    {
        private readonly ILogger<DefaultGslbManager> _logger;
        private readonly IKubernetesClient _client;

        public DefaultGslbManager(ILogger<DefaultGslbManager> logger, IKubernetesClient client)
        {
            _logger = logger;
            _client = client;
        }

        public async Task<V1Gslb[]> GetGslbsAsync()
        {
            _logger.LogInformation("Getting all gslb resources in the cluster");
            var resources = await _client.ListAsync<V1Gslb>();
            var veccGslbs = await _client.ListAsync<V1VeccGslb>();
            var allResources = resources.ToList();
            allResources.AddRange(veccGslbs.Select(v => ToV1Gslb(v)));
            return allResources.ToArray();
        }

        public static V1Gslb ToV1Gslb(V1VeccGslb veccGslb)
        {
            return new V1Gslb
            {
                ApiVersion = veccGslb.ApiVersion,
                Kind = "Gslb",
                Metadata = veccGslb.Metadata,
                Spec = new V1Gslb.GslbSpec
                {
                    Hostnames=veccGslb.Hostnames,
                    IPOverrides = veccGslb.IPOverrides,
                    ObjectReference = new V1Gslb.V1ObjectReference
                    {
                        Kind = veccGslb.ObjectReference.Kind == V1VeccGslb.V1ObjectReference.ReferenceType.Ingress
                                ? V1Gslb.V1ObjectReference.ReferenceType.Ingress
                                : V1Gslb.V1ObjectReference.ReferenceType.Service,
                        Name = veccGslb.ObjectReference.Name,
                    },
                    Priority = veccGslb.Priority,
                    Weight = veccGslb.Weight
                }
            };
        }
    }
}
