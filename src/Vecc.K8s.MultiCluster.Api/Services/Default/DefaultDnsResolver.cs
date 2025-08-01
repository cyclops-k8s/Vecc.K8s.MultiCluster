using DNS.Protocol;
using DNS.Protocol.ResourceRecords;
using Microsoft.Extensions.Options;
using NewRelic.Api.Agent;
using System.Collections.Concurrent;
using System.Net;
using System.Xml.Linq;
using Vecc.K8s.MultiCluster.Api.Models.Core;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultDnsResolver
    {
        private readonly ILogger<DefaultDnsResolver> _logger;
        private readonly IOptions<MultiClusterOptions> _options;
        private readonly IRandom _random;
        private readonly ICache _cache;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly ConcurrentDictionary<string, WeightedHostIp[]> _hosts;
        private Task _resyncer;

        public DefaultDnsResolver(ILogger<DefaultDnsResolver> logger, IOptions<MultiClusterOptions> options, IRandom random, ICache cache, IHostApplicationLifetime hostApplicationLifetime)
        {
            _logger = logger;
            _options = options;
            _random = random;
            _cache = cache;
            _hostApplicationLifetime = hostApplicationLifetime;
            _hosts = new ConcurrentDictionary<string, WeightedHostIp[]>();
            _resyncer = Task.CompletedTask; // Initialize to a completed task to avoid null reference exceptions
        }

        public OnHostChangedAsyncDelegate OnHostChangedAsync => new OnHostChangedAsyncDelegate(RefreshHostInformationAsync);

        [Transaction]
        public Task<Response> ResolveAsync(Request incoming)
        {
            var result = Response.FromRequest(incoming);

            foreach (var question in incoming.Questions)
            {
                using var scope = _logger.BeginScope(new { Hostname = question.Name.ToString() });

                switch (question.Type)
                {
                    case RecordType.A:
                        SetARecords(question.Name, result);
                        break;
                    case RecordType.NS:
                        SetNSRecords(question.Name, result);
                        break;
                }
            }

            return Task.FromResult(result);
        }

        [Transaction]
        public async Task InitializeAsync()
        {
            _logger.LogInformation("Initializing DNS resolver.");
            await ResyncAsync();

            _logger.LogInformation("Starting resyncer task for DNS resolver.");
            _resyncer = new Task(async () => await ResyncTimerAsync());

            if (_resyncer.Status != TaskStatus.Running)
            {
                _logger.LogInformation("Task status: {status}", _resyncer.Status);
                _resyncer.Start();
            }

            _logger.LogInformation("DNS resolver initialized and resyncer started.");
        }

        [Transaction]
        private async Task RefreshHostInformationAsync(string? hostname)
        {
            _logger.LogInformation("{@hostname} updated, refreshing state.", hostname);
            if (hostname == null)
            {
                _logger.LogError("Hostname is null");
                return;
            }

            var hostInformation = await _cache.GetHostInformationAsync(hostname);
            if (hostInformation?.HostIPs == null || hostInformation.HostIPs.Length == 0)
            {
                //no host information
                _logger.LogWarning("Host information lost for {@hostname}", hostname);
                _hosts.Remove(hostname, out var _);
                return;
            }

            var weightStart = 1;
            var weightedIPs = new List<WeightedHostIp>();
            foreach (var cluster in hostInformation.HostIPs.Distinct().GroupBy(x => x.ClusterIdentifier))
            {
                var alreadyDidCluster = false;
                foreach (var ip in cluster)
                {
                    if (alreadyDidCluster)
                    {
                        _logger.LogWarning("Multiple IPs for the same cluster {@hostname} {@clusterIdentifier} {@ip} {@weight} {@priority}, skipping this one.", hostname, ip.ClusterIdentifier, ip.IPAddress, ip.Weight, ip.Priority);
                        continue; // Skip if we already processed an IP for this cluster
                    }
                    alreadyDidCluster = true;

                    WeightedHostIp weightedHostIp;
                    if (!IPAddress.TryParse(ip.IPAddress, out var ipAddress))
                    {
                        _logger.LogWarning("IPAddress is not parseable {@hostname} {@clusterIdentifier} {@ip}, likely should be a CNAME which isn't implemented yet.", hostname, ip.ClusterIdentifier, ip.IPAddress);
                    }

                    if (ip.Weight == 0)
                    {
                        weightedHostIp = new WeightedHostIp
                        {
                            IP = ip,
                            Priority = ip.Priority,
                            WeightMin = 0,
                            WeightMax = 0
                        };
                    }
                    else
                    {
                        var maxWeight = weightStart + ip.Weight;
                        weightedHostIp = new WeightedHostIp
                        {
                            IP = ip,
                            Priority = ip.Priority,
                            WeightMin = weightStart,
                            WeightMax = maxWeight
                        };
                        weightStart = maxWeight + 1;
                    }
                    weightedIPs.Add(weightedHostIp);
                }
            }
            _hosts[hostname] = weightedIPs.ToArray();
        }

        [Trace]
        private void SetARecords(Domain hostname, Response packet)
        {
            if (_hosts.TryGetValue(hostname.ToString(), out var host))
            {
                //figure out the host ips here
                var highestPriority = host.Min(h => h.Priority);
                var hostIPs = host.Where(h => h.Priority == highestPriority).ToArray();
                WeightedHostIp chosenHostIP;

                if (hostIPs.Length == 0)
                {
                    _logger.LogError("No IP found in cache");
                    return;
                }
                if (hostIPs.Length == 1)
                {
                    _logger.LogTrace("Only one host, no need to calculate weights");
                    chosenHostIP = hostIPs[0];
                }
                else
                {
                    var maxWeight = hostIPs.Max(h => h.WeightMax);
                    if (maxWeight == 0)
                    {
                        //no weighted hosts available, randomly choose one
                        var max = hostIPs.Length - 1;
                        var next = _random.Next(max);
                        chosenHostIP = hostIPs[next];
                    }
                    else
                    {
                        //don't choose the 0 weights, they are fail over endpoints
                        var next = _random.Next(1, maxWeight);
                        chosenHostIP = hostIPs.Single(h => h.WeightMin <= next && h.WeightMax >= next);
                    }
                }

                var record = GetIPResourceRecord(hostname, chosenHostIP.IP.IPAddress);
                if (record != null)
                {
                    packet.AnswerRecords.Add(record);
                }
            }
            else
            {
                _logger.LogInformation("Unknown host");
            }
        }

        [Trace]
        private void SetNSRecords(Domain hostname, Response packet)
        {
            IEnumerable<IResourceRecord>? answers = default;
            IEnumerable<IResourceRecord>? additionalAnswers = default;

            if (_options.Value.NameserverNames.TryGetValue(hostname.ToString(), out var names))
            {
                var moreAnswers = new List<IResourceRecord>();

                answers = names.Select(name => new NameServerResourceRecord(hostname, new Domain(name), TimeSpan.FromSeconds(_options.Value.DefaultRecordTTL)));

                foreach (var name in names)
                {
                    if (_hosts.TryGetValue(name, out var hosts))
                    {
                        var ips = new List<string>();
                        foreach (var host in hosts)
                        {
                            if (!ips.Contains(host.IP.IPAddress))
                            {
                                var record = GetIPResourceRecord(new Domain(name), host.IP.IPAddress);
                                if (record != null)
                                {
                                    moreAnswers.Add(record);
                                }
                                ips.Add(host.IP.IPAddress);
                            }
                        }
                    }
                }

                additionalAnswers = moreAnswers;
            }
            else
            {
                var answer = new StartOfAuthorityResourceRecord(
                    hostname,
                    new Domain(_options.Value.DNSHostname),
                    new Domain(_options.Value.DNSServerResponsibleEmailAddress),
                    ttl: TimeSpan.FromSeconds(_options.Value.DefaultRecordTTL));
                answers = [answer];
            }

            if (answers != null)
            {
                foreach (var answer in answers)
                {
                    packet.AnswerRecords.Add(answer);
                }
            }

            if (additionalAnswers != null)
            {
                foreach (var additionalAnswer in additionalAnswers)
                {
                    packet.AdditionalRecords.Add(additionalAnswer);
                }
            }
        }

        [Trace]
        private BaseResourceRecord? GetIPResourceRecord(Domain hostname, string ip)
        {
            var ipAddress = IPAddress.Parse(ip);

            var record = new IPAddressResourceRecord(
                hostname,
                ipAddress,
                TimeSpan.FromSeconds(_options.Value.DefaultRecordTTL));

            return record;
        }

        private struct WeightedHostIp
        {
            public int Priority { get; set; }
            public int WeightMin { get; set; }
            public int WeightMax { get; set; }
            public HostIP IP { get; set; }
        }

        private async Task ResyncTimerAsync()
        {
            while (!_hostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Resyncing hostnames");
                    await Task.Delay(TimeSpan.FromSeconds(_options.Value.DNSRefreshInterval), _hostApplicationLifetime.ApplicationStopping);
                    await ResyncAsync();
                }
                catch (TaskCanceledException)
                {
                    _logger.LogInformation("Periodic refresh of DNS resolver was cancelled due to application stopping.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during periodic refresh of DNS resolver");
                }
            }

        }

        [Trace]
        private async Task ResyncAsync()
        {
            var hostnames = await _cache.GetHostnamesAsync();

            if (hostnames != null)
            {
                foreach (var hostname in hostnames)
                {
                    await RefreshHostInformationAsync(hostname);
                }

                foreach (var hostname in _hosts.Keys.ToArray())
                {
                    if (!hostnames.Contains(hostname))
                    {
                        _hosts.Remove(hostname, out var _);
                    }
                }
            }
        }
    }
}
