using Cyclops.MultiCluster.Services.Default;

namespace Cyclops.MultiCluster.Tests.Services.Default
{
    public class KubernetesQueueTests
    {
        [Fact]
        public async Task OnHostChangedAsync_DefaultsToNoOp()
        {
            var queue = new KubernetesQueue();

            // Should not throw
            await queue.OnHostChangedAsync("test");
        }

        [Fact]
        public async Task OnHostChangedAsync_CanBeSet()
        {
            var queue = new KubernetesQueue();
            string? capturedValue = null;
            queue.OnHostChangedAsync = (value) =>
            {
                capturedValue = value;
                return Task.CompletedTask;
            };

            await queue.OnHostChangedAsync("test-host");

            Assert.Equal("test-host", capturedValue);
        }

        [Fact]
        public async Task PublishHostChangedAsync_ThrowsNotImplementedException()
        {
            var queue = new KubernetesQueue();

            await Assert.ThrowsAsync<NotImplementedException>(() => queue.PublishHostChangedAsync("test"));
        }

        [Fact]
        public async Task OnHostChangedAsync_CanHandleNull()
        {
            var queue = new KubernetesQueue();
            string? capturedValue = "initial";
            queue.OnHostChangedAsync = (value) =>
            {
                capturedValue = value;
                return Task.CompletedTask;
            };

            await queue.OnHostChangedAsync(null);

            Assert.Null(capturedValue);
        }
    }
}
