using Cyclops.MultiCluster.Services.Authentication;

namespace Cyclops.MultiCluster.Tests.Services.Authentication
{
    public class ApiAuthenticationHandlerOptionsTests
    {
        [Fact]
        public void DefaultScheme_IsApiKey()
        {
            Assert.Equal("ApiKey", ApiAuthenticationHandlerOptions.DefaultScheme);
        }

        [Fact]
        public void ApiKeys_DefaultsToEmptyArray()
        {
            var options = new ApiAuthenticationHandlerOptions();

            Assert.NotNull(options.ApiKeys);
            Assert.Empty(options.ApiKeys);
        }

        [Fact]
        public void ApiKeys_CanBeSet()
        {
            var options = new ApiAuthenticationHandlerOptions
            {
                ApiKeys = new[]
                {
                    new ApiKey { ClusterIdentifier = "cluster-1", Key = "key-1" },
                    new ApiKey { ClusterIdentifier = "cluster-2", Key = "key-2" }
                }
            };

            Assert.Equal(2, options.ApiKeys.Length);
        }
    }
}
