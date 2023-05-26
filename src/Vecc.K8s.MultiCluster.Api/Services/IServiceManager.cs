using k8s.Models;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface IServiceManager
    {
        Task<Dictionary<string, IList<V1Service>>> GetAvailableHostnamesAsync();
        Task<List<V1Service>> GetServicesAsync(string? ns);
        Task<List<V1Endpoints>> GetEndpointsAsync(string? ns);
    }
}
