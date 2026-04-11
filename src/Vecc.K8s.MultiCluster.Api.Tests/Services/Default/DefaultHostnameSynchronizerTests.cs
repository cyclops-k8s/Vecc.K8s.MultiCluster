using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Net.Http.Json;
using Vecc.K8s.MultiCluster.Api.Models.Core;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;
using Vecc.K8s.MultiCluster.Api.Services;
using Vecc.K8s.MultiCluster.Api.Services.Default;
using CoreHost = Vecc.K8s.MultiCluster.Api.Models.Core.Host;

namespace Vecc.K8s.MultiCluster.Api.Tests.Services.Default
{
    public class DefaultHostnameSynchronizerTests
    {
        private readonly Mock<ILogger<DefaultHostnameSynchronizer>> _loggerMock;
        private readonly Mock<IIngressManager> _ingressManagerMock;
        private readonly Mock<IServiceManager> _serviceManagerMock;
        private readonly Mock<ICache> _cacheMock;
        private readonly Mock<IHostApplicationLifetime> _lifetimeMock;
        private readonly LeaderStatus _leaderStatus;
        private readonly Mock<IDateTimeProvider> _dateTimeProviderMock;
        private readonly Mock<IHttpClientFactory> _clientFactoryMock;
        private readonly Mock<IGslbManager> _gslbManagerMock;
        private readonly MultiClusterOptions _options;

        public DefaultHostnameSynchronizerTests()
        {
            _loggerMock = new Mock<ILogger<DefaultHostnameSynchronizer>>();
            _ingressManagerMock = new Mock<IIngressManager>();
            _serviceManagerMock = new Mock<IServiceManager>();
            _cacheMock = new Mock<ICache>();
            _lifetimeMock = new Mock<IHostApplicationLifetime>();
            _lifetimeMock.Setup(x => x.ApplicationStopping).Returns(new CancellationToken());
            _leaderStatus = new LeaderStatus();
            _dateTimeProviderMock = new Mock<IDateTimeProvider>();
            _clientFactoryMock = new Mock<IHttpClientFactory>();
            _gslbManagerMock = new Mock<IGslbManager>();
            _options = new MultiClusterOptions
            {
                ClusterIdentifier = "test-cluster",
                Namespace = "test-ns",
                HeartbeatCheckInterval = 30,
                HeartbeatSetInterval = 10,
                HeartbeatTimeout = 60,
                Peers = Array.Empty<PeerHosts>()
            };
        }

        private DefaultHostnameSynchronizer CreateSynchronizer()
        {
            return new DefaultHostnameSynchronizer(
                _loggerMock.Object,
                _ingressManagerMock.Object,
                _serviceManagerMock.Object,
                _cacheMock.Object,
                Options.Create(_options),
                _lifetimeMock.Object,
                _leaderStatus,
                _dateTimeProviderMock.Object,
                _clientFactoryMock.Object,
                _gslbManagerMock.Object);
        }

        #region SynchronizeLocalIngressAsync

        [Fact]
        public async Task SynchronizeLocalIngressAsync_CallsSynchronizeLocalClusterAsync_ReturnsTrue()
        {
            // Arrange
            var ingress = new V1Ingress
            {
                Metadata = new V1ObjectMeta { Name = "test-ingress", NamespaceProperty = "default" }
            };

            SetupDefaultSyncMocks();
            var synchronizer = CreateSynchronizer();

            // Act
            var result = await synchronizer.SynchronizeLocalIngressAsync(ingress);

            // Assert
            Assert.True(result);
        }

        #endregion

        #region SynchronizeLocalServiceAsync

        [Fact]
        public async Task SynchronizeLocalServiceAsync_CallsSynchronizeLocalClusterAsync_ReturnsTrue()
        {
            // Arrange
            var service = new V1Service
            {
                Metadata = new V1ObjectMeta { Name = "test-service", NamespaceProperty = "default" }
            };

            SetupDefaultSyncMocks();
            var synchronizer = CreateSynchronizer();

            // Act
            var result = await synchronizer.SynchronizeLocalServiceAsync(service);

            // Assert
            Assert.True(result);
        }

        #endregion

        #region SynchronizeLocalEndpointSliceAsync

