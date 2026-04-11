using k8s.Models;

namespace Cyclops.MultiCluster.Models.Core
{
    public class ServiceCache
    {
        public V1ObjectReference Service { get; set; } = new V1ObjectReference();
        public int EndpointCount { get; set; }
    }
}
