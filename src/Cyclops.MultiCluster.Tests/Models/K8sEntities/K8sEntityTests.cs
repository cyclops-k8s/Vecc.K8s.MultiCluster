using Cyclops.MultiCluster.Models.K8sEntities;
using Cyclops.MultiCluster.Models.Core;

namespace Cyclops.MultiCluster.Tests.K8sEntityModels
{
    public class V1ClusterCacheTests
    {
        [Fact]
        public void Constructor_SetsKindAndApiVersion()
        {
            var cache = new V1ClusterCache();
            Assert.Equal("ClusterCache", cache.Kind);
            Assert.Equal("multicluster.cyclops.io/v1alpha", cache.ApiVersion);
        }

        [Fact]
        public void DefaultValues()
        {
            var cache = new V1ClusterCache();
            Assert.Equal(string.Empty, cache.LastHeartbeat);
            Assert.Empty(cache.Hostnames);
            Assert.Empty(cache.ServiceEndpointCounts);
        }

        [Fact]
        public void HostCache_ToCore_ConvertsCorrectly()
        {
            var hostCache = new V1ClusterCache.HostCache
            {
                Hostname = "test.example.com",
                HostIPs = new[]
                {
                    new V1ClusterCache.HostIPCache
                    {
                        IPAddress = "10.0.0.1",
                        Priority = 5,
                        Weight = 100,
                        ClusterIdentifier = "cl-1"
                    }
                }
            };

            var core = hostCache.ToCore();

            Assert.Equal("test.example.com", core.Hostname);
            Assert.Single(core.HostIPs);
            Assert.Equal("10.0.0.1", core.HostIPs[0].IPAddress);
            Assert.Equal(5, core.HostIPs[0].Priority);
            Assert.Equal(100, core.HostIPs[0].Weight);
            Assert.Equal("cl-1", core.HostIPs[0].ClusterIdentifier);
        }

        [Fact]
        public void HostIPCache_ToCore_ConvertsCorrectly()
        {
            var ipCache = new V1ClusterCache.HostIPCache
            {
                IPAddress = "192.168.1.1",
                Priority = 10,
                Weight = 50,
                ClusterIdentifier = "cluster-1"
            };

            var core = ipCache.ToCore();

            Assert.Equal("192.168.1.1", core.IPAddress);
            Assert.Equal(10, core.Priority);
            Assert.Equal(50, core.Weight);
            Assert.Equal("cluster-1", core.ClusterIdentifier);
        }

        [Fact]
        public void HostIPCache_FromCore_ConvertsCorrectly()
        {
            var hostIP = new HostIP
            {
                IPAddress = "172.16.0.1",
                Priority = 3,
                Weight = 75,
                ClusterIdentifier = "my-cluster"
            };

            var cache = V1ClusterCache.HostIPCache.FromCore(hostIP);

            Assert.Equal("172.16.0.1", cache.IPAddress);
            Assert.Equal(3, cache.Priority);
            Assert.Equal(75, cache.Weight);
            Assert.Equal("my-cluster", cache.ClusterIdentifier);
        }

        [Fact]
        public void HostIPCache_RoundTrip_PreservesValues()
        {
            var original = new HostIP
            {
                IPAddress = "10.10.10.10",
                Priority = 7,
                Weight = 33,
                ClusterIdentifier = "roundtrip"
            };

            var cached = V1ClusterCache.HostIPCache.FromCore(original);
            var restored = cached.ToCore();

            Assert.True(original.Equals(restored));
        }
    }

    public class V1HostnameCacheTests
    {
        [Fact]
        public void Constructor_SetsKindAndApiVersion()
        {
            var cache = new V1HostnameCache();
            Assert.Equal("HostnameCache", cache.Kind);
            Assert.Equal("multicluster.cyclops.io/v1alpha", cache.ApiVersion);
        }

        [Fact]
        public void DefaultValues()
        {
            var cache = new V1HostnameCache();
            Assert.Empty(cache.Addresses);
            Assert.Null(cache.Hostname);
        }

