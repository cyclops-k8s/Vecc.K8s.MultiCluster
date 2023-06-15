namespace Vecc.K8s.MultiCluster.Api.Models.Core
{
    public class Host
    {
        public string Hostname { get; set; } = string.Empty;
        public HostIP[] HostIPs { get; set; } = Array.Empty<HostIP>();
    }
}
