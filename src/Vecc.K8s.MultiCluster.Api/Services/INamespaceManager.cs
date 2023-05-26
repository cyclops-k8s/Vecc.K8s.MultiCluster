using k8s.Models;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface INamespaceManager
    {
        Task<IList<V1Namespace>> GetNamsepacesAsync();
    }
}
