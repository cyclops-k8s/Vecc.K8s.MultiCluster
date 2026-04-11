namespace Cyclops.MultiCluster.Services
{
    public interface IRandom
    {
        int Next(int max);
        int Next(int min, int max);
    }
}
