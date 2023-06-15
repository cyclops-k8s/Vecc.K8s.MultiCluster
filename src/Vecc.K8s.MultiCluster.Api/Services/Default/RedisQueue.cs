using StackExchange.Redis;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class RedisQueue : IQueue
    {
        private const string CHANNEL_HOST_CHANGE = "channel_host_change";
        private const string CHANNEL_CLUSTER_HOST_CHANGE = "channel_cluster_host_change";

        private readonly ILogger<RedisQueue> _logger;
        private readonly IDatabase _database;
        private readonly ISubscriber _subscriber;

        public RedisQueue(ILogger<RedisQueue> logger, IDatabase database, ISubscriber subscriber)
        {
            _logger = logger;
            _database = database;
            _subscriber = subscriber;

            _subscriber.Subscribe(CHANNEL_HOST_CHANGE, Redis_OnHostChanged);
            OnHostChangedAsync = new OnHostChangedAsyncDelegate(_ => Task.CompletedTask);
        }

        public OnHostChangedAsyncDelegate OnHostChangedAsync { get; set; }

        public async Task PublishHostChangedAsync(string hostname)
        {
            _logger.LogDebug("Notifiying host {@hostname} state changed.", hostname);
            await _subscriber.PublishAsync(CHANNEL_HOST_CHANGE, hostname);
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
