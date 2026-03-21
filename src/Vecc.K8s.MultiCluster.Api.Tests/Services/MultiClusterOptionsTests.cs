using Vecc.K8s.MultiCluster.Api.Services;

namespace Vecc.K8s.MultiCluster.Api.Tests.Services
{
    public class MultiClusterOptionsTests
    {
        [Fact]
        public void ClusterIdentifier_DefaultsToLocal()
        {
            var options = new MultiClusterOptions();
            Assert.Equal("local", options.ClusterIdentifier);
        }

        [Fact]
        public void DNSRefreshInterval_DefaultsTo30()
        {
            var options = new MultiClusterOptions();
            Assert.Equal(30, options.DNSRefreshInterval);
        }

        [Fact]
        public void HeartbeatTimeout_DefaultsTo30()
        {
            var options = new MultiClusterOptions();
            Assert.Equal(30, options.HeartbeatTimeout);
        }

        [Fact]
        public void HeartbeatCheckInterval_DefaultsTo1()
        {
            var options = new MultiClusterOptions();
            Assert.Equal(1, options.HeartbeatCheckInterval);
        }

        [Fact]
        public void HeartbeatSetInterval_DefaultsTo10()
        {
            var options = new MultiClusterOptions();
            Assert.Equal(10, options.HeartbeatSetInterval);
        }

        [Fact]
        public void ListenGrpcPort_DefaultsToZero()
        {
            var options = new MultiClusterOptions();
            Assert.Equal(0, options.ListenGrpcPort);
        }

        [Fact]
        public void ListenPort_DefaultsToZero()
        {
            var options = new MultiClusterOptions();
            Assert.Equal(0, options.ListenPort);
        }

        [Fact]
        public void Peers_DefaultsToEmptyArray()
        {
            var options = new MultiClusterOptions();
            Assert.NotNull(options.Peers);
            Assert.Empty(options.Peers);
        }

        [Fact]
        public void ClusterSalt_DefaultsToEmptyArray()
        {
            var options = new MultiClusterOptions();
            Assert.NotNull(options.ClusterSalt);
            Assert.Empty(options.ClusterSalt);
        }

        [Fact]
        public void NameserverNames_DefaultsToEmptyDictionary()
        {
            var options = new MultiClusterOptions();
            Assert.NotNull(options.NameserverNames);
            Assert.Empty(options.NameserverNames);
        }

        [Fact]
        public void DefaultRecordTTL_DefaultsTo5()
        {
            var options = new MultiClusterOptions();
            Assert.Equal(5, options.DefaultRecordTTL);
        }

        [Fact]
        public void DNSServerResponsibleEmailAddress_HasDefault()
        {
            var options = new MultiClusterOptions();
            Assert.Equal("null.vecck8smulticlusteringress.com", options.DNSServerResponsibleEmailAddress);
        }

        [Fact]
        public void DNSHostname_HasDefault()
        {
            var options = new MultiClusterOptions();
            Assert.Equal("dns.vecck8smulticlusteringress.com", options.DNSHostname);
        }

        [Fact]
        public void PeriodicRefreshInterval_DefaultsTo300()
        {
            var options = new MultiClusterOptions();
            Assert.Equal(300, options.PeriodicRefreshInterval);
        }

        [Fact]
        public void Peers_CanBeSet()
        {
            var options = new MultiClusterOptions
            {
                Peers = new[]
                {
                    new PeerHosts { Identifier = "peer1", Key = "key1", Url = "https://peer1.example.com" }
                }
            };

            Assert.Single(options.Peers);
            Assert.Equal("peer1", options.Peers[0].Identifier);
        }

        [Fact]
        public void ClusterSalt_CanBeSet()
        {
            var salt = new byte[] { 1, 2, 3, 4 };
            var options = new MultiClusterOptions { ClusterSalt = salt };

            Assert.Equal(salt, options.ClusterSalt);
        }
    }
}
