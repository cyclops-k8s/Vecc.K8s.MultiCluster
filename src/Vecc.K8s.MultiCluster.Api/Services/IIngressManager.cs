using k8s.Models;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface IIngressManager
    {
        Task<IList<V1Ingress>> GetAllIngressesAsync(string? ns);
        Task<IList<V1Ingress>> GetValidIngressesAsync(string? ns);
        Task<Dictionary<string, IList<V1Ingress>>> GetAvailableHostnamesAsync(
            IList<V1Ingress> ingresses,
            IList<V1Service> services,
            IList<V1Endpoints> endpoints);
        bool IsIngressValid(V1Ingress ingress, IList<V1Service> services, IList<V1Endpoints> endpoints);
        Task<IList<string>> GetRelatedServiceNamesAsync(V1Ingress ingress);
    }
}
