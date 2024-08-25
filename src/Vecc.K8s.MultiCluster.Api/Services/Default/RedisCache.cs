//using NewRelic.Api.Agent;
//using StackExchange.Redis;
//using System.Collections.Concurrent;
//using System.Text.Json;
//using Vecc.K8s.MultiCluster.Api.Models.Core;

//namespace Vecc.K8s.MultiCluster.Api.Services.Default
//{
//    public class RedisCache_Dead : ICache
//    {
//        private readonly ILogger<RedisCache_Dead> _logger;
//        private readonly IDatabase _database;
//        private readonly IQueue _queue;

//        public RedisCache_Dead(ILogger<RedisCache_Dead> logger, IDatabase database, IQueue queue)
//        {
//            _logger = logger;
//            _database = database;
//            _queue = queue;
//        }

//        public async Task<int> GetEndpointsCountAsync(string ns, string name)
//        {
//            var key = GetEndpointKey(ns, name);
//            var count = await _database.StringGetAsync(key);

//            if (int.TryParse(count, out var result))
//            {
//                return result;
//            }

//            return 0;
//        }

//        [Trace]
//        public async Task<Models.Core.Host?> GetHostInformationAsync(string hostname)
//        {
//            var key = GetHostnameIpsKey(hostname);
//            var hostData = await _database.StringGetAsync(key);

//            if (!hostData.HasValue)
//            {
//                _logger.LogWarning("{@hostname} not found in cache", hostname);
//                return null;
//            }

//            var host = JsonSerializer.Deserialize<HostModel>((string)hostData!);
//            if (host == null)
//            {
//                _logger.LogError("{@hostname} found in cache but did not fit in the model. {@hostdata}", hostname, (string)hostData!);
//                return null;
//            }

//            var result = new Models.Core.Host
//            {
//                Hostname = host.Hostname,
//                HostIPs = host.HostIPs
//            };

//            return result;
//        }

//        [Trace]
//        public async Task<string[]> GetClusterIdentifiersAsync()
//        {
//            var key = GetClusterIdentifiersKey();
//            var identifierResult = await _database.StringGetAsync(key);

//            if (!identifierResult.HasValue)
//            {
//                _logger.LogWarning("No cluster identifiers found, expected on first run.");
//                return Array.Empty<string>();
//            }

//            var identifiers = (string)identifierResult!;
//            var result = identifiers.Split('\t');

//            return result;
//        }

//        [Trace]
//        public async Task<DateTime?> GetClusterHeartbeatTimeAsync(string clusterIdentifier)
//        {
//            var key = GetClusterHeartbeatKey(clusterIdentifier);
//            _logger.LogTrace("Checking heartbeat key {key}", key);
//            if (await _database.KeyExistsAsync(key))
//            {
//                _logger.LogTrace("Heartbeat key exists");
//                var heartbeat = await _database.StringGetAsync(key);

//                if (DateTime.TryParseExact(heartbeat, "O", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var result))
//                {
//                    _logger.LogTrace("Heartbeat found, {result}", result);
//                    return result;
//                }
//                else
//                {
//                    var exception = new FormatException($"Unable to parse datetime value {(string?)heartbeat}");
//                    _logger.LogError(exception, "Unable to parse heartbeat for cluster {@clusteridentifier}", clusterIdentifier);
//                    throw exception;
//                }
//            }

//            _logger.LogTrace("Key {key} didn't exist", key);
//            return null;
//        }

//        [Trace]
//        public async Task<string[]> GetHostnamesAsync(string clusterIdentifier)
//        {
//            var keys = Array.Empty<string>();

//            if (string.IsNullOrWhiteSpace(clusterIdentifier))
//            {
//                _logger.LogDebug("Getting all hostnames");
//                var key = GetHostnameIpsKey("*");
//                keys = await GetKeysAsync(key);
//                keys = keys.Select(key => key.Split('.', 3)[2]).ToArray();
//            }
//            else
//            {
//                _logger.LogDebug("Getting all hostnames for cluster {clusterIdentifier}", clusterIdentifier);
//                keys = await GetKeysAsync(GetClusterHostnameIpsKey(clusterIdentifier, "*"));
//                _logger.LogTrace("Got keys {@keys}", (object)keys);
//                keys = keys.Select(key => key.Split('.', 4)[3]).ToArray();
//            }

