using ARSoft.Tools.Net.Dns;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface IDnsResolver
    {
        OnHostChangedAsyncDelegate OnHostChangedAsync { get; }
        Task InitializeAsync();
        Task<DnsMessage> ResolveAsync(DnsMessage message);
    }
}
