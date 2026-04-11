using Cyclops.MultiCluster.Services.Default;

namespace Cyclops.MultiCluster.Tests.Services.Default
{
    public class DefaultDateTimeProviderTests
    {
        [Fact]
        public void UtcNow_ReturnsCurrentUtcTime()
        {
            var provider = new DefaultDateTimeProvider();
            var before = DateTime.UtcNow;

            var result = provider.UtcNow;

            var after = DateTime.UtcNow;
            Assert.True(result >= before);
            Assert.True(result <= after);
        }

        [Fact]
        public void UtcNow_ReturnsUtcKind()
        {
            var provider = new DefaultDateTimeProvider();

            var result = provider.UtcNow;

            Assert.Equal(DateTimeKind.Utc, result.Kind);
        }

        [Fact]
        public void UtcNow_CalledTwice_SecondCallIsEqualOrLater()
        {
            var provider = new DefaultDateTimeProvider();

            var first = provider.UtcNow;
            var second = provider.UtcNow;

            Assert.True(second >= first);
        }
    }
}
