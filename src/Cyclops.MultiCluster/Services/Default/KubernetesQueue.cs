using Cyclops.MultiCluster.Models.Core;

namespace Cyclops.MultiCluster.Services.Default
{
    public class KubernetesQueue : IQueue
    {
        public OnHostChangedAsyncDelegate OnHostChangedAsync { get; set; } = (_, _) => Task.CompletedTask;

        public Task PublishHostChangedAsync(string hostname)
        {
            throw new NotImplementedException();
        }
    }
}
