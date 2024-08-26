using KubeOps.KubernetesClient;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
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
