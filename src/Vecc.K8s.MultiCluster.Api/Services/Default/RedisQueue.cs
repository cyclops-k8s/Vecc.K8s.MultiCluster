using NewRelic.Api.Agent;
using StackExchange.Redis;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class RedisQueue : IQueue
    {
        private const string _channelHostChange = "channel_host_change";

        private readonly ILogger<RedisQueue> _logger;
        private readonly ISubscriber _subscriber;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly IDateTimeProvider _dateTimeProvider;
        private bool _subscribed = false;
        private OnHostChangedAsyncDelegate _onHostChangedAsync = _ => Task.CompletedTask;
        private RedisChannel _channel;
        public RedisQueue(ILogger<RedisQueue> logger, ISubscriber subscriber, IHostApplicationLifetime applicationLifetime, IDateTimeProvider dateTimeProvider)
        {
            _logger = logger;
            _subscriber = subscriber;
            _applicationLifetime = applicationLifetime;
            _dateTimeProvider = dateTimeProvider;
        }

        public OnHostChangedAsyncDelegate OnHostChangedAsync
        {
            get
            {
                _logger.LogDebug("Getting OnHostChangedAsync");
                return _onHostChangedAsync;
            }
            set
            {
                if (value == null)
                {
                    _logger.LogError("OnHostChangedAsync is attempted to be set to null, not setting!");
                }
                else
                {
                    _logger.LogInformation("Setting onhostchangedasync");
                    _onHostChangedAsync = value;

                    if (!_subscribed)
                    {
                        _logger.LogInformation("Subscribing to channel");
                        _channel = new RedisChannel(_channelHostChange, RedisChannel.PatternMode.Auto);
                        _subscriber.Subscribe(_channel, Redis_OnHostChanged);
                        _subscribed = true;
                    }
                    else
                    {
                        _logger.LogInformation("Already subscribed to channel");
                    }
                }
            }
        }

        [Trace]
        public async Task PublishHostChangedAsync(string hostname)
        {
            _logger.LogDebug("Notifiying host {@hostname} state changed.", hostname);
            var start = _dateTimeProvider.UtcNow;

            while ((_dateTimeProvider.UtcNow - start).TotalSeconds < 60) //wait up to 1 minute for the queue to accept the message
            {
                if (_applicationLifetime.ApplicationStopping.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    _logger.LogDebug("Publishing");
                    await _subscriber.PublishAsync(new RedisChannel(_channelHostChange, RedisChannel.PatternMode.Auto), hostname);
                    _logger.LogDebug("Published");
                    return;
                }
                catch (RedisServerException exception)
                {
                    _logger.LogError(exception, "Redis server issue while publishing host changed message to the queue. {@hostname}", hostname);
                    throw;
                }
                catch (RedisCommandException exception)
                {
                    _logger.LogError(exception, "Redis command issue while publishing host changed message to the queue. {@hostname}", hostname);
                    throw;
                }
                catch (RedisConnectionException exception)
                {
                    _logger.LogError(exception, "Redis connection issue while publishing host changed message to the queue. {@hostname}", hostname);
                    await Task.Delay(1000); //wait 1 second and try again.
                }
            }
        }

        [Transaction]
        private async void Redis_OnHostChanged(RedisChannel channel, RedisValue value)
        {
            _logger.LogInformation("Incoming queue event {@value}", value);

            if (!value.HasValue)
            {
                _logger.LogInformation("Incoming event with no content");
            }

            var result = (string?)value;
            if (result == null)
            {
                _logger.LogInformation("Value was null");
            }

            if (_onHostChangedAsync != null)
            {
                await _onHostChangedAsync(result);
            }
        }
    }
}
