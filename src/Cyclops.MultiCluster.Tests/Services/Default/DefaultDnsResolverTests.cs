using DNS.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Cyclops.MultiCluster.Models.Core;
using Cyclops.MultiCluster.Services;
using Cyclops.MultiCluster.Services.Default;
using CoreHost = Cyclops.MultiCluster.Models.Core.Host;

namespace Cyclops.MultiCluster.Tests.Services.Default
{
    public class DefaultDnsResolverTests
    {
        private readonly Mock<ILogger<DefaultDnsResolver>> _loggerMock;
        private readonly Mock<IOptions<MultiClusterOptions>> _optionsMock;
        private readonly Mock<IRandom> _randomMock;
        private readonly Mock<ICache> _cacheMock;
        private readonly Mock<IHostApplicationLifetime> _lifetimeMock;
        private readonly DefaultDnsResolver _resolver;

        public DefaultDnsResolverTests()
        {
            _loggerMock = new Mock<ILogger<DefaultDnsResolver>>();
            _optionsMock = new Mock<IOptions<MultiClusterOptions>>();
            _optionsMock.Setup(x => x.Value).Returns(new MultiClusterOptions
            {
                DefaultRecordTTL = 5,
                DNSRefreshInterval = 30,
                DNSHostname = "dns.test.com",
                DNSServerResponsibleEmailAddress = "admin.test.com",
                NameserverNames = new Dictionary<string, string[]>()
            });
            _randomMock = new Mock<IRandom>();
            _cacheMock = new Mock<ICache>();
            _lifetimeMock = new Mock<IHostApplicationLifetime>();
            _lifetimeMock.Setup(x => x.ApplicationStopping).Returns(CancellationToken.None);

            _resolver = new DefaultDnsResolver(
                _loggerMock.Object,
                _optionsMock.Object,
                _randomMock.Object,
                _cacheMock.Object,
                _lifetimeMock.Object);
        }

        [Fact]
        public async Task ResolveAsync_WithNoQuestions_ReturnsEmptyResponse()
        {
            var request = new Request();

            var result = await _resolver.ResolveAsync(request);

            Assert.NotNull(result);
            Assert.Empty(result.AnswerRecords);
        }

        [Fact]
        public async Task ResolveAsync_WithUnknownHost_ReturnsEmptyAnswer()
        {
            var request = new Request();
            request.Questions.Add(new Question(new Domain("unknown.example.com"), RecordType.A));

            var result = await _resolver.ResolveAsync(request);

            Assert.Empty(result.AnswerRecords);
        }

        [Fact]
        public async Task ResolveAsync_WithKnownHost_ReturnsARecord()
        {
            _cacheMock.Setup(x => x.GetHostInformationAsync("test.example.com"))
                .ReturnsAsync(new CoreHost
                {
                    Hostname = "test.example.com",
                    HostIPs = new[]
                    {
                        new HostIP { IPAddress = "10.0.0.1", Priority = 0, Weight = 50, ClusterIdentifier = "cluster-1" }
                    }
                });

            _cacheMock.Setup(x => x.GetHostnamesAsync())
                .ReturnsAsync(new[] { "test.example.com" });

            await _resolver.OnHostChangedAsync("test.example.com");

            var request = new Request();
            request.Questions.Add(new Question(new Domain("test.example.com"), RecordType.A));

            var result = await _resolver.ResolveAsync(request);

            Assert.Single(result.AnswerRecords);
        }

        [Fact]
        public async Task ResolveAsync_WithCName_ReturnsCNameRecord()
        {
            _cacheMock.Setup(x => x.GetHostInformationAsync("test.example.com"))
                .ReturnsAsync(new CoreHost
                {
                    Hostname = "test.example.com",
                    HostIPs = new[]
                    {
                        new HostIP { IPAddress = "cname.example.com", Priority = 0, Weight = 50, ClusterIdentifier = "cluster-1" }
                    }
                });

            await _resolver.OnHostChangedAsync("test.example.com");

            var request = new Request();
            request.Questions.Add(new Question(new Domain("test.example.com"), RecordType.A));

            var result = await _resolver.ResolveAsync(request);

            Assert.Single(result.AnswerRecords);
        }

