using IdentityModel;
using k8s;
using k8s.Models;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Controller;
using KubeOps.Operator.Controller.Results;
using KubeOps.Operator.Rbac;

namespace Vecc.IngressOperator
{
    [EntityRbac(typeof(V1Ingress), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Service), Verbs = RbacVerb.All)]
    public class IngressModifier : IResourceController<V1Ingress>, IResourceController<V1Service>
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

        public async Task<ResourceControllerResult?> ReconcileAsync(V1Service entity)
        {
            if (entity.Namespace() == "kube-system")
            {
                _logger.LogInformation("Ignoring service in kube-system namespace");
                return null;
            }

            if ((entity.Status?.LoadBalancer?.Ingress?.Count ?? 0) == 0)
            {
                _logger.LogInformation("No load balancer status, updating");

                var ingresses = new List<V1LoadBalancerIngress>
                {
                    new V1LoadBalancerIngress { Ip = Environment.GetEnvironmentVariable("ingressip") }
                };

                entity.Status = new V1ServiceStatus
                {
                    LoadBalancer = new V1LoadBalancerStatus
                    {
                        Ingress = ingresses
                    }
                };

                await _kubernetes.UpdateStatus(entity);

                _logger.LogInformation("Service status updated");
            }
            else
            {
                _logger.LogInformation("Service status already set, ignoring");
            }

            return null;
        }
    }
}
