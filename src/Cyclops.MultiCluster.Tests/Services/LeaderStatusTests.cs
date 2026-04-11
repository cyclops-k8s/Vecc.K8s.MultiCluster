using Cyclops.MultiCluster.Services;

namespace Cyclops.MultiCluster.Tests.Services
{
    public class LeaderStatusTests
    {
        [Fact]
        public void IsLeader_DefaultsFalse()
        {
            var status = new LeaderStatus();

            Assert.False(status.IsLeader);
        }

        [Fact]
        public void IsLeader_CanBeSetToTrue()
        {
            var status = new LeaderStatus();
            status.IsLeader = true;

            Assert.True(status.IsLeader);
        }

        [Fact]
        public void IsLeader_CanBeToggledBackToFalse()
        {
            var status = new LeaderStatus();
            status.IsLeader = true;
            status.IsLeader = false;

            Assert.False(status.IsLeader);
        }
    }
}
