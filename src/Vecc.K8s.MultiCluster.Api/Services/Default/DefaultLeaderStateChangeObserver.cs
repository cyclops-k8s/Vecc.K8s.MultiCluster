using KubeOps.Operator.Leadership;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultLeaderStateChangeObserver : IObserver<LeaderState>, IDisposable
    {
        private readonly ILogger<DefaultLeaderStateChangeObserver> _logger;
        private readonly IHostnameSynchronizer _hostnameSynchronizer;
        private readonly LeaderStatus _leaderStatus;
        private readonly ManualResetEventSlim _leaderStateChangeEvent;
        private readonly Task _leaderStateChangeTask;
        private bool _disposed;

        public DefaultLeaderStateChangeObserver(ILogger<DefaultLeaderStateChangeObserver> logger,
            IHostnameSynchronizer hostnameSynchronizer,
            IHostApplicationLifetime applicationLifetime,
            LeaderStatus leaderStatus)
        {
            _logger = logger;
            _hostnameSynchronizer = hostnameSynchronizer;
            _leaderStatus = leaderStatus;
            _leaderStateChangeEvent = new ManualResetEventSlim(false);
            _leaderStateChangeTask = Task.Run(() => LeadershipChanged(applicationLifetime.ApplicationStopped));
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(LeaderState value)
        {
            _logger.LogDebug("Leader state changed and is {@leaderstate}", value);

            switch (value)
            {
                case LeaderState.None:
                case LeaderState.Candidate:
                    // we're not the leader, stop the leader task
                    _leaderStatus.IsLeader = false;
                    _leaderStateChangeEvent.Reset();
                    return;
                case LeaderState.Leader:
                    break;
                default:
                    _logger.LogError("Unknown leader state detected: {@leaderstate}", value);
                    return;
            }

            //we're the new leader, yay
            _leaderStateChangeEvent.Set();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        _logger.LogInformation("Waiting for the leader election observer to stop");
                        _leaderStateChangeTask.Wait(new TimeSpan(0, 0, 10));
                        _logger.LogInformation("Observer stopped");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error waiting for leader observer to stop");
                    }
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected async Task LeadershipChanged(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Waiting to become the leader");

                try
                {
                    _leaderStateChangeEvent.Wait(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Application is shutting down, stopping the leader election observer");
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Cancelling leadership changed thread");
                    return;
                }

                try
                {
                    _logger.LogInformation("I'm the new leader");

                    _leaderStatus.IsLeader = true;
                    await _hostnameSynchronizer.SynchronizeLocalClusterAsync();
                    await _hostnameSynchronizer.SynchronizeRemoteClustersAsync();
                }
                catch (Exception exception)
                {
                    _logger.LogCritical(exception, "Unhandled exception handling leadership change");
                }

                _leaderStateChangeEvent.Reset();
            }
        }
    }
}
