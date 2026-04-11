namespace Cyclops.MultiCluster.Services
{
    public delegate Task OnHostChangedAsyncDelegate(string? value);

    public interface IQueue
    {
        OnHostChangedAsyncDelegate OnHostChangedAsync { get; set; }

        Task PublishHostChangedAsync(string hostname);
    }
}
