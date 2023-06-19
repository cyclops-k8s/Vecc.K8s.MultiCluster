namespace Vecc.K8s.MultiCluster.Api.Services
{
    public class MultiClusterOptions
    {
        public string ClusterIdentifier { get; set; } = "local";
        public int HeartbeatTimeout { get; set; } = 30;
        public int HeartbeatCheckInterval { get; set; } = 1;
        public int HeartbeatSetInterval { get; set; } = 10;
        public PeerHosts[] Peers { get; set; } = Array.Empty<PeerHosts>();
        public byte[] ClusterSalt { get; set; } = Array.Empty<byte>();
    }
}