        [Fact]
        public async Task SynchronizeLocalEndpointSliceAsync_CallsSynchronizeLocalClusterAsync_ReturnsTrue()
        {
            // Arrange
            var endpointSlice = new V1EndpointSlice
            {
                Metadata = new V1ObjectMeta { Name = "test-slice", NamespaceProperty = "default" }
            };

            SetupDefaultSyncMocks();
            var synchronizer = CreateSynchronizer();

            // Act
            var result = await synchronizer.SynchronizeLocalEndpointSliceAsync(endpointSlice);

            // Assert
            Assert.True(result);
        }

        #endregion

        #region SynchronizeRemoteClustersAsync

        [Fact]
        public async Task SynchronizeRemoteClustersAsync_NoPeers_ReturnsEarly()
        {
            // Arrange
            _options.Peers = Array.Empty<PeerHosts>();
            var synchronizer = CreateSynchronizer();

            // Act
            await synchronizer.SynchronizeRemoteClustersAsync();

            // Assert
            _cacheMock.Verify(x => x.SetClusterCacheAsync(It.IsAny<string>(), It.IsAny<CoreHost[]>()), Times.Never);
        }

        [Fact]
        public async Task SynchronizeRemoteClustersAsync_WithPeer_SetsClusterCache()
        {
            // Arrange
            _options.Peers = new[]
            {
                new PeerHosts { Url = "http://peer1", Identifier = "peer-1" }
            };

            var responseHosts = new[]
            {
                new Vecc.K8s.MultiCluster.Api.Models.Api.HostModel
                {
                    Hostname = "test.example.com",
                    HostIPs = new[]
                    {
                        new Vecc.K8s.MultiCluster.Api.Models.Api.HostIP { IPAddress = "10.0.0.1", Priority = 1, Weight = 50 }
                    }
                }
            };

            var handler = new FakeHttpMessageHandler(responseHosts);
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://peer1") };
            _clientFactoryMock.Setup(x => x.CreateClient("http://peer1")).Returns(httpClient);

            var synchronizer = CreateSynchronizer();

            // Act
            await synchronizer.SynchronizeRemoteClustersAsync();

            // Assert
            _cacheMock.Verify(x => x.SetClusterCacheAsync("peer-1", It.Is<CoreHost[]>(h =>
                h.Length == 1 &&
                h[0].Hostname == "test.example.com" &&
                h[0].HostIPs[0].IPAddress == "10.0.0.1" &&
                h[0].HostIPs[0].ClusterIdentifier == "peer-1"
            )), Times.Once);
        }

        [Fact]
        public async Task SynchronizeRemoteClustersAsync_PeerThrows_ContinuesWithoutThrowing()
        {
            // Arrange
            _options.Peers = new[]
            {
                new PeerHosts { Url = "http://bad-peer", Identifier = "bad" }
            };

            _clientFactoryMock.Setup(x => x.CreateClient("http://bad-peer"))
                .Throws(new Exception("Connection failed"));

            var synchronizer = CreateSynchronizer();

            // Act (should not throw)
            await synchronizer.SynchronizeRemoteClustersAsync();

            // Assert
            _cacheMock.Verify(x => x.SetClusterCacheAsync(It.IsAny<string>(), It.IsAny<CoreHost[]>()), Times.Never);
        }

        [Fact]
        public async Task SynchronizeRemoteClustersAsync_MultiplePeers_SyncsBoth()
        {
            // Arrange
            _options.Peers = new[]
            {
                new PeerHosts { Url = "http://peer1", Identifier = "peer-1" },
                new PeerHosts { Url = "http://peer2", Identifier = "peer-2" }
            };

            var hosts1 = new[] { new Vecc.K8s.MultiCluster.Api.Models.Api.HostModel { Hostname = "a.com", HostIPs = Array.Empty<Vecc.K8s.MultiCluster.Api.Models.Api.HostIP>() } };
            var hosts2 = new[] { new Vecc.K8s.MultiCluster.Api.Models.Api.HostModel { Hostname = "b.com", HostIPs = Array.Empty<Vecc.K8s.MultiCluster.Api.Models.Api.HostIP>() } };

            var handler1 = new FakeHttpMessageHandler(hosts1);
            var handler2 = new FakeHttpMessageHandler(hosts2);
            _clientFactoryMock.Setup(x => x.CreateClient("http://peer1")).Returns(new HttpClient(handler1) { BaseAddress = new Uri("http://peer1") });
            _clientFactoryMock.Setup(x => x.CreateClient("http://peer2")).Returns(new HttpClient(handler2) { BaseAddress = new Uri("http://peer2") });

            var synchronizer = CreateSynchronizer();

            // Act
            await synchronizer.SynchronizeRemoteClustersAsync();

            // Assert
            _cacheMock.Verify(x => x.SetClusterCacheAsync("peer-1", It.IsAny<CoreHost[]>()), Times.Once);
            _cacheMock.Verify(x => x.SetClusterCacheAsync("peer-2", It.IsAny<CoreHost[]>()), Times.Once);
        }

