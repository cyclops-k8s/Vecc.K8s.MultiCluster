namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultHostnameSynchronizer : IHostnameSynchronizer
    {
        private readonly ILogger<DefaultHostnameSynchronizer> _logger;
        private readonly IIngressManager _ingressManager;

        public DefaultHostnameSynchronizer(ILogger<DefaultHostnameSynchronizer> logger, IIngressManager ingressManager)
        {
            _logger = logger;
            _ingressManager = ingressManager;
        }

        public async Task SynchronizeLocalClusterAsync()
        {
            _logger.LogInformation("Synchronizing the cluster");

            _logger.LogInformation("Getting hostnames from ingress objects");
            var ingressHostnames = await _ingressManager.GetAvailableHostnamesAsync();
            _logger.LogInformation("Done getting ingress hostnames.");

            _logger.LogTrace("Hostnames: {@hostnames}", ingressHostnames.Keys);

            throw new NotImplementedException();
        }
    }
}
