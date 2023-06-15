namespace Vecc.K8s.MultiCluster.Api.Services
{
    public interface IRandom
    {
        int Next(int max);
        int Next(int min, int max);
    }
}
