using k8s.Models;

namespace Cyclops.MultiCluster.Services
{
    public interface IServiceManager
    {
        Task<List<V1Service>> GetLoadBalancerServicesAsync(IList<V1Service> services, IList<V1EndpointSlice> endpointSlices);
        Task<IList<V1Service>> GetServicesAsync();
        Task<IList<V1EndpointSlice>> GetEndpointSlicesAsync();
        Task<IList<V1EndpointSlice>> GetEndpointSlicesAsync(string ns, string serviceName);
        
        /// <summary>
        /// Gets the count of ready endpoints for a service from its endpoint slices
        /// </summary>
        int GetReadyEndpointCount(IEnumerable<V1EndpointSlice> slices);
    }
}
