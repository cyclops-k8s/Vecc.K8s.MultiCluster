using k8s.Models;
using KubeOps.Abstractions.Entities;

namespace Vecc.K8s.MultiCluster.Api.Models.K8sEntities
{
    [KubernetesEntity(Group ="multicluster.veccsolutions.io", ApiVersion = "v1alpha", Kind = "ResourceCache")]
    public class V1ResourceCache : CustomKubernetesEntity
    {
        public string CurrentResourceVersion { get; set; } = string.Empty;
    }
}
