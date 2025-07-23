using KubeOps.KubernetesClient;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public class MultiClusterOptions
    {
        public string ClusterIdentifier { get; set; } = "local";
        public int DNSRefreshInterval { get; set; } = 30; // resync every 30 seconds
        public int HeartbeatTimeout { get; set; } = 30;
        public int HeartbeatCheckInterval { get; set; } = 1;
        public int HeartbeatSetInterval { get; set; } = 10;
        public int ListenGrpcPort { get; set; } = 0;
        public int ListenPort { get; set; } = 0;
        public PeerHosts[] Peers { get; set; } = Array.Empty<PeerHosts>();
        [Sensitive]
        public byte[] ClusterSalt { get; set; } = Array.Empty<byte>();
        public Dictionary<string, string[]> NameserverNames { get; set; } = new Dictionary<string, string[]>();
        public int DefaultRecordTTL { get; set; } = 5;
        public string DNSServerResponsibleEmailAddress { get; set; } = "null.vecck8smulticlusteringress.com";
        public string DNSHostname { get; set; } = "dns.vecck8smulticlusteringress.com";
        public string Namespace { get; set; } = Environment.GetEnvironmentVariable("POD_NAMESPACE") ?? new KubernetesClient().GetCurrentNamespace();
        public int PeriodicRefreshInterval { get; set; } = 60 * 5; //default to 5 minutes
    }
}
