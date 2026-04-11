using Microsoft.Extensions.Options;
using Cyclops.MultiCluster.Models.Core;
using k8s.Models;

namespace Cyclops.MultiCluster.Services.Default;

public class MemoryCache(
    IBasicCache _cache,
    IKubernetesCache _kubernetesCache) : ICache
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
        => Task.FromResult(_cache.TryGetValue<ServiceCache>(GetServiceCacheKey(ns, name), out _));

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

        var keys = rawKeys.OfType<string>()
            .Where(key => key.StartsWith("service-"));

        foreach (var key in keys)
        {
            _cache.Remove(key);
        }

        return Task.CompletedTask;
    }

    private Task<ServiceCache?> GetOrCreateServiceCache(string namespaceName, string name, bool createMissing = true)
    {
        if (createMissing)
        {
            var cacheKey = GetServiceCacheKey(namespaceName, name);
            var result = _cache.GetOrCreate(cacheKey, () =>
                {
                    var serviceCache = new ServiceCache
                    {
                        Service = new V1ObjectReference
                        {
                            Name = name,
                            NamespaceProperty = namespaceName
                        }
                    };
                    return serviceCache;
                }
            );
            return Task.FromResult(result);
        }

        return Task.FromResult(_cache.Get<ServiceCache>(GetServiceCacheKey(namespaceName, name)));
    }

    private string GetIngressKey(string ns, string name)
        => $"ingress-{ns}-{name}";

    private string GetResourceVersionKey(string uniqueIdentifier)
        => $"resourceVersion-{uniqueIdentifier}";
 
    private string GetServiceCacheKey(string ns, string name)
        => $"service-{ns}-{name}";
}
