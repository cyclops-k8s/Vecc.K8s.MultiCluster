using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface IGslbManager
    {
        Task<V1Gslb[]> GetGslbsAsync();
    }
}
