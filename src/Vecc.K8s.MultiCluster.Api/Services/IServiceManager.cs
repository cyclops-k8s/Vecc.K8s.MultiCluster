using k8s.Models;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface IServiceManager
    {
        Task<List<V1Service>> GetLoadBalancerServicesAsync(IList<V1Service> services, IList<V1EndpointSlice> endpointSlices);
        Task<IList<V1Service>> GetServicesAsync();
        Task<IList<V1EndpointSlice>> GetEndpointSlicesAsync();
        
        /// <summary>
        /// Gets the count of ready endpoints for a service from its endpoint slices
        /// </summary>
        int GetReadyEndpointCount(IEnumerable<V1EndpointSlice> slices);
    }
}
