using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using Cyclops.MultiCluster.Controllers;
using Cyclops.MultiCluster.Models.Api;
using Cyclops.MultiCluster.Services;

namespace Cyclops.MultiCluster.Tests.Controllers
{
    public class HostControllerTests
    {
        private readonly Mock<ILogger<HostController>> _loggerMock;
        private readonly Mock<ICache> _cacheMock;
        private readonly Mock<IOptions<MultiClusterOptions>> _optionsMock;
        private readonly HostController _controller;

        public HostControllerTests()
        {
            _loggerMock = new Mock<ILogger<HostController>>();
            _cacheMock = new Mock<ICache>();
            _optionsMock = new Mock<IOptions<MultiClusterOptions>>();
            _optionsMock.Setup(x => x.Value).Returns(new MultiClusterOptions
            {
                ClusterIdentifier = "local-cluster"
            });

            _controller = new HostController(_loggerMock.Object, _cacheMock.Object, _optionsMock.Object);
        }

        private void SetupAuthenticatedUser(string clusterIdentifier)
        {
            var claims = new[] { new Claim(ClaimTypes.Name, clusterIdentifier) };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };
        }

        [Fact]
        public async Task UpdateHost_WithValidModel_ReturnsNoContent()
        {
            SetupAuthenticatedUser("cluster-1");
            _cacheMock.Setup(x => x.GetHostsAsync("cluster-1"))
                .ReturnsAsync(Array.Empty<Models.Core.Host>());

            var model = new HostModel
            {
                Hostname = "test.example.com",
                HostIPs = new[]
                {
                    new HostIP { IPAddress = "10.0.0.1", Priority = 0, Weight = 50 }
                }
            };

            var result = await _controller.UpdateHost(model);

            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public async Task UpdateHost_SetsClusterCache_WithCorrectHosts()
        {
            SetupAuthenticatedUser("cluster-1");
            _cacheMock.Setup(x => x.GetHostsAsync("cluster-1"))
                .ReturnsAsync(Array.Empty<Models.Core.Host>());

            var model = new HostModel
            {
                Hostname = "test.example.com",
                HostIPs = new[]
                {
                    new HostIP { IPAddress = "10.0.0.1", Priority = 0, Weight = 50 }
                }
            };

            await _controller.UpdateHost(model);

            _cacheMock.Verify(x => x.SetClusterCacheAsync(
                "cluster-1",
                It.Is<Models.Core.Host[]>(hosts =>
                    hosts.Length == 1 &&
                    hosts[0].Hostname == "test.example.com" &&
                    hosts[0].HostIPs.Length == 1 &&
                    hosts[0].HostIPs[0].IPAddress == "10.0.0.1" &&
                    hosts[0].HostIPs[0].ClusterIdentifier == "cluster-1")),
                Times.Once);
        }

        [Fact]
        public async Task UpdateHost_ReplacesExistingHostWithSameHostname()
        {
            SetupAuthenticatedUser("cluster-1");
            var existingHosts = new[]
            {
                new Models.Core.Host
                {
                    Hostname = "test.example.com",
                    HostIPs = new[]
                    {
                        new Models.Core.HostIP { IPAddress = "10.0.0.99", Priority = 0, Weight = 50, ClusterIdentifier = "cluster-1" }
                    }
                },
                new Models.Core.Host
                {
                    Hostname = "other.example.com",
                    HostIPs = new[]
                    {
                        new Models.Core.HostIP { IPAddress = "10.0.0.2", Priority = 0, Weight = 50, ClusterIdentifier = "cluster-1" }
                    }
                }
            };
            _cacheMock.Setup(x => x.GetHostsAsync("cluster-1")).ReturnsAsync(existingHosts);

            var model = new HostModel
            {
                Hostname = "test.example.com",
                HostIPs = new[]
                {
                    new HostIP { IPAddress = "10.0.0.1", Priority = 0, Weight = 100 }
                }
            };

            await _controller.UpdateHost(model);

            _cacheMock.Verify(x => x.SetClusterCacheAsync(
                "cluster-1",
                It.Is<Models.Core.Host[]>(hosts =>
                    hosts.Length == 2 &&
                    hosts.Any(h => h.Hostname == "other.example.com") &&
                    hosts.Any(h => h.Hostname == "test.example.com" && h.HostIPs[0].IPAddress == "10.0.0.1"))),
                Times.Once);
        }

        [Fact]
        public async Task UpdateHost_WithNullHostIPs_SetsEmptyHostIPs()
        {
            SetupAuthenticatedUser("cluster-1");
            _cacheMock.Setup(x => x.GetHostsAsync("cluster-1"))
                .ReturnsAsync(Array.Empty<Models.Core.Host>());

            var model = new HostModel
            {
                Hostname = "test.example.com",
                HostIPs = null!
            };

            await _controller.UpdateHost(model);

            _cacheMock.Verify(x => x.SetClusterCacheAsync(
                "cluster-1",
                It.Is<Models.Core.Host[]>(hosts =>
                    hosts.Length == 1 &&
                    hosts[0].HostIPs.Length == 0)),
                Times.Once);
        }

        [Fact]
        public async Task UpdateHost_WithEmptyHostIPs_SetsEmptyHostIPs()
        {
            SetupAuthenticatedUser("cluster-1");
            _cacheMock.Setup(x => x.GetHostsAsync("cluster-1"))
                .ReturnsAsync(Array.Empty<Models.Core.Host>());

            var model = new HostModel
            {
                Hostname = "test.example.com",
                HostIPs = Array.Empty<HostIP>()
            };

            await _controller.UpdateHost(model);

            _cacheMock.Verify(x => x.SetClusterCacheAsync(
                "cluster-1",
                It.Is<Models.Core.Host[]>(hosts =>
                    hosts.Length == 1 &&
                    hosts[0].HostIPs.Length == 0)),
                Times.Once);
        }

        [Fact]
        public async Task UpdateHost_WhenCacheThrows_ReturnsProblem()
        {
            SetupAuthenticatedUser("cluster-1");
            _cacheMock.Setup(x => x.GetHostsAsync(It.IsAny<string>()))
                .ThrowsAsync(new Exception("Cache error"));

            var model = new HostModel
            {
                Hostname = "test.example.com",
                HostIPs = new[] { new HostIP { IPAddress = "10.0.0.1" } }
            };

            var result = await _controller.UpdateHost(model);

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, objectResult.StatusCode);
        }

