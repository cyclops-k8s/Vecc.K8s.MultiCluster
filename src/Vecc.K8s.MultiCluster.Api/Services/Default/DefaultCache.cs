using Microsoft.Extensions.Options;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;
using k8s.Models;

namespace Vecc.K8s.MultiCluster.Api.Services.Default;

public class MemoryCache(
    IBasicCache _cache,
    IKubernetesCache _kubernetesCache,
    IOptions<MultiClusterOptions> _options) : ICache
{
    public Task<DateTime?> GetClusterHeartbeatTimeAsync(string clusterIdentifier)
        => _kubernetesCache.GetClusterHeartbeatTimeAsync(clusterIdentifier);

    public Task<string[]> GetClusterIdentifiersAsync()
        => _kubernetesCache.GetClusterIdentifiersAsync();

    public async Task<int> GetEndpointsCountAsync(string ns, string name)
    {
        var service = await GetOrCreateServiceCache(ns, name, false);
        var result = service?.EndpointCount ?? 0;
        return result;
    }

    public Task<Models.Core.Host?> GetHostInformationAsync(string hostname)
        => _kubernetesCache.GetHostInformationAsync(hostname);

    public Task<string[]> GetHostnamesAsync()
        => _kubernetesCache.GetHostnamesAsync();

    public Task<Models.Core.Host[]?> GetHostsAsync(string clusterIdentifier)
        => _kubernetesCache.GetHostsAsync(clusterIdentifier);

    public Task<string> GetLastResourceVersionAsync(string uniqueIdentifier)
    {
        lock (_cache)
        {
            _cache.TryGetValue(GetResourceVersionKey(uniqueIdentifier), out string? cacheValue);
            var result = cacheValue ?? string.Empty;
            return Task.FromResult(result);
        }
    }

    public Task<bool> IsServiceMonitoredAsync(string ns, string name)
        => Task.FromResult(_cache.TryGetValue<V1ServiceCache>(GetServiceCacheKey(ns, name), out _));

    public Task RemoveClusterCacheAsync(string clusterIdentifier)
        => _kubernetesCache.RemoveClusterCacheAsync(clusterIdentifier);

    public Task SetClusterCacheAsync(string identifier, Models.Core.Host[] hosts)
        => _kubernetesCache.SetClusterCacheAsync(identifier, hosts);

    public Task SetClusterHeartbeatAsync(string clusterIdentifier, DateTime heartbeat)
        => _kubernetesCache.SetClusterHeartbeatAsync(clusterIdentifier, heartbeat);

    public async Task SetEndpointsCountAsync(string ns, string name, int count)
    {
        var serviceCache = await GetOrCreateServiceCache(ns, name);
        serviceCache!.EndpointCount = count;
    }

    public Task SetResourceVersionAsync(string uniqueIdentifier, string version)
    {
        lock (_cache)
        {
            _cache.Set(GetResourceVersionKey(uniqueIdentifier), version);
        }
        return Task.CompletedTask;

    }

    public Task SynchronizeCachesAsync()
        => _kubernetesCache.SynchronizeCachesAsync();

    public Task TrackServiceAsync(string ns, string name)
        => GetOrCreateServiceCache(ns, name);

    public Task UntrackAllServicesAsync()
    {
        var rawKeys = Array.Empty<object>();

        lock (_cache)
        {
            rawKeys = _cache.Keys.ToArray();
        }

        var keys = rawKeys.OfType<KeyValuePair<string, object>>()
            .Where(kvp=>kvp.Key.StartsWith("service-"))
            .Select(kvp => kvp.Key);

        foreach (var key in keys)
        {
            _cache.Remove(key);
        }

        return Task.CompletedTask;
    }

    private async Task<V1ServiceCache?> GetOrCreateServiceCache(string namespaceName, string name, bool createMissing = true)
    {
        if (createMissing)
        {
            var cacheKey = GetServiceCacheKey(namespaceName, name);
            _cache.GetOrCreate(cacheKey, () =>
                {
                    var serviceCache = new V1ServiceCache();
                    var metadata = serviceCache.EnsureMetadata();
                    var labels = metadata.EnsureLabels();

                    labels["namespace"] = namespaceName;
                    labels["name"] = name;

                    metadata.Name = namespaceName + "." + name;
                    metadata.SetNamespace(_options.Value.Namespace);
                    serviceCache.Service = new V1ObjectReference
                    {
                        Name = name,
                        NamespaceProperty = namespaceName
                    };
                    return serviceCache;
                }
            );
        }

        return _cache.Get<V1ServiceCache>(GetServiceCacheKey(namespaceName, name));
    }

    private string GetIngressKey(string ns, string name)
        => $"ingress-{ns}-{name}";

    private string GetResourceVersionKey(string uniqueIdentifier)
        => $"resourceVersion-{uniqueIdentifier}";
 
    private string GetServiceCacheKey(string ns, string name)
        => $"service-{ns}-{name}";
}
