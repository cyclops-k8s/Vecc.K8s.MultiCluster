using Vecc.Dns.Server;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultDnsHost : IDnsHost
    {
        private readonly ILogger<DefaultDnsHost> _logger;
        private readonly IDnsServer _dnsServer;
        private readonly CancellationToken _shutdownCancellationToken;

        public DefaultDnsHost(ILogger<DefaultDnsHost> logger, IDnsServer dnsServer, IHostApplicationLifetime lifetime)
        {
            _logger = logger;
            _dnsServer = dnsServer;
            _shutdownCancellationToken = lifetime.ApplicationStopping;
        }

        public Task StartAsync()
        {
            _logger.LogInformation("Starting dns server");
            return _dnsServer.ExecuteAsync(_shutdownCancellationToken);
        }
    }
}