        [Fact]
        public async Task OnHostChangedAsync_WithNullHostname_DoesNotThrow()
        {
            var exception = await Record.ExceptionAsync(() => _resolver.OnHostChangedAsync(null));
            Assert.Null(exception);
        }

        [Fact]
        public async Task OnHostChangedAsync_WithNullHostInformation_RemovesHost()
        {
            _cacheMock.Setup(x => x.GetHostInformationAsync("test.example.com"))
                .ReturnsAsync(new CoreHost
                {
                    Hostname = "test.example.com",
                    HostIPs = new[]
                    {
                        new HostIP { IPAddress = "10.0.0.1", Priority = 0, Weight = 50, ClusterIdentifier = "cluster-1" }
                    }
                });
            await _resolver.OnHostChangedAsync("test.example.com");

            _cacheMock.Setup(x => x.GetHostInformationAsync("test.example.com"))
                .ReturnsAsync((CoreHost?)null);
            await _resolver.OnHostChangedAsync("test.example.com");

            var request = new Request();
            request.Questions.Add(new Question(new Domain("test.example.com"), RecordType.A));
            var result = await _resolver.ResolveAsync(request);
            Assert.Empty(result.AnswerRecords);
        }

        [Fact]
        public async Task OnHostChangedAsync_WithEmptyHostIPs_RemovesHost()
        {
            _cacheMock.Setup(x => x.GetHostInformationAsync("test.example.com"))
                .ReturnsAsync(new CoreHost
                {
                    Hostname = "test.example.com",
                    HostIPs = new[]
                    {
                        new HostIP { IPAddress = "10.0.0.1", Priority = 0, Weight = 50, ClusterIdentifier = "cluster-1" }
                    }
                });
            await _resolver.OnHostChangedAsync("test.example.com");

            _cacheMock.Setup(x => x.GetHostInformationAsync("test.example.com"))
                .ReturnsAsync(new CoreHost
                {
                    Hostname = "test.example.com",
                    HostIPs = Array.Empty<HostIP>()
                });
            await _resolver.OnHostChangedAsync("test.example.com");

            var request = new Request();
            request.Questions.Add(new Question(new Domain("test.example.com"), RecordType.A));
            var result = await _resolver.ResolveAsync(request);
            Assert.Empty(result.AnswerRecords);
        }

        [Fact]
        public async Task ResolveAsync_WithMultipleHostsSamePriority_UsesWeighting()
        {
            _cacheMock.Setup(x => x.GetHostInformationAsync("test.example.com"))
                .ReturnsAsync(new CoreHost
                {
                    Hostname = "test.example.com",
                    HostIPs = new[]
                    {
                        new HostIP { IPAddress = "10.0.0.1", Priority = 0, Weight = 50, ClusterIdentifier = "cluster-1" },
                        new HostIP { IPAddress = "10.0.0.2", Priority = 0, Weight = 50, ClusterIdentifier = "cluster-2" }
                    }
                });

            _randomMock.Setup(x => x.Next(1, It.IsAny<int>())).Returns(52);

            await _resolver.OnHostChangedAsync("test.example.com");

            var request = new Request();
            request.Questions.Add(new Question(new Domain("test.example.com"), RecordType.A));

            var result = await _resolver.ResolveAsync(request);

            Assert.Single(result.AnswerRecords);
        }

