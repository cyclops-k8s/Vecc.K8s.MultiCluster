using k8s.Models;
using KubeOps.Abstractions.Rbac;
using KubeOps.Abstractions.Reconciliation;
using KubeOps.Abstractions.Reconciliation.Controller;
using KubeOps.KubernetesClient;

namespace Vecc.IngressOperator
{
    [EntityRbac(typeof(V1Ingress), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Service), Verbs = RbacVerb.All)]
    public class IngressModifier : IEntityController<V1Ingress>, IEntityController<V1Service>
    {
        private readonly IKubernetesClient _kubernetes;
        private readonly ILogger<IngressModifier> _logger;

        public IngressModifier(IKubernetesClient kubernetes, ILogger<IngressModifier> logger)
        {
            _kubernetes = kubernetes;
            _logger = logger;
        }

        public Task<ReconciliationResult<V1Ingress>> DeletedAsync(V1Ingress entity, CancellationToken cancellationToken)
            => Task.FromResult(ReconciliationResult<V1Ingress>.Success(entity));

        public async Task<ReconciliationResult<V1Ingress>> ReconcileAsync(V1Ingress entity, CancellationToken cancellationToken)
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

                await _kubernetes.UpdateStatusAsync(entity);

                _logger.LogInformation("Load balancer status updated");
            }
            else
            {
                _logger.LogInformation("Load balancer status already set, ignoring");
            }

            return ReconciliationResult<V1Ingress>.Success(entity);
        }

        public Task<ReconciliationResult<V1Service>> DeletedAsync(V1Service entity, CancellationToken cancellationToken)
            => Task.FromResult(ReconciliationResult<V1Service>.Success(entity));

        public async Task<ReconciliationResult<V1Service>> ReconcileAsync(V1Service entity, CancellationToken cancellationToken)
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

                await _kubernetes.UpdateStatusAsync(entity);

                _logger.LogInformation("Service status updated");
            }
            else
            {
                _logger.LogInformation("Service status already set, ignoring");
            }

            return ReconciliationResult<V1Service>.Success(entity);
        }
    }
}
