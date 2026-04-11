
namespace Cyclops.MultiCluster.Services.Default
{
    public class KubernetesQueue : IQueue
    {
        public OnHostChangedAsyncDelegate OnHostChangedAsync { get; set; } = _ => Task.CompletedTask;

        public Task PublishHostChangedAsync(string hostname)
        {
            throw new NotImplementedException();
        }
    }
}
