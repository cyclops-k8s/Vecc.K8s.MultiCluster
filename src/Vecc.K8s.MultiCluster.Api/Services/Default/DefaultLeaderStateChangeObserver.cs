using k8s.Models;
using KubeOps.KubernetesClient;
using KubeOps.Operator.Leadership;
using System.Net;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultLeaderStateChangeObserver : IObserver<LeaderState>, IDisposable
    {
        private readonly ILogger<DefaultLeaderStateChangeObserver> _logger;
        private readonly IKubernetesClient _kubernetesClient;
        private readonly IIngressManager _ingressManager;
        private readonly IServiceManager _serviceManager;
        private readonly ManualResetEventSlim _leaderStateChangeEvent;
        private readonly Task _leaderStateChangeTask;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private bool _disposed;
        private Dictionary<string, IPAddress[]> _ipAddresses;

        public DefaultLeaderStateChangeObserver(ILogger<DefaultLeaderStateChangeObserver> logger, IKubernetesClient kubernetesClient, IIngressManager ingressManager, IServiceManager serviceManager)
        {
            _logger = logger;
            _kubernetesClient = kubernetesClient;
            _ingressManager = ingressManager;
            _serviceManager = serviceManager;
            _leaderStateChangeEvent = new ManualResetEventSlim(false);
            _cancellationTokenSource = new CancellationTokenSource();
            _leaderStateChangeTask = Task.Run(() => LeadershipChanged(_cancellationTokenSource.Token));
            _ipAddresses = new Dictionary<string, IPAddress[]>();
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
                    _cancellationTokenSource.Cancel();
                    _leaderStateChangeTask.Wait(new TimeSpan(0, 0, 10));
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
            while (true)
            {
                _logger.LogInformation("Waiting to become the leader");
                _leaderStateChangeEvent.Wait(cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Cancelling leadership changed thread");
                    return;
                }

                try
                {
                    _logger.LogInformation("I'm the new leader");

                    var ingressHostnameTask = Task.Run(_ingressManager.GetAvailableHostnamesAsync);
                    var serviceHostnameTask = Task.Run(_serviceManager.GetAvailableHostnamesAsync);

                    await Task.WhenAll(ingressHostnameTask, serviceHostnameTask);

                    var ingressHosts = await ingressHostnameTask;
                    var serviceHosts = await serviceHostnameTask;
                    var validServiceHosts = new Dictionary<string, V1Service>();
                    var validIngressHosts = new Dictionary<string, V1Ingress>();
                    var invalidHostnames = new List<string>();

                    foreach (var service in serviceHosts)
                    {
                        if (service.Value.Count > 1)
                        {
                            _logger.LogWarning("Too many service hosts for {@hostname}", service.Key);
                            invalidHostnames.Add(service.Key);
                            continue;
                        }
                        if (ingressHosts.ContainsKey(service.Key))
                        {
                            _logger.LogWarning("Host {@hostname} exists in both an ingress and a service", service.Key);
                            invalidHostnames.Add(service.Key);
                            continue;
                        }

                        validServiceHosts[service.Key] = service.Value[0];
                    }

                    foreach (var ingressHost in ingressHosts)
                    {
                        if (serviceHosts.ContainsKey(ingressHost.Key))
                        {
                            _logger.LogWarning("Host {@hostname} exists in both an ingress and a service", ingressHost.Key);
                            continue;
                        }

                        foreach (var ingress in ingressHost.Value)
                        {
                            if (validIngressHosts.TryGetValue(ingressHost.Key, out var foundIngress))
                            {
                                // check to make sure the endpoint IP's match, otherwise, mark as invalid and ignore this hostname.
                                var balancerEndpoints = foundIngress.Status.LoadBalancer.Ingress;
                                var ingressEndpoints = ingress.Status.LoadBalancer.Ingress;
                                var same = ingressEndpoints.All(lb => balancerEndpoints.Any(blb => blb.Ip == lb.Ip));
                                same = same && balancerEndpoints.All(blb => ingressEndpoints.Any(lb => lb.Ip == blb.Ip));
                                if (!same)
                                {
                                    _logger.LogWarning("Exposed IP mismatch for {@hostname}", ingressHost.Key);
                                    invalidHostnames.Add(ingressHost.Key);
                                }
                                continue;
                            }

                            validIngressHosts[ingressHost.Key] = ingress;
                        }
                    }

                    var ipAddresses = new Dictionary<string, IPAddress[]>();
                    foreach (var service in validServiceHosts)
                    {
                        var addresses = service.Value.Status.LoadBalancer.Ingress.Select(ingress => IPAddress.Parse(ingress.Ip)).ToArray();
                        ipAddresses[service.Key] = addresses;
                    }

                    foreach (var ingress in validIngressHosts)
                    {
                        var addresses = ingress.Value.Status.LoadBalancer.Ingress.Select(ingress => IPAddress.Parse(ingress.Ip)).ToArray();
                        ipAddresses[ingress.Key] = addresses;
                    }

                    _ipAddresses = ipAddresses;
                }
                catch(Exception exception)
                {
                    _logger.LogCritical(exception, "Unhandled exception handling leadership change");
                }

                _leaderStateChangeEvent.Reset();
            }
        }
    }
}
