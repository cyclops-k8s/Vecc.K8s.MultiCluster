using k8s.Models;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vecc.K8s.MultiCluster.Api.Models.Core;
using YamlDotNet.Core.Tokens;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class RedisCache : ICache
    {
        private readonly ILogger<RedisCache> _logger;
        private readonly IDatabase _database;
        private readonly IQueue _queue;

        public RedisCache(ILogger<RedisCache> logger, IDatabase database, IQueue queue)
        {
            _logger = logger;
            _database = database;
            _queue = queue;
        }

        public async Task<Models.Core.Host?> GetHostInformationAsync(string hostname)
        {
            var key = $"hostnames.ips.{hostname}";
            var hostData = await _database.StringGetAsync(key);

            if (!hostData.HasValue)
            {
                _logger.LogWarning("{@hostname} not found in cache", hostname);
                return null;
            }

            var host = JsonSerializer.Deserialize<HostModel>((string)hostData!);
            if (host == null)
            {
                _logger.LogError("{@hostname} found in cache but did not fit in the model. {@hostdata}", hostname, (string)hostData!);
                return null;
            }

            var result = new Models.Core.Host
            {
                Hostname = hostname,
                HostIPs = host.HostIPs
            };

            return result;
        }

        public async Task<string[]> GetHostnamesAsync(string clusterIdentifier)
        {
            var keys = Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(clusterIdentifier))
            {
                _logger.LogDebug("Getting all hostnames");

                keys = await GetKeysAsync("hostnames.ips.*");
                keys = keys.Select(key => key.Split('.')[2]).ToArray();
            }
            else
            {
                keys = await GetKeysAsync($"cluster.{clusterIdentifier}.hosts.*");
                keys = keys.Select(key => key.Split('.')[3]).ToArray();
            }

            return keys.Distinct().ToArray();
        }

        public async Task<string[]> GetKeysAsync(string prefix)
        {
            var allKeys = await _database.ExecuteAsync("KEYS", prefix);

            if (allKeys.Type != ResultType.MultiBulk)
            {
                _logger.LogError("KEYS returned incorrect type {@type}", allKeys.Type);
                return Array.Empty<string>();
            }

            var result = (string[])allKeys!;
            return result;
        }

        public async Task SetHostIPsAsync(string hostname, HostIP[] hostIPs)
        {
            foreach (var g in hostIPs.GroupBy(ip => ip.ClusterIdentifier))
            {
                var key = $"cluster.{g.Key}.hosts.{hostname}";
                var hostModel = new HostModel { HostIPs = g.ToArray() };
                var ips = JsonSerializer.Serialize(hostModel);
                var status = await _database.StringSetAsync(key, ips);

                if (!status)
                {
                    //TODO: Implement retry logic for redis cache
                    _logger.LogError("Unable to update ips for host {@hostname}", hostname);
                }

                await VerifyClusterExistsAsync(g.Key);
            }

            await RefreshHostnameIps(hostname);
        }

        public async Task<string[]> GetClusterIdentifiersAsync()
        {
            var identifierResult = await _database.StringGetAsync("clusteridentifiers");

            if (!identifierResult.HasValue)
            {
                _logger.LogWarning("No cluster identifiers found, expected on first run.");
                return Array.Empty<string>();
            }

            var identifiers = (string)identifierResult;
            var result = identifiers.Split('\t');

            return result;
        }

        public async Task<DateTime> GetClusterHeartbeatTimeAsync(string clusterIdentifier)
        {
            var heartbeat = await _database.StringGetAsync($"cluster.{clusterIdentifier}.heartbeat");
            if (DateTime.TryParseExact(heartbeat, "O", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var result))
            {
                return result;
            }
            else
            {
                _logger.LogError("Unable to parse heartbeat {@heartbeat} for cluster {@clusteridentifier}", (string?)heartbeat, clusterIdentifier);
                return default;
            }
        }

        public async Task RemoveClusterHostnameAsync(string clusterIdentifier, string hostname)
        {
            var key = $"cluster.{clusterIdentifier}.hosts.{hostname}";

            await _database.KeyDeleteAsync(key);
            await RefreshHostnameIps(hostname);

            return;
        }

        public async Task SetClusterHeartbeatAsync(string clusterIdentifier, DateTime heartbeat)
        {
            var key = $"cluster.{clusterIdentifier}.heartbeat";
            await _database.StringSetAsync(clusterIdentifier, heartbeat.ToString("O"));
        }

        public async Task SetResourceVersionAsync(string uniqueIdentifier, string version)
        {
            await _database.StringSetAsync($"resourceversion.{uniqueIdentifier}", version);
        }

        public async Task<string> GetLastResourceVersionAsync(string uniqueIdentifier)
        {
            var result = await _database.StringGetAsync($"resourceversion.{uniqueIdentifier}");

            if (!result.HasValue)
            {
                return string.Empty;
            }

            return result!;
        }

        private async Task RefreshHostnameIps(string hostname)
        {
            string key;
            var ipList = new List<HostIP>();

            var clusterIdentifiers = await GetClusterIdentifiersAsync();
            foreach (var identifier in clusterIdentifiers)
            {
                key = $"cluster.{identifier}.hosts.{hostname}";
                var clusterIps = await _database.StringGetAsync(key);
                var value = (string?)clusterIps;
                if (value != null)
                {
                    var hostModel = JsonSerializer.Deserialize<HostModel>(value);
                    if (hostModel == null)
                    {
                        _logger.LogError("Serialized host data does not fit the hostmodel type. {@serialized}", value);
                    }
                    else
                    {
                        ipList.AddRange(hostModel.HostIPs);
                    }
                }
                else
                {
                    _logger.LogDebug("Key value {@key} is null", key);
                }
            }

            key = $"hostnames.ips.{hostname}";
            var host = new HostModel
            {
                HostIPs = ipList.ToArray()
            };

            var ips = JsonSerializer.Serialize<HostModel>(host);
            var status = await _database.StringSetAsync(key, ips);
            if (!status)
            {
                //TODO: Implement retry logic for redis cache
                _logger.LogError("Unable to update ips for host {@hostname}", hostname);
            }

            await _queue.PublishHostChangedAsync(hostname);
        }

        private async Task VerifyClusterExistsAsync(string clusterIdentifier)
        {
            var clusterIdentifiers = await GetClusterIdentifiersAsync();
            if (!clusterIdentifiers.Contains(clusterIdentifier))
            {
                var identifiers = string.Join(',', clusterIdentifiers.Union(new[] { clusterIdentifier }));
                await _database.StringSetAsync("clusteridentifiers", identifiers);
            }
        }

        public async Task<bool> IsServiceMonitoredAsync(string ns, string name)
        {
            var cached = await _database.StringGetAsync($"trackedservices.{ns}.{name}");
            var result = cached.HasValue;

            return result;
        }

        public async Task TrackServiceAsync(string ns, string name)
        {
            await _database.StringSetAsync($"trackedservices.{ns}.{name}", "yes");
        }

        public async Task UntrackAllServicesAsync()
        {
            var keys = await GetKeysAsync("trackedservices.*");
            foreach (var key in keys)
            {
                await _database.KeyDeleteAsync(key);
            }
        }

        private class HostModel
        {
            public HostIP[] HostIPs { get; set; } = Array.Empty<HostIP>();
        }
    }
}
