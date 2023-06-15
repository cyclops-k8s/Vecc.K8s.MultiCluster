namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
