using k8s.LeaderElection;
using NewRelic.Api.Agent;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public class LeaderStateChanged : IDisposable
    {
        private readonly ILogger<LeaderStateChanged> _logger;
        private readonly IHostnameSynchronizer _hostnameSynchronizer;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly LeaderElector _leaderElector;
        private readonly LeaderStatus _leaderStatus;
        private readonly Task _leaderStateChangeTask;
        private readonly ManualResetEventSlim _leaderStateChangeEvent;
        private bool _disposed;

        public LeaderStateChanged(ILogger<LeaderStateChanged> logger,
            IHostnameSynchronizer hostnameSynchronizer,
            IHostApplicationLifetime applicationLifetime,
            LeaderElector leaderElector,
            LeaderStatus leaderStatus)
        {
            _logger = logger;
            _hostnameSynchronizer = hostnameSynchronizer;
            _applicationLifetime = applicationLifetime;
            _leaderElector = leaderElector;
            _leaderStatus = leaderStatus;
            _leaderStateChangeEvent = new ManualResetEventSlim(false);

            _leaderElector.OnStartedLeading += LeaderElectorOnOnStartedLeading;
            _leaderElector.OnStoppedLeading += LeaderElectorOnOnStoppedLeading;
            _leaderStateChangeTask = LeadershipChanged();
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

        private Task LeadershipChanged() =>
            Task.Run(async () =>
            {
                _logger.LogInformation("Leadership changed task started");
                var cancellationToken = _applicationLifetime.ApplicationStopping;
                while (!cancellationToken.IsCancellationRequested)
                {
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
                        _logger.LogInformation("Exiting leadership changed task");
                        return;
                    }

                    try
                    {
                        _logger.LogInformation("I'm the new leader");

                        _leaderStatus.IsLeader = true;
                        await OnLeaderElected();
                    }
                    catch (Exception exception)
                    {
                        _logger.LogCritical(exception, "Unhandled exception handling leadership change");
                    }

                    _leaderStateChangeEvent.Reset();

                    _logger.LogInformation("Leadership changed event triggered");
                }
            });

        private void LeaderElectorOnOnStoppedLeading()
        {
            _logger.LogInformation("We're not the leader anymore.");
            _leaderStatus.IsLeader = false;
            _leaderStateChangeEvent.Reset();
        }

        private void LeaderElectorOnOnStartedLeading()
        {
            _logger.LogInformation("We're the leader now.");
            _leaderStatus.IsLeader = true;
            _leaderStateChangeEvent.Set();
        }

        [Transaction]
        private async Task OnLeaderElected()
        {
            await _hostnameSynchronizer.SynchronizeLocalClusterAsync();
            await _hostnameSynchronizer.SynchronizeRemoteClustersAsync();
        }
    }
}
