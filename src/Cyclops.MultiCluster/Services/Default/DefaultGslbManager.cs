using KubeOps.KubernetesClient;
using Cyclops.MultiCluster.Models.K8sEntities;

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
            return resources.ToArray();
        }
    }
}
