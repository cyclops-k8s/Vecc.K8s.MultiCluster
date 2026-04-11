using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;
using Cyclops.MultiCluster.Models.Core;
using System.Text.Json.Serialization;

namespace Cyclops.MultiCluster.Models.K8sEntities
{
    [EntityScope(EntityScope.Namespaced)]
    [KubernetesEntity(Group ="multicluster.cyclops.io", ApiVersion = "v1alpha", Kind = "HostnameCache")]
    public class V1HostnameCache : CustomKubernetesEntity
    {
        public V1HostnameCache()
        {
            Kind = "HostnameCache";
            ApiVersion = "multicluster.cyclops.io/v1alpha";
        }

        public HostIPCache[] Addresses { get; set; } = Array.Empty<HostIPCache>();
        public string? Hostname { get; set; }

        public class HostIPCache
        {
            [JsonPropertyName("ipAddress")]
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
