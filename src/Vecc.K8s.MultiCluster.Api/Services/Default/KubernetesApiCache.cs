using k8s;
using k8s.Models;
using KubeOps.KubernetesClient;
using KubeOps.KubernetesClient.LabelSelectors;
using Microsoft.Extensions.Options;
using System.Runtime.Intrinsics.Arm;
using Vecc.Dns.Parts.RecordData;
using Vecc.K8s.MultiCluster.Api.Models.Core;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class KubernetesApiCache : ICache
    {
        private readonly ILogger<KubernetesApiCache> _logger;
        private readonly IKubernetesClient _kubernetesClient;
        private readonly IOptions<MultiClusterOptions> _options;

        public KubernetesApiCache(ILogger<KubernetesApiCache> logger, IKubernetesClient kubernetesClient, IOptions<MultiClusterOptions> options)
        {
            _logger = logger;
            _kubernetesClient = kubernetesClient;
            _options = options;
        }

        public async Task<DateTime?> GetClusterHeartbeatTimeAsync(string clusterIdentifier)
        {
            var item = await _kubernetesClient.GetAsync<V1ClusterCache>(clusterIdentifier, _options.Value.Namespace);
            if (item != null)
            {
                if (DateTime.TryParseExact(item.LastHeartbeat, "O", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var lastHeartbeat))
                {
                    return lastHeartbeat;
                }
            }
            return null;
        }

        public async Task<string[]> GetClusterIdentifiersAsync()
        {
            var items = await _kubernetesClient.ListAsync<V1ClusterCache>(_options.Value.Namespace);
            var result = items?.Select(x => x.Metadata.Name)?.ToArray();

            return result ?? Array.Empty<string>();
        }

        public async Task<int> GetEndpointsCountAsync(string ns, string name)
        {
            var services = await _kubernetesClient.ListAsync<V1ServiceCache>(_options.Value.Namespace, new EqualsSelector("namespace", ns), new EqualsSelector("name", name));

            var service = services.FirstOrDefault(x => x.Service.NamespaceProperty == ns && x.Service.Name == name);

            return service?.EndpointCount ?? 0;
        }

        public async Task<Models.Core.Host?> GetHostInformationAsync(string hostname)
        {
            var host = await _kubernetesClient.GetAsync<V1HostnameCache>(hostname, _options.Value.Namespace);

            if (host != null)
            {
                var result = new Models.Core.Host
                {
                    HostIPs = host.Addresses.Select(x => x.ToCore()).ToArray(),
                    Hostname = hostname
                };
                return result;
            }

            return null;
        }

        public async Task<string[]> GetHostnamesAsync(string clusterIdentifier)
        {
            if (string.IsNullOrWhiteSpace(clusterIdentifier))
            {
                //Get all hostnames from all clusters
                var hostnames = await _kubernetesClient.ListAsync<V1HostnameCache>(_options.Value.Namespace);
                var result = hostnames.Select(x => x.Metadata.Name).ToArray();

                return result;
            }
            else
            {
                var clusterCache = await _kubernetesClient.GetAsync<V1ClusterCache>(clusterIdentifier, _options.Value.Namespace);

                if (clusterCache == null)
                {
                    return Array.Empty<string>();
                }

                var result = clusterCache.Hostnames.Select(x => x.Hostname).ToArray();
                return result;
            }
        }

        public async Task<Models.Core.Host[]?> GetHostsAsync(string clusterIdentifier)
        {
            var clusterCache = await _kubernetesClient.GetAsync<V1ClusterCache>(clusterIdentifier, _options.Value.Namespace);

            if (clusterCache == null)
            {
                return null;
            }

            var result = clusterCache.Hostnames.Select(x => x.ToCore()).ToArray();
            return result;
        }

        public async Task<string> GetLastResourceVersionAsync(string uniqueIdentifier)
        {
            var resource = await _kubernetesClient.GetAsync<V1ResourceCache>(uniqueIdentifier, _options.Value.Namespace);

            if (resource != null)
            {
                return resource.CurrentResourceVersion;
            }

            return string.Empty;
        }

        public async Task<bool> IsServiceMonitoredAsync(string ns, string name)
        {
            var services = await _kubernetesClient.ListAsync<V1ServiceCache>(_options.Value.Namespace, new EqualsSelector("namespace", ns), new EqualsSelector("name", name));

            return services.Any();
        }

        public async Task RemoveClusterHostnameAsync(string clusterIdentifier, string hostname)
        {
            var cluster = await _kubernetesClient.GetAsync<V1ClusterCache>(clusterIdentifier, _options.Value.Namespace);

            if (cluster != null)
            {
                var newHosts = cluster.Hostnames.Where(x => x.Hostname != hostname).ToArray();
                cluster.Hostnames = newHosts;

                await _kubernetesClient.UpdateAsync(cluster);
            }

            var host = await _kubernetesClient.GetAsync<V1HostnameCache>(hostname, _options.Value.Namespace);
            if (host != null)
            {
                var newHosts = host.Addresses.Where(x => x.ClusterIdentifier != clusterIdentifier).ToArray();
                host.Addresses = newHosts;

                await _kubernetesClient.UpdateAsync(host);
            }
        }

        public async Task SetClusterHeartbeatAsync(string clusterIdentifier, DateTime heartbeat)
        {
            _logger.LogDebug("Updating cluster heartbeat");
            var cluster = await GetOrCreateClusterCache(clusterIdentifier);
            cluster.LastHeartbeat = heartbeat.ToString("O");
            await _kubernetesClient.SaveAsync(cluster);
        }

        public async Task SetEndpointsCountAsync(string ns, string name, int count)
        {
            var service = await GetOrCreateServiceCache(ns, name);

            service.EndpointCount = count; ;

            await _kubernetesClient.SaveAsync(service);
        }

        public async Task<bool> SetHostIPsAsync(string hostname, string clusterIdentifier, HostIP[] hostIPs)
        {
            using var _ = _logger.BeginScope("{@clusterIdentifier} {@hostname}", clusterIdentifier, hostname);

            var result = false;
            _logger.LogDebug("Setting host IPs to {@hostIPs}", hostIPs);

            _logger.LogDebug("Updating cluster cache");
            var clusterCache = await GetOrCreateClusterCache(clusterIdentifier);
            var clusterHosts = clusterCache.Hostnames.Where(x => x.Hostname != hostname).Append(new V1ClusterCache.HostCache
            {
                HostIPs = hostIPs.Select(V1ClusterCache.HostIPCache.FromCore).ToArray(),
                Hostname = hostname
            }).ToArray();
            clusterCache.Hostnames = clusterHosts;
            _logger.LogDebug("Setting {clusterIdentifier} to {@clusterCache}", clusterIdentifier, clusterCache);
            await _kubernetesClient.SaveAsync(clusterCache);

            _logger.LogDebug("Updating hostname cache");
            var hostnameCache = await GetOrCreateHostnameCache(hostname);
            var hostAddresses = hostnameCache.Addresses.Where(x => x.ClusterIdentifier == clusterIdentifier);
            if (!hostAddresses.Any())
            {
                _logger.LogDebug("New cluster for @{hostname} detected.", hostname);
                result = true;
            }
            else if (hostAddresses.Count() != hostIPs.Length)
            {
                _logger.LogDebug("Host IP Count differs");
            }
            else if (hostAddresses.Any(src => !hostIPs.Any(dst => dst.Equals(src))))
            {
                _logger.LogDebug("Host IP array differs.");
                result = true;
            }
            else
            {
                _logger.LogDebug("Host IP array is the same.");
            }

            if (result)
            {
                var clusters = hostnameCache.Addresses.Where(x => x.ClusterIdentifier != clusterIdentifier).Union(hostIPs.Select(V1HostnameCache.HostIPCache.FromCore)).ToArray();
                hostnameCache.Addresses = clusters;
                _logger.LogDebug("Sending to kubernetes");
                await _kubernetesClient.SaveAsync(hostnameCache);
            }

            _logger.LogDebug("Done");
            return result;
        }

        public async Task SetResourceVersionAsync(string uniqueIdentifier, string version)
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
        }

        public async Task SynchronizeCachesAsync()
        {
            _logger.LogInformation("Syncronizing caches");
            _logger.LogInformation("Getting clusters");
            var clusters = await _kubernetesClient.ListAsync<V1ClusterCache>(_options.Value.Namespace);
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

            _logger.LogInformation("Updating host cache");
            var hostcaches = await _kubernetesClient.ListAsync<V1HostnameCache>(_options.Value.Namespace);
            foreach (var hostcache in hostcaches)
            {
                if (hosts.TryGetValue(hostcache.Name(), out var host))
                {
                    var update = false;

                    if (host.Count != hostcache.Addresses.Length)
                    {
                        _logger.LogInformation("Host cache length mismatch {@hostname}", hostcache.Name());
                        update = true;
                    }
                    else if (hostcache.Addresses.Any(src => !host.Any(dst => dst.Equals(src.ToCore()))))
                    {
                        _logger.LogInformation("Host cache value mismatch {@hostname}, got {@actual} expected {@expected}", hostcache.Name(), hostcache.Addresses, host);
                        update = true;
                    }
                    else
                    {
                        _logger.LogDebug("No changes found");
                    }

                    if (update)
                    {
                        _logger.LogInformation("Updating host cache for {@hostname} from {@oldaddresses} to {@newaddresses}", hostcache.Name(), hostcache.Addresses, host);
                        hostcache.Addresses = host.Select(V1HostnameCache.HostIPCache.FromCore).ToArray();

                        _logger.LogInformation("Sending to kubernetes");
                        await _kubernetesClient.SaveAsync(hostcache);
                    }
                }
            }
        }

        public async Task TrackServiceAsync(string ns, string name)
        {
            await GetOrCreateServiceCache(ns, name);
        }

        public async Task UntrackAllServicesAsync()
        {
            var serviceCaches = await _kubernetesClient.ListAsync<V1ServiceCache>(_options.Value.Namespace);
            await _kubernetesClient.DeleteAsync(serviceCaches);
        }

        private async Task<V1ServiceCache> GetOrCreateServiceCache(string namespaceName, string name)
        {
            var items = await _kubernetesClient.ListAsync<V1ServiceCache>(_options.Value.Namespace, new EqualsSelector("namespace", namespaceName), new EqualsSelector("name", name));
            if (items.Any())
            {
                return items.First();
            }

            var serviceCache = new V1ServiceCache();
            var metadata = serviceCache.EnsureMetadata();
            var labels = metadata.EnsureLabels();

            labels["namespace"] = namespaceName;
            labels["name"] = name;

            metadata.Name = namespaceName + "." + name;
            metadata.SetNamespace(_options.Value.Namespace);
            serviceCache.Service = new V1ObjectReference(name: name, namespaceProperty: namespaceName);
            await _kubernetesClient.CreateAsync(serviceCache);

            return serviceCache;
        }

        private async Task<V1ClusterCache> GetOrCreateClusterCache(string clusterIdentifier)
        {
            var cluster = await _kubernetesClient.GetAsync<V1ClusterCache>(clusterIdentifier, _options.Value.Namespace);
            if (cluster != null)
            {
                return cluster;
            }

            cluster = new V1ClusterCache();

            var metadata = cluster.EnsureMetadata();
            metadata.Name = clusterIdentifier;
            metadata.SetNamespace(_options.Value.Namespace);

            await _kubernetesClient.SaveAsync(cluster);

            return cluster;
        }

        private async Task<V1HostnameCache> GetOrCreateHostnameCache(string hostname)
        {
            var hostnameCache = await _kubernetesClient.GetAsync<V1HostnameCache>(hostname, _options.Value.Namespace);
            if (hostnameCache != null)
            {
                return hostnameCache;
            }

            hostnameCache = new V1HostnameCache();

            var metadata = hostnameCache.EnsureMetadata();
            metadata.Name = hostname;
            metadata.SetNamespace(_options.Value.Namespace);

            await _kubernetesClient.SaveAsync(hostnameCache);

            return hostnameCache;
        }
    }
}
