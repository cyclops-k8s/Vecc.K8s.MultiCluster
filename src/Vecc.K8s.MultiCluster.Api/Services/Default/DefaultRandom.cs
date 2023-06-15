namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultRandom : IRandom
    {
        private readonly Random _random = new Random();

        public int Next(int max) => _random.Next(max);

        public int Next(int min, int max) => _random.Next(min, max);
    }
}
