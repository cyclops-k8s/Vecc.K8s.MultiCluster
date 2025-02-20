using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;
using Microsoft.Extensions.Options;
using NewRelic.Api.Agent;
using System.Collections.Concurrent;
using System.Net;
using System.Xml.Linq;
using Vecc.K8s.MultiCluster.Api.Models.Core;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultDnsResolver : IDnsResolver
    {
        private readonly ILogger<DefaultDnsHost> _logger;
        private readonly IOptions<MultiClusterOptions> _options;
        private readonly IRandom _random;
        private readonly ICache _cache;
        private readonly ConcurrentDictionary<string, WeightedHostIp[]> _hosts;

        public DefaultDnsResolver(ILogger<DefaultDnsHost> logger, IOptions<MultiClusterOptions> options, IRandom random, ICache cache)
        {
            _logger = logger;
            _options = options;
            _random = random;
            _cache = cache;
            _hosts = new ConcurrentDictionary<string, WeightedHostIp[]>();
        }

        public OnHostChangedAsyncDelegate OnHostChangedAsync => new OnHostChangedAsyncDelegate(RefreshHostInformationAsync);

        [Transaction]
        public Task<DnsMessage> ResolveAsync(DnsMessage message)
        {
            var response = message.CreateResponseInstance();
            foreach (var question in message.Questions)
            {
                switch (question.RecordType)
                {
                    case RecordType.A:
                        SetARecords(question, response);
                        break;
                    case RecordType.Ns:
                        SetNSRecords(question, response);
                        break;
                }
            }
            return Task.FromResult(response);
        }

        [Transaction]
        public async Task InitializeAsync()
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
                    if (!hostnames.Contains(hostname + "."))
                    {
                        _hosts.Remove(hostname + ".", out var _);
                    }
                }
            }

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
            foreach (var ip in hostInformation.HostIPs)
            {
                WeightedHostIp weightedHostIp;
                if (!IPAddress.TryParse(ip.IPAddress, out var ipAddress))
                {
                    _logger.LogWarning("IPAddress is not parseable {@hostname} {@clusterIdentifier} {@ip}", hostname, ip.ClusterIdentifier, ip.IPAddress);
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

            _hosts[hostname + "."] = weightedIPs.ToArray();
        }

        [Trace]
        private void SetARecords(DnsQuestion question, DnsMessage response)
        {
            var hostname = question.Name.ToString();

            if (_hosts.TryGetValue(hostname, out var host))
            {
                //figure out the host ips here
                var highestPriority = host.Min(h => h.Priority);
                var hostIPs = host.Where(h => h.Priority == highestPriority).ToArray();
                WeightedHostIp chosenHostIP;

                if (hostIPs.Length == 0)
                {
                    _logger.LogError("No host for {@hostname}", hostname);
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
                response.AnswerRecords.Add(new ARecord(question.Name, _options.Value.DefaultRecordTTL, IPAddress.Parse(chosenHostIP.IP.IPAddress)));
            }
            else
            {
                _logger.LogInformation("Unknown host: {@hostname}", hostname);
            }
        }

        [Trace]
        private void SetNSRecords(DnsQuestion question, DnsMessage response)
        {
            var hostname = question.Name.ToString();

            if (_options.Value.NameserverNames.TryGetValue(hostname, out var names))
            {
                response.AnswerRecords.AddRange(
                    names.Select(name => 
                        new NsRecord(DomainName.Parse(name), _options.Value.DefaultRecordTTL, DomainName.Parse(name))));

                foreach (var name in names)
                {
                    if (_hosts.TryGetValue(name, out var hosts))
                    {
                        var ips = new List<string>();
                        foreach (var host in hosts)
                        {
                            if (!ips.Contains(host.IP.IPAddress))
                            {
                                response.AdditionalRecords.Add(
                                    new ARecord(DomainName.Parse(name), _options.Value.DefaultRecordTTL, IPAddress.Parse(host.IP.IPAddress)));
                                ips.Add(host.IP.IPAddress);
                            }
                        }
                    }
                }
            }
            else
            {
                response.AnswerRecords.Add(CreateSoaRecord());
            }
        }

        private SoaRecord CreateSoaRecord()
        {
            var result = new SoaRecord(
                DomainName.Parse(_options.Value.DNSHostname),
                _options.Value.DefaultRecordTTL,
                DomainName.Parse(_options.Value.DNSHostname),
                DomainName.Parse(_options.Value.DNSServerResponsibleEmailAddress),
                1,
                1,
                1,
                1,
                1);

            return result;
        }

        private struct WeightedHostIp
        {
            public int Priority { get; set; }
            public int WeightMin { get; set; }
            public int WeightMax { get; set; }
            public HostIP IP { get; set; }
        }
    }
}
