using k8s.Models;
using KubeOps.KubernetesClient;
using KubeOps.KubernetesClient.LabelSelectors;
using Microsoft.Extensions.Options;
using Vecc.K8s.MultiCluster.Api.Models.Core;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class KubernetesApiCache : ICache
    {
        private readonly ILogger<KubernetesApiCache> _logger;
        private readonly IKubernetesClient _kubernetesClient;
        private readonly IOptions<MultiClusterOptions> _options;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IRandom _random;
        private readonly AutoResetEvent _synchronizeCacheHolder = new(true);
        private readonly AutoResetEvent _setClusterCacheSemaphore = new(true);

        public KubernetesApiCache(ILogger<KubernetesApiCache> logger,
            IKubernetesClient kubernetesClient,
            IOptions<MultiClusterOptions> options,
            IDateTimeProvider dateTimeProvider,
            IRandom random)
        {
            _logger = logger;
            _kubernetesClient = kubernetesClient;
            _options = options;
            _dateTimeProvider = dateTimeProvider;
            _random = random;
        }

        public async Task<DateTime?> GetClusterHeartbeatTimeAsync(string clusterIdentifier)
        {
            using var _scope = _logger.BeginScope(new { clusterIdentifier });
            _logger.LogTrace("Getting cluster heartbeat time");

            var item = await _kubernetesClient.GetAsync<V1ClusterCache>(clusterIdentifier, _options.Value.Namespace);
            if (item != null)
            {
                if (DateTime.TryParseExact(item.LastHeartbeat, "O", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var lastHeartbeat))
                {
                    _logger.LogTrace("Got {lastHeartbeat}", lastHeartbeat);
                    return lastHeartbeat;
                }
                _logger.LogError("Failed to parse last heartbeat time {lastheartbeat}", item.LastHeartbeat);
            }

            _logger.LogWarning("Cluster heartbeat time not found");
            return null;
        }

        public async Task<string[]> GetClusterIdentifiersAsync()
        {
            _logger.LogTrace("Getting cluster identifiers");
            var items = await _kubernetesClient.ListAsync<V1ClusterCache>(_options.Value.Namespace);
            var result = items?.Select(x => x.GetLabel("clusteridentifier"))?.Distinct()?.ToArray() ?? Array.Empty<string>();

            _logger.LogTrace("Found {@result} identifieres", result);
            return result;
        }

        public async Task<int> GetEndpointsCountAsync(string ns, string name)
        {
            _logger.LogTrace("Getting endpoint count for {ns}/{name}", ns, name);

            var service = await GetOrCreateServiceCache(ns, name, false);
            var result = service?.EndpointCount ?? 0;

            _logger.LogTrace("Got {result} for {ns}/{name}", result, ns, name);
            return result;
        }

        public async Task<Models.Core.Host?> GetHostInformationAsync(string hostname)
        {
            using var _scope = _logger.BeginScope(new { hostname });

            _logger.LogTrace("Getting host information");
            var host = await GetOrCreateHostnameCache(hostname, false);

            if (host != null)
            {
                var result = new Models.Core.Host
                {
                    HostIPs = host.Addresses.Select(x => x.ToCore()).ToArray(),
                    Hostname = hostname
                };
                _logger.LogTrace("Got {@host}", result);
                return result;
            }

            _logger.LogInformation("Host information not found, it was probably deleted");
            return null;
        }

        public async Task<string[]> GetHostnamesAsync()
        {
            _logger.LogTrace("Getting hostnames");

            var hostnames = await _kubernetesClient.ListAsync<V1HostnameCache>(_options.Value.Namespace);
            var result = hostnames.Select(x => x.Hostname ?? x.GetLabel("hostname")).Distinct().ToArray();

            _logger.LogTrace("Got {@hostnames}", result);
            return result;
        }

        public async Task<Models.Core.Host[]?> GetHostsAsync(string clusterIdentifier)
        {
            using var _scope = _logger.BeginScope(new { clusterIdentifier });
            _logger.LogTrace("Getting hosts");
            var clusterCache = await GetOrCreateClusterCache(clusterIdentifier, false);

            if (clusterCache == null)
            {
                _logger.LogDebug("Cluster cache not found");
                return null;
            }

            var result = clusterCache.Hostnames.Select(x => x.ToCore()).ToArray();

            _logger.LogTrace("Got {@hosts}", result);
            return result;
        }

        public async Task<string> GetLastResourceVersionAsync(string uniqueIdentifier)
        {
            using var _scope = _logger.BeginScope(new { uniqueIdentifier });
            _logger.LogTrace("Getting last resource version");
            var resource = await _kubernetesClient.GetAsync<V1ResourceCache>(uniqueIdentifier, _options.Value.Namespace);
            var result = resource?.CurrentResourceVersion ?? string.Empty;

            _logger.LogTrace("Last resource version is {result}", result);
            return result;
        }

        public async Task<bool> IsServiceMonitoredAsync(string ns, string name)
        {
            using var _scope = _logger.BeginScope(new { @namespace = ns, name });
            _logger.LogTrace("Checking if service");

            var service = await GetOrCreateServiceCache(ns, name, false);
            var result = service != null;

            _logger.LogTrace("Service monitor state: {result}", result);
            return result;
        }

        public async Task RemoveClusterCacheAsync(string clusterIdentifier)
        {
            using var _scope = _logger.BeginScope(new { clusterIdentifier });
            _logger.LogTrace("Removing cluster from cache");

            var deleted = false;
            for (var iterator = 0; iterator < 5; iterator++)
            {
                try
                {
                    var cluster = await GetOrCreateClusterCache(clusterIdentifier, false);

                    if (cluster != null)
                    {
                        await _kubernetesClient.DeleteAsync(cluster);
                    }
                    deleted = true;
                    break;
                }
                catch (Exception thrown)
                {
                    var jitter = _random.Next(5000);
                    _logger.LogWarning(thrown, "Error removing cluster cache. Attempt {attempt} waiting {jitter} ms", iterator + 1, jitter);
                    await Task.Delay(jitter);
                }
            }

            if (!deleted)
            {
                throw new Exception("Unable to delete cluster cache after multiple attempts");
            }

            _logger.LogTrace("Done");
        }

        public async Task SetClusterCacheAsync(string clusterIdentifier, Models.Core.Host[] hosts)
        {
            using var _scope = _logger.BeginScope(new { clusterIdentifier });
            _logger.LogTrace("Setting cluster cache to {@hosts}", hosts);

            var set = false;
            try
            {
                _setClusterCacheSemaphore.WaitOne();
                for (var iterator = 0; iterator < 5; iterator++)
                {
                    try
                    {
                        var cluster = await GetOrCreateClusterCache(clusterIdentifier);

                        cluster!.Hostnames = hosts.Select(x => new V1ClusterCache.HostCache
                        {
                            Hostname = x.Hostname,
                            HostIPs = x.HostIPs.Select(V1ClusterCache.HostIPCache.FromCore).Distinct().ToArray()
                        }).ToArray();

                        cluster.LastHeartbeat = DateTime.UtcNow.ToString("O");

                        await _kubernetesClient.SaveAsync(cluster);
                        set = true;
                        break;
                    }
                    catch (Exception exception)
                    {
                        var jitter = _random.Next(500);
                        _logger.LogError(exception, "Error saving cluster cache. Attempt {attempt}. Waiting {jitter} ms", iterator + 1, jitter);
                        await Task.Delay(jitter);
                    }
                }

                if (!set)
                {
                    throw new Exception("Unable to set cluster cache after multiple attempts");
                }
            }
            finally
            {
                _setClusterCacheSemaphore.Set();
            }

            _logger.LogTrace("Done");
        }

        public async Task SetClusterHeartbeatAsync(string clusterIdentifier, DateTime heartbeat)
        {
            using var _scope = _logger.BeginScope(new { clusterIdentifier, heartbeat });
            _logger.LogDebug("Updating cluster heartbeat");

            try
            {
                _setClusterCacheSemaphore.WaitOne();
                var saved = false;
                for (var iterator = 0; iterator < 5; iterator++)
                {
                    try
                    {
                        var cluster = await GetOrCreateClusterCache(clusterIdentifier);
                        cluster!.LastHeartbeat = heartbeat.ToString("O");
                        await _kubernetesClient.SaveAsync(cluster);
                        saved = true;
                        break;
                    }
                    catch (Exception exception)
                    {
                        var jitter = _random.Next(500);
                        _logger.LogError(exception, "Unable to set cluster heartbeat. Attempt {attempt}. Waiting {jitter} ms",
                            iterator + 1, jitter);
                        await Task.Delay(jitter);
                    }
                }
                if (!saved)
                {
                    throw new Exception("Unable to save cluster heartbeat after multiple attempts");
                }
            }
            finally
            {
                _setClusterCacheSemaphore.Set();
            }

            _logger.LogDebug("Done");
        }

        public async Task SetEndpointsCountAsync(string ns, string name, int count)
        {
            using var _scope = _logger.BeginScope(new { @namespace = ns, name, count });
            _logger.LogTrace("Setting endpoint count");

            var set = false;
            for (var iterator = 0; iterator < 5; iterator++)
            {
                try
                {
                    var service = await GetOrCreateServiceCache(ns, name);

                    service!.EndpointCount = count;

                    await _kubernetesClient.SaveAsync(service);
                    set = true;
                    break;
                }
                catch (Exception exception)
                {
                    var jitter = _random.Next(500);
                    _logger.LogWarning(exception, "Error setting endpoint. Attempt {attempt}. Waiting {jitter} ms", iterator + 1, jitter);
                    await Task.Delay(jitter);
                }
            }

            if (!set)
            {
                throw new Exception("Unable to set cluster endpoint count after multiple attempts");
            }

            _logger.LogTrace("Done");
        }

        public async Task SetResourceVersionAsync(string uniqueIdentifier, string version)
        {
            using var _scope = _logger.BeginScope(new { uniqueIdentifier, version });
            _logger.LogDebug("Setting resource version");

            var set = false;
            for (var iterator = 0; iterator < 5; iterator++)
            {
                try
                {
                    var resource = await _kubernetesClient.GetAsync<V1ResourceCache>(uniqueIdentifier, _options.Value.Namespace);
                    if (resource == null)
                    {
                        resource = new V1ResourceCache();
                        var metadata = resource.EnsureMetadata();
                        metadata.Name = uniqueIdentifier;
                        metadata.SetNamespace(_options.Value.Namespace);
                    }
                    resource.CurrentResourceVersion = version;
                    await _kubernetesClient.SaveAsync(resource);
                    set = true;
                    break;
                }
                catch (Exception exception)
                {
                    var jitter = _random.Next(500);
                    _logger.LogWarning(exception, "Unable to set resource version. Attempt {attempt}. Waiting {jitter} ms", iterator + 1, jitter);
                    await Task.Delay(jitter);
                }
            }

            if (!set)
            {
                throw new Exception("Unable to set resource version after multiple attempts");
            }

            _logger.LogTrace("Done");
        }

        public async Task SynchronizeCachesAsync()
        {
            using var scope = _logger.BeginScope(new { CacheSynchronizeId = Guid.NewGuid() });

            _logger.LogInformation("Beginning to synchronize cache");
            try
            {
                _synchronizeCacheHolder.WaitOne();
                _logger.LogInformation("Waiting a second");
                await Task.Delay(1000);
                _logger.LogInformation("Synchronizing caches");
                var syncronized = false;
                for (var iterator = 0; iterator < 5; iterator++)
                {
                    try
                    {
                        _logger.LogInformation("Getting clusters");
                        var clusters = await _kubernetesClient.ListAsync<V1ClusterCache>(_options.Value.Namespace);
                        _logger.LogDebug("Got {count} clusters", clusters.Count);

                        var hosts = new Dictionary<string, List<HostIP>>();

                        _logger.LogInformation("Combining host entries");
                        foreach (var cluster in clusters)
                        {
                            foreach (var hostname in cluster.Hostnames)
                            {
                                if (!hosts.ContainsKey(hostname.Hostname))
                                {
                                    hosts[hostname.Hostname] = new List<HostIP>();
                                }
                                hosts[hostname.Hostname].AddRange(hostname.HostIPs.Select(x => x.ToCore()));
                            }
                        }
                        _logger.LogDebug("Got {count} host entries", hosts.Count);

                        _logger.LogInformation("Getting current hostnames");
                        var hostcaches = await _kubernetesClient.ListAsync<V1HostnameCache>(_options.Value.Namespace);
                        _logger.LogDebug("Hostnames found: {count}", hostcaches.Count);
                        _logger.LogTrace("Hostnames: {@hostnames}", hostcaches.Select(x => new { Hostname = x.Hostname ?? x.GetLabel("hostname"), IPs = x.Addresses }));

                        _logger.LogInformation("Setting hostnames ip addresses");
                        foreach (var host in hosts)
                        {
                            var _hostScope = _logger.BeginScope(new { Hostname = host.Key });
                            try
                            {
                                var hostcache = (await GetOrCreateHostnameCache(host.Key))!;
                                var outOfSync = false;

                                if (hostcache.Hostname == null)
                                {
                                    hostcache.Hostname = host.Key;
                                    outOfSync = true;
                                }

                                if (hostcache.Addresses.Length != host.Value.Count)
                                {
                                    _logger.LogDebug("Address length mismatch");
                                    outOfSync = true;
                                }
                                else if (hostcache.Addresses.Any(src => !host.Value.Any(dst => dst.Equals(src.ToCore()))))
                                {
                                    _logger.LogDebug("Address value mismatch");
                                    outOfSync = true;
                                }
                                else
                                {
                                    _logger.LogTrace("No changes found");
                                }

                                //check for address changes
                                if (outOfSync && host.Value.Count != 0)
                                {
                                    _logger.LogTrace("Setting host cache from {@oldAddresses} to {@addresses}", hostcache.Addresses, host.Value);
                                    hostcache.Addresses = host.Value.Select(V1HostnameCache.HostIPCache.FromCore).ToArray();
                                    await _kubernetesClient.SaveAsync(hostcache);
                                    _logger.LogTrace("Done saving host cache entry");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Unable to set hostname cache for {@hostname} with addresses {@addresses}", host.Key, host.Value);
                                continue;
                            }
                        }

                        _logger.LogInformation("Removing old hosts");
                        foreach (var hostcache in hostcaches)
                        {
                            var hostname = hostcache.Hostname ?? hostcache.GetLabel("hostname");
                            if (!hosts.ContainsKey(hostname) || hosts[hostname].Count == 0)
                            {
                                _logger.LogDebug("Removing host cache entry for {@hostname}", hostname);
                                await _kubernetesClient.DeleteAsync(hostcache);
                            }
                        }
                        syncronized = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        var jitter = _random.Next(500);
                        _logger.LogWarning(ex, "Unable to synchronize caches. Attempt {attempt}. Waiting {jitter} ms", iterator+1, jitter);
                        await Task.Delay(jitter);
                    }
                }
                if (!syncronized)
                {
                    throw new Exception("Multiple synchronization attempts failed.");
                }
            }
            finally
            {
                _synchronizeCacheHolder.Set();
            }

            _logger.LogInformation("Done synchronizing caches");
        }

        public async Task TrackServiceAsync(string ns, string name)
        {
            using var _scope = _logger.BeginScope(new { @namespace = ns, name });
            var tracked = false;
            for (var iterator = 0; iterator < 5; iterator++)
            {
                try
                {
                    _logger.LogDebug("Tracking service");
                    await GetOrCreateServiceCache(ns, name);
                    tracked = true;
                    break;
                }
                catch (Exception exception)
                {
                    var jitter = _random.Next(500);
                    _logger.LogWarning(exception, "Unable to track service. Attempt {attempt}. Waiting {jitter} ms", iterator + 1, jitter);
                    await Task.Delay(jitter);
                }
            }

            if (!tracked)
            {
                throw new Exception("Unable to track service after multiple attempts");
            }

            _logger.LogDebug("Done");
        }

        public async Task UntrackAllServicesAsync()
        {
            _logger.LogInformation("Untracking all services");
            var deleted = false;
            for (var iterator = 0; iterator < 5; iterator++)
            {
                try
                {
                    var serviceCaches = await _kubernetesClient.ListAsync<V1ServiceCache>(_options.Value.Namespace);
                    await _kubernetesClient.DeleteAsync(serviceCaches);
                    deleted = true;
                    break;
                }
                catch (Exception exception)
                {
                    var jitter = _random.Next(500);
                    _logger.LogWarning(exception, "Unable to delete all services. Attempt {attempt}. Waiting {jitter} ms", iterator + 1, jitter);
                    await Task.Delay(jitter);
                }
            }

            if (!deleted)
            {
                throw new Exception("Unable to untrack all services after multiple attempts.");
            }

            _logger.LogDebug("Done");
        }

        private string GenerateName(string name)
        {
            name = string.Concat(name.Where(x => char.IsLetterOrDigit(x))).ToLower();

            while (char.IsDigit(name[0]))
            {
                name = name.Substring(1);
            }

            if (name.Length <= 63)
            {
                return name;
            }

            var newName = name.Substring(0, 46) + "-" + Guid.NewGuid().ToString("N").Substring(16).ToLower();
            _logger.LogDebug("Name {name} is too long, converting to {newname}", name, newName);
            return newName;
        }

        private async Task<V1ClusterCache?> GetOrCreateClusterCache(string clusterIdentifier, bool create = true)
        {
            using var _scope = _logger.BeginScope(new { clusterIdentifier, create });
            for (var iterator = 0; iterator < 5; iterator++)
            {
                try
                {
                    var clusters = await _kubernetesClient.ListAsync<V1ClusterCache>(_options.Value.Namespace, new EqualsSelector("clusteridentifier", clusterIdentifier));
                    if (clusters.Count == 1)
                    {
                        _logger.LogDebug("Cluster cache entry found");
                        return clusters[0];
                    }
                    else if (clusters.Count > 1)
                    {
                        _logger.LogError("Too many cluster cache objects matching, returning the oldest");
                        return clusters.OrderBy(x => x.Metadata.CreationTimestamp ?? DateTime.MinValue).First();
                    }

                    if (create)
                    {
                        _logger.LogInformation("Cluster cache entry didn't exist, creating it.");
                        var cluster = await _kubernetesClient.GetAsync<V1ClusterCache>(clusterIdentifier, _options.Value.Namespace);
                        if (cluster != null)
                        {
                            return cluster;
                        }

                        cluster = new V1ClusterCache();

                        var metadata = cluster.EnsureMetadata();
                        metadata.Name = GenerateName(clusterIdentifier);
                        metadata.SetNamespace(_options.Value.Namespace);
                        cluster.LastHeartbeat = _dateTimeProvider.UtcNow.ToString("O");
                        cluster.SetLabel("clusteridentifier", clusterIdentifier);
                        await _kubernetesClient.SaveAsync(cluster);

                        return cluster;
                    }

                    _logger.LogDebug("Cluster cache entry not found");
                    return null;
                }
                catch (Exception excepion)
                {
                    var jitter = _random.Next(500);
                    _logger.LogWarning(excepion, "Unable to get or create a cluster cache object. Attempt {attempt}. Waiting {jitter} ms", iterator + 1, jitter);
                    await Task.Delay(jitter);
                }
            }

            throw new Exception("Unable to get or create cluster cache object after multiple attempts.");
        }

        private async Task<V1HostnameCache?> GetOrCreateHostnameCache(string hostname, bool create = true)
        {
            for (var iterator = 0; iterator < 5; iterator++)
            {
                try
                {
                    var allCaches = await _kubernetesClient.ListAsync<V1HostnameCache>(_options.Value.Namespace);
                    var caches = allCaches.Where(x => x.Hostname == hostname || x.GetLabel("hostname") == hostname).ToArray();

                    if (caches.Length == 1)
                    {
                        _logger.LogDebug("Hostname cache entry found for {hostname}", hostname);
                        return caches[0];
                    }
                    else if (caches.Length > 1)
                    {
                        _logger.LogError("Too many hostname cache objects matching {hostname}, returning the oldest", hostname);
                        return caches.OrderBy(x => x.Metadata.CreationTimestamp ?? DateTime.MinValue).First();
                    }

                    if (create)
                    {
                        _logger.LogDebug("Hostname cache entry didn't exist, creating it. {@hostname}", hostname);
                        var hostnameCache = new V1HostnameCache();

                        var metadata = hostnameCache.EnsureMetadata();
                        metadata.Name = GenerateName(hostname);
                        metadata.SetNamespace(_options.Value.Namespace);
                        hostnameCache.Hostname = hostname;

                        await _kubernetesClient.SaveAsync(hostnameCache);

                        return hostnameCache;
                    }

                    _logger.LogDebug("Hostname cache entry not found for {hostname}", hostname);
                    return null;
                }
                catch(Exception exception)
                {
                    var jitter = _random.Next(500);
                    _logger.LogWarning(exception, "Unable to get or create the hostname cache entry. Attempt {attempt}. Waiting {jitter} ms",
                        iterator + 1, jitter);
                    await Task.Delay(jitter);
                }
            }

            throw new Exception("Unable to get or create the hostname cache entry after multiple attempts.");
        }

        private async Task<V1ServiceCache?> GetOrCreateServiceCache(string namespaceName, string name, bool createMissing = true)
        {
            for (var iterator = 0; iterator < 5; iterator++)
            {
                try
                {
                    var items = await _kubernetesClient.ListAsync<V1ServiceCache>(_options.Value.Namespace, new EqualsSelector("namespace", namespaceName), new EqualsSelector("name", name));
                    if (items.Count == 1)
                    {
                        _logger.LogDebug("Service cache entry found for {namespace}/{name}", namespaceName, name);
                        return items[0];
                    }
                    else if (items.Count > 1)
                    {
                        _logger.LogInformation("Too many service cache objects matching {namespace}/{name}, returning the oldest", namespaceName, name);
                        return items.OrderBy(x => x.Metadata.CreationTimestamp ?? DateTime.MinValue).First();
                    }

                    if (createMissing)
                    {
                        _logger.LogDebug("Service cache entry didn't exist, creating it. {namespace}/{name}", namespaceName, name);
                        var serviceCache = new V1ServiceCache();
                        var metadata = serviceCache.EnsureMetadata();
                        var labels = metadata.EnsureLabels();

                        labels["namespace"] = namespaceName;
                        labels["name"] = name;

                        metadata.Name = GenerateName(namespaceName + "." + name);
                        metadata.SetNamespace(_options.Value.Namespace);
                        serviceCache.Service = new V1ObjectReference(name: name, namespaceProperty: namespaceName);
                        await _kubernetesClient.CreateAsync(serviceCache);

                        return serviceCache;
                    }

                    _logger.LogDebug("Service cache entry not found for {namespace}/{name}", namespaceName, name);
                    return null;
                }
                catch (Exception exception)
                {
                    var jitter = _random.Next(500);
                    _logger.LogWarning(exception, "Unable to get or create service cache. Attempt {attempt}. Waiting {jitter} ms", iterator + 1, jitter);
                    await Task.Delay(jitter);
                }
            }

            throw new Exception("Unable to get or create service cache after multiple attempts.");
        }
    }
}
