using k8s.Models;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface IServiceManager
    {
        Task<List<V1Service>> GetLoadBalancerEndpointsAsync(IList<V1Service> services);
        Task<Dictionary<string, IList<V1Service>>> GetAvailableHostnamesAsync(IList<V1Service> services, IList<V1Endpoints> endpoints);
        Task<List<V1Service>> GetServicesAsync(string? ns);
        Task<List<V1Endpoints>> GetEndpointsAsync(string? ns);
    }
}
