using Vecc.K8s.MultiCluster.Api.Models.Core;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface ICache
    {
        Task<string[]> GetClusterIdentifiersAsync();
        Task<bool> SetHostIPsAsync(string hostname, string clusterIdentifier, HostIP[] hostIPs);
        Task<Models.Core.Host?> GetHostInformationAsync(string hostname);
        Task<string[]> GetHostnamesAsync(string clusterIdentifier);
        Task<Models.Core.Host[]?> GetHostsAsync(string clusterIdentifier);
        Task<string[]> GetKeysAsync(string prefix);
        Task<DateTime> GetClusterHeartbeatTimeAsync(string clusterIdentifier);
        Task RemoveClusterHostnameAsync(string clusterIdentifier, string hostname);
        Task SetClusterHeartbeatAsync(string clusterIdentifier, DateTime heartbeat);
        Task<bool> IsServiceMonitoredAsync(string ns, string name);
        Task SynchronizeCachesAsync();
        Task TrackServiceAsync(string ns, string name);
        Task UntrackAllServicesAsync();
        Task SetResourceVersionAsync(string uniqueIdentifier, string version);
        Task<string> GetLastResourceVersionAsync(string uniqueIdentifier);
    }
}