//            return keys.Distinct().ToArray();
//        }

//        [Trace]
//        public async Task<Models.Core.Host[]?> GetHostsAsync(string clusterIdentifier)
//        {
//            var clusterIdentifiers = await GetClusterIdentifiersAsync();
//            if (!clusterIdentifiers.Contains(clusterIdentifier))
//            {
//                _logger.LogWarning("Cluster identifier not found while getting hosts for {@clusterIdentifier}", clusterIdentifier);
//                return null;
//            }

//            var keys = await GetKeysAsync(GetClusterHostnameIpsKey(clusterIdentifier, "*"));
//            var result = new List<Models.Core.Host>();

//            foreach (var key in keys)
//            {
//                var slugs = key.Split('.', 4);
//                if (slugs.Length != 4)
//                {
//                    _logger.LogError("Invalid key {@key} while fetching cluster hosts", key);
//                    continue;
//                }
//                var clusterHostnameKey = GetClusterHostnameIpsKey(clusterIdentifier, slugs[3]);
//                var cachedHost = await _database.StringGetAsync(clusterHostnameKey);
//                if (cachedHost.HasValue)
//                {
//                    var host = JsonSerializer.Deserialize<HostModel>(cachedHost!);
//                    if (host == null || host.HostIPs == null || string.IsNullOrEmpty(host.Hostname))
//                    {
//                        _logger.LogError("Unable to deserialize entry {@key} into type Host with value {@value}", key, cachedHost);
//                        continue;
//                    }
//                    result.Add(new Models.Core.Host
//                    {
//                        HostIPs = host.HostIPs,
//                        Hostname = host.Hostname
//                    });
//                }
//                else
//                {
//                    _logger.LogWarning("Host entry not found by {@key} when it should be there, race condition maybe?", key);
//                }
//            }

//            return result.ToArray();
//        }

//        [Trace]
//        public async Task<string> GetLastResourceVersionAsync(string uniqueIdentifier)
//        {
//            var key = GetResourceVersionKey(uniqueIdentifier);
//            var result = await _database.StringGetAsync(key);

//            if (!result.HasValue)
//            {
//                return string.Empty;
//            }

//            return result!;
//        }

//        [Trace]
//        public async Task<bool> IsServiceMonitoredAsync(string ns, string name)
//        {
//            var key = GetTrackedServiceKey(ns, name);
//            var cached = await _database.StringGetAsync(key);
//            var result = cached.HasValue;

//            return result;
//        }

//        [Trace]
//        public async Task RemoveClusterHostnameAsync(string clusterIdentifier, string hostname)
//        {
//            if (string.IsNullOrWhiteSpace(clusterIdentifier) || clusterIdentifier.Contains('*'))
//            {
//                throw new ArgumentOutOfRangeException(nameof(clusterIdentifier), "Cluster identifier is invalid.");
//            }

//            if (string.IsNullOrWhiteSpace(hostname) || hostname.Contains('*'))
//            {
//                throw new ArgumentOutOfRangeException(nameof(hostname), "Cluster identifier is invalid.");
//            }

//            var key = GetClusterHostnameIpsKey(clusterIdentifier, hostname);

//            _logger.LogTrace("Deleting cluster hostname key: {key}", key);
//            await _database.KeyDeleteAsync(key);
//            await RefreshHostnameIps(hostname);

//            return;
//        }

//        [Trace]
//        public async Task<bool> RemoveClusterIdentifierAsync(string clusterIdentifier)
//        {
//            if (string.IsNullOrWhiteSpace(clusterIdentifier) || clusterIdentifier.Contains('*'))
//            {
//                throw new ArgumentOutOfRangeException(nameof(clusterIdentifier), "Cluster identifier is invalid.");
//            }

