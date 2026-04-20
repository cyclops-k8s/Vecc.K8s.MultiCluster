using Microsoft.Extensions.Logging;
using Moq;
using Cyclops.MultiCluster.Controllers;
using Cyclops.MultiCluster.Services;
using Cyclops.MultiCluster.Services.Default;
using Cyclops.MultiCluster.Models.K8sEntities;

namespace Cyclops.MultiCluster.Tests.Controllers
{
    public class K8sHostnameCacheControllerTests
    {
        private readonly Mock<ILogger<K8sHostnameCacheController>> _loggerMock;
        private readonly KubernetesQueue _queue;
        private readonly K8sHostnameCacheController _controller;

        public K8sHostnameCacheControllerTests()
        {
            _loggerMock = new Mock<ILogger<K8sHostnameCacheController>>();
            _queue = new KubernetesQueue();
            _controller = new K8sHostnameCacheController(_loggerMock.Object, _queue);
        }

        [Fact]
        public async Task DeletedAsync_ReturnsSuccess()
        {
            var entity = new V1HostnameCache
            {
                Hostname = "test.example.com",
                Metadata = new k8s.Models.V1ObjectMeta
                {
                    Name = "test-cache",
                    NamespaceProperty = "default",
                    Labels = new Dictionary<string, string> { { "hostname", "test.example.com" } }
                }
            };

            var result = await _controller.DeletedAsync(entity, CancellationToken.None);

            Assert.NotNull(result);
        }

        [Fact]
        public async Task ReconcileAsync_ReturnsSuccess()
        {
            var entity = new V1HostnameCache
            {
                Hostname = "test.example.com",
                Metadata = new k8s.Models.V1ObjectMeta
                {
                    Name = "test-cache",
                    NamespaceProperty = "default",
                    Labels = new Dictionary<string, string> { { "hostname", "test.example.com" } }
                }
            };

            var result = await _controller.ReconcileAsync(entity, CancellationToken.None);

            Assert.NotNull(result);
        }

        [Fact]
        public async Task DeletedAsync_UsesHostnameFromEntity()
        {
            string? capturedHostname = null;
            _queue.OnHostChangedAsync = (hostname, _) =>
            {
                capturedHostname = hostname;
                return Task.CompletedTask;
            };

            var entity = new V1HostnameCache
            {
                Hostname = "my-host.example.com",
                Metadata = new k8s.Models.V1ObjectMeta
                {
                    Name = "test-cache",
                    NamespaceProperty = "default"
                }
            };

            await _controller.DeletedAsync(entity, CancellationToken.None);

            Assert.Equal("my-host.example.com", capturedHostname);
        }

        [Fact]
        public async Task DeletedAsync_FallsBackToLabel_WhenHostnameNull()
        {
            string? capturedHostname = null;
            _queue.OnHostChangedAsync = (hostname, _) =>
            {
                capturedHostname = hostname;
                return Task.CompletedTask;
            };

            var entity = new V1HostnameCache
            {
                Hostname = null,
                Metadata = new k8s.Models.V1ObjectMeta
                {
                    Name = "test-cache",
                    NamespaceProperty = "default",
                    Labels = new Dictionary<string, string> { { "hostname", "label-host.example.com" } }
                }
            };

            await _controller.DeletedAsync(entity, CancellationToken.None);

            Assert.Equal("label-host.example.com", capturedHostname);
        }

        [Fact]
        public async Task ReconcileAsync_UsesHostnameFromEntity()
        {
            string? capturedHostname = null;
            _queue.OnHostChangedAsync = (hostname, _) =>
            {
                capturedHostname = hostname;
                return Task.CompletedTask;
            };

            var entity = new V1HostnameCache
            {
                Hostname = "reconcile-host.example.com",
                Metadata = new k8s.Models.V1ObjectMeta
                {
                    Name = "test-cache",
                    NamespaceProperty = "default"
                }
            };

            await _controller.ReconcileAsync(entity, CancellationToken.None);

            Assert.Equal("reconcile-host.example.com", capturedHostname);
        }
    }
}