        [Fact]
        public void HostIPCache_ToCore_ConvertsCorrectly()
        {
            var ipCache = new V1HostnameCache.HostIPCache
            {
                IPAddress = "10.0.0.1",
                Priority = 2,
                Weight = 60,
                ClusterIdentifier = "test"
            };

            var core = ipCache.ToCore();

            Assert.Equal("10.0.0.1", core.IPAddress);
            Assert.Equal(2, core.Priority);
            Assert.Equal(60, core.Weight);
            Assert.Equal("test", core.ClusterIdentifier);
        }

        [Fact]
        public void HostIPCache_FromCore_ConvertsCorrectly()
        {
            var hostIP = new HostIP
            {
                IPAddress = "10.0.0.2",
                Priority = 4,
                Weight = 80,
                ClusterIdentifier = "cluster-a"
            };

            var cache = V1HostnameCache.HostIPCache.FromCore(hostIP);

            Assert.Equal("10.0.0.2", cache.IPAddress);
            Assert.Equal(4, cache.Priority);
            Assert.Equal(80, cache.Weight);
            Assert.Equal("cluster-a", cache.ClusterIdentifier);
        }

        [Fact]
        public void HostIPCache_RoundTrip_PreservesValues()
        {
            var original = new HostIP
            {
                IPAddress = "192.168.0.1",
                Priority = 1,
                Weight = 99,
                ClusterIdentifier = "rt-cluster"
            };

            var cached = V1HostnameCache.HostIPCache.FromCore(original);
            var restored = cached.ToCore();

            Assert.True(original.Equals(restored));
        }
    }

    public class V1GslbTests
    {
        [Fact]
        public void Constructor_SetsKindAndApiVersion()
        {
            var gslb = new V1Gslb();
            Assert.Equal("GSLB", gslb.Kind);
            Assert.Equal("multicluster.cyclops.io/v1alpha", gslb.ApiVersion);
        }

        [Fact]
        public void DefaultValues()
        {
            var gslb = new V1Gslb();
            Assert.NotNull(gslb.Spec.ObjectReference);
            Assert.Empty(gslb.Spec.Hostnames);
            Assert.Null(gslb.Spec.IPOverrides);
            Assert.Equal(0, gslb.Spec.Priority);
            Assert.Equal(50, gslb.Spec.Weight);
        }

        [Fact]
        public void ObjectReference_DefaultValues()
        {
            var objRef = new V1Gslb.V1ObjectReference();
            Assert.Equal(string.Empty, objRef.Name);
            Assert.Equal(V1Gslb.V1ObjectReference.ReferenceType.Ingress, objRef.Kind);
        }

        [Fact]
        public void ReferenceType_HasIngressAndService()
        {
            Assert.Equal(0, (int)V1Gslb.V1ObjectReference.ReferenceType.Ingress);
            Assert.Equal(1, (int)V1Gslb.V1ObjectReference.ReferenceType.Service);
        }
    }

    public class V1ResourceCacheTests
    {
        [Fact]
        public void Constructor_SetsKindAndApiVersion()
        {
            var cache = new V1ResourceCache();
            Assert.Equal("ResourceCache", cache.Kind);
            Assert.Equal("multicluster.cyclops.io/v1alpha", cache.ApiVersion);
        }

        [Fact]
        public void DefaultValues()
        {
            var cache = new V1ResourceCache();
            Assert.Equal(string.Empty, cache.CurrentResourceVersion);
        }

        [Fact]
        public void SetAndGetResourceVersion()
        {
            var cache = new V1ResourceCache { CurrentResourceVersion = "v42" };
            Assert.Equal("v42", cache.CurrentResourceVersion);
        }
    }

    public class V1ServiceCacheTests
    {
        [Fact]
        public void Constructor_SetsKindAndApiVersion()
        {
            var cache = new V1ServiceCache();
            Assert.Equal("ServiceCache", cache.Kind);
            Assert.Equal("multicluster.cyclops.io/v1alpha", cache.ApiVersion);
        }

        [Fact]
        public void DefaultValues()
        {
            var cache = new V1ServiceCache();
            Assert.NotNull(cache.Service);
            Assert.Equal(0, cache.EndpointCount);
        }

        [Fact]
        public void SetAndGetEndpointCount()
        {
            var cache = new V1ServiceCache { EndpointCount = 5 };
            Assert.Equal(5, cache.EndpointCount);
        }
    }
}
