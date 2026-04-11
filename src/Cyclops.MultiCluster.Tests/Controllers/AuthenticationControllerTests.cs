using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Cyclops.MultiCluster.Controllers;
using Cyclops.MultiCluster.Models.Api;
using Cyclops.MultiCluster.Services;
using Cyclops.MultiCluster.Services.Authentication;

namespace Cyclops.MultiCluster.Tests.Controllers
{
    public class AuthenticationControllerTests
    {
        private readonly Mock<IOptions<ApiAuthenticationHandlerOptions>> _authOptionsMock;
        private readonly Mock<IOptions<MultiClusterOptions>> _optionsMock;
        private readonly ApiAuthenticationHasher _hasher;
        private readonly AuthenticationController _controller;

        public AuthenticationControllerTests()
        {
            _authOptionsMock = new Mock<IOptions<ApiAuthenticationHandlerOptions>>();
            _optionsMock = new Mock<IOptions<MultiClusterOptions>>();

            var authOptions = new ApiAuthenticationHandlerOptions
            {
                ApiKeys = new[]
                {
                    new ApiKey { ClusterIdentifier = "cluster1", Key = "key1" }
                }
            };
            _authOptionsMock.Setup(x => x.Value).Returns(authOptions);

            var multiClusterOptions = new MultiClusterOptions
            {
                ClusterIdentifier = "local-cluster",
                ClusterSalt = new byte[] { 1, 2, 3, 4 }
            };
            _optionsMock.Setup(x => x.Value).Returns(multiClusterOptions);

            _hasher = new ApiAuthenticationHasher(_optionsMock.Object);

            _controller = new AuthenticationController(_authOptionsMock.Object, _hasher, _optionsMock.Object);
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            _controller.ControllerContext.HttpContext.Request.Scheme = "https";
            _controller.ControllerContext.HttpContext.Request.Host = new HostString("test.example.com");
        }

        [Fact]
        public async Task Auth_WithIdentifier_UsesProvidedIdentifier()
        {
            var result = await _controller.Auth("my-cluster");

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var model = Assert.IsType<NewAuthModel>(okResult.Value);
            Assert.Contains("my-cluster", model.LocalAuthModel.ConfigMap.ClusterIdentifier);
        }

        [Fact]
        public async Task Auth_WithoutIdentifier_GeneratesGuidIdentifier()
        {
            var result = await _controller.Auth(null);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var model = Assert.IsType<NewAuthModel>(okResult.Value);
            Assert.False(string.IsNullOrWhiteSpace(model.LocalAuthModel.ConfigMap.ClusterIdentifier));
            Assert.Contains("Authentication__ApiKeys__", model.LocalAuthModel.ConfigMap.ClusterIdentifier);
        }

        [Fact]
        public async Task Auth_WithEmptyString_GeneratesGuidIdentifier()
        {
            var result = await _controller.Auth("");

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var model = Assert.IsType<NewAuthModel>(okResult.Value);
            Assert.False(string.IsNullOrWhiteSpace(model.LocalAuthModel.ConfigMap.ClusterIdentifier));
        }

        [Fact]
        public async Task Auth_ReturnsCorrectNextIndex_WhenApiKeysExist()
        {
            var result = await _controller.Auth("test");

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var model = Assert.IsType<NewAuthModel>(okResult.Value);
            Assert.StartsWith("Authentication__ApiKeys__1__ClusterIdentifier:", model.LocalAuthModel.ConfigMap.ClusterIdentifier);
        }

        [Fact]
        public async Task Auth_ReturnsCorrectNextIndex_WhenNoApiKeysExist()
        {
            var emptyAuthOptions = new ApiAuthenticationHandlerOptions { ApiKeys = Array.Empty<ApiKey>() };
            _authOptionsMock.Setup(x => x.Value).Returns(emptyAuthOptions);

            var controller = new AuthenticationController(_authOptionsMock.Object, _hasher, _optionsMock.Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            controller.ControllerContext.HttpContext.Request.Scheme = "https";
            controller.ControllerContext.HttpContext.Request.Host = new HostString("test.example.com");

            var result = await controller.Auth("test");

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var model = Assert.IsType<NewAuthModel>(okResult.Value);
            Assert.StartsWith("Authentication__ApiKeys__0__ClusterIdentifier:", model.LocalAuthModel.ConfigMap.ClusterIdentifier);
        }

        [Fact]
        public async Task Auth_ReturnsRemoteAuthModel_WithCorrectClusterIdentifier()
        {
            var result = await _controller.Auth("test");

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var model = Assert.IsType<NewAuthModel>(okResult.Value);
            Assert.Contains("local-cluster", model.RemoteAuthModel.ConfigMap.ClusterIdentifier);
        }

        [Fact]
        public async Task Auth_ReturnsRemoteAuthModel_WithCorrectUrl()
        {
            var result = await _controller.Auth("test");

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var model = Assert.IsType<NewAuthModel>(okResult.Value);
            Assert.Contains("https://test.example.com", model.RemoteAuthModel.ConfigMap.Url);
        }

        [Fact]
        public async Task Auth_ReturnsLocalSecretHash_NonEmpty()
        {
            var result = await _controller.Auth("test");

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var model = Assert.IsType<NewAuthModel>(okResult.Value);
            Assert.False(string.IsNullOrWhiteSpace(model.LocalAuthModel.Secret.Hash));
            Assert.StartsWith("Authentication__ApiKeys__", model.LocalAuthModel.Secret.Hash);
        }

        [Fact]
        public async Task Auth_ReturnsRemoteSecretKey_NonEmpty()
        {
            var result = await _controller.Auth("test");

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var model = Assert.IsType<NewAuthModel>(okResult.Value);
            Assert.False(string.IsNullOrWhiteSpace(model.RemoteAuthModel.Secret.Key));
            Assert.StartsWith("Peers__0__Key:", model.RemoteAuthModel.Secret.Key);
        }

        [Fact]
        public async Task Salt_ReturnsOkResult_WithSaltModel()
        {
            var result = await _controller.Salt();

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var model = Assert.IsType<NewSaltModel>(okResult.Value);
            Assert.StartsWith("ClusterSalt:", model.Salt);
        }

        [Fact]
        public async Task Salt_ReturnsSaltWithBase64Value()
        {
            var result = await _controller.Salt();

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var model = Assert.IsType<NewSaltModel>(okResult.Value);
            var saltValue = model.Salt.Replace("ClusterSalt: ", "");
            var exception = Record.Exception(() => Convert.FromBase64String(saltValue));
            Assert.Null(exception);
        }
    }
}