//            var clusterIdentifiers = await GetClusterIdentifiersAsync();
//            if (!clusterIdentifiers.Contains(clusterIdentifier))
//            {
//                var identifiers = string.Join('\t', clusterIdentifiers.Where(i => i.ToLowerInvariant() != clusterIdentifier.ToLowerInvariant()));
//                var key = GetClusterIdentifiersKey();
//                await _database.StringSetAsync(key, identifiers);
//                return true;
//            }
//            return false;
//        }

//        [Trace]
//        public async Task SetClusterHeartbeatAsync(string clusterIdentifier, DateTime heartbeat)
//        {
//            if (string.IsNullOrWhiteSpace(clusterIdentifier) || clusterIdentifier.Contains('*'))
//            {
//                throw new ArgumentOutOfRangeException(nameof(clusterIdentifier), "Cluster identifier is invalid.");
//            }

//            var key = GetClusterHeartbeatKey(clusterIdentifier);
//            _logger.LogTrace("Setting heartbeat key {key} to {heartbeat}", key, heartbeat);

//            await _database.StringSetAsync(key, heartbeat.ToString("O"));
//            _logger.LogTrace("Done");
//        }

//        [Trace]
//        public async Task SetEndpointsCountAsync(string ns, string name, int count)
//        {
//            if (string.IsNullOrWhiteSpace(ns) || ns.Contains('*'))
//            {
//                throw new ArgumentOutOfRangeException(nameof(ns), "Namespace is invalid.");
//            }

//            if (string.IsNullOrWhiteSpace(name) || name.Contains('*'))
//            {
//                throw new ArgumentOutOfRangeException(nameof(name), "Endpoint name is invalid.");
//            }

//            var key = GetEndpointKey(ns, name);
//            await _database.StringSetAsync(key, count.ToString());
//        }

//        [Trace]
//        public async Task<bool> SetHostIPsAsync(string hostname, string clusterIdentifier, HostIP[] hostIPs)
//        {
//            if (string.IsNullOrWhiteSpace(clusterIdentifier) || clusterIdentifier.Contains('*'))
//            {
//                throw new ArgumentOutOfRangeException(nameof(clusterIdentifier), "Cluster identifier is invalid.");
//            }

//            if (string.IsNullOrWhiteSpace(hostname) || hostname.Contains('*'))
//            {
//                throw new ArgumentOutOfRangeException(nameof(hostname), "Hostname is invalid.");
//            }

//            var result = false;
//            await VerifyClusterExistsAsync(clusterIdentifier);

//            var key = GetClusterHostnameIpsKey(clusterIdentifier, hostname);
//            var hostModel = new HostModel
//            {
//                HostIPs = hostIPs,
//                Hostname = hostname,
//                ClusterIdentifier = clusterIdentifier
//            };

//            if (hostIPs.Length == 0)
//            {
//                _logger.LogInformation("No IPS for {hostname}/{cluster}", hostname, clusterIdentifier);
//                result = true;
//                await _database.KeyDeleteAsync(key);
//            }
//            else
//            {
//                var oldConfig = await _database.StringGetAsync(key);
//                var ips = JsonSerializer.Serialize(hostModel);
//                if (!oldConfig.HasValue || oldConfig != ips)
//                {
//                    _logger.LogTrace("Setting {key} to {@ips}", key, ips);

//                    result = true;
//                    var status = await _database.StringSetAsync(key, ips);

//                    if (!status)
//                    {
//                        _logger.LogError("Unable to update ips for host {@hostname}", hostname);
//                    }
//                }
//                else
//                {
//                    _logger.LogTrace("{oldConfig} has a value and it matches {ips}", (string?)oldConfig, ips);
//                }
//            }

//            //Only refresh the hostname ip's if they actually changed
//            if (result)
//            {
//                _logger.LogTrace("Refreshing hostname ips");
//                await RefreshHostnameIps(hostname);
//            }
//            return result;
//        }

//        [Trace]
//        public async Task SetResourceVersionAsync(string uniqueIdentifier, string version)
//        {
//            if (string.IsNullOrWhiteSpace(uniqueIdentifier) || uniqueIdentifier.Contains('*'))
//            {
//                throw new ArgumentOutOfRangeException(nameof(uniqueIdentifier), "Unique identifier is invalid.");
//            }

