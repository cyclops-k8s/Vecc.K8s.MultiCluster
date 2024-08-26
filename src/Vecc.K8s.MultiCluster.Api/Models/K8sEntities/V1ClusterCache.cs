using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;
using System.Security;
using Vecc.K8s.MultiCluster.Api.Models.Core;

namespace Vecc.K8s.MultiCluster.Api.Models.K8sEntities
{
    [EntityScope(EntityScope.Namespaced)]
    [KubernetesEntity(Group ="multicluster.veccsolutions.io", ApiVersion = "v1alpha", Kind = "ClusterCache")]
    public class V1ClusterCache : CustomKubernetesEntity
    {
        public V1ClusterCache()
        {
            Kind = "ClusterCache";
            ApiVersion = "multicluster.veccsolutions.io/v1alpha";
        }

        public string LastHeartbeat { get; set; } = string.Empty;
        public HostCache[] Hostnames { get; set; } = Array.Empty<HostCache>();
        public EndpointCacheCount[] ServiceEndpointCounts { get; set; } = Array.Empty<EndpointCacheCount>();


        public class EndpointCacheCount
        {
            public string Namespace { get; set; } = string.Empty;
            public string Service { get; set; } = string.Empty;
            public int Count { get; set; }
        }

        public class HostCache
        {
            public string Hostname { get; set; } = string.Empty;
            public HostIPCache[] HostIPs { get; set; } = Array.Empty<HostIPCache>();

            public Core.Host ToCore()
            {
                var result = new Core.Host
                {
                    Hostname = Hostname,
                    HostIPs = HostIPs.Select(x => x.ToCore()).ToArray()
                };

                return result;
            }
        }

        public class HostIPCache
        {
            public string IPAddress { get; set; } = string.Empty;
            public int Priority { get; set; }
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
