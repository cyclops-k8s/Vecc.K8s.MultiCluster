using System.Net;

namespace Cyclops.MultiCluster.Models.Core
{
    public class HostIP
    {
        public string IPAddress { get; set; } = string.Empty;
        public int Priority { get; set; }
        public int Weight { get; set; }
        public string ClusterIdentifier { get; set; } = string.Empty;

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
                    Priority == hostIp.Priority &&
                    ClusterIdentifier == hostIp.ClusterIdentifier)
                {
                    return true;
                }
            }

            return false;
        }

        public override int GetHashCode() => base.GetHashCode();
    }
}
