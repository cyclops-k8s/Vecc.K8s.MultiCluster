using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace Cyclops.MultiCluster.Models.Core
{
    public class ServiceCache
    {
        public V1ObjectReference Service { get; set; } = new V1ObjectReference();
        public int EndpointCount { get; set; }
    }
}
