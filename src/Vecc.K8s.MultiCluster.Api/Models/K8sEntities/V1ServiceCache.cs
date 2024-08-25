using k8s.Models;
using KubeOps.Abstractions.Entities;

namespace Vecc.K8s.MultiCluster.Api.Models.K8sEntities
{
    [KubernetesEntity(Group ="multicluster.veccsolutions.io", ApiVersion = "v1alpha", Kind = "ServiceCache")]
    public class V1ServiceCache : CustomKubernetesEntity
    {
        public V1ObjectReference Service { get; set; } = new V1ObjectReference();
        public int EndpointCount { get; set; }
    }
}
