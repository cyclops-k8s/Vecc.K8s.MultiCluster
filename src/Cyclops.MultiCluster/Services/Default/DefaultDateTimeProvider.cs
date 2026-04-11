namespace Cyclops.MultiCluster.Services.Default
{
    public class DefaultDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
