using KubeOps.KubernetesClient;
using Microsoft.Extensions.Logging;
using Moq;
using Cyclops.MultiCluster.Models.K8sEntities;
using Cyclops.MultiCluster.Services.Default;

namespace Cyclops.MultiCluster.Tests.Services.Default
{
    public class DefaultGslbManagerTests
    {
        private readonly Mock<ILogger<DefaultGslbManager>> _loggerMock;
        private readonly Mock<IKubernetesClient> _clientMock;

        public DefaultGslbManagerTests()
        {
            _loggerMock = new Mock<ILogger<DefaultGslbManager>>();
            _clientMock = new Mock<IKubernetesClient>();
        }

        [Fact]
        public async Task GetGslbsAsync_ReturnsArray()
        {
            // Arrange
            var gslb1 = new V1Gslb { Metadata = new k8s.Models.V1ObjectMeta { Name = "gslb1" } };
            var gslb2 = new V1Gslb { Metadata = new k8s.Models.V1ObjectMeta { Name = "gslb2" } };
            var veccGslb1 = new V1VeccGslb { Metadata = new k8s.Models.V1ObjectMeta { Name = "veccgslb1" } };
            var veccGslb2 = new V1VeccGslb { Metadata = new k8s.Models.V1ObjectMeta { Name = "veccgslb2" } };

            _clientMock.Setup(x => x.ListAsync<V1Gslb>(null))
                .ReturnsAsync(new List<V1Gslb> { gslb1, gslb2 });
            _clientMock.Setup(x => x.ListAsync<V1VeccGslb>(null))
                .ReturnsAsync(new List<V1VeccGslb> { veccGslb1, veccGslb2 });

            var manager = new DefaultGslbManager(_loggerMock.Object, _clientMock.Object);

            // Act
            var result = await manager.GetGslbsAsync();

            // Assert
            Assert.Equal(4, result.Length);
            Assert.Equal("gslb1", result[0].Metadata.Name);
            Assert.Equal("gslb2", result[1].Metadata.Name);
            Assert.Equal("veccgslb1", result[2].Metadata.Name);
            Assert.Equal("veccgslb2", result[3].Metadata.Name);
        }

        [Fact]
        public async Task GetGslbsAsync_EmptyList_ReturnsEmptyArray()
        {
            // Arrange
            _clientMock.Setup(x => x.ListAsync<V1Gslb>(null))
                .ReturnsAsync(new List<V1Gslb>());
            _clientMock.Setup(x => x.ListAsync<V1VeccGslb>(null))
                .ReturnsAsync(new List<V1VeccGslb>());

            var manager = new DefaultGslbManager(_loggerMock.Object, _clientMock.Object);

            // Act
            var result = await manager.GetGslbsAsync();

            // Assert
            Assert.Empty(result);
        }
    }
}
