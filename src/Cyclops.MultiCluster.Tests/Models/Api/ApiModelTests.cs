namespace Cyclops.MultiCluster.Tests.ApiModels
{
    public class ApiModelTests
    {
        [Fact]
        public void HostIP_DefaultValues()
        {
            var hostIp = new Cyclops.MultiCluster.Models.Api.HostIP();
            Assert.Equal(string.Empty, hostIp.IPAddress);
            Assert.Equal(0, hostIp.Priority);
            Assert.Equal(0, hostIp.Weight);
        }

        [Fact]
        public void HostIP_SetAndGet()
        {
            var hostIp = new Cyclops.MultiCluster.Models.Api.HostIP
            {
                IPAddress = "192.168.1.1",
                Priority = 10,
                Weight = 75
            };

            Assert.Equal("192.168.1.1", hostIp.IPAddress);
            Assert.Equal(10, hostIp.Priority);
            Assert.Equal(75, hostIp.Weight);
        }

        [Fact]
        public void HostModel_DefaultValues()
        {
            var model = new Cyclops.MultiCluster.Models.Api.HostModel();
            Assert.Equal(string.Empty, model.Hostname);
            Assert.Empty(model.HostIPs);
        }

        [Fact]
        public void HostModel_SetAndGet()
        {
            var model = new Cyclops.MultiCluster.Models.Api.HostModel
            {
                Hostname = "test.example.com",
                HostIPs = new[]
                {
                    new Cyclops.MultiCluster.Models.Api.HostIP { IPAddress = "10.0.0.1" }
                }
            };

            Assert.Equal("test.example.com", model.Hostname);
            Assert.Single(model.HostIPs);
        }
    }
}