//            if (string.IsNullOrWhiteSpace(version) || version.Contains('*'))
//            {
//                throw new ArgumentOutOfRangeException(nameof(version), "Version is invalid.");
//            }

//            var key = GetResourceVersionKey(uniqueIdentifier);
//            await _database.StringSetAsync(key, version);
//        }

//        [Trace]
//        public async Task SynchronizeCachesAsync()
//        {
//            var clusterIdentifiers = await GetClusterIdentifiersAsync();
//            var clusterKey = GetClusterKey("*");
//            var clusterKeys = await GetKeysAsync(clusterKey);

//            foreach (var key in clusterKeys)
//            {
//                var slugs = key.Split('.', 4);
//                var clusterIdentifier = slugs[1];
//                if (!clusterIdentifiers.Contains(clusterIdentifier))
//                {
//                    await RemoveClusterIdentifierAsync(clusterIdentifier);
//                    if (slugs.Length == 4 && slugs[2] == "hosts")
//                    {
//                        await RemoveClusterHostnameAsync(clusterIdentifier, slugs[3]);
//                    }
//                    else
//                    {
//                        _logger.LogTrace("Deleting key: {key}", key);
//                        await _database.KeyDeleteAsync(key);
//                    }
//                }
//                else if (slugs.Length == 4 && slugs[2] == "hosts")
//                {
//                    var serialized = await _database.StringGetAsync(key);
//                    var valid = false;

//                    if (!string.IsNullOrWhiteSpace(serialized))
//                    {
//                        var host = JsonSerializer.Deserialize<HostModel>((string)serialized!);
//                        valid = host?.HostIPs?.Any() ?? false;
//                    }

//                    if (!valid)
//                    {
//                        _logger.LogInformation("Found stale cluster host entry {key} {data}", key, (string?)serialized);
//                        await _database.KeyDeleteAsync(key);
//                    }
//                }
//            }


//            var hostKey = GetHostnameIpsKey("*");
//            var hostKeys = await GetKeysAsync(hostKey);
//            foreach (var key in hostKeys)
//            {
//                var slugs = key.Split('.', 3);
//                var hostname = slugs[2];
//                var serialized = await _database.StringGetAsync(key);
//                if (!serialized.HasValue)
//                {
//                    _logger.LogWarning("No value in key, probably already removed, ignoring");
//                    continue;
//                }
//                var host = JsonSerializer.Deserialize<HostModel>(serialized!);
//                var refresh = false;

//                if (host?.HostIPs == null)
//                {
//                    _logger.LogWarning("Invalid host entry. Refreshing {@host}", host);
//                    refresh = true;
//                }
//                else
//                {
//                    //check for clusters that should be removed
//                    foreach (var hostIp in host.HostIPs)
//                    {
//                        if (!clusterIdentifiers.Contains(hostIp.ClusterIdentifier))
//                        {
//                            refresh = true;
//                            break;
//                        }
//                    }
//                }

//                if (refresh)
//                {
//                    await RefreshHostnameIps(hostname);
//                }
//            }
//        }

//        [Trace]
//        public async Task TrackServiceAsync(string ns, string name)
//        {
//            if (string.IsNullOrWhiteSpace(ns) || ns.Contains('*'))
//            {
//                throw new ArgumentOutOfRangeException(nameof(ns), "Namespace is invalid.");
//            }

//            if (string.IsNullOrWhiteSpace(name) || name.Contains('*'))
//            {
//                throw new ArgumentOutOfRangeException(nameof(name), "Service name is invalid.");
//            }

//            var key = GetTrackedServiceKey(ns, name);
//            await _database.StringSetAsync(key, "yes");
//        }

//        [Trace]
//        public async Task UntrackAllServicesAsync()
//        {
//            var trackedServiceKey = GetTrackedServiceKey("*");
//            var keys = await GetKeysAsync(trackedServiceKey);
//            foreach (var key in keys)
//            {
//                _logger.LogTrace("Deleting tracked service key: {key}", key);
//                await _database.KeyDeleteAsync(key);
//            }
//        }

