using k8s.Models;
using KubeOps.KubernetesClient;
using NewRelic.Api.Agent;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultNamespaceManager : INamespaceManager
    {
        private readonly ILogger<DefaultNamespaceManager> _logger;
        private readonly IKubernetesClient _kubernetesClient;

        public DefaultNamespaceManager(ILogger<DefaultNamespaceManager> logger, IKubernetesClient kubernetesClient)
        {
            _logger = logger;
            _kubernetesClient = kubernetesClient;
        }

        [Trace]
        public async Task<IList<V1Namespace>> GetNamsepacesAsync()
        {
            var result = new List<V1Namespace>();

            _logger.LogDebug("Getting all namespaces");
            var namespaces = await _kubernetesClient.List<V1Namespace>();
            _logger.LogDebug("Done getting all namespaces");

            result.AddRange(namespaces);

            return result;
        }
    }
}
