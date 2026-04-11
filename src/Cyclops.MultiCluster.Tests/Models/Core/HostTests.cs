using Cyclops.MultiCluster.Models.Core;

namespace Cyclops.MultiCluster.Tests.CoreModels
{
    public class HostTests
    {
        [Fact]
        public void Hostname_DefaultValue_IsEmptyString()
        {
            var host = new Host();
            Assert.Equal(string.Empty, host.Hostname);
        }

        [Fact]
        public void HostIPs_DefaultValue_IsEmptyArray()
        {
            var host = new Host();
            Assert.Empty(host.HostIPs);
        }

        [Fact]
        public void Properties_SetAndGet()
        {
            var host = new Host
            {
                Hostname = "test.example.com",
                HostIPs = new[]
                {
                    new HostIP { IPAddress = "10.0.0.1", Priority = 1, Weight = 50, ClusterIdentifier = "cl1" }
                }
            };

            Assert.Equal("test.example.com", host.Hostname);
            Assert.Single(host.HostIPs);
            Assert.Equal("10.0.0.1", host.HostIPs[0].IPAddress);
        }
    }

    public class HostIPTests
    {
        [Fact]
        public void DefaultValues()
        {
            var hostIp = new HostIP();
            Assert.Equal(string.Empty, hostIp.IPAddress);
            Assert.Equal(0, hostIp.Priority);
            Assert.Equal(0, hostIp.Weight);
            Assert.Equal(string.Empty, hostIp.ClusterIdentifier);
        }

        [Fact]
        public void Equals_SameValues_ReturnsTrue()
        {
            var a = new HostIP { IPAddress = "10.0.0.1", Priority = 1, Weight = 50, ClusterIdentifier = "c1" };
            var b = new HostIP { IPAddress = "10.0.0.1", Priority = 1, Weight = 50, ClusterIdentifier = "c1" };
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equals_DifferentIPAddress_ReturnsFalse()
        {
            var a = new HostIP { IPAddress = "10.0.0.1", Priority = 1, Weight = 50, ClusterIdentifier = "c1" };
            var b = new HostIP { IPAddress = "10.0.0.2", Priority = 1, Weight = 50, ClusterIdentifier = "c1" };
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_DifferentPriority_ReturnsFalse()
        {
            var a = new HostIP { IPAddress = "10.0.0.1", Priority = 1, Weight = 50, ClusterIdentifier = "c1" };
            var b = new HostIP { IPAddress = "10.0.0.1", Priority = 2, Weight = 50, ClusterIdentifier = "c1" };
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_DifferentWeight_ReturnsFalse()
        {
            var a = new HostIP { IPAddress = "10.0.0.1", Priority = 1, Weight = 50, ClusterIdentifier = "c1" };
            var b = new HostIP { IPAddress = "10.0.0.1", Priority = 1, Weight = 100, ClusterIdentifier = "c1" };
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_DifferentClusterIdentifier_ReturnsFalse()
        {
            var a = new HostIP { IPAddress = "10.0.0.1", Priority = 1, Weight = 50, ClusterIdentifier = "c1" };
            var b = new HostIP { IPAddress = "10.0.0.1", Priority = 1, Weight = 50, ClusterIdentifier = "c2" };
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_Null_ReturnsFalse()
        {
            var a = new HostIP { IPAddress = "10.0.0.1" };
            Assert.False(a.Equals(null));
        }

        [Fact]
        public void Equals_DifferentType_ReturnsFalse()
        {
            var a = new HostIP { IPAddress = "10.0.0.1" };
            Assert.False(a.Equals("not a HostIP"));
        }

        [Fact]
        public void GetHashCode_DoesNotThrow()
        {
            var a = new HostIP { IPAddress = "10.0.0.1" };
            // GetHashCode uses base implementation, just make sure it doesn't throw
            var hash = a.GetHashCode();
            Assert.IsType<int>(hash);
        }
    }
}
