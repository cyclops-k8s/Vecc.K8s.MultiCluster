using k8s.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Vecc.K8s.MultiCluster.Api.Services;
using Vecc.K8s.MultiCluster.Api.Services.Default;

namespace Vecc.K8s.MultiCluster.Api.Tests.Services.Default
{
    public class DefaultIngressManagerTests
    {
        private readonly Mock<ILogger<DefaultIngressManager>> _loggerMock;
        private readonly Mock<KubeOps.KubernetesClient.IKubernetesClient> _kubernetesClientMock;
        private readonly Mock<INamespaceManager> _namespaceManagerMock;
        private readonly Mock<IServiceManager> _serviceManagerMock;
        private readonly DefaultIngressManager _ingressManager;

        public DefaultIngressManagerTests()
        {
            _loggerMock = new Mock<ILogger<DefaultIngressManager>>();
            _kubernetesClientMock = new Mock<KubeOps.KubernetesClient.IKubernetesClient>();
            _namespaceManagerMock = new Mock<INamespaceManager>();
            _serviceManagerMock = new Mock<IServiceManager>();

            _ingressManager = new DefaultIngressManager(
                _loggerMock.Object,
                _kubernetesClientMock.Object,
                _namespaceManagerMock.Object,
                _serviceManagerMock.Object);
        }

