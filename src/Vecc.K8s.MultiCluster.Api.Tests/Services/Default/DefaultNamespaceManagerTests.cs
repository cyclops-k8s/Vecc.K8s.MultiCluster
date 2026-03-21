using k8s.Models;
using KubeOps.KubernetesClient;
using Microsoft.Extensions.Logging;
using Moq;
using Vecc.K8s.MultiCluster.Api.Services.Default;

namespace Vecc.K8s.MultiCluster.Api.Tests.Services.Default
{
    public class DefaultNamespaceManagerTests
    {
        private readonly Mock<ILogger<DefaultNamespaceManager>> _loggerMock;
        private readonly Mock<IKubernetesClient> _clientMock;

        public DefaultNamespaceManagerTests()
        {
            _loggerMock = new Mock<ILogger<DefaultNamespaceManager>>();
            _clientMock = new Mock<IKubernetesClient>();
        }

        [Fact]
        public async Task GetNamsepacesAsync_ReturnsList()
        {
            // Arrange
            var ns1 = new V1Namespace { Metadata = new V1ObjectMeta { Name = "default" } };
            var ns2 = new V1Namespace { Metadata = new V1ObjectMeta { Name = "kube-system" } };
            _clientMock.Setup(x => x.ListAsync<V1Namespace>(null))
                .ReturnsAsync(new List<V1Namespace> { ns1, ns2 });

            var manager = new DefaultNamespaceManager(_loggerMock.Object, _clientMock.Object);

            // Act
            var result = await manager.GetNamsepacesAsync();

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal("default", result[0].Metadata.Name);
            Assert.Equal("kube-system", result[1].Metadata.Name);
        }

        [Fact]
        public async Task GetNamsepacesAsync_EmptyList_ReturnsEmpty()
        {
            // Arrange
            _clientMock.Setup(x => x.ListAsync<V1Namespace>(null))
                .ReturnsAsync(new List<V1Namespace>());

            var manager = new DefaultNamespaceManager(_loggerMock.Object, _clientMock.Object);

            // Act
            var result = await manager.GetNamsepacesAsync();

            // Assert
            Assert.Empty(result);
        }
    }
}
