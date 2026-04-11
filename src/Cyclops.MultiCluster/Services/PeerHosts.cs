namespace Cyclops.MultiCluster.Services
{
    public class PeerHosts
    {
        public string Identifier { get; set; } = string.Empty;
        [Sensitive]
        public string Key { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}
