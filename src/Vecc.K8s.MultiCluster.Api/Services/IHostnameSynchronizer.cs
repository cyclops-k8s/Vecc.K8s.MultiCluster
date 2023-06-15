using k8s.Models;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface IHostnameSynchronizer
    {
        Task ClusterHeartbeatAsync();
        Task SynchronizeLocalClusterAsync();
        Task<bool> SynchronizeLocalEndpointsAsync(V1Endpoints endpoints);
        Task<bool> SynchronizeLocalIngressAsync(V1Ingress ingress);
        Task<bool> SynchronizeLocalServiceAsync(V1Service service);
        Task WatchClusterHeartbeatsAsync();
    }
}
