using k8s.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class InMemoryCache : ICache
    {
        private readonly IMemoryCache _cache;
        private readonly List<string> _keys;

        public InMemoryCache(IMemoryCache cache)
        {
            _cache = cache;
            _keys = new List<string>();
        }

        public Task<string[]> GetHostnamesAsync()
        {
            string[] hosts;

            lock (_keys)
            {
                hosts = _keys.Select((host) => host.Split(':')[0]).Distinct().ToArray();
            }

            return Task.FromResult(hosts);
        }

        public Task<bool> IsHostUpAsync(string hostname)
        {
            var foundHost = false;
            string[] keys;

            lock (_keys)
            {
                keys = _keys.Where((host) =>
                {
                    var key = host.Split(':');
                    return key[0] == "State" && key[1] == hostname;
                }).ToArray();
            }

            foreach (var host in _keys)
            {
                foundHost = true;
                var state = _cache.Get(host) as string;

                if (state == null || state != "true")
                {
                    return Task.FromResult(false);
                }
            }

            return Task.FromResult(foundHost);
        }

        public Task SetHostStateAsync(string hostname, string clusterIdentifier, bool up, V1Ingress? ingress = null, V1Service? service = null)
        {
            if (string.IsNullOrWhiteSpace(hostname))
            {
                throw new ArgumentNullException(nameof(hostname));
            }

            if (string.IsNullOrWhiteSpace(clusterIdentifier))
            {
                throw new ArgumentNullException(nameof(clusterIdentifier));
            }

            var key = $"State:{hostname}:{clusterIdentifier}:{ingress?.Uid()}:{service?.Uid()}";

            lock (_keys)
            {
                _cache.Set(key, up.ToString());
                if (!_keys.Contains(key))
                {
                    _keys.Add(key);
                }
            }

            return Task.CompletedTask;
        }
    }
}
