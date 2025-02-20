using ARSoft.Tools.Net.Dns;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultDnsHost : IDnsHost
    {
        private readonly ILogger<DefaultDnsHost> _logger;
        private readonly DnsServer _dnsServer;

        public DefaultDnsHost(ILogger<DefaultDnsHost> logger, DnsServer dnsServer)
        {
            _logger = logger;
            _dnsServer = dnsServer;
        }

        public Task StartAsync()
        {
            _logger.LogInformation("Starting dns server");
            _dnsServer.Start();
            return Task.CompletedTask;
        }
    }
}
