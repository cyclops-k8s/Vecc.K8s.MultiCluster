using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace Vecc.K8s.MultiCluster.Api.Models.K8sEntities
{
    [EntityScope(EntityScope.Namespaced)]
    [KubernetesEntity(Group ="multicluster.veccsolutions.io", ApiVersion = "v1alpha", Kind = "ResourceCache")]
    public class V1ResourceCache : CustomKubernetesEntity
    {
        public V1ResourceCache()
        {
            Kind = "ResourceCache";
            ApiVersion = "multicluster.veccsolutions.io/v1alpha";
        }

        public string CurrentResourceVersion { get; set; } = string.Empty;
    }
}
