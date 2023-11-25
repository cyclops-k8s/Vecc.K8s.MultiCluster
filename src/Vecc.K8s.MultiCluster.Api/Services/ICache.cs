using Vecc.K8s.MultiCluster.Api.Models.Core;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface ICache
    {
        Task<string[]> GetClusterIdentifiersAsync();
        Task<int> GetEndpointsCountAsync(string ns, string name);
        Task<Models.Core.Host?> GetHostInformationAsync(string hostname);
        Task<string[]> GetHostnamesAsync(string clusterIdentifier);
        Task<Models.Core.Host[]?> GetHostsAsync(string clusterIdentifier);
        Task<DateTime?> GetClusterHeartbeatTimeAsync(string clusterIdentifier);
        Task<bool> IsServiceMonitoredAsync(string ns, string name);
        Task RemoveClusterHostnameAsync(string clusterIdentifier, string hostname);
        Task SetClusterHeartbeatAsync(string clusterIdentifier, DateTime heartbeat);
        Task SetEndpointsCountAsync(string ns, string name, int count);
        Task<bool> SetHostIPsAsync(string hostname, string clusterIdentifier, HostIP[] hostIPs);
        Task SynchronizeCachesAsync();
        Task TrackServiceAsync(string ns, string name);
        Task UntrackAllServicesAsync();
        Task SetResourceVersionAsync(string uniqueIdentifier, string version);
        Task<string> GetLastResourceVersionAsync(string uniqueIdentifier);
    }
}
