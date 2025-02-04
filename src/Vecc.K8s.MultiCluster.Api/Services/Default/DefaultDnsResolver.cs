using Microsoft.Extensions.Options;
using NewRelic.Api.Agent;
using System.Collections.Concurrent;
using System.Net;
using System.Xml.Linq;
using Vecc.Dns;
using Vecc.Dns.Parts;
using Vecc.Dns.Parts.RecordData;
using Vecc.Dns.Server;
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
        public Task<Packet?> ResolveAsync(Packet incoming)
        {
            var result = new Packet
            {
                Header = new Header
                {
                    AdditionalRecordCount = 0,
                    Authenticated = false,
                    AuthoritativeServer = false,
                    CheckingDisabled = false,
                    Id = incoming.Header.Id,
                    OpCode = incoming.Header.OpCode,
                    PacketType = PacketType.Response,
                    QuestionCount = incoming.Header.QuestionCount,
                    RecursionAvailable = false,
                    RecursionDesired = incoming.Header.RecursionDesired,
                    ResponseCode = ResponseCodes.NoError,
                    Truncated = false
                },
                Questions = incoming.Questions
            };

            foreach (var question in incoming.Questions)
            {
                var q = question.Name.ToString();
                switch (question.QuestionType)
                {
                    case ResourceRecordTypes.A:
                        SetARecords(q, result);
                        break;
                    case ResourceRecordTypes.NS:
                        SetNSRecords(q, result);
                        break;
                }

            }

            result.Header.AnswerCount = (ushort)result.Answers.Count;
            result.Header.AdditionalRecordCount = (ushort)0;

            return Task.FromResult<Packet?>(result);
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
                    if (!hostnames.Contains(hostname))
                    {
                        _hosts.Remove(hostname, out var _);
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

            _hosts[hostname] = weightedIPs.ToArray();
        }

        [Trace]
        private void SetARecords(string hostname, Packet packet)
        {
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

                var record = GetIPResourceRecord(hostname, chosenHostIP.IP.IPAddress);
                if (record != null)
                {
                    packet.Answers.Add(record);
                }
            }
            else
            {
                _logger.LogInformation("Unknown host: {@hostname}", hostname);
            }
        }

        [Trace]
        private void SetNSRecords(string hostname, Packet packet)
        {
            IEnumerable<ResourceRecord>? answers = default;
            IEnumerable<ResourceRecord>? additionalAnswers = default;

            if (_options.Value.NameserverNames.TryGetValue(hostname, out var names))
            {
                var moreAnswers = new List<ResourceRecord>();

                answers = names.Select(name => new ResourceRecord
                {
                    Data = new NS
                    {
                        Class = Classes.Internet,
                        Target = new Name { Value = name },
                    },
                    Name = new Name { Value = hostname },
                    TTL = (uint)_options.Value.DefaultRecordTTL
                });

                foreach (var name in names)
                {
                    if (_hosts.TryGetValue(name, out var hosts))
                    {
                        var ips = new List<string>();
                        foreach (var host in hosts)
                        {
                            if (!ips.Contains(host.IP.IPAddress))
                            {
                                var record = GetIPResourceRecord(name, host.IP.IPAddress);
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
                var answer = new ResourceRecord
                {
                    Name = hostname,
                    TTL = 0,
                    Data = new Soa
                    {
                        Class = Classes.Internet,
                        Expire = 3600000,
                        Refresh = 86400,
                        Retry = 7200,
                        RName = new Name { Value = _options.Value.DNSHostname },
                        Minimum = 172800,
                        MName = new Name { Value = _options.Value.DNSServerResponsibleEmailAddress },
                        Serial = 1
                    }
                };
                answers = new[] { answer };
            }

            if (answers != null)
            {
                foreach (var answer in answers)
                {
                    packet.Answers.Add(answer);
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
        private ResourceRecord? GetIPResourceRecord(string hostname, string ip)
        {
            var ipAddress = IPAddress.Parse(ip);

            //TODO: support ipv6 addresses here
            if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var record = new ResourceRecord
                {
                    Data = new A
                    {
                        Class = Classes.Internet,
                        IPAddress = ipAddress
                    },
                    Name = new Name { Value = hostname },
                    TTL = (uint)_options.Value.DefaultRecordTTL,
                };

                return record;
            }

            return null;
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
