using k8s.LeaderElection;
using NewRelic.Api.Agent;

namespace Vecc.K8s.MultiCluster.Api.Services;

public class OrchestratorLeader(ILogger<OrchestratorLeader> _logger,
    IHostApplicationLifetime _applicationLifetime,
    LeaderElector _leaderElector,
    LeaderStatus _leaderStatus,
    ICache _cache) : Leader<OrchestratorLeader>(_logger, _applicationLifetime, _leaderElector, _leaderStatus)
{
    [Transaction]
    protected override async Task OnLeaderElectedAsync()
    {
        await _cache.SynchronizeCachesAsync();
    }
}