        [Fact]
        public async Task GetRelatedServiceNamesAsync_ReturnsServiceNames()
        {
            var ingress = new V1Ingress
            {
                Metadata = new V1ObjectMeta { Name = "test", NamespaceProperty = "default" },
                Spec = new V1IngressSpec
                {
                    Rules = new List<V1IngressRule>
                    {
                        new V1IngressRule
                        {
                            Host = "test.example.com",
                            Http = new V1HTTPIngressRuleValue
                            {
                                Paths = new List<V1HTTPIngressPath>
                                {
                                    new V1HTTPIngressPath
                                    {
                                        Path = "/",
                                        Backend = new V1IngressBackend
                                        {
                                            Service = new V1IngressServiceBackend
                                            {
                                                Name = "my-service"
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var result = await _ingressManager.GetRelatedServiceNamesAsync(ingress);

            Assert.Single(result);
            Assert.Equal("my-service", result[0]);
        }

        [Fact]
        public async Task GetRelatedServiceNamesAsync_WithMultipleRules_ReturnsAllServiceNames()
        {
            var ingress = new V1Ingress
            {
                Metadata = new V1ObjectMeta { Name = "test", NamespaceProperty = "default" },
                Spec = new V1IngressSpec
                {
                    Rules = new List<V1IngressRule>
                    {
                        new V1IngressRule
                        {
                            Host = "host1.example.com",
                            Http = new V1HTTPIngressRuleValue
                            {
                                Paths = new List<V1HTTPIngressPath>
                                {
                                    new V1HTTPIngressPath
                                    {
                                        Path = "/",
                                        Backend = new V1IngressBackend
                                        {
                                            Service = new V1IngressServiceBackend { Name = "svc-1" }
                                        }
                                    }
                                }
                            }
                        },
                        new V1IngressRule
                        {
                            Host = "host2.example.com",
                            Http = new V1HTTPIngressRuleValue
                            {
                                Paths = new List<V1HTTPIngressPath>
                                {
                                    new V1HTTPIngressPath
                                    {
                                        Path = "/api",
                                        Backend = new V1IngressBackend
                                        {
                                            Service = new V1IngressServiceBackend { Name = "svc-2" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var result = await _ingressManager.GetRelatedServiceNamesAsync(ingress);

            Assert.Equal(2, result.Count);
            Assert.Contains("svc-1", result);
            Assert.Contains("svc-2", result);
        }

        [Fact]
        public async Task GetRelatedServiceNamesAsync_WithNoRules_ReturnsEmpty()
        {
            var ingress = new V1Ingress
            {
                Metadata = new V1ObjectMeta { Name = "test", NamespaceProperty = "default" },
                Spec = new V1IngressSpec
                {
                    Rules = null
                }
            };

            var result = await _ingressManager.GetRelatedServiceNamesAsync(ingress);

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetRelatedServiceNamesAsync_WithRulesWithoutHttp_ReturnsEmpty()
        {
            var ingress = new V1Ingress
            {
                Metadata = new V1ObjectMeta { Name = "test", NamespaceProperty = "default" },
                Spec = new V1IngressSpec
                {
                    Rules = new List<V1IngressRule>
                    {
                        new V1IngressRule
                        {
                            Host = "test.example.com",
                            Http = null
                        }
                    }
                }
            };

            var result = await _ingressManager.GetRelatedServiceNamesAsync(ingress);

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetAvailableHostnamesAsync_WithValidIngress_ReturnsHostnames()
        {
            _serviceManagerMock.Setup(x => x.GetReadyEndpointCount(It.IsAny<IEnumerable<V1EndpointSlice>>()))
                .Returns(1);

            var ingresses = new List<V1Ingress>
            {
                CreateValidIngress("test.example.com", "my-service", "default")
            };

            var services = new List<V1Service>
            {
                CreateService("my-service", "default")
            };

            var endpointSlices = new List<V1EndpointSlice>
            {
                CreateEndpointSliceForService("my-service", "default", readyCount: 1)
            };

            var result = await _ingressManager.GetAvailableHostnamesAsync(ingresses, services, endpointSlices);

            Assert.True(result.ContainsKey("test.example.com"));
        }

        [Fact]
        public async Task GetAvailableHostnamesAsync_WithInvalidIngressNoStatus_ExcludesHostname()
        {
            var ingresses = new List<V1Ingress>
            {
                new V1Ingress
                {
                    Metadata = new V1ObjectMeta { Name = "test", NamespaceProperty = "default" },
                    Spec = new V1IngressSpec
                    {
                        Rules = new List<V1IngressRule>
                        {
                            new V1IngressRule { Host = "test.example.com" }
                        }
                    },
                    Status = null
                }
            };

            var services = new List<V1Service>();
            var endpointSlices = new List<V1EndpointSlice>();

            var result = await _ingressManager.GetAvailableHostnamesAsync(ingresses, services, endpointSlices);

            Assert.False(result.ContainsKey("test.example.com"));
        }

        [Fact]
        public async Task GetAvailableHostnamesAsync_WithEmptyInputs_ReturnsEmpty()
        {
            var result = await _ingressManager.GetAvailableHostnamesAsync(
                new List<V1Ingress>(),
                new List<V1Service>(),
                new List<V1EndpointSlice>());

            Assert.Empty(result);
        }

        private V1Ingress CreateValidIngress(string hostname, string serviceName, string ns)
        {
            return new V1Ingress
            {
                Metadata = new V1ObjectMeta { Name = $"ingress-{hostname}", NamespaceProperty = ns },
                Spec = new V1IngressSpec
                {
                    Rules = new List<V1IngressRule>
                    {
                        new V1IngressRule
                        {
                            Host = hostname,
                            Http = new V1HTTPIngressRuleValue
                            {
                                Paths = new List<V1HTTPIngressPath>
                                {
                                    new V1HTTPIngressPath
                                    {
                                        Path = "/",
                                        Backend = new V1IngressBackend
                                        {
                                            Service = new V1IngressServiceBackend { Name = serviceName }
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                Status = new V1IngressStatus
                {
                    LoadBalancer = new V1IngressLoadBalancerStatus
                    {
                        Ingress = new List<V1IngressLoadBalancerIngress>
                        {
                            new V1IngressLoadBalancerIngress { Ip = "192.168.1.1" }
                        }
                    }
                }
            };
        }

        private V1Service CreateService(string name, string ns)
        {
            return new V1Service
            {
                Metadata = new V1ObjectMeta { Name = name, NamespaceProperty = ns }
            };
        }

        private V1EndpointSlice CreateEndpointSliceForService(string serviceName, string ns, int readyCount)
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

            return new V1EndpointSlice
            {
                Metadata = new V1ObjectMeta
                {
                    Name = $"slice-{serviceName}",
                    NamespaceProperty = ns,
                    Labels = new Dictionary<string, string>
                    {
                        { "kubernetes.io/service-name", serviceName }
                    }
                },
                Endpoints = endpoints
            };
        }
    }
}
