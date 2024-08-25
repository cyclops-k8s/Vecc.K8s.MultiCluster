
namespace Vecc.K8s.MultiCluster.Api.Services.Default
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
