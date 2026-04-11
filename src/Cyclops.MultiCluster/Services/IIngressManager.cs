using k8s.Models;

namespace Cyclops.MultiCluster.Services
{
    public interface IIngressManager
    {
        Task<Dictionary<string, IList<V1Ingress>>> GetAvailableHostnamesAsync(
            IList<V1Ingress> ingresses,
            IList<V1Service> services,
            IList<V1EndpointSlice> endpointSlices);
        Task<IList<V1Ingress>> GetIngressesAsync();
        Task<IList<string>> GetRelatedServiceNamesAsync(V1Ingress ingress);
    }
}
