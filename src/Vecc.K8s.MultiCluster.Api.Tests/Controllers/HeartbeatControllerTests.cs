using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Vecc.K8s.MultiCluster.Api.Controllers;
using Vecc.K8s.MultiCluster.Api.Services;

namespace Vecc.K8s.MultiCluster.Api.Tests.Controllers
{
    public class HeartbeatControllerTests
    {
        private readonly Mock<ILogger<HeartbeatController>> _loggerMock;
        private readonly Mock<ICache> _cacheMock;
        private readonly Mock<IDateTimeProvider> _dateTimeProviderMock;
        private readonly HeartbeatController _controller;

        public HeartbeatControllerTests()
        {
            _loggerMock = new Mock<ILogger<HeartbeatController>>();
            _cacheMock = new Mock<ICache>();
            _dateTimeProviderMock = new Mock<IDateTimeProvider>();
            _dateTimeProviderMock.Setup(x => x.UtcNow).Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            _controller = new HeartbeatController(_loggerMock.Object, _cacheMock.Object, _dateTimeProviderMock.Object);
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

        private void SetupAnonymousUser()
        {
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
        }

        [Fact]
        public async Task Heartbeat_WithAuthenticatedUser_ReturnsNoContent()
        {
            SetupAuthenticatedUser("cluster-1");

            var result = await _controller.Heartbeat();

            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public async Task Heartbeat_WithAuthenticatedUser_SetsClusterHeartbeat()
        {
            SetupAuthenticatedUser("cluster-1");
            var expectedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            await _controller.Heartbeat();

            _cacheMock.Verify(x => x.SetClusterHeartbeatAsync("cluster-1", expectedTime), Times.Once);
        }

        [Fact]
        public async Task Heartbeat_WithAnonymousUser_ReturnsUnauthorized()
        {
            SetupAnonymousUser();

            var result = await _controller.Heartbeat();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task Heartbeat_WhenCacheThrows_ReturnsProblem()
        {
            SetupAuthenticatedUser("cluster-1");
            _cacheMock.Setup(x => x.SetClusterHeartbeatAsync(It.IsAny<string>(), It.IsAny<DateTime>()))
                .ThrowsAsync(new Exception("Cache error"));

            var result = await _controller.Heartbeat();

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, objectResult.StatusCode);
        }

        [Fact]
        public async Task Heartbeat_UsesDateTimeProviderForTimestamp()
        {
            var specificTime = new DateTime(2026, 6, 15, 12, 30, 0, DateTimeKind.Utc);
            _dateTimeProviderMock.Setup(x => x.UtcNow).Returns(specificTime);
            SetupAuthenticatedUser("cluster-2");

            await _controller.Heartbeat();

            _cacheMock.Verify(x => x.SetClusterHeartbeatAsync("cluster-2", specificTime), Times.Once);
        }
    }
}
