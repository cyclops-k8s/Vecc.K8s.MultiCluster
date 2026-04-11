using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace Cyclops.MultiCluster.Models.K8sEntities
{
    [EntityScope(EntityScope.Namespaced)]
    [KubernetesEntity(Group ="multicluster.cyclops.io", ApiVersion = "v1alpha", Kind = "ServiceCache")]
    public class V1ServiceCache : CustomKubernetesEntity
    {
        public V1ServiceCache()
        {
            Kind = "ServiceCache";
            ApiVersion = "multicluster.cyclops.io/v1alpha";
        }

        public V1ObjectReference Service { get; set; } = new V1ObjectReference();
        public int EndpointCount { get; set; }
    }
}
