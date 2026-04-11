using Cyclops.MultiCluster.Models.K8sEntities;

namespace Cyclops.MultiCluster.Services
{
    public interface IGslbManager
    {
        Task<V1Gslb[]> GetGslbsAsync();
    }
}
