using Cyclops.MultiCluster.Models.Core;

namespace Cyclops.MultiCluster.Services
{
    public interface IKubernetesCache
    {
        Task<string[]> GetClusterIdentifiersAsync();
        Task<Models.Core.Host?> GetHostInformationAsync(string hostname);
        Task<Dictionary<string, Models.Core.Host>> GetAllHostInformationAsync();
        Task<string[]> GetHostnamesAsync();
        Task<Models.Core.Host[]?> GetHostsAsync(string clusterIdentifier);
        Task<DateTime?> GetClusterHeartbeatTimeAsync(string clusterIdentifier);
        Task SetClusterCacheAsync(string identifier, Models.Core.Host[] hosts);
        Task SetClusterHeartbeatAsync(string clusterIdentifier, DateTime heartbeat);
        Task SynchronizeCachesAsync();
        Task RemoveClusterCacheAsync(string clusterIdentifier);
    }
}
