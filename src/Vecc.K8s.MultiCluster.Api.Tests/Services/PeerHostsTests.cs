using Vecc.K8s.MultiCluster.Api.Services;

namespace Vecc.K8s.MultiCluster.Api.Tests.Services
{
    public class PeerHostsTests
    {
        [Fact]
        public void Identifier_DefaultsToEmptyString()
        {
            var peer = new PeerHosts();
            Assert.Equal(string.Empty, peer.Identifier);
        }

        [Fact]
        public void Key_DefaultsToEmptyString()
        {
            var peer = new PeerHosts();
            Assert.Equal(string.Empty, peer.Key);
        }

        [Fact]
        public void Url_DefaultsToEmptyString()
        {
            var peer = new PeerHosts();
            Assert.Equal(string.Empty, peer.Url);
        }

        [Fact]
        public void Properties_CanBeSet()
        {
            var peer = new PeerHosts
            {
                Identifier = "cluster-2",
                Key = "secret-key",
                Url = "https://cluster2.example.com"
            };

            Assert.Equal("cluster-2", peer.Identifier);
            Assert.Equal("secret-key", peer.Key);
            Assert.Equal("https://cluster2.example.com", peer.Url);
        }
    }
}
