using k8s.Models;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface IHostnameSynchronizer
    {
        Task ClusterHeartbeatAsync();
        Task SynchronizeLocalClusterAsync();
        Task<bool> SynchronizeLocalEndpointSliceAsync(V1EndpointSlice endpointSlice);
        Task<bool> SynchronizeLocalIngressAsync(V1Ingress ingress);
        Task<bool> SynchronizeLocalServiceAsync(V1Service service);
        Task SynchronizeRemoteClustersAsync();
        Task WatchClusterHeartbeatsAsync();
    }
}
