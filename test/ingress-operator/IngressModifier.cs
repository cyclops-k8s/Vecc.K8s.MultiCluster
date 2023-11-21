using IdentityModel;
using k8s;
using k8s.Models;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Controller;
using KubeOps.Operator.Controller.Results;
using KubeOps.Operator.Rbac;

namespace Vecc.IngressOperator
{
    [EntityRbac(typeof(V1Ingress), Verbs = RbacVerb.Get | RbacVerb.List | RbacVerb.Watch | RbacVerb.Patch)]
    public class IngressModifier : IResourceController<V1Ingress>
    {
        private readonly IKubernetesClient _kubernetes;
        private readonly ILogger<IngressModifier> _logger;

        public IngressModifier(IKubernetesClient kubernetes, ILogger<IngressModifier> logger)
        {
            _kubernetes = kubernetes;
            _logger = logger;
        }

        public async Task<ResourceControllerResult?> ReconcileAsync(V1Ingress entity)
        {
            if ((entity.Status?.LoadBalancer?.Ingress?.Count ?? 0) == 0)
            {
                _logger.LogInformation("No load balancer status, updating");

                var ingresses = new List<V1IngressLoadBalancerIngress>
                {
                    new V1IngressLoadBalancerIngress { Ip = Environment.GetEnvironmentVariable("ingressip") }
                };

                entity.Status = new V1IngressStatus
                {
                    LoadBalancer = new V1IngressLoadBalancerStatus
                    {
                        Ingress = ingresses
                    }
                };

                await _kubernetes.UpdateStatus(entity);

                _logger.LogInformation("Load balancer status updated");
            }
            else
            {
                _logger.LogInformation("Load balancer status already set, ignoring");
            }

            return null;
        }
    }
}
