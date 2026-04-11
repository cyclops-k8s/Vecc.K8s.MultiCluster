using k8s.Models;

namespace Cyclops.MultiCluster.Services
{
    public interface IHostStateChangeNotifier
    {
        Task HostStateChangedAsync(string hostName);
    }
}
