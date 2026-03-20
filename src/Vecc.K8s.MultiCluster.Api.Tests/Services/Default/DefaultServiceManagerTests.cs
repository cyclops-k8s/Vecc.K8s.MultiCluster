using k8s.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Vecc.K8s.MultiCluster.Api.Services;
using Vecc.K8s.MultiCluster.Api.Services.Default;

namespace Vecc.K8s.MultiCluster.Api.Tests.Services.Default
{
    public class DefaultServiceManagerTests
    {
        private readonly Mock<ILogger<DefaultServiceManager>> _loggerMock;

        public DefaultServiceManagerTests()
        {
            _loggerMock = new Mock<ILogger<DefaultServiceManager>>();
        }

        [Fact]
        public void GetReadyEndpointCount_WithReadyEndpoints_ReturnsCorrectCount()
        {
            var serviceManager = CreateServiceManager();
            var slices = new List<V1EndpointSlice>
            {
                CreateEndpointSlice(readyCount: 3, notReadyCount: 1),
                CreateEndpointSlice(readyCount: 2, notReadyCount: 0)
            };

            var result = serviceManager.GetReadyEndpointCount(slices);

            Assert.Equal(5, result);
        }

        [Fact]
        public void GetReadyEndpointCount_WithNoReadyEndpoints_ReturnsZero()
        {
            var serviceManager = CreateServiceManager();
            var slices = new List<V1EndpointSlice>
            {
                CreateEndpointSlice(readyCount: 0, notReadyCount: 3)
            };

            var result = serviceManager.GetReadyEndpointCount(slices);

            Assert.Equal(0, result);
        }

        [Fact]
        public void GetReadyEndpointCount_WithEmptySlices_ReturnsZero()
        {
            var serviceManager = CreateServiceManager();
            var slices = new List<V1EndpointSlice>();

            var result = serviceManager.GetReadyEndpointCount(slices);

            Assert.Equal(0, result);
        }

        [Fact]
        public void GetReadyEndpointCount_WithNullEndpoints_ReturnsZero()
        {
            var serviceManager = CreateServiceManager();
            var slices = new List<V1EndpointSlice>
            {
                new V1EndpointSlice
                {
                    Metadata = new V1ObjectMeta { Name = "test" },
                    Endpoints = null
                }
            };

            var result = serviceManager.GetReadyEndpointCount(slices);

            Assert.Equal(0, result);
        }

        [Fact]
        public async Task GetLoadBalancerServicesAsync_FiltersNonLoadBalancerServices()
        {
            var serviceManager = CreateServiceManager();
            var services = new List<V1Service>
            {
                CreateService("svc-lb", "default", "LoadBalancer"),
                CreateService("svc-cluster", "default", "ClusterIP"),
                CreateService("svc-nodeport", "default", "NodePort")
            };

            var endpointSlices = new List<V1EndpointSlice>
            {
                CreateEndpointSliceForService("svc-lb", "default", readyCount: 2),
                CreateEndpointSliceForService("svc-cluster", "default", readyCount: 1),
                CreateEndpointSliceForService("svc-nodeport", "default", readyCount: 1)
            };

            var result = await serviceManager.GetLoadBalancerServicesAsync(services, endpointSlices);

            Assert.Single(result);
            Assert.Equal("svc-lb", result[0].Metadata.Name);
        }

        [Fact]
        public async Task GetLoadBalancerServicesAsync_ExcludesServicesWithNoReadyEndpoints()
        {
            var serviceManager = CreateServiceManager();
            var services = new List<V1Service>
            {
                CreateService("svc-lb", "default", "LoadBalancer")
            };

            var endpointSlices = new List<V1EndpointSlice>
            {
                CreateEndpointSliceForService("svc-lb", "default", readyCount: 0)
            };

            var result = await serviceManager.GetLoadBalancerServicesAsync(services, endpointSlices);

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetLoadBalancerServicesAsync_ExcludesServicesWithMissingEndpointSlices()
        {
            var serviceManager = CreateServiceManager();
            var services = new List<V1Service>
            {
                CreateService("svc-lb", "default", "LoadBalancer")
            };

            var endpointSlices = new List<V1EndpointSlice>();

            var result = await serviceManager.GetLoadBalancerServicesAsync(services, endpointSlices);

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetLoadBalancerServicesAsync_HandlesMultipleNamespaces()
        {
            var serviceManager = CreateServiceManager();
            var services = new List<V1Service>
            {
                CreateService("svc-lb-1", "ns1", "LoadBalancer"),
                CreateService("svc-lb-2", "ns2", "LoadBalancer")
            };

            var endpointSlices = new List<V1EndpointSlice>
            {
                CreateEndpointSliceForService("svc-lb-1", "ns1", readyCount: 1),
                CreateEndpointSliceForService("svc-lb-2", "ns2", readyCount: 1)
            };

            var result = await serviceManager.GetLoadBalancerServicesAsync(services, endpointSlices);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task GetLoadBalancerServicesAsync_WithEmptyServices_ReturnsEmpty()
        {
            var serviceManager = CreateServiceManager();
            var services = new List<V1Service>();
            var endpointSlices = new List<V1EndpointSlice>();

            var result = await serviceManager.GetLoadBalancerServicesAsync(services, endpointSlices);

            Assert.Empty(result);
        }

        private DefaultServiceManager CreateServiceManager()
        {
            var kubernetesClientMock = new Mock<KubeOps.KubernetesClient.IKubernetesClient>();
            var namespaceManagerMock = new Mock<INamespaceManager>();
            return new DefaultServiceManager(_loggerMock.Object, kubernetesClientMock.Object, namespaceManagerMock.Object);
        }

        private V1EndpointSlice CreateEndpointSlice(int readyCount, int notReadyCount)
        {
            var endpoints = new List<V1Endpoint>();
            for (int i = 0; i < readyCount; i++)
            {
                endpoints.Add(new V1Endpoint
                {
                    Conditions = new V1EndpointConditions { Ready = true },
                    Addresses = new[] { $"10.0.0.{i + 1}" }
                });
            }
            for (int i = 0; i < notReadyCount; i++)
            {
                endpoints.Add(new V1Endpoint
                {
                    Conditions = new V1EndpointConditions { Ready = false },
                    Addresses = new[] { $"10.0.1.{i + 1}" }
                });
            }

            return new V1EndpointSlice
            {
                Metadata = new V1ObjectMeta { Name = "test-slice" },
                Endpoints = endpoints
            };
        }

        private V1EndpointSlice CreateEndpointSliceForService(string serviceName, string ns, int readyCount)
        {
            var slice = CreateEndpointSlice(readyCount, 0);
            slice.Metadata.NamespaceProperty = ns;
            slice.Metadata.Labels = new Dictionary<string, string>
            {
                { "kubernetes.io/service-name", serviceName }
            };
            return slice;
        }

        private V1Service CreateService(string name, string ns, string type)
        {
            return new V1Service
            {
                Metadata = new V1ObjectMeta
                {
                    Name = name,
                    NamespaceProperty = ns
                },
                Spec = new V1ServiceSpec
                {
                    Type = type
                }
            };
        }
    }
}
