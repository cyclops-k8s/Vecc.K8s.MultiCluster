namespace Vecc.K8s.MultiCluster.Api.Models.Api
{
    public class HostIP
    {
        public string IPAddress { get; set; } = string.Empty;
        public int Priority { get; set; }
        public int Weight { get; set; }
    }
}
