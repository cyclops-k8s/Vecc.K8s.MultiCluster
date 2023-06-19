using StackExchange.Redis;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class RedisQueue : IQueue
    {
        private const string CHANNEL_HOST_CHANGE = "channel_host_change";

        private readonly ILogger<RedisQueue> _logger;
        private readonly ISubscriber _subscriber;

        public RedisQueue(ILogger<RedisQueue> logger, ISubscriber subscriber)
        {
            _logger = logger;
            _subscriber = subscriber;

            _subscriber.Subscribe(new RedisChannel(CHANNEL_HOST_CHANGE, RedisChannel.PatternMode.Auto), Redis_OnHostChanged);
            OnHostChangedAsync = new OnHostChangedAsyncDelegate(_ => Task.CompletedTask);
        }

        public OnHostChangedAsyncDelegate OnHostChangedAsync { get; set; }

        public async Task PublishHostChangedAsync(string hostname)
        {
            _logger.LogDebug("Notifiying host {@hostname} state changed.", hostname);
            await _subscriber.PublishAsync(new RedisChannel(CHANNEL_HOST_CHANGE, RedisChannel.PatternMode.Auto), hostname);
        }

        private async void Redis_OnHostChanged(RedisChannel channel, RedisValue value)
        {
            if (!value.HasValue)
            {
                _logger.LogInformation("Incoming event with no content");
            }

            var result = (string?)value;
            if (result == null)
            {
                _logger.LogInformation("Value was null");
            }

            if (OnHostChangedAsync != null)
            {
                await OnHostChangedAsync(result);
            }
            else
            {
                _logger.LogError("OnHostChangedAsync is null!");
            }
        }
    }
}
