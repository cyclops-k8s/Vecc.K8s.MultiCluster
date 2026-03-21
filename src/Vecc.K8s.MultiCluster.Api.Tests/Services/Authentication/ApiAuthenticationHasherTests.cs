using Microsoft.Extensions.Options;
using Moq;
using Vecc.K8s.MultiCluster.Api.Services;
using Vecc.K8s.MultiCluster.Api.Services.Authentication;

namespace Vecc.K8s.MultiCluster.Api.Tests.Services.Authentication
{
    public class ApiAuthenticationHasherTests
    {
        private readonly Mock<IOptions<MultiClusterOptions>> _optionsMock;
        private readonly ApiAuthenticationHasher _hasher;

        public ApiAuthenticationHasherTests()
        {
            _optionsMock = new Mock<IOptions<MultiClusterOptions>>();
            _optionsMock.Setup(x => x.Value).Returns(new MultiClusterOptions
            {
                ClusterSalt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }
            });
            _hasher = new ApiAuthenticationHasher(_optionsMock.Object);
        }

        [Fact]
        public async Task GetHashAsync_ReturnsSameHash_ForSameInputAndSalt()
        {
            var hash1 = await _hasher.GetHashAsync("test-key");
            var hash2 = await _hasher.GetHashAsync("test-key");

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public async Task GetHashAsync_ReturnsDifferentHash_ForDifferentKeys()
        {
            var hash1 = await _hasher.GetHashAsync("key-1");
            var hash2 = await _hasher.GetHashAsync("key-2");

            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public async Task GetHashAsync_ReturnsBase64String()
        {
            var hash = await _hasher.GetHashAsync("test-key");

            Assert.False(string.IsNullOrWhiteSpace(hash));
            var exception = Record.Exception(() => Convert.FromBase64String(hash));
            Assert.Null(exception);
        }

        [Fact]
        public async Task GetHashAsync_ReturnsDifferentHash_WithDifferentSalt()
        {
            var hash1 = await _hasher.GetHashAsync("test-key");

            var otherOptionsMock = new Mock<IOptions<MultiClusterOptions>>();
            otherOptionsMock.Setup(x => x.Value).Returns(new MultiClusterOptions
            {
                ClusterSalt = new byte[] { 9, 10, 11, 12, 13, 14, 15, 16 }
            });
            var otherHasher = new ApiAuthenticationHasher(otherOptionsMock.Object);
            var hash2 = await otherHasher.GetHashAsync("test-key");

            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public async Task GetHashAsync_ReturnsNonEmpty_ForEmptyKey()
        {
            var hash = await _hasher.GetHashAsync("");

            Assert.False(string.IsNullOrWhiteSpace(hash));
        }

        [Fact]
        public async Task GenerateSaltAsync_ReturnsByteArray()
        {
            var salt = await _hasher.GenerateSaltAsync();

            Assert.NotNull(salt);
            Assert.NotEmpty(salt);
        }

        [Fact]
        public async Task GenerateSaltAsync_Returns64Bytes()
        {
            var salt = await _hasher.GenerateSaltAsync();

            // 512 / 8 = 64 bytes
            Assert.Equal(64, salt.Length);
        }
    }
}
