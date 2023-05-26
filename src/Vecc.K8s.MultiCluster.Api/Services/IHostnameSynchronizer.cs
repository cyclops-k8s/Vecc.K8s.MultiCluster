namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface IHostnameSynchronizer
    {
        Task SynchronizeLocalClusterAsync();
    }
}
