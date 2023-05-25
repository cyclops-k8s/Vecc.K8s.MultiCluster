using k8s.Models;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface IHostStateChangeNotifier
    {
        Task HostStateChangedAsync(string hostName);
    }
}
