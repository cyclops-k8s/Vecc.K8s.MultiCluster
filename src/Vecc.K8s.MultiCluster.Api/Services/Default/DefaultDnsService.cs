using Coredns.Dns;
using Google.Protobuf;
using Grpc.Core;
using DNS.Protocol;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    /// <summary>
    /// 
    /// </summary>
    public class DefaultDnsService : DnsService.DnsServiceBase
    {
        private readonly DefaultDnsResolver _dnsResolver;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dnsResolver"></param>
        public DefaultDnsService(DefaultDnsResolver dnsResolver)
        {
            _dnsResolver = dnsResolver;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        [NewRelic.Api.Agent.Transaction]
        public override async Task<DnsPacket> Query(DnsPacket request, ServerCallContext context)
        {
            NewRelic.Api.Agent.NewRelic.SetTransactionName("CoreDNSQuery", "Query");
            var agent = NewRelic.Api.Agent.NewRelic.GetAgent();
            var start = DateTime.UtcNow.Ticks;
            try
            {
                var requestRecord = Request.FromArray(request.Msg.ToByteArray());
                using var response = new MemoryStream();

                agent.CurrentTransaction.AddCustomAttribute("Host", requestRecord.Questions.FirstOrDefault()?.Name.ToString() ?? "Unknown");

                var resultPacket = await _dnsResolver.ResolveAsync(requestRecord);
                if (resultPacket == null)
                {
                    context.Status = new Status(StatusCode.Unknown, "Unable to render packet");
                    return new DnsPacket();
                }

                var result = new DnsPacket
                {
                    Msg = ByteString.CopyFrom(resultPacket.ToArray())
                };

                context.Status = Status.DefaultSuccess;

                return result;
            }
            catch (Exception exception)
            {
                context.Status = new Status(StatusCode.Unknown, "Unable to render packet", exception);
                throw;
            }
            finally
            {
                var end = DateTime.UtcNow.Ticks;
                NewRelic.Api.Agent.NewRelic.RecordResponseTimeMetric("CoreDNSQuery", end -  start);
            }
        }
    }
}
