using Cyclops.MultiCluster.Services.Authentication;

namespace Cyclops.MultiCluster.Tests.Services.Authentication
{
    public class ApiKeyTests
    {
        [Fact]
        public void ClusterIdentifier_DefaultsToEmptyString()
        {
            var apiKey = new ApiKey();

            Assert.Equal(string.Empty, apiKey.ClusterIdentifier);
        }

        [Fact]
        public void Key_DefaultsToEmptyString()
        {
            var apiKey = new ApiKey();

            Assert.Equal(string.Empty, apiKey.Key);
        }

        [Fact]
        public void ClusterIdentifier_CanBeSet()
        {
            var apiKey = new ApiKey { ClusterIdentifier = "my-cluster" };

            Assert.Equal("my-cluster", apiKey.ClusterIdentifier);
        }

        [Fact]
        public void Key_CanBeSet()
        {
            var apiKey = new ApiKey { Key = "my-secret-key" };

            Assert.Equal("my-secret-key", apiKey.Key);
        }
    }
}