//        private string GetClusterHeartbeatKey(string clusterIdentifier) => $"{GetClusterKey(clusterIdentifier)}.heartbeat";
//        private string GetClusterHostnameIpsKey(string clusterIdentifier, string hostname) => $"{GetClusterKey(clusterIdentifier)}.hosts.{hostname}";
//        private string GetClusterIdentifiersKey() => "clusteridentifiers";
//        private string GetClusterKey(string clusterIdentifier) => $"clusters.{clusterIdentifier}";
//        private string GetEndpointKey(string ns, string name) => $"endpoints.{ns}.{name}";
//        private string GetHostnameIpsKey(string hostname) => $"hostnames.ips.{hostname}";
//        private string GetResourceVersionKey(string uniqueIdentifier) => $"resourceversions.{uniqueIdentifier}";
//        private string GetTrackedServiceKey(string ns) => $"trackedservices.{ns}";
//        private string GetTrackedServiceKey(string ns, string serviceName) => $"{GetTrackedServiceKey(ns)}.{serviceName}";

//        [Trace]
//        private async Task<string[]> GetKeysAsync(string prefix)
//        {
//            var allKeys = await _database.ExecuteAsync("KEYS", prefix);

//            if (allKeys.Type != ResultType.MultiBulk)
//            {
//                _logger.LogError("KEYS returned incorrect type {@type}", allKeys.Type);
//                return Array.Empty<string>();
//            }

//            var result = (string[])allKeys!;
//            return result;
//        }

//        [Trace]
//        private async Task RefreshHostnameIps(string hostname)
//        {
//            string key;
//            var ipList = new List<HostIP>();

//            var clusterIdentifiers = await GetClusterIdentifiersAsync();
//            _logger.LogTrace("Got cluster identifiers {@clusterIdentifiers}", (object)clusterIdentifiers);

//            foreach (var identifier in clusterIdentifiers)
//            {
//                key = GetClusterHostnameIpsKey(identifier, hostname);
//                var clusterIps = await _database.StringGetAsync(key);
//                var value = (string?)clusterIps;
//                _logger.LogTrace("Cluster IPs {@clusterIps}", value);

//                if (value != null)
//                {
//                    _logger.LogTrace("ClusterIps != null");
//                    var hostModel = JsonSerializer.Deserialize<HostModel>(value);
//                    if (hostModel?.HostIPs == null)
//                    {
//                        _logger.LogError("Serialized host data does not fit the hostmodel type. {@serialized}", value);
//                    }
//                    else
//                    {
//                        ipList.AddRange(hostModel.HostIPs);
//                        _logger.LogTrace("Added hostips with a resulting list of {@iplist}", ipList);
//                    }
//                }
//                else
//                {
//                    _logger.LogDebug("Key value {@key} is null", key);
//                }
//            }

//            key = GetHostnameIpsKey(hostname);
//            if (ipList.Any())
//            {
//                var host = new HostModel
//                {
//                    Hostname = hostname,
//                    HostIPs = ipList.ToArray()
//                };

//                var ips = JsonSerializer.Serialize(host);
//                var status = await _database.StringSetAsync(key, ips);
//                if (!status)
//                {
//                    _logger.LogError("Unable to update ips for host {@hostname}", hostname);
//                }
//            }
//            else
//            {
//                _logger.LogInformation("Last IP address removed from the hostname, deleting the key");
//                await _database.KeyDeleteAsync(key);
//            }
//            await _queue.PublishHostChangedAsync(hostname);
//        }

//        [Trace]
//        private async Task VerifyClusterExistsAsync(string clusterIdentifier)
//        {
//            var clusterIdentifiers = await GetClusterIdentifiersAsync();
//            if (!clusterIdentifiers.Contains(clusterIdentifier))
//            {
//                var identifiers = string.Join('\t', clusterIdentifiers.Union(new[] { clusterIdentifier }));
//                var key = GetClusterIdentifiersKey();
//                await _database.StringSetAsync(key, identifiers);
//            }
//        }

//        private class HostModel
//        {
//            public string ClusterIdentifier { get; set; } = string.Empty;
//            public string Hostname { get; set; } = string.Empty;
//            public HostIP[] HostIPs { get; set; } = Array.Empty<HostIP>();
//        }
//    }
//}
