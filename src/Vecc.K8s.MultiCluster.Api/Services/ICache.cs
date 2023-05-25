using k8s.Models;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface ICache
    {
        Task SetHostStateAsync(string hostname, string clusterIdentifier, bool up, V1Ingress? ingress = null, V1Service? service = null);
        Task<string[]> GetHostnamesAsync();
        Task<bool> IsHostUpAsync(string hostName);
    }
}
