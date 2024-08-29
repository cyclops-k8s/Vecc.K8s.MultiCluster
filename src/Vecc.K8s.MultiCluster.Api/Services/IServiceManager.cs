using k8s.Models;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface IServiceManager
    {
        Task<List<V1Service>> GetLoadBalancerServicesAsync(IList<V1Service> services, IList<V1Endpoints> endpoints);
        Task<IList<V1Service>> GetServicesAsync();
        Task<IList<V1Endpoints>> GetEndpointsAsync();
    }
}
