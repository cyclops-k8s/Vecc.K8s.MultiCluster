using k8s.Models;
using KubeOps.Abstractions.Entities;
using Vecc.K8s.MultiCluster.Api.Models.Core;

namespace Vecc.K8s.MultiCluster.Api.Models.K8sEntities
{
    [KubernetesEntity(Group ="multicluster.veccsolutions.io", ApiVersion = "v1alpha", Kind = "HostnameCache")]
    public class V1HostnameCache : CustomKubernetesEntity
    {
        public HostIPCache[] Addresses { get; set; } = Array.Empty<HostIPCache>();

        public class HostIPCache
        {
            public string IPAddress { get; set; } = string.Empty;
            public int Priority {get; set; }
            public int Weight { get; set; }
            public string ClusterIdentifier { get; set; } = string.Empty;

            public HostIP ToCore()
            {
                var result = new HostIP
                {
                    IPAddress = IPAddress,
                    Priority = Priority,
                    Weight = Weight,
                    ClusterIdentifier = ClusterIdentifier
                };

                return result;
            }

            public static HostIPCache FromCore(HostIP hostIP)
            {
                var result = new HostIPCache
                {
                    IPAddress = hostIP.IPAddress,
                    Priority = hostIP.Priority,
                    Weight = hostIP.Weight,
                    ClusterIdentifier = hostIP.ClusterIdentifier
                };

                return result;
            }
        }
    }
}
