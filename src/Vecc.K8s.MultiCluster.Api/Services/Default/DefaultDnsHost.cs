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

        public async Task StartAsync()
        {
            while (!_shutdownCancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Starting dns server");

                try
                {
                    await _dnsServer.ExecuteAsync(_shutdownCancellationToken);

                    if (!_shutdownCancellationToken.IsCancellationRequested)
                    {
                        _logger.LogError("Unexpected shutdown of the dns server, restarting.");
                    }
                }
                catch (Exception ex)
                {
                    if (!_shutdownCancellationToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Crash in the dns server, restarting.");
                    }
                }
            }
            _logger.LogInformation("Shutdown of dns server complete");
        }
    }
}
