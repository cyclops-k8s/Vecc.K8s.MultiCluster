namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface IDateTimeProvider
    {
        DateTime UtcNow { get; }
    }
}
