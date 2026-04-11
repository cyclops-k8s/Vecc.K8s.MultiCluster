namespace Cyclops.MultiCluster.Services
{
    public interface IDateTimeProvider
    {
        DateTime UtcNow { get; }
    }
}