        #endregion

        #region SynchronizeLocalClusterAsync

        [Fact]
        public async Task SynchronizeLocalClusterAsync_NoIngressesOrGslbs_SetsEmptyCache()
        {
            // Arrange
            SetupDefaultSyncMocks();
            var synchronizer = CreateSynchronizer();

            // Act
            await synchronizer.SynchronizeLocalClusterAsync();

            // Assert
            _cacheMock.Verify(x => x.SetClusterCacheAsync("test-cluster", It.Is<CoreHost[]>(h => h.Length == 0)), Times.Once);
        }

        [Fact]
        public async Task SynchronizeLocalClusterAsync_WithGslbServiceReference_SetsIpAddresses()
        {
            // Arrange
            var service = new V1Service
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "my-service",
                    NamespaceProperty = "default",
                    Uid = "svc-uid",
                    ResourceVersion = "1"
                },
                Spec = new V1ServiceSpec { Type = "LoadBalancer" },
                Status = new V1ServiceStatus
                {
                    LoadBalancer = new V1LoadBalancerStatus
                    {
                        Ingress = new List<V1LoadBalancerIngress>
                        {
                            new V1LoadBalancerIngress { Ip = "192.168.1.1" }
                        }
                    }
                }
            };

            var gslb = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "my-gslb", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "my-service",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Service
                },
                Hostnames = new[] { "test.example.com" },
                Priority = 10,
                Weight = 50
            };

            var endpointSlice = new V1EndpointSlice
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "my-service-slice",
                    NamespaceProperty = "default",
                    Uid = "slice-uid",
                    ResourceVersion = "1",
                    Labels = new Dictionary<string, string> { { "kubernetes.io/service-name", "my-service" } }
                },
                Endpoints = new List<V1Endpoint>
                {
                    new V1Endpoint { Conditions = new V1EndpointConditions { Ready = true } }
                }
            };

            _cacheMock.Setup(x => x.GetHostsAsync("test-cluster")).ReturnsAsync(Array.Empty<CoreHost>());
            _ingressManagerMock.Setup(x => x.GetIngressesAsync()).ReturnsAsync(new List<V1Ingress>());
            _serviceManagerMock.Setup(x => x.GetServicesAsync()).ReturnsAsync(new List<V1Service> { service });
            _serviceManagerMock.Setup(x => x.GetEndpointSlicesAsync()).ReturnsAsync(new List<V1EndpointSlice> { endpointSlice });
            _gslbManagerMock.Setup(x => x.GetGslbsAsync()).ReturnsAsync(new[] { gslb });
            _ingressManagerMock.Setup(x => x.GetAvailableHostnamesAsync(It.IsAny<IList<V1Ingress>>(), It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new Dictionary<string, IList<V1Ingress>>());
            _serviceManagerMock.Setup(x => x.GetLoadBalancerServicesAsync(It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new List<V1Service> { service });
            _serviceManagerMock.Setup(x => x.GetReadyEndpointCount(It.IsAny<IEnumerable<V1EndpointSlice>>())).Returns(1);

            var synchronizer = CreateSynchronizer();

            // Act
            await synchronizer.SynchronizeLocalClusterAsync();

            // Assert
            _cacheMock.Verify(x => x.SetClusterCacheAsync("test-cluster", It.Is<CoreHost[]>(h =>
                h.Length == 1 &&
                h[0].Hostname == "test.example.com" &&
                h[0].HostIPs[0].IPAddress == "192.168.1.1" &&
                h[0].HostIPs[0].Priority == 10 &&
                h[0].HostIPs[0].Weight == 50
            )), Times.Once);
        }

        [Fact]
        public async Task SynchronizeLocalClusterAsync_GslbServiceWithNoReadyEndpoints_SkipsHostname()
        {
            // Arrange
            var service = new V1Service
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "my-service", NamespaceProperty = "default",
                    Uid = "svc-uid", ResourceVersion = "1"
                },
                Spec = new V1ServiceSpec { Type = "LoadBalancer" },
                Status = new V1ServiceStatus
                {
                    LoadBalancer = new V1LoadBalancerStatus
                    {
                        Ingress = new List<V1LoadBalancerIngress> { new V1LoadBalancerIngress { Ip = "10.0.0.1" } }
                    }
                }
            };

            var gslb = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "gslb1", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "my-service",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Service
                },
                Hostnames = new[] { "test.example.com" },
                Priority = 1,
                Weight = 50
            };

            var endpointSlice = new V1EndpointSlice
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "my-service-slice", NamespaceProperty = "default",
                    Uid = "slice-uid", ResourceVersion = "1",
                    Labels = new Dictionary<string, string> { { "kubernetes.io/service-name", "my-service" } }
                },
                Endpoints = new List<V1Endpoint>
                {
                    new V1Endpoint { Conditions = new V1EndpointConditions { Ready = false } }
                }
            };

            _cacheMock.Setup(x => x.GetHostsAsync("test-cluster")).ReturnsAsync(Array.Empty<CoreHost>());
            _ingressManagerMock.Setup(x => x.GetIngressesAsync()).ReturnsAsync(new List<V1Ingress>());
            _serviceManagerMock.Setup(x => x.GetServicesAsync()).ReturnsAsync(new List<V1Service> { service });
            _serviceManagerMock.Setup(x => x.GetEndpointSlicesAsync()).ReturnsAsync(new List<V1EndpointSlice> { endpointSlice });
            _gslbManagerMock.Setup(x => x.GetGslbsAsync()).ReturnsAsync(new[] { gslb });
            _ingressManagerMock.Setup(x => x.GetAvailableHostnamesAsync(It.IsAny<IList<V1Ingress>>(), It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new Dictionary<string, IList<V1Ingress>>());
            _serviceManagerMock.Setup(x => x.GetLoadBalancerServicesAsync(It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new List<V1Service> { service });
            _serviceManagerMock.Setup(x => x.GetReadyEndpointCount(It.IsAny<IEnumerable<V1EndpointSlice>>())).Returns(0);

            var synchronizer = CreateSynchronizer();

            // Act
            await synchronizer.SynchronizeLocalClusterAsync();

            // Assert - hostname skipped due to no ready endpoints
            _cacheMock.Verify(x => x.SetClusterCacheAsync("test-cluster", It.Is<CoreHost[]>(h => h.Length == 0)), Times.Once);
        }

        [Fact]
        public async Task SynchronizeLocalClusterAsync_GslbExternalNameService_SkipsEndpointCheck()
        {
            // Arrange
            var service = new V1Service
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "my-service", NamespaceProperty = "default",
                    Uid = "svc-uid", ResourceVersion = "1"
                },
                Spec = new V1ServiceSpec { Type = "ExternalName" },
                Status = new V1ServiceStatus
                {
                    LoadBalancer = new V1LoadBalancerStatus
                    {
                        Ingress = new List<V1LoadBalancerIngress> { new V1LoadBalancerIngress { Ip = "10.0.0.1" } }
                    }
                }
            };

            var gslb = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "gslb1", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "my-service",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Service
                },
                Hostnames = new[] { "test.example.com" },
                Priority = 1,
                Weight = 50
            };

            _cacheMock.Setup(x => x.GetHostsAsync("test-cluster")).ReturnsAsync(Array.Empty<CoreHost>());
            _ingressManagerMock.Setup(x => x.GetIngressesAsync()).ReturnsAsync(new List<V1Ingress>());
            _serviceManagerMock.Setup(x => x.GetServicesAsync()).ReturnsAsync(new List<V1Service> { service });
            _serviceManagerMock.Setup(x => x.GetEndpointSlicesAsync()).ReturnsAsync(new List<V1EndpointSlice>());
            _gslbManagerMock.Setup(x => x.GetGslbsAsync()).ReturnsAsync(new[] { gslb });
            _ingressManagerMock.Setup(x => x.GetAvailableHostnamesAsync(It.IsAny<IList<V1Ingress>>(), It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new Dictionary<string, IList<V1Ingress>>());
            _serviceManagerMock.Setup(x => x.GetLoadBalancerServicesAsync(It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new List<V1Service> { service });

            var synchronizer = CreateSynchronizer();

            // Act
            await synchronizer.SynchronizeLocalClusterAsync();

            // Assert - ExternalName services skip endpoint check, so hostname should be present
            _cacheMock.Verify(x => x.SetClusterCacheAsync("test-cluster", It.Is<CoreHost[]>(h =>
                h.Length == 1 && h[0].Hostname == "test.example.com"
            )), Times.Once);
        }

        [Fact]
        public async Task SynchronizeLocalClusterAsync_GslbWithMultipleServicesForSameHostname_Skips()
        {
            // Arrange
            var gslb1 = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "gslb1", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "service-a",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Service
                },
                Hostnames = new[] { "test.example.com" },
                Priority = 1,
                Weight = 50
            };

            var gslb2 = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "gslb2", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "service-b",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Service
                },
                Hostnames = new[] { "test.example.com" },
                Priority = 1,
                Weight = 50
            };

            _cacheMock.Setup(x => x.GetHostsAsync("test-cluster")).ReturnsAsync(Array.Empty<CoreHost>());
            _ingressManagerMock.Setup(x => x.GetIngressesAsync()).ReturnsAsync(new List<V1Ingress>());
            _serviceManagerMock.Setup(x => x.GetServicesAsync()).ReturnsAsync(new List<V1Service>());
            _serviceManagerMock.Setup(x => x.GetEndpointSlicesAsync()).ReturnsAsync(new List<V1EndpointSlice>());
            _gslbManagerMock.Setup(x => x.GetGslbsAsync()).ReturnsAsync(new[] { gslb1, gslb2 });
            _ingressManagerMock.Setup(x => x.GetAvailableHostnamesAsync(It.IsAny<IList<V1Ingress>>(), It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new Dictionary<string, IList<V1Ingress>>());
            _serviceManagerMock.Setup(x => x.GetLoadBalancerServicesAsync(It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new List<V1Service>());

            var synchronizer = CreateSynchronizer();

            // Act
            await synchronizer.SynchronizeLocalClusterAsync();

            // Assert - multiple services for same hostname gets skipped
            _cacheMock.Verify(x => x.SetClusterCacheAsync("test-cluster", It.Is<CoreHost[]>(h => h.Length == 0)), Times.Once);
        }

        [Fact]
        public async Task SynchronizeLocalClusterAsync_MixedGslbReferences_SkipsHostname()
        {
            // Arrange
            var gslb1 = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "gslb1", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "service-a",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Service
                },
                Hostnames = new[] { "mixed.example.com" },
                Priority = 1,
                Weight = 50
            };

            var gslb2 = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "gslb2", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "ingress-a",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Ingress
                },
                Hostnames = new[] { "mixed.example.com" },
                Priority = 1,
                Weight = 50
            };

            _cacheMock.Setup(x => x.GetHostsAsync("test-cluster")).ReturnsAsync(Array.Empty<CoreHost>());
            _ingressManagerMock.Setup(x => x.GetIngressesAsync()).ReturnsAsync(new List<V1Ingress>());
            _serviceManagerMock.Setup(x => x.GetServicesAsync()).ReturnsAsync(new List<V1Service>());
            _serviceManagerMock.Setup(x => x.GetEndpointSlicesAsync()).ReturnsAsync(new List<V1EndpointSlice>());
            _gslbManagerMock.Setup(x => x.GetGslbsAsync()).ReturnsAsync(new[] { gslb1, gslb2 });
            _ingressManagerMock.Setup(x => x.GetAvailableHostnamesAsync(It.IsAny<IList<V1Ingress>>(), It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new Dictionary<string, IList<V1Ingress>>());
            _serviceManagerMock.Setup(x => x.GetLoadBalancerServicesAsync(It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new List<V1Service>());

            var synchronizer = CreateSynchronizer();

            // Act
            await synchronizer.SynchronizeLocalClusterAsync();

            // Assert - mixed references get skipped
            _cacheMock.Verify(x => x.SetClusterCacheAsync("test-cluster", It.Is<CoreHost[]>(h => h.Length == 0)), Times.Once);
        }

        [Fact]
        public async Task SynchronizeLocalClusterAsync_GslbIngressReference_SetsIpAddresses()
        {
            // Arrange
            var ingress = new V1Ingress
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "my-ingress", NamespaceProperty = "default",
                    Uid = "ingress-uid", ResourceVersion = "1"
                },
                Status = new V1IngressStatus
                {
                    LoadBalancer = new V1IngressLoadBalancerStatus
                    {
                        Ingress = new List<V1IngressLoadBalancerIngress>
                        {
                            new V1IngressLoadBalancerIngress { Ip = "10.0.0.5" }
                        }
                    }
                }
            };

            var gslb = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "gslb1", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "my-ingress",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Ingress
                },
                Hostnames = new[] { "ingress.example.com" },
                Priority = 5,
                Weight = 100
            };

            var ingressHosts = new Dictionary<string, IList<V1Ingress>>
            {
                { "some-host.com", new List<V1Ingress> { ingress } }
            };

            _cacheMock.Setup(x => x.GetHostsAsync("test-cluster")).ReturnsAsync(Array.Empty<CoreHost>());
            _ingressManagerMock.Setup(x => x.GetIngressesAsync()).ReturnsAsync(new List<V1Ingress> { ingress });
            _ingressManagerMock.Setup(x => x.GetRelatedServiceNamesAsync(It.IsAny<V1Ingress>())).ReturnsAsync(Array.Empty<string>());
            _serviceManagerMock.Setup(x => x.GetServicesAsync()).ReturnsAsync(new List<V1Service>());
            _serviceManagerMock.Setup(x => x.GetEndpointSlicesAsync()).ReturnsAsync(new List<V1EndpointSlice>());
            _gslbManagerMock.Setup(x => x.GetGslbsAsync()).ReturnsAsync(new[] { gslb });
            _ingressManagerMock.Setup(x => x.GetAvailableHostnamesAsync(It.IsAny<IList<V1Ingress>>(), It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(ingressHosts);
            _serviceManagerMock.Setup(x => x.GetLoadBalancerServicesAsync(It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new List<V1Service>());

            var synchronizer = CreateSynchronizer();

            // Act
            await synchronizer.SynchronizeLocalClusterAsync();

            // Assert
            _cacheMock.Verify(x => x.SetClusterCacheAsync("test-cluster", It.Is<CoreHost[]>(h =>
                h.Length == 1 &&
                h[0].Hostname == "ingress.example.com" &&
                h[0].HostIPs[0].IPAddress == "10.0.0.5" &&
                h[0].HostIPs[0].Priority == 5 &&
                h[0].HostIPs[0].Weight == 100
            )), Times.Once);
        }

        [Fact]
        public async Task SynchronizeLocalClusterAsync_RemovesStaleHosts()
        {
            // Arrange
            var existingHosts = new[]
            {
                new CoreHost { Hostname = "old.example.com", HostIPs = new[] { new HostIP { IPAddress = "1.1.1.1" } } }
            };

            _cacheMock.Setup(x => x.GetHostsAsync("test-cluster")).ReturnsAsync(existingHosts);
            _ingressManagerMock.Setup(x => x.GetIngressesAsync()).ReturnsAsync(new List<V1Ingress>());
            _serviceManagerMock.Setup(x => x.GetServicesAsync()).ReturnsAsync(new List<V1Service>());
            _serviceManagerMock.Setup(x => x.GetEndpointSlicesAsync()).ReturnsAsync(new List<V1EndpointSlice>());
            _gslbManagerMock.Setup(x => x.GetGslbsAsync()).ReturnsAsync(Array.Empty<V1Gslb>());
            _ingressManagerMock.Setup(x => x.GetAvailableHostnamesAsync(It.IsAny<IList<V1Ingress>>(), It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new Dictionary<string, IList<V1Ingress>>());
            _serviceManagerMock.Setup(x => x.GetLoadBalancerServicesAsync(It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new List<V1Service>());

            // No peers, so SendHostUpdatesAsync is a no-op
            var synchronizer = CreateSynchronizer();

            // Act
            await synchronizer.SynchronizeLocalClusterAsync();

            // Assert - cache is set with empty hosts (old.example.com removed)
            _cacheMock.Verify(x => x.SetClusterCacheAsync("test-cluster", It.Is<CoreHost[]>(h => h.Length == 0)), Times.Once);
        }

        [Fact]
        public async Task SynchronizeLocalClusterAsync_TracksServicesAndEndpointSlices()
        {
            // Arrange
            var ingress = new V1Ingress
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-ingress", NamespaceProperty = "default",
                    Uid = "ingress-uid", ResourceVersion = "v1"
                }
            };

            _cacheMock.Setup(x => x.GetHostsAsync("test-cluster")).ReturnsAsync(Array.Empty<CoreHost>());
            _ingressManagerMock.Setup(x => x.GetIngressesAsync()).ReturnsAsync(new List<V1Ingress> { ingress });
            _ingressManagerMock.Setup(x => x.GetRelatedServiceNamesAsync(ingress)).ReturnsAsync(new[] { "backend-svc" });
            _serviceManagerMock.Setup(x => x.GetServicesAsync()).ReturnsAsync(new List<V1Service>());
            _serviceManagerMock.Setup(x => x.GetEndpointSlicesAsync()).ReturnsAsync(new List<V1EndpointSlice>());
            _gslbManagerMock.Setup(x => x.GetGslbsAsync()).ReturnsAsync(Array.Empty<V1Gslb>());
            _ingressManagerMock.Setup(x => x.GetAvailableHostnamesAsync(It.IsAny<IList<V1Ingress>>(), It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new Dictionary<string, IList<V1Ingress>>());
            _serviceManagerMock.Setup(x => x.GetLoadBalancerServicesAsync(It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new List<V1Service>());

            var synchronizer = CreateSynchronizer();

            // Act
            await synchronizer.SynchronizeLocalClusterAsync();

            // Assert
            _cacheMock.Verify(x => x.UntrackAllServicesAsync(), Times.Once);
            _cacheMock.Verify(x => x.SetResourceVersionAsync("ingress-uid", "v1"), Times.Once);
            _cacheMock.Verify(x => x.TrackServiceAsync("default", "backend-svc"), Times.Once);
        }

        #endregion

        #region Helper Methods

        private void SetupDefaultSyncMocks()
        {
            _cacheMock.Setup(x => x.GetHostsAsync("test-cluster")).ReturnsAsync(Array.Empty<CoreHost>());
            _ingressManagerMock.Setup(x => x.GetIngressesAsync()).ReturnsAsync(new List<V1Ingress>());
            _serviceManagerMock.Setup(x => x.GetServicesAsync()).ReturnsAsync(new List<V1Service>());
            _serviceManagerMock.Setup(x => x.GetEndpointSlicesAsync()).ReturnsAsync(new List<V1EndpointSlice>());
            _gslbManagerMock.Setup(x => x.GetGslbsAsync()).ReturnsAsync(Array.Empty<V1Gslb>());
            _ingressManagerMock.Setup(x => x.GetAvailableHostnamesAsync(It.IsAny<IList<V1Ingress>>(), It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new Dictionary<string, IList<V1Ingress>>());
            _serviceManagerMock.Setup(x => x.GetLoadBalancerServicesAsync(It.IsAny<IList<V1Service>>(), It.IsAny<IList<V1EndpointSlice>>()))
                .ReturnsAsync(new List<V1Service>());
        }

        #endregion

        #region FakeHttpMessageHandler

        private class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly object? _responseContent;
            private readonly HttpStatusCode _statusCode;

            public FakeHttpMessageHandler(object? responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
            {
                _responseContent = responseContent;
                _statusCode = statusCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(_statusCode);
                if (_responseContent != null)
                {
                    response.Content = JsonContent.Create(_responseContent);
                }
                return Task.FromResult(response);
            }
        }

        #endregion
    }
}
