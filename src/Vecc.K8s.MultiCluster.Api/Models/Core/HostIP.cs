using System.Net;

namespace Vecc.K8s.MultiCluster.Api.Models.Core
{
    public class HostIP
    {
        public string IPAddress { get; set; } = string.Empty;
        public int Priority { get; set; }
        public int Weight { get; set; }

        public override bool Equals(object? obj)
        {
            if (obj == null)
            {
                return false;
            }

            if (obj is HostIP hostIp)
            {
                if (IPAddress == hostIp.IPAddress &&
                    Weight == hostIp.Weight &&
                    Priority == hostIp.Priority)
                {
                    return true;
                }
            }

            return false;
        }

        public override int GetHashCode() => base.GetHashCode();
    }
}
