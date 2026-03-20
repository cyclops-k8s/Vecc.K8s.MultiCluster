using k8s.Models;
using KubeOps.KubernetesClient;
using Microsoft.Extensions.Logging;
using Moq;
using Vecc.K8s.MultiCluster.Api.Controllers;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;
using Vecc.K8s.MultiCluster.Api.Services;

namespace Vecc.K8s.MultiCluster.Api.Tests.Controllers
{
    public class K8sChangedControllerTests
    {
        private readonly Mock<ILogger<K8sChangedController>> _loggerMock;
        private readonly Mock<ICache> _cacheMock;
        private readonly Mock<IHostnameSynchronizer> _synchronizerMock;
        private readonly Mock<IKubernetesClient> _clientMock;
        private readonly K8sChangedController _controller;
        private readonly Mock<IServiceManager> _serviceManagerMock;

        public K8sChangedControllerTests()
        {
            _loggerMock = new Mock<ILogger<K8sChangedController>>();
            _cacheMock = new Mock<ICache>();
            _synchronizerMock = new Mock<IHostnameSynchronizer>();
            _clientMock = new Mock<IKubernetesClient>();
            _serviceManagerMock = new Mock<IServiceManager>();
            _controller = new K8sChangedController(_loggerMock.Object,
                _cacheMock.Object,
                _synchronizerMock.Object,
                _clientMock.Object,
                _serviceManagerMock.Object);
        }

        #region Ingress

        [Fact]
        public async Task IngressDeleted_ResourceVersionChanged_SyncsIngress()
        {
            // Arrange
            var ingress = new V1Ingress
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-ingress",
                    NamespaceProperty = "default",
                    Uid = "uid-1",
                    ResourceVersion = "v2"
                }
            };
            _cacheMock.Setup(x => x.GetLastResourceVersionAsync("uid-1")).ReturnsAsync("v1");
            _synchronizerMock.Setup(x => x.SynchronizeLocalIngressAsync(ingress)).ReturnsAsync(true);

            // Act
            var result = await _controller.DeletedAsync(ingress, CancellationToken.None);

