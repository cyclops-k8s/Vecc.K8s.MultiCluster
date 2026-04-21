using Cyclops.MultiCluster.Models.Core;

namespace Cyclops.MultiCluster.Services
{
    public interface ICache : IKubernetesCache
    {
        Task<int> GetEndpointsCountAsync(string ns, string name);
        Task<bool> IsServiceMonitoredAsync(string ns, string name);
        Task SetEndpointsCountAsync(string ns, string name, int count);
        Task TrackServiceAsync(string ns, string name);
        Task UntrackAllServicesAsync();
        Task SetResourceVersionAsync(string uniqueIdentifier, string version);
        Task<string> GetLastResourceVersionAsync(string uniqueIdentifier);
    }
}
