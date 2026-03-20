using Microsoft.Extensions.Logging;
using Moq;
using Vecc.K8s.MultiCluster.Api.Controllers;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;
using Vecc.K8s.MultiCluster.Api.Services;

namespace Vecc.K8s.MultiCluster.Api.Tests.Controllers
{
    public class K8sClusterCacheControllerTests
    {
        private readonly Mock<ILogger<K8sClusterCacheController>> _loggerMock;
        private readonly Mock<ICache> _cacheMock;
        private readonly K8sClusterCacheController _controller;

        public K8sClusterCacheControllerTests()
        {
            _loggerMock = new Mock<ILogger<K8sClusterCacheController>>();
            _cacheMock = new Mock<ICache>();
            _controller = new K8sClusterCacheController(_loggerMock.Object, _cacheMock.Object);
        }

        [Fact]
        public async Task DeletedAsync_CallsSynchronizeCaches()
        {
            var entity = new V1ClusterCache
            {
                Metadata = new k8s.Models.V1ObjectMeta
                {
                    Name = "cluster-1",
                    NamespaceProperty = "default"
                }
            };

            await _controller.DeletedAsync(entity, CancellationToken.None);

            _cacheMock.Verify(x => x.SynchronizeCachesAsync(), Times.Once);
        }

        [Fact]
        public async Task ReconcileAsync_CallsSynchronizeCaches()
        {
            var entity = new V1ClusterCache
            {
                Metadata = new k8s.Models.V1ObjectMeta
                {
                    Name = "cluster-1",
                    NamespaceProperty = "default"
                }
            };

            await _controller.ReconcileAsync(entity, CancellationToken.None);

            _cacheMock.Verify(x => x.SynchronizeCachesAsync(), Times.Once);
        }

        [Fact]
        public async Task DeletedAsync_ReturnsSuccess_EvenWhenSynchronizeThrows()
        {
            _cacheMock.Setup(x => x.SynchronizeCachesAsync()).ThrowsAsync(new Exception("Sync error"));

            var entity = new V1ClusterCache
            {
                Metadata = new k8s.Models.V1ObjectMeta
                {
                    Name = "cluster-1",
                    NamespaceProperty = "default"
                }
            };

            var result = await _controller.DeletedAsync(entity, CancellationToken.None);

            Assert.NotNull(result);
        }

        [Fact]
        public async Task ReconcileAsync_ReturnsSuccess_EvenWhenSynchronizeThrows()
        {
            _cacheMock.Setup(x => x.SynchronizeCachesAsync()).ThrowsAsync(new Exception("Sync error"));

            var entity = new V1ClusterCache
            {
                Metadata = new k8s.Models.V1ObjectMeta
                {
                    Name = "cluster-1",
                    NamespaceProperty = "default"
                }
            };

            var result = await _controller.ReconcileAsync(entity, CancellationToken.None);

            Assert.NotNull(result);
        }
    }
}
