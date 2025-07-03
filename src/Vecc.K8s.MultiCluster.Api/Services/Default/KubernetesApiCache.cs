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
        private readonly AutoResetEvent _synchronizeCacheHolder = new (true);

        public KubernetesApiCache(ILogger<KubernetesApiCache> logger, IKubernetesClient kubernetesClient, IOptions<MultiClusterOptions> options, IDateTimeProvider dateTimeProvider)
        {
            _logger = logger;
            _kubernetesClient = kubernetesClient;
            _options = options;
            _dateTimeProvider = dateTimeProvider;
        }

        public async Task<DateTime?> GetClusterHeartbeatTimeAsync(string clusterIdentifier)
        {
            _logger.LogTrace("Getting cluster heartbeat time for {clusterIdentifier}", clusterIdentifier);

            var item = await _kubernetesClient.GetAsync<V1ClusterCache>(clusterIdentifier, _options.Value.Namespace);
            if (item != null)
            {
                if (DateTime.TryParseExact(item.LastHeartbeat, "O", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var lastHeartbeat))
                {
                    _logger.LogTrace("Got {lastHeartbeat} for {clusterIdentifier}", lastHeartbeat, clusterIdentifier);
                    return lastHeartbeat;
                }
                _logger.LogError("Failed to parse last heartbeat time for {clusterIdentifier} {lastheartbeat}", clusterIdentifier, item.LastHeartbeat);
            }

            _logger.LogWarning("Cluster heartbeat time for {clusterIdentifier} not found", clusterIdentifier);
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
            _logger.LogTrace("Getting host information for {hostname}", hostname);
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

            _logger.LogInformation("Host information for {hostname} not found, it was probably deleted", hostname);
            return null;
        }

        public async Task<string[]> GetHostnamesAsync()
        {
            _logger.LogTrace("Getting hostnames");

            var hostnames = await _kubernetesClient.ListAsync<V1HostnameCache>(_options.Value.Namespace);
            var result = hostnames.Select(x => x.GetLabel("hostname")).Distinct().ToArray();

            _logger.LogTrace("Got {@hostnames}", result);
            return result;
        }

        public async Task<Models.Core.Host[]?> GetHostsAsync(string clusterIdentifier)
        {
            _logger.LogTrace("Getting hosts for {clusterIdentifier}", clusterIdentifier);
            var clusterCache = await GetOrCreateClusterCache(clusterIdentifier, false);

            if (clusterCache == null)
            {
                _logger.LogDebug("Cluster cache not found for {clusterIdentifier}", clusterIdentifier);
                return null;
            }

            var result = clusterCache.Hostnames.Select(x => x.ToCore()).ToArray();

            _logger.LogTrace("Got {@hosts} for {clusterIdentifier}", result, clusterIdentifier);
            return result;
        }

        public async Task<string> GetLastResourceVersionAsync(string uniqueIdentifier)
        {
            _logger.LogTrace("Getting last resource version for {uniqueIdentifier}", uniqueIdentifier);
            var resource = await _kubernetesClient.GetAsync<V1ResourceCache>(uniqueIdentifier, _options.Value.Namespace);
            var result = resource?.CurrentResourceVersion ?? string.Empty;

            _logger.LogTrace("Last resource version for {uniqueIdentifier} is {result}", uniqueIdentifier, result);
            return result;
        }

        public async Task<bool> IsServiceMonitoredAsync(string ns, string name)
        {
            _logger.LogTrace("Checking if service {ns}/{name} is monitored", ns, name);

            var service = await GetOrCreateServiceCache(ns, name, false);
            var result = service != null;

            _logger.LogTrace("Service {ns}/{name} is monitored: {result}", ns, name, result);
            return service != null;
        }

        public async Task RemoveClusterCacheAsync(string clusterIdentifier)
        {
            _logger.LogTrace("Removing cluster {clusterIdentifier} from cache", clusterIdentifier);

            var cluster = await GetOrCreateClusterCache(clusterIdentifier, false);

            if (cluster != null)
            {
                await _kubernetesClient.DeleteAsync(cluster);
            }

            _logger.LogTrace("Done");
        }

        public async Task SetClusterCacheAsync(string clusterIdentifier, Models.Core.Host[] hosts)
        {
            _logger.LogTrace("Setting cluster cache for {clusterIdentifier} to {@hosts}", clusterIdentifier, hosts);

            var cluster = await GetOrCreateClusterCache(clusterIdentifier);

            cluster!.Hostnames = hosts.Select(x => new V1ClusterCache.HostCache
            {
                Hostname = x.Hostname,
                HostIPs = x.HostIPs.Select(V1ClusterCache.HostIPCache.FromCore).ToArray()
            }).ToArray();

            cluster.LastHeartbeat = DateTime.UtcNow.ToString("O");

            await _kubernetesClient.SaveAsync(cluster);

            _logger.LogTrace("Done");
        }

        public async Task SetClusterHeartbeatAsync(string clusterIdentifier, DateTime heartbeat)
        {
            _logger.LogDebug("Updating cluster heartbeat for {clusterIdentifier} to {heartbeat}", clusterIdentifier, heartbeat);

            var cluster = await GetOrCreateClusterCache(clusterIdentifier);
            cluster!.LastHeartbeat = heartbeat.ToString("O");
            await _kubernetesClient.SaveAsync(cluster);

            _logger.LogDebug("Done");
        }

        public async Task SetEndpointsCountAsync(string ns, string name, int count)
        {
            _logger.LogTrace("Setting endpoint count for {ns}/{name} to {count}", ns, name, count);

            var service = await GetOrCreateServiceCache(ns, name);

            service!.EndpointCount = count;

            await _kubernetesClient.SaveAsync(service);

            _logger.LogTrace("Done");
        }

        public async Task SetResourceVersionAsync(string uniqueIdentifier, string version)
        {
            _logger.LogDebug("Setting resource version for {uniqueIdentifier} to {version}", uniqueIdentifier, version);

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

            _logger.LogTrace("Done");
        }

        public async Task SynchronizeCachesAsync()
        {
            using var scope = _logger.BeginScope(new { CacheSynchronizeId = Guid.NewGuid() });

            _logger.LogInformation("Beginning to synchronize cache");
            _synchronizeCacheHolder.WaitOne();
            _logger.LogInformation("Waiting a second");
            await Task.Delay(1000);
            _logger.LogInformation("Synchronizing caches");

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
                _logger.LogTrace("Hostnames: {@hostnames}", hostcaches.Select(x => new { Hostname = x.GetLabel("hostname"), IPs = x.Addresses }));

                _logger.LogInformation("Setting hostnames ip addresses");
                foreach (var host in hosts)
                {
                    var hostcache = await GetOrCreateHostnameCache(host.Key);
                    var outOfSync = false;
                    if (hostcache!.Addresses.Length != host.Value.Count)
                    {
                        _logger.LogDebug("Address length mismatch for {@hostname}", host.Key);
                        outOfSync = true;
                    }
                    else if (hostcache.Addresses.Any(src => !host.Value.Any(dst => dst.Equals(src.ToCore()))))
                    {
                        _logger.LogDebug("Address value mismatch for {@hostname}", host.Key);
                        outOfSync = true;
                    }
                    else
                    {
                        _logger.LogDebug("No changes found for {@hostname}", host.Key);
                    }
                    //check for address changes
                    if (outOfSync && host.Value.Count != 0)
                    {
                        _logger.LogTrace("Setting host cache for {@hostname} from {@oldAddresses} to {@addresses}", host.Key, hostcache.Addresses, host.Value);
                        hostcache!.Addresses = host.Value.Select(V1HostnameCache.HostIPCache.FromCore).ToArray();
                        await _kubernetesClient.SaveAsync(hostcache);
                        _logger.LogTrace("Done saving host cache entry");
                    }
                }

                _logger.LogInformation("Removing old hosts");
                foreach (var hostcache in hostcaches)
                {
                    var hostname = hostcache.GetLabel("hostname");
                    if (!hosts.ContainsKey(hostname) || hosts[hostname].Count == 0)
                    {
                        _logger.LogDebug("Removing host cache entry for {@hostname}", hostname);
                        await _kubernetesClient.DeleteAsync(hostcache);
                    }
                }
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error synchronizing caches");
            }
            finally
            {
                _synchronizeCacheHolder.Set();
            }

            _logger.LogInformation("Done synchronizing caches");
        }

        public async Task TrackServiceAsync(string ns, string name)
        {
            _logger.LogInformation("Tracking service {ns}/{name}", ns, name);
            await GetOrCreateServiceCache(ns, name);
            _logger.LogDebug("Done");
        }

        public async Task UntrackAllServicesAsync()
        {
            _logger.LogInformation("Untracking all services");
            var serviceCaches = await _kubernetesClient.ListAsync<V1ServiceCache>(_options.Value.Namespace);
            await _kubernetesClient.DeleteAsync(serviceCaches);
            _logger.LogDebug("Done");
        }

        private string GenerateName(string name)
        {
            name = string.Concat(name.Where(x => x == '-' || char.IsLetterOrDigit(x) || x == '.')).ToLower();

            if (name.StartsWith('-') || char.IsDigit(name[0]))
            {
                name = name.Substring(1);
            }

            if (name.Length <= 63)
            {
                return name;
            }

            var newName = name.Substring(46) +  "-" + Guid.NewGuid().ToString("N").Substring(16).ToLower();
            _logger.LogInformation("Name {name} is too long, converting to {newname}", name, newName);
            return newName;
        }

        private async Task<V1ClusterCache?> GetOrCreateClusterCache(string clusterIdentifier, bool create = true)
        {
            var clusters = await _kubernetesClient.ListAsync<V1ClusterCache>(_options.Value.Namespace, new EqualsSelector("clusteridentifier", clusterIdentifier));
            if (clusters.Count == 1)
            {
                _logger.LogDebug("Cluster cache entry found for {clusterIdentifier}", clusterIdentifier);
                return clusters[0];
            }
            else if (clusters.Count > 1)
            {
                _logger.LogError("Too many cluster cache objects matching {clusterIdentifier}, returning the oldest", clusterIdentifier);
                return clusters.OrderBy(x => x.Metadata.CreationTimestamp ?? DateTime.MinValue).First();
            }

            if (create)
            {
                _logger.LogInformation("Cluster cache entry didn't exist, creating it. {@clusterIdentifier}", clusterIdentifier);
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

            _logger.LogDebug("Cluster cache entry not found for {clusterIdentifier}", clusterIdentifier);
            return null;
        }

        private async Task<V1HostnameCache?> GetOrCreateHostnameCache(string hostname, bool create = true)
        {
            var caches = await _kubernetesClient.ListAsync<V1HostnameCache>(_options.Value.Namespace, new EqualsSelector("hostname", hostname));

            if (caches.Count == 1)
            {
                _logger.LogDebug("Hostname cache entry found for {hostname}", hostname);
                return caches[0];
            }
            else if (caches.Count > 1)
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
                metadata.EnsureLabels()["hostname"] = hostname;

                await _kubernetesClient.SaveAsync(hostnameCache);

                return hostnameCache;
            }

            _logger.LogDebug("Hostname cache entry not found for {hostname}", hostname);
            return null;
        }

        private async Task<V1ServiceCache?> GetOrCreateServiceCache(string namespaceName, string name, bool createMissing = true)
        {
            var items = await _kubernetesClient.ListAsync<V1ServiceCache>(_options.Value.Namespace, new EqualsSelector("namespace", namespaceName), new EqualsSelector("name", name));
            if (items.Count == 1)
            {
                _logger.LogDebug("Service cache entry found for {namespace}/{name}", namespaceName, name);
                return items[0];
            }
            else if (items.Count > 1)
            {
                _logger.LogError("Too many service cache objects matching {namespace}/{name}, returning the oldest", namespaceName, name);
                return items.OrderBy(x => x.Metadata.CreationTimestamp ?? DateTime.MinValue).First();
            }

            if (createMissing)
            {
                _logger.LogInformation("Service cache entry didn't exist, creating it. {namespace}/{name}", namespaceName, name);
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
    }
}
