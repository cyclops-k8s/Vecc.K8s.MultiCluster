using k8s.LeaderElection;
using NewRelic.Api.Agent;

namespace Vecc.K8s.MultiCluster.Api.Services;

public class OperatorLeader : Leader<OperatorLeader>
{
    private readonly IHostnameSynchronizer _hostnameSynchronizer;

    public OperatorLeader(ILogger<OperatorLeader> logger,
        IHostnameSynchronizer hostnameSynchronizer,
        IHostApplicationLifetime applicationLifetime,
        LeaderElector leaderElector,
        LeaderStatus leaderStatus)
        : base(logger, applicationLifetime, leaderElector, leaderStatus)
    {
        _hostnameSynchronizer = hostnameSynchronizer;
    }

    [Transaction]
    protected override async Task OnLeaderElectedAsync()
    {
        await _hostnameSynchronizer.SynchronizeLocalClusterAsync();
    }
}
