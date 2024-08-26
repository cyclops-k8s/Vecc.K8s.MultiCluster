using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace Vecc.K8s.MultiCluster.Api.Models.K8sEntities
{
    [EntityScope(EntityScope.Namespaced)]
    [KubernetesEntity(Group ="multicluster.veccsolutions.io", ApiVersion = "v1alpha", Kind = "ServiceCache")]
    public class V1ServiceCache : CustomKubernetesEntity
    {
        public V1ServiceCache()
        {
            Kind = "ServiceCache";
            ApiVersion = "multicluster.veccsolutions.io/v1alpha";
        }

        public V1ObjectReference Service { get; set; } = new V1ObjectReference();
        public int EndpointCount { get; set; }
    }
}
