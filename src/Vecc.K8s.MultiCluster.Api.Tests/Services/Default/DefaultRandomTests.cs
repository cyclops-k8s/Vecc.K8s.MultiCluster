using Vecc.K8s.MultiCluster.Api.Services.Default;

namespace Vecc.K8s.MultiCluster.Api.Tests.Services.Default
{
    public class DefaultRandomTests
    {
        [Fact]
        public void Next_WithMax_ReturnsValueInRange()
        {
            var random = new DefaultRandom();

            for (int i = 0; i < 100; i++)
            {
                var result = random.Next(10);
                Assert.True(result >= 0);
                Assert.True(result < 10);
            }
        }

        [Fact]
        public void Next_WithMinAndMax_ReturnsValueInRange()
        {
            var random = new DefaultRandom();

            for (int i = 0; i < 100; i++)
            {
                var result = random.Next(5, 15);
                Assert.True(result >= 5);
                Assert.True(result < 15);
            }
        }

        [Fact]
        public void Next_WithMaxOfOne_ReturnsZero()
        {
            var random = new DefaultRandom();

            var result = random.Next(1);

            Assert.Equal(0, result);
        }

        [Fact]
        public void Next_WithEqualMinAndMax_DoesNotThrow()
        {
            var random = new DefaultRandom();

            // Random.Next(5, 5) returns 5 in .NET
            var exception = Record.Exception(() => random.Next(5, 5));
            Assert.Null(exception);
        }
    }
}
