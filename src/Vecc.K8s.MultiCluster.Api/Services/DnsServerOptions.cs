namespace Vecc.K8s.MultiCluster.Api.Services
{
    public class DnsServerOptions
    {
        public int ListenTCPPort { get; set; } = 53;
        public int ListenUDPPort { get; set; } = 53;
    }
}