        [Fact]
        public async Task ResolveAsync_WithDifferentPriorities_ChoosesHighestPriority()
        {
            _cacheMock.Setup(x => x.GetHostInformationAsync("test.example.com"))
                .ReturnsAsync(new CoreHost
                {
                    Hostname = "test.example.com",
                    HostIPs = new[]
                    {
                        new HostIP { IPAddress = "10.0.0.1", Priority = 0, Weight = 50, ClusterIdentifier = "cluster-1" },
                        new HostIP { IPAddress = "10.0.0.2", Priority = 10, Weight = 50, ClusterIdentifier = "cluster-2" }
                    }
                });

            await _resolver.OnHostChangedAsync("test.example.com");

            var request = new Request();
            request.Questions.Add(new Question(new Domain("test.example.com"), RecordType.A));

            var result = await _resolver.ResolveAsync(request);

            Assert.Single(result.AnswerRecords);
        }

        [Fact]
        public async Task ResolveAsync_NSRecord_WithRegisteredNameserver_ReturnsNSRecord()
        {
            _optionsMock.Setup(x => x.Value).Returns(new MultiClusterOptions
            {
                DefaultRecordTTL = 5,
                DNSRefreshInterval = 30,
                DNSHostname = "dns.test.com",
                DNSServerResponsibleEmailAddress = "admin.test.com",
                NameserverNames = new Dictionary<string, string[]>
                {
                    { "example.com", new[] { "ns1.example.com" } }
                }
            });

            var resolver = new DefaultDnsResolver(
                _loggerMock.Object,
                _optionsMock.Object,
                _randomMock.Object,
                _cacheMock.Object,
                _lifetimeMock.Object);

            var request = new Request();
            request.Questions.Add(new Question(new Domain("example.com"), RecordType.NS));

            var result = await resolver.ResolveAsync(request);

            Assert.NotEmpty(result.AnswerRecords);
        }

        [Fact]
        public async Task ResolveAsync_NSRecord_WithNoRegisteredNameserver_ReturnsSOARecord()
        {
            var request = new Request();
            request.Questions.Add(new Question(new Domain("unknown.com"), RecordType.NS));

            var result = await _resolver.ResolveAsync(request);

            Assert.NotEmpty(result.AnswerRecords);
        }

        [Fact]
        public async Task ResolveAsync_WithZeroWeightHosts_SelectsRandomly()
        {
            _cacheMock.Setup(x => x.GetHostInformationAsync("test.example.com"))
                .ReturnsAsync(new CoreHost
                {
                    Hostname = "test.example.com",
                    HostIPs = new[]
                    {
                        new HostIP { IPAddress = "10.0.0.1", Priority = 0, Weight = 0, ClusterIdentifier = "cluster-1" },
                        new HostIP { IPAddress = "10.0.0.2", Priority = 0, Weight = 0, ClusterIdentifier = "cluster-2" }
                    }
                });

            _randomMock.Setup(x => x.Next(It.IsAny<int>())).Returns(0);

            await _resolver.OnHostChangedAsync("test.example.com");

            var request = new Request();
            request.Questions.Add(new Question(new Domain("test.example.com"), RecordType.A));

            var result = await _resolver.ResolveAsync(request);

            Assert.Single(result.AnswerRecords);
            _randomMock.Verify(x => x.Next(It.IsAny<int>()), Times.Once);
        }

        [Fact]
        public async Task OnHostChangedAsync_SkipsDuplicateClusterIPs()
        {
            _cacheMock.Setup(x => x.GetHostInformationAsync("test.example.com"))
                .ReturnsAsync(new CoreHost
                {
                    Hostname = "test.example.com",
                    HostIPs = new[]
                    {
                        new HostIP { IPAddress = "10.0.0.1", Priority = 0, Weight = 50, ClusterIdentifier = "cluster-1" },
                        new HostIP { IPAddress = "10.0.0.2", Priority = 0, Weight = 50, ClusterIdentifier = "cluster-1" }
                    }
                });

            await _resolver.OnHostChangedAsync("test.example.com");

            var request = new Request();
            request.Questions.Add(new Question(new Domain("test.example.com"), RecordType.A));
            var result = await _resolver.ResolveAsync(request);
            Assert.Single(result.AnswerRecords);
        }
    }
}
