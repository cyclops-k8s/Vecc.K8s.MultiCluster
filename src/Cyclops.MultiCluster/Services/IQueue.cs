namespace Cyclops.MultiCluster.Services
{
    public delegate Task OnHostChangedAsyncDelegate(string? value, Models.Core.Host? hostInformation = null);

    public interface IQueue
    {
        OnHostChangedAsyncDelegate OnHostChangedAsync { get; set; }

        Task PublishHostChangedAsync(string hostname);
    }
}