        [Fact]
        public async Task UpdateHost_WhenExistingHostsNull_CreatesNewList()
        {
            SetupAuthenticatedUser("cluster-1");
            _cacheMock.Setup(x => x.GetHostsAsync("cluster-1"))
                .ReturnsAsync((Models.Core.Host[]?)null);

            var model = new HostModel
            {
                Hostname = "test.example.com",
                HostIPs = new[] { new HostIP { IPAddress = "10.0.0.1", Priority = 0, Weight = 50 } }
            };

            var result = await _controller.UpdateHost(model);

            Assert.IsType<NoContentResult>(result);
            _cacheMock.Verify(x => x.SetClusterCacheAsync(
                "cluster-1",
                It.Is<Models.Core.Host[]>(hosts => hosts.Length == 1)),
                Times.Once);
        }

        [Fact]
        public async Task Get_WhenHostsExist_ReturnsOkWithHosts()
        {
            var hosts = new[]
            {
                new Models.Core.Host
                {
                    Hostname = "test.example.com",
                    HostIPs = new[]
                    {
                        new Models.Core.HostIP { IPAddress = "10.0.0.1", Priority = 0, Weight = 50 }
                    }
                }
            };
            _cacheMock.Setup(x => x.GetHostsAsync("local-cluster")).ReturnsAsync(hosts);

            var result = await _controller.Get();

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var models = Assert.IsAssignableFrom<IEnumerable<HostModel>>(okResult.Value);
            var modelList = models.ToList();
            Assert.Single(modelList);
            Assert.Equal("test.example.com", modelList[0].Hostname);
            Assert.Single(modelList[0].HostIPs);
            Assert.Equal("10.0.0.1", modelList[0].HostIPs[0].IPAddress);
        }

        [Fact]
        public async Task Get_WhenHostsNull_ReturnsNotFound()
        {
            _cacheMock.Setup(x => x.GetHostsAsync("local-cluster"))
                .ReturnsAsync((Models.Core.Host[]?)null);

            var result = await _controller.Get();

            Assert.IsType<NotFoundResult>(result.Result);
        }

        [Fact]
        public async Task Get_UsesLocalClusterIdentifier()
        {
            _cacheMock.Setup(x => x.GetHostsAsync("local-cluster"))
                .ReturnsAsync(Array.Empty<Models.Core.Host>());

            await _controller.Get();

            _cacheMock.Verify(x => x.GetHostsAsync("local-cluster"), Times.Once);
        }

        [Fact]
        public async Task Get_MapsHostIPsCorrectly()
        {
            var hosts = new[]
            {
                new Models.Core.Host
                {
                    Hostname = "test.example.com",
                    HostIPs = new[]
                    {
                        new Models.Core.HostIP { IPAddress = "10.0.0.1", Priority = 1, Weight = 50 },
                        new Models.Core.HostIP { IPAddress = "10.0.0.2", Priority = 2, Weight = 100 }
                    }
                }
            };
            _cacheMock.Setup(x => x.GetHostsAsync("local-cluster")).ReturnsAsync(hosts);

            var result = await _controller.Get();

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var models = Assert.IsAssignableFrom<IEnumerable<HostModel>>(okResult.Value);
            var host = models.First();
            Assert.Equal(2, host.HostIPs.Length);
            Assert.Equal(1, host.HostIPs[0].Priority);
            Assert.Equal(50, host.HostIPs[0].Weight);
            Assert.Equal(2, host.HostIPs[1].Priority);
            Assert.Equal(100, host.HostIPs[1].Weight);
        }
    }
}
