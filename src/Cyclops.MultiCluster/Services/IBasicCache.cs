namespace Cyclops.MultiCluster.Services;

public interface IBasicCache
{
    bool TryGetValue<T>(string key, out T? value);
    void Set<T>(string key, T value);
    IEnumerable<object> Keys { get; }
    void Remove(string key);
    T? GetOrCreate<T>(string key, Func<T> createFunc);
    T? Get<T>(string key);
}