            // Assert
            _synchronizerMock.Verify(x => x.SynchronizeLocalIngressAsync(ingress), Times.Once);
        }

        [Fact]
        public async Task IngressDeleted_ResourceVersionSame_DoesNotSync()
        {
            // Arrange
            var ingress = new V1Ingress
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-ingress",
                    NamespaceProperty = "default",
                    Uid = "uid-1",
                    ResourceVersion = "v1"
                }
            };
            _cacheMock.Setup(x => x.GetLastResourceVersionAsync("uid-1")).ReturnsAsync("v1");

            // Act
            var result = await _controller.DeletedAsync(ingress, CancellationToken.None);

            // Assert
            _synchronizerMock.Verify(x => x.SynchronizeLocalIngressAsync(It.IsAny<V1Ingress>()), Times.Never);
        }

        [Fact]
        public async Task IngressReconcile_ResourceVersionChanged_SyncsIngress()
        {
            // Arrange
            var ingress = new V1Ingress
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-ingress",
                    NamespaceProperty = "default",
                    Uid = "uid-1",
                    ResourceVersion = "v3"
                }
            };
            _cacheMock.Setup(x => x.GetLastResourceVersionAsync("uid-1")).ReturnsAsync("v2");
            _synchronizerMock.Setup(x => x.SynchronizeLocalIngressAsync(ingress)).ReturnsAsync(true);

            // Act
            var result = await _controller.ReconcileAsync(ingress, CancellationToken.None);

            // Assert
            _synchronizerMock.Verify(x => x.SynchronizeLocalIngressAsync(ingress), Times.Once);
        }

        #endregion

        #region Service

        [Fact]
        public async Task ServiceDeleted_MonitoredAndChanged_SyncsService()
        {
            // Arrange
            var service = new V1Service
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-svc",
                    NamespaceProperty = "default",
                    Uid = "svc-uid",
                    ResourceVersion = "v2"
                }
            };
            _cacheMock.Setup(x => x.IsServiceMonitoredAsync("default", "test-svc")).ReturnsAsync(true);
            _cacheMock.Setup(x => x.GetLastResourceVersionAsync("svc-uid")).ReturnsAsync("v1");
            _synchronizerMock.Setup(x => x.SynchronizeLocalServiceAsync(service)).ReturnsAsync(true);

            // Act
            await _controller.DeletedAsync(service, CancellationToken.None);

            // Assert
            _synchronizerMock.Verify(x => x.SynchronizeLocalServiceAsync(service), Times.Once);
        }

        [Fact]
        public async Task ServiceDeleted_NotMonitored_DoesNotSync()
        {
            // Arrange
            var service = new V1Service
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-svc",
                    NamespaceProperty = "default",
                    Uid = "svc-uid",
                    ResourceVersion = "v2"
                }
            };
            _cacheMock.Setup(x => x.IsServiceMonitoredAsync("default", "test-svc")).ReturnsAsync(false);

            // Act
            await _controller.DeletedAsync(service, CancellationToken.None);

            // Assert
            _synchronizerMock.Verify(x => x.SynchronizeLocalServiceAsync(It.IsAny<V1Service>()), Times.Never);
        }

        [Fact]
        public async Task ServiceReconcile_MonitoredAndChanged_SyncsService()
        {
            // Arrange
            var service = new V1Service
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-svc",
                    NamespaceProperty = "default",
                    Uid = "svc-uid",
                    ResourceVersion = "v3"
                }
            };
            _cacheMock.Setup(x => x.IsServiceMonitoredAsync("default", "test-svc")).ReturnsAsync(true);
            _cacheMock.Setup(x => x.GetLastResourceVersionAsync("svc-uid")).ReturnsAsync("v2");
            _synchronizerMock.Setup(x => x.SynchronizeLocalServiceAsync(service)).ReturnsAsync(true);

            // Act
            await _controller.ReconcileAsync(service, CancellationToken.None);

            // Assert
            _synchronizerMock.Verify(x => x.SynchronizeLocalServiceAsync(service), Times.Once);
        }

        #endregion

        #region EndpointSlice

        [Fact]
        public async Task EndpointSliceDeleted_MonitoredAndCountChanged_SyncsEndpointSlice()
        {
            // Arrange
            var endpointSlice = new V1EndpointSlice
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-slice",
                    NamespaceProperty = "default",
                    Uid = "slice-uid",
                    ResourceVersion = "v2",
                    Labels = new Dictionary<string, string> { { "kubernetes.io/service-name", "my-svc" } }
                },
                Endpoints = new List<V1Endpoint>
                {
                    new V1Endpoint { Conditions = new V1EndpointConditions { Ready = true } }
                }
            };
            _cacheMock.Setup(x => x.IsServiceMonitoredAsync("default", "my-svc")).ReturnsAsync(true);
            _cacheMock.Setup(x => x.GetLastResourceVersionAsync("slice-uid")).ReturnsAsync("v1");
            _cacheMock.Setup(x => x.GetEndpointsCountAsync("default", "my-svc")).ReturnsAsync(0);
            _serviceManagerMock.Setup(x => x.GetEndpointSlicesAsync("default", "my-svc"))
                .ReturnsAsync(new List<V1EndpointSlice> { endpointSlice });
            _synchronizerMock.Setup(x => x.SynchronizeLocalEndpointSliceAsync(endpointSlice)).ReturnsAsync(true);

            // Act
            await _controller.DeletedAsync(endpointSlice, CancellationToken.None);

            // Assert - old was 0, new is 1 ready endpoint -> sync required
            _synchronizerMock.Verify(x => x.SynchronizeLocalEndpointSliceAsync(endpointSlice), Times.Once);
        }

        [Fact]
        public async Task EndpointSliceReconcile_MonitoredButCountBothNonZero_DoesNotSync()
        {
            // Arrange
            var endpointSlice = new V1EndpointSlice
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-slice",
                    NamespaceProperty = "default",
                    Uid = "slice-uid",
                    ResourceVersion = "v2",
                    Labels = new Dictionary<string, string> { { "kubernetes.io/service-name", "my-svc" } }
                },
                Endpoints = new List<V1Endpoint>
                {
                    new V1Endpoint { Conditions = new V1EndpointConditions { Ready = true } },
                    new V1Endpoint { Conditions = new V1EndpointConditions { Ready = true } }
                }
            };
            _cacheMock.Setup(x => x.IsServiceMonitoredAsync("default", "my-svc")).ReturnsAsync(true);
            _cacheMock.Setup(x => x.GetLastResourceVersionAsync("slice-uid")).ReturnsAsync("v1");
            _cacheMock.Setup(x => x.GetEndpointsCountAsync("default", "my-svc")).ReturnsAsync(3);
            _serviceManagerMock.Setup(x => x.GetEndpointSlicesAsync("default", "my-svc"))
                .ReturnsAsync(new List<V1EndpointSlice> { endpointSlice });
            // Act
            await _controller.ReconcileAsync(endpointSlice, CancellationToken.None);

            // Assert - old count 3, new count 2, both non-zero, no sync
            _synchronizerMock.Verify(x => x.SynchronizeLocalEndpointSliceAsync(It.IsAny<V1EndpointSlice>()), Times.Never);
            _cacheMock.Verify(x => x.SetEndpointsCountAsync("default", "my-svc", 2), Times.Once);
        }

        [Fact]
        public async Task EndpointSliceDeleted_MonitoredAndCountGoesToZero_SyncsEndpointSlice()
        {
            // Arrange
            var endpointSlice = new V1EndpointSlice
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-slice",
                    NamespaceProperty = "default",
                    Uid = "slice-uid",
                    ResourceVersion = "v2",
                    Labels = new Dictionary<string, string> { { "kubernetes.io/service-name", "my-svc" } }
                },
                Endpoints = new List<V1Endpoint>()
            };
            _cacheMock.Setup(x => x.IsServiceMonitoredAsync("default", "my-svc")).ReturnsAsync(true);
            _cacheMock.Setup(x => x.GetLastResourceVersionAsync("slice-uid")).ReturnsAsync("v1");
            _cacheMock.Setup(x => x.GetEndpointsCountAsync("default", "my-svc")).ReturnsAsync(2);
            _serviceManagerMock.Setup(x => x.GetEndpointSlicesAsync("default", "my-svc"))
                .ReturnsAsync(new List<V1EndpointSlice>());
            _synchronizerMock.Setup(x => x.SynchronizeLocalEndpointSliceAsync(endpointSlice)).ReturnsAsync(true);

            // Act
            await _controller.DeletedAsync(endpointSlice, CancellationToken.None);

            // Assert - old count 2, new count 0, sync required
            _synchronizerMock.Verify(x => x.SynchronizeLocalEndpointSliceAsync(endpointSlice), Times.Once);
        }

        [Fact]
        public async Task EndpointSliceReconcile_NotMonitored_DoesNotSync()
        {
            // Arrange
            var endpointSlice = new V1EndpointSlice
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-slice",
                    NamespaceProperty = "default",
                    Uid = "slice-uid",
                    ResourceVersion = "v2",
                    Labels = new Dictionary<string, string> { { "kubernetes.io/service-name", "my-svc" } }
                }
            };
            _cacheMock.Setup(x => x.IsServiceMonitoredAsync("default", "my-svc")).ReturnsAsync(false);

            // Act
            await _controller.ReconcileAsync(endpointSlice, CancellationToken.None);

            // Assert
            _synchronizerMock.Verify(x => x.SynchronizeLocalEndpointSliceAsync(It.IsAny<V1EndpointSlice>()), Times.Never);
            _cacheMock.Verify(x => x.SetEndpointsCountAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        #endregion

        #region Gslb

        [Fact]
        public async Task GslbReconcile_IngressReference_IngressFound_SyncsIngress()
        {
            // Arrange
            var gslb = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "gslb1", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "my-ingress",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Ingress
                },
                Hostnames = new[] { "test.com" }
            };

            var ingress = new V1Ingress { Metadata = new V1ObjectMeta { Name = "my-ingress", NamespaceProperty = "default" } };
            _clientMock.Setup(x => x.GetAsync<V1Ingress>("my-ingress", "default")).ReturnsAsync(ingress);
            _synchronizerMock.Setup(x => x.SynchronizeLocalIngressAsync(ingress)).ReturnsAsync(true);

            // Act
            await _controller.ReconcileAsync(gslb, CancellationToken.None);

            // Assert
            _synchronizerMock.Verify(x => x.SynchronizeLocalIngressAsync(ingress), Times.Once);
        }

        [Fact]
        public async Task GslbReconcile_IngressReference_IngressNotFound_SyncsFullCluster()
        {
            // Arrange
            var gslb = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "gslb1", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "missing-ingress",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Ingress
                },
                Hostnames = new[] { "test.com" }
            };

            _clientMock.Setup(x => x.GetAsync<V1Ingress>("missing-ingress", "default")).ReturnsAsync((V1Ingress?)null);

            // Act
            await _controller.ReconcileAsync(gslb, CancellationToken.None);

            // Assert
            _synchronizerMock.Verify(x => x.SynchronizeLocalClusterAsync(), Times.Once);
        }

        [Fact]
        public async Task GslbReconcile_ServiceReference_ServiceFound_SyncsService()
        {
            // Arrange
            var gslb = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "gslb1", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "my-svc",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Service
                },
                Hostnames = new[] { "test.com" }
            };

            var service = new V1Service { Metadata = new V1ObjectMeta { Name = "my-svc", NamespaceProperty = "default" } };
            _clientMock.Setup(x => x.GetAsync<V1Service>("my-svc", "default")).ReturnsAsync(service);
            _synchronizerMock.Setup(x => x.SynchronizeLocalServiceAsync(service)).ReturnsAsync(true);

            // Act
            await _controller.ReconcileAsync(gslb, CancellationToken.None);

            // Assert
            _synchronizerMock.Verify(x => x.SynchronizeLocalServiceAsync(service), Times.Once);
        }

        [Fact]
        public async Task GslbDeleted_IngressReference_IngressNotFound_SyncsFullCluster()
        {
            // Arrange
            var gslb = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "gslb1", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "missing-ingress",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Ingress
                },
                Hostnames = new[] { "test.com" }
            };

            _clientMock.Setup(x => x.GetAsync<V1Ingress>("missing-ingress", "default")).ReturnsAsync((V1Ingress?)null);

            // Act
            await _controller.DeletedAsync(gslb, CancellationToken.None);

            // Assert
            _synchronizerMock.Verify(x => x.SynchronizeLocalClusterAsync(), Times.Once);
        }

        [Fact]
        public async Task GslbDeleted_ServiceReference_ServiceNotFound_SyncsCluster()
        {
            // Arrange
            var gslb = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "gslb1", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "missing-svc",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Service
                },
                Hostnames = new[] { "test.com" }
            };

            _clientMock.Setup(x => x.GetAsync<V1Service>("missing-svc", "default")).ReturnsAsync((V1Service?)null);

            // Act
            await _controller.DeletedAsync(gslb, CancellationToken.None);

            // Assert
            _synchronizerMock.Verify(x => x.SynchronizeLocalClusterAsync(), Times.Once);
        }

        [Fact]
        public async Task GslbDeleted_ServiceReference_ServiceFound_SyncsService()
        {
            // Arrange
            var gslb = new V1Gslb
            {
                Metadata = new V1ObjectMeta { Name = "gslb1", NamespaceProperty = "default" },
                ObjectReference = new V1Gslb.V1ObjectReference
                {
                    Name = "my-svc",
                    Kind = V1Gslb.V1ObjectReference.ReferenceType.Service
                },
                Hostnames = new[] { "test.com" }
            };

            var service = new V1Service { Metadata = new V1ObjectMeta { Name = "my-svc", NamespaceProperty = "default" } };
            _clientMock.Setup(x => x.GetAsync<V1Service>("my-svc", "default")).ReturnsAsync(service);
            _synchronizerMock.Setup(x => x.SynchronizeLocalServiceAsync(service)).ReturnsAsync(true);

            // Act
            await _controller.DeletedAsync(gslb, CancellationToken.None);

            // Assert
            _synchronizerMock.Verify(x => x.SynchronizeLocalServiceAsync(service), Times.Once);
        }

        #endregion
    }
}
