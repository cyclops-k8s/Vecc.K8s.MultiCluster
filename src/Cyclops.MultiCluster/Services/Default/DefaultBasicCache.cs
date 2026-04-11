using Microsoft.Extensions.Caching.Memory;
using M = Microsoft.Extensions.Caching.Memory;

namespace Cyclops.MultiCluster.Services.Default
{
    public class BasicCache : IBasicCache
    {
        private readonly M.MemoryCache _cache;

        public BasicCache(M.IMemoryCache cache)
        {
            _cache = cache as M.MemoryCache ?? throw new ArgumentException("Expected MemoryCache", nameof(cache));
        }

        public IEnumerable<object> Keys => _cache.Keys;

        public T? Get<T>(string key)
            => _cache.Get<T>(key);

        public T? GetOrCreate<T>(string key, Func<T> createFunc)
            => _cache.GetOrCreate(key, (_) => createFunc());

        public bool TryGetValue<T>(string key, out T? value)
            => _cache.TryGetValue(key, out value);

        public void Set<T>(string key, T value)
            => _cache.Set(key, value);

        public void Remove(string key)
            => _cache.Remove(key);
    }
}