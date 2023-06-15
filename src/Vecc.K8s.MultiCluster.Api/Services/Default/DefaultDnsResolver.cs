using System.Collections.Concurrent;
using System.Net;
using Vecc.Dns;
using Vecc.Dns.Server;
using Vecc.K8s.MultiCluster.Api.Models.Core;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultDnsResolver : IDnsResolver
    {
        private readonly ILogger<DefaultDnsHost> _logger;
        private readonly IRandom _random;
        private readonly ICache _cache;
        private readonly ConcurrentDictionary<string, WeightedHostIp[]> _hosts;

        public DefaultDnsResolver(ILogger<DefaultDnsHost> logger, IRandom random, ICache cache)
        {
            _logger = logger;
            _random = random;
            _cache = cache;
            _hosts = new ConcurrentDictionary<string, WeightedHostIp[]>();
        }

        public OnHostChangedAsyncDelegate OnHostChangedAsync => new OnHostChangedAsyncDelegate(RefreshHostInformationAsync);

        public Task<Packet?> ResolveAsync(Packet incoming)
        {
            var result = new Packet
            {
                Header = new Dns.Parts.Header
                {
                    AdditionalRecordCount = 0,
                    Authenticated = false,
                    AuthoritativeServer = false,
                    //CheckingDisabled = false,
                    Id = incoming.Header.Id,
                    OpCode = incoming.Header.OpCode,
                    PacketType = Dns.Parts.PacketType.Response,
                    QuestionCount = incoming.Header.QuestionCount,
                    RecursionAvailable = false,
                    RecursionDesired = incoming.Header.RecursionDesired,
                    ResponseCode = Dns.Parts.ResponseCodes.NoError,
                    Truncated = false
                },
                Questions = incoming.Questions
            };

            var answers = new List<Dns.Parts.ResourceRecord>();

            foreach (var question in incoming.Questions)
            {
                var q = question.Name.ToString();
                if (_hosts.TryGetValue(q, out var host))
                {
                    //figure out the host ips here
                    var highestPriority = host.Min(h => h.Priority);
                    var hostIPs = host.Where(h => h.Priority == highestPriority).ToArray();
                    WeightedHostIp chosenHostIP;

                    if (hostIPs.Length == 0)
                    {
                        _logger.LogError("No host for {@hostname}", q);
                        continue;
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

                    var ipAddress = IPAddress.Parse(chosenHostIP.IP.IPAddress);

                    //TODO: support ipv6 addresses here
                    if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        answers.Add(new Dns.Parts.ResourceRecord
                        {
                            Data = new Dns.Parts.RecordData.A
                            {
                                Class = Dns.Parts.Classes.Internet,
                                IPAddress = ipAddress
                            },
                            Name = question.Name,
                            TTL = 5,
                        });
                    }
                }
            }

            result.Answers = answers;
            result.Header.AnswerCount =  (ushort)answers.Count;

            return Task.FromResult<Packet?>(result);
        }

        private async Task RefreshHostInformationAsync(string? hostname)
        {
            _logger.LogInformation("{@hostname} updated, refreshing state.", hostname);
            if (hostname == null)
            {
                _logger.LogError("Hostname is null");
                return;
            }

            var hostInformation = await _cache.GetHostInformationAsync(hostname);
            if (hostInformation == null)
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
                if (ip.Weight == 0)
                {
                    weightedHostIp = new WeightedHostIp
                    {
                        Priority = ip.Priority,
                        IP = ip,
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

        private struct WeightedHostIp
        {
            public int Priority { get; set; }
            public int WeightMin { get; set; }
            public int WeightMax { get; set; }
            public HostIP IP { get; set; }
        }
    }
}
