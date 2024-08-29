using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace Vecc.K8s.MultiCluster.Api.Models.K8sEntities
{
    [EntityScope(EntityScope.Namespaced)]
    [KubernetesEntity(Group = "multicluster.veccsolutions.io", ApiVersion = "v1alpha", Kind = "GSLB")]
    [Description("GSLB object to expose services or ingresses across clusters")]
    public class V1Gslb : CustomKubernetesEntity
    {
        public V1Gslb()
        {
            Kind = "GSLB";
            ApiVersion = "multicluster.veccsolutions.io/v1alpha";
        }

        /// <summary>
        /// Reference to the ingress or service
        /// </summary>
        [Required]
        [Description("Reference to the ingress or service")]
        public V1ObjectReference ObjectReference { get; set; } = new V1ObjectReference();

        /// <summary>
        /// Hostnames to expose the ingress or service as
        /// </summary>
        [Description("Hostnames to expose the ingress or service as")]
        [Required]
        public string[] Hostnames { get; set; } = Array.Empty<string>();

        /// <summary>
        /// External IP to return instead of what is in the ingress or service
        /// </summary>
        [Description("External IP to return instead of what is in the ingress or service")]
        public string[]? IPOverrides { get; set; }

        /// <summary>
        /// Priority to assign this GSLB object. Highest priority is chosen first.
        /// </summary>
        [Description("Priority to assign this GSLB object. Highest priority is chosen first.")]
        [Required]
        [RangeMinimum(0)]
        public int Priority { get; set; } = 0;

        /// <summary>
        /// Weight to assign this GSLB object when doing round robin load balancing type. Defaults to 50.
        /// The calculation to determine the final weighting of all objects is (weight / sum of all weights) * 100.
        /// </summary>
        [Description("Weight to assign this GSLB object when doing round robin load balancing type. Defaults to 50. The calculation to determine the final weighting of all objects is (weight / sum of all weights) * 100.")]
        [Required]
        [RangeMinimum(0)]
        public int Weight { get; set; } = 50;

        public class V1ObjectReference
        {
            [Required]
            [Length(minLength: 1)]
            public string Name { get; set; } = string.Empty;

            [Required]
            public ReferenceType Kind { get; set; }

            public enum ReferenceType
            {
                Ingress,
                Service
            }
        }
    }
}
