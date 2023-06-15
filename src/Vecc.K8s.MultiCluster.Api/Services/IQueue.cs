namespace Vecc.K8s.MultiCluster.Api.Services
{
    public delegate Task OnHostChangedAsyncDelegate(string? value);

    public interface IQueue
    {
        OnHostChangedAsyncDelegate OnHostChangedAsync { get; set; }

        Task PublishHostChangedAsync(string hostname);
    }
}
