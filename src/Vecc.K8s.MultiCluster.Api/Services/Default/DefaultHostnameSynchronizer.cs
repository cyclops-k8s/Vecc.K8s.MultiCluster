using k8s.Models;
using Microsoft.Extensions.Options;
using NewRelic.Api.Agent;
using System.Net;
using Vecc.K8s.MultiCluster.Api.Models.Core;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultHostnameSynchronizer : IHostnameSynchronizer
    {
        private readonly ILogger<DefaultHostnameSynchronizer> _logger;
        private readonly IIngressManager _ingressManager;
        private readonly INamespaceManager _namespaceManager;
        private readonly IServiceManager _serviceManager;
        private readonly ICache _cache;
        private readonly IOptions<MultiClusterOptions> _multiClusterOptions;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly LeaderStatus _leaderStatus;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IHttpClientFactory _clientFactory;
        private readonly CancellationTokenSource _shutdownCancellationTokenSource;
        private readonly CancellationToken _shutdownCancellationToken;
        private readonly ManualResetEvent _shutdownEvent;
        private readonly AutoResetEvent _synchronizeLocalClusterHolder;

        public DefaultHostnameSynchronizer(
            ILogger<DefaultHostnameSynchronizer> logger,
            IIngressManager ingressManager,
            INamespaceManager namespaceManager,
            IServiceManager serviceManager,
            ICache cache,
            IOptions<MultiClusterOptions> multiClusterOptions,
            IHostApplicationLifetime lifetime,
            LeaderStatus leaderStatus,
            IDateTimeProvider dateTimeProvider,
            IHttpClientFactory clientFactory)
        {
            _logger = logger;
            _ingressManager = ingressManager;
            _namespaceManager = namespaceManager;
            _serviceManager = serviceManager;
            _cache = cache;
            _multiClusterOptions = multiClusterOptions;
            _lifetime = lifetime;
            _leaderStatus = leaderStatus;
            _dateTimeProvider = dateTimeProvider;
            _clientFactory = clientFactory;
            _lifetime.ApplicationStopping.Register(OnApplicationStopping);
            _shutdownCancellationTokenSource = new CancellationTokenSource();
            _shutdownEvent = new ManualResetEvent(false);
            _shutdownCancellationToken = _shutdownCancellationTokenSource.Token;
            _synchronizeLocalClusterHolder = new AutoResetEvent(true);
        }

        [Trace]
        public async Task SynchronizeLocalClusterAsync()
        {
            _synchronizeLocalClusterHolder.WaitOne();
            try
            {
                _logger.LogInformation("Synchronizing local cluster");
                var ipAddresses = new Dictionary<string, List<HostIP>>();
                var localClusterIdentifier = _multiClusterOptions.Value.ClusterIdentifier;
                var validServiceHosts = new Dictionary<string, V1Service>();
                var validIngressHosts = new Dictionary<string, V1Ingress>();
                var invalidHostnames = new List<string>();
                IList<V1Ingress>? ingresses = null;
                IList<V1Service>? services = null;
                IList<V1Endpoints>? endpoints = null;
                IList<V1Service>? loadBalancerServices = null;
                Dictionary<string, IList<V1Ingress>>? ingressHosts = null;
                var myHosts = Array.Empty<string>();
                _logger.LogTrace("Initiating namespace getter");
                var namespaces = await _namespaceManager.GetNamsepacesAsync();
                _logger.LogTrace("Got the namespaces");

                _logger.LogTrace("Getting ingresses, services and endpoints");
                await Task.WhenAll(
                    Task.Run(async () => myHosts = await _cache.GetHostnamesAsync(_multiClusterOptions.Value.ClusterIdentifier)),
                    Task.Run(async () => ingresses = await _ingressManager.GetIngressesAsync(namespaces)),
                    Task.Run(async () => services = await _serviceManager.GetServicesAsync(namespaces)),
                    Task.Run(async () => endpoints = await _serviceManager.GetEndpointsAsync(namespaces)));
                _logger.LogTrace("Got ingresses, services and endpoints");

                _logger.LogDebug("Counts: ingress-{@ingress} services-{@services} endpoints-{@endpoints}", ingresses!.Count, services!.Count, endpoints!.Count);
                _logger.LogTrace("Current hostnames: {@hostnames}", (object)myHosts);

                _logger.LogTrace("Getting available hostnames and load balancer services");
                await Task.WhenAll(
                    Task.Run(async () => ingressHosts = await _ingressManager.GetAvailableHostnamesAsync(ingresses, services, endpoints)),
                    Task.Run(async () => loadBalancerServices = await _serviceManager.GetLoadBalancerEndpointsAsync(services)));
                _logger.LogTrace("Done getting available hostnames and load balancer services");

                _logger.LogTrace("Getting available hostnames from services");
                var serviceHosts = await _serviceManager.GetAvailableHostnamesAsync(loadBalancerServices!, endpoints);
                _logger.LogTrace("Done getting available hostnames from services");

                _logger.LogDebug("Counts validingresses-{@ingress} load balancer services-{@lbservices} valid services-{@services}",
                    ingressHosts!.Count, loadBalancerServices!.Count, serviceHosts.Count);

                _logger.LogDebug("Setting service tracking");
                _logger.LogDebug("Purging current tracked list");
                await _cache.UntrackAllServicesAsync();
                _logger.LogDebug("Tracking ingress related services");
                foreach (var ingress in ingresses)
                {
                    await _cache.SetResourceVersionAsync(ingress.Metadata.Uid, ingress.Metadata.ResourceVersion);
                    var serviceNames = await _ingressManager.GetRelatedServiceNamesAsync(ingress);
                    foreach (var name in serviceNames)
                    {
                        await _cache.TrackServiceAsync(ingress.Metadata.NamespaceProperty, name);
                    }
                }
                _logger.LogDebug("Tracking load balancer services");
                foreach (var service in loadBalancerServices)
                {
                    await _cache.TrackServiceAsync(service.Metadata.NamespaceProperty, service.Metadata.Name);
                }
                _logger.LogDebug("Tracking services");
                foreach (var service in services)
                {
                    await _cache.SetResourceVersionAsync(service.Metadata.Uid, service.Metadata.ResourceVersion);
                }
                _logger.LogDebug("Tracking endpoints and counts");
                foreach (var endpoint in endpoints)
                {
                    await _cache.SetResourceVersionAsync(endpoint.Metadata.Uid, endpoint.Metadata.ResourceVersion);
                    await _cache.SetEndpointsCountAsync(endpoint.Namespace(), endpoint.Name(), endpoint.Subsets?.Count() ?? 0);
                }
                _logger.LogDebug("Done tracking services");

                foreach (var service in serviceHosts)
                {
                    _logger.LogDebug("Checking service: {hostname}", service.Key);
                    var valid = true;
                    if (service.Value.Count > 1)
                    {
                        _logger.LogWarning("Too many service hosts for {hostname} {@services}",
                            service.Key,
                            service.Value.Select(s => new
                            {
                                Namespace = s.Namespace(),
                                Name = s.Name()
                            }));
                        invalidHostnames.Add(service.Key);
                        valid = false;
                    }

                    if (ingressHosts.ContainsKey(service.Key))
                    {
                        _logger.LogWarning("Host {hostname} exists in both an ingress and a service {@services} {@ingresses}",
                            service.Key,
                            service.Value.Select(s => new
                            {
                                Namespace = s.Namespace(),
                                Name = s.Name()
                            }),
                            ingressHosts[service.Key].Select(i => new
                            {
                                Namespace = i.Namespace(),
                                Name = i.Name()
                            }));
                        invalidHostnames.Add(service.Key);
                        valid = false;
                    }

                    if (valid)
                    {
                        validServiceHosts[service.Key] = service.Value[0];
                    }
                }

                foreach (var ingressHost in ingressHosts)
                {
                    using var ingressHostScope = _logger.BeginScope("{hostname}", ingressHost.Key);

                    _logger.LogDebug("Checking ingress for validity");
                    if (serviceHosts.ContainsKey(ingressHost.Key))
                    {
                        _logger.LogWarning("Host {hostname} exists in both an ingress and a service {@services} {@ingresses}",
                            ingressHost.Key,
                            serviceHosts[ingressHost.Key].Select(s => new
                            {
                                Namespace = s.Namespace(),
                                Name = s.Name()
                            }),
                            ingressHost.Value.Select(i => new
                            {
                                Namespace = i.Namespace(),
                                Name = i.Name()
                            }));
                        continue;
                    }

                    foreach (var ingress in ingressHost.Value)
                    {
                        using var ingressScope = _logger.BeginScope("{namespace}/{ingress}", ingress.Namespace(), ingress.Name());
                        _logger.LogDebug("Checking ingress exposed ip's to make sure its hostname ip is same if found in multiple ingresses");
                        if (validIngressHosts.TryGetValue(ingressHost.Key, out var foundIngress))
                        {
                            _logger.LogTrace("Ingress in valid hosts");
                            // check to make sure the endpoint IP's match, otherwise, mark as invalid and ignore this hostname.
                            var balancerEndpoints = foundIngress.Status.LoadBalancer.Ingress;
                            var ingressEndpoints = ingress.Status.LoadBalancer.Ingress;
                            var same = ingressEndpoints.All(lb => balancerEndpoints.Any(blb => blb.Ip == lb.Ip));
                            same = same && balancerEndpoints.All(blb => ingressEndpoints.Any(lb => lb.Ip == blb.Ip));
                            if (!same)
                            {
                                _logger.LogWarning("Exposed IP mismatch with hostname in multiple ingresses for {@hostname}", ingressHost.Key);
                                invalidHostnames.Add(ingressHost.Key);
                            }
                            continue;
                        }

                        validIngressHosts[ingressHost.Key] = ingress;
                    }
                }

                foreach (var service in validServiceHosts)
                {
                    _logger.LogInformation("Found valid service: {namespace}/{name}", service.Value.Namespace(), service.Value.Name());
                    var addresses = service.Value.Status.LoadBalancer.Ingress.Select(ingress => IPAddress.Parse(ingress.Ip)).ToArray();
                    var priorityAnnotation = service.Value.GetAnnotation("multicluster.veccsolutions.com/priority");
                    if (string.IsNullOrWhiteSpace(priorityAnnotation))
                    {
                        _logger.LogTrace("No priority annotation, defaulting to 0");
                        priorityAnnotation = "0";
                    }

                    if (!int.TryParse(priorityAnnotation, out var priority) &&
                        priorityAnnotation != null)
                    {
                        _logger.LogError("Priority annotation was not parsable as an int, defaulting to 0.");
                    }

                    var weightAnnotation = service.Value.GetAnnotation("multicluster.veccsolutions.com/weight");
                    if (string.IsNullOrWhiteSpace(weightAnnotation))
                    {
                        _logger.LogTrace("No weight annotation, defaulting to 50");
                        weightAnnotation = "50";
                    }
                    if (!int.TryParse(weightAnnotation, out var weight))
                    {
                        weight = 50;
                        _logger.LogError("Weight annotation was not parsable as an int, defaulting to 50");
                    }

                    if (!ipAddresses.ContainsKey(service.Key))
                    {
                        ipAddresses[service.Key] = new List<HostIP>();
                    }

                    ipAddresses[service.Key].AddRange(addresses.Select(address => new HostIP
                    {
                        IPAddress = address.ToString(),
                        Priority = priority,
                        Weight = weight,
                        ClusterIdentifier = _multiClusterOptions.Value.ClusterIdentifier
                    }));
                }

                foreach (var ingress in validIngressHosts)
                {
                    using var scope = _logger.BeginScope("{namespace}/{name}", ingress.Value.Namespace(), ingress.Value.Name());
                    _logger.LogDebug("Found valid ingress");
                    var addresses = ingress.Value.Status.LoadBalancer.Ingress.Select(ingress => IPAddress.Parse(ingress.Ip)).ToArray();
                    _logger.LogTrace("Addresses: {@addresses}", (object)addresses);

                    var priorityAnnotation = ingress.Value.GetAnnotation("multicluster.veccsolutions.com/priority");
                    if (!int.TryParse(priorityAnnotation, out var priority) &&
                        priorityAnnotation != null)
                    {
                        _logger.LogError("Priority annotation was not parsable as an int, defaulting to 0.");
                    }

                    var weightAnnotation = ingress.Value.GetAnnotation("multicluster.veccsolutions.com/weight");
                    if (!int.TryParse(weightAnnotation, out var weight))
                    {
                        weight = 50;
                        if (weightAnnotation != null)
                        {
                            _logger.LogError("Weight annotation was not parsable as an int, defaulting to 50");
                        }
                    }

                    if (!ipAddresses.ContainsKey(ingress.Key))
                    {
                        _logger.LogTrace("Setting empty list for {key}", ingress.Key);
                        ipAddresses[ingress.Key] = new List<HostIP>();
                    }

                    _logger.LogTrace("Adding {@addresses} to the list of ips for {key}", addresses, ingress.Key);

                    ipAddresses[ingress.Key].AddRange(addresses.Select(address => new HostIP
                    {
                        IPAddress = address.ToString(),
                        Priority = priority,
                        Weight = weight,
                        ClusterIdentifier = _multiClusterOptions.Value.ClusterIdentifier
                    }));

                    _logger.LogTrace("Resulting addresses {@ipaddresses}", ipAddresses[ingress.Key]);
                }

                foreach (var host in ipAddresses)
                {
                    _logger.LogTrace("Host information: {@host}", host);
                    if (await _cache.SetHostIPsAsync(host.Key, localClusterIdentifier, host.Value.ToArray()))
                    {
                        _logger.LogInformation("Host information changed for {host}", host.Key);
                        await SendHostUpdatesAsync(host.Key, host.Value.ToArray());
                    }
                }

                foreach (var host in myHosts)
                {
                    _logger.LogDebug("Checking if {host} is removed.", host);
                    if (!ipAddresses.ContainsKey(host))
                    {
                        _logger.LogInformation("Removing old host {host}", host);
                        await _cache.SetHostIPsAsync(host, localClusterIdentifier, Array.Empty<HostIP>());
                        await SendHostUpdatesAsync(host, Array.Empty<HostIP>());
                    }
                }

                _logger.LogInformation("Done synchronizing local cluster");
            }
            finally
            {
                _synchronizeLocalClusterHolder.Set();
            }
        }

        [Trace]
        public async Task<bool> SynchronizeLocalIngressAsync(V1Ingress ingress)
        {
            //TODO: we need to cache the service/ingress and state of the service object related to a hostname
            //      regardless if they are valid or not. We can then reference those cached objects to
            //      decrease the load on the kubernetes api server, instead of querying the entire cluster state
            //      we would only query the necessary services/endpoints and ingresses
            _logger.LogInformation("Synchronizing local cluster ingress {@namespace}/{@ingress}", ingress.Namespace(), ingress.Name());
            await SynchronizeLocalClusterAsync();
            _logger.LogInformation("Done");

            return true;
        }

        [Trace]
        public async Task<bool> SynchronizeLocalServiceAsync(V1Service service)
        {
            //TODO: see SynchronizeLocalIngressAsync
            _logger.LogInformation("Synchronizing local cluster ingress {@namespace}/{@ingress}", service.Namespace(), service.Name());
            await SynchronizeLocalClusterAsync();
            _logger.LogInformation("Done");

            return true;
        }

        [Trace]
        public async Task<bool> SynchronizeLocalEndpointsAsync(V1Endpoints endpoints)
        {
            _logger.LogInformation("Synchronizing local cluster ingress {@namespace}/{@ingress}", endpoints.Namespace(), endpoints.Name());
            await SynchronizeLocalClusterAsync();
            _logger.LogInformation("Done");

            return true;
        }

        [Trace]
        public async Task SynchronizeRemoteClustersAsync()
        {
            var peers = _multiClusterOptions.Value.Peers;
            if (peers.Length == 0)
            {
                _logger.LogInformation("No peers to synchronize with.");
                return;
            }

            foreach (var peer in peers)
            {
                try
                {
                    var client = _clientFactory.CreateClient(peer.Url);
                    var hosts = await client.GetFromJsonAsync<Models.Api.HostModel[]?>("Host");
                    if (hosts == null)
                    {
                        _logger.LogError("Unable to get hosts from remote peer, result is null.");
                        continue;
                    }

                    var currentHosts = await _cache.GetHostsAsync(peer.Identifier);
                    if (currentHosts != null)
                    {
                        _logger.LogTrace("We got hosts from the cache for {clusteridentifier}", peer.Identifier);
                        var hostsToRemove = currentHosts.Where(h => !hosts.Any(x => x.Hostname == h.Hostname));
                        _logger.LogTrace("Removing clustered hosts {@hosts}", hostsToRemove);
                        foreach (var host in hostsToRemove)
                        {
                            _logger.LogInformation("Removing stale cluster host {clusteridentifier}/{hostname}", peer.Identifier, host.Hostname);
                            await _cache.RemoveClusterHostnameAsync(peer.Identifier, host.Hostname);
                        }
                    }

                    foreach (var host in hosts)
                    {
                        _logger.LogTrace("Setting host {host}", host.Hostname);
                        var hostIps = host.HostIPs.Select(i => new HostIP
                        {
                            IPAddress = i.IPAddress,
                            Priority = i.Priority,
                            Weight = i.Weight,
                            ClusterIdentifier = peer.Identifier
                        }).ToArray();
                        await _cache.SetHostIPsAsync(host.Hostname, peer.Identifier, hostIps);
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Unable to synchronize remote cluster: {@clusterIdentifier}", peer.Identifier);
                }
            }
        }

        [Trace]
        public async Task WatchClusterHeartbeatsAsync()
        {
            while (!_shutdownCancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_multiClusterOptions.Value.HeartbeatCheckInterval * 1000, _shutdownCancellationToken);
                }
                catch
                {
                    _logger.LogInformation("Cluster heartbeat monitor shutdown.");
                    return;
                }

                if (!_leaderStatus.IsLeader)
                {
                    _logger.LogTrace("Not the leader, not checking cluster heartbeats");
                    continue;
                }

                _logger.LogInformation("Checking cluster heartbeats");

                try
                {
                    var clusterIdentifiers = await _cache.GetClusterIdentifiersAsync();
                    var timeout = _dateTimeProvider.UtcNow.AddSeconds(-_multiClusterOptions.Value.HeartbeatTimeout);

                    foreach (var clusterIdentifier in clusterIdentifiers)
                    {
                        var clusterHeartbeat = await _cache.GetClusterHeartbeatTimeAsync(clusterIdentifier);

                        if (clusterHeartbeat == null)
                        {
                            _logger.LogWarning("Cluster heartbeat not set for identifier {clusterIdentifier}", clusterIdentifier);
                            continue;
                        }

                        if (clusterHeartbeat < timeout)
                        {
                            var clusterHosts = await _cache.GetHostnamesAsync(clusterIdentifier);
                            _logger.LogWarning("Cluster {@clusterIdentifier} timeout is expired, last heartbeat was at {@heartbeat}", clusterIdentifier, clusterHeartbeat);

                            foreach (var hostname in clusterHosts)
                            {
                                _logger.LogWarning("Removing cluster hostname {@clusterIdentifier}/{@hostname} due to expired timeout", clusterIdentifier, hostname);
                                await _cache.RemoveClusterHostnameAsync(clusterIdentifier, hostname);
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Error checking cluster heartbeats");
                }

                try
                {
                    _logger.LogTrace("Making sure stale records are not in the cache");
                    await _cache.SynchronizeCachesAsync();
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Error cleaning stale records in the cache");
                }
            }

            _shutdownEvent.Set();
        }

        public async Task ClusterHeartbeatAsync()
        {
            while (!_shutdownCancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_multiClusterOptions.Value.HeartbeatSetInterval * 1000, _shutdownCancellationToken);
                }
                catch
                {
                    _logger.LogInformation("Shutting down heartbeat due to application shutdown.");
                }

                if (!_leaderStatus.IsLeader)
                {
                    _logger.LogTrace("Not the leader, skipping cluster heartbeat");
                    continue;
                }

                try
                {
                    await SendHeartbeats();
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Error sending heartbeats");
                }
            }
        }

        [Transaction]
        private async Task SendHeartbeats()
        {
            _logger.LogInformation("Sending heartbeat");
            //set our own heartbeat
            var localClusterIdentifier = _multiClusterOptions.Value.ClusterIdentifier;
            var now = _dateTimeProvider.UtcNow;
            await _cache.SetClusterHeartbeatAsync(localClusterIdentifier, now);

            if (!_multiClusterOptions.Value.Peers.Any())
            {
                _logger.LogTrace("No peers, not processing them.");
                return;
            }

            var heartbeatTasks = _multiClusterOptions.Value.Peers.Select(peer =>
            {
                try
                {
                    return Task.Run(async () =>
                    {
                        using var scope1 = _logger.BeginScope("{@peer}", peer.Url);
                        try
                        {
                            var httpClient = _clientFactory.CreateClient(peer.Url);
                            _logger.LogDebug("Sending heartbeat");
                            var response = await httpClient.PostAsync($"/Heartbeat", null);
                            response.EnsureSuccessStatusCode();
                            _logger.LogDebug("Done");
                        }
                        catch (Exception exception)
                        {
                            _logger.LogError(exception, "Unable to post heartbeat to {@peer}", peer);
                        }
                    }, _shutdownCancellationToken);
                }
                catch (TaskCanceledException exception)
                {
                    if (!_shutdownCancellationToken.IsCancellationRequested)
                    {
                        _logger.LogError(exception, "Unexpected task cancelled while sending heartbeat.");
                    }
                    else
                    {
                        _logger.LogInformation("Shutdown requested while sending heartbeat to {@peer}", peer);
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Unexpexted exception posting heartbeat to {@peer}", peer);
                }
                return Task.CompletedTask;
            });
            try
            {
                await Task.WhenAll(heartbeatTasks);
            }
            catch (TaskCanceledException exception)
            {
                _logger.LogWarning(exception, "A task was cancelled while sending heartbeats");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error while handling heartbeats.");
                throw;
            }
        }

        [Trace]
        private async Task SendHostUpdatesAsync(string hostname, HostIP[] hosts)
        {
            using var scope = _logger.BeginScope("{hostname}", hostname);
            if (!_multiClusterOptions.Value.Peers.Any())
            {
                return;
            }

            var updateTasks = _multiClusterOptions.Value.Peers.Select(peer =>
            {
                try
                {
                    return Task.Run(async () =>
                    {
                        using var scope1 = _logger.BeginScope("{@peer}", peer.Url);
                        try
                        {
                            var httpClient = _clientFactory.CreateClient(peer.Url);
                            var data = new Models.Api.HostModel
                            {
                                Hostname = hostname,
                                HostIPs = hosts.Select(ip => new Models.Api.HostIP
                                {
                                    IPAddress = ip.IPAddress,
                                    Priority = ip.Priority,
                                    Weight = ip.Weight
                                }).ToArray()
                            };
                            _logger.LogDebug("Sending update to {@url}", peer.Url);
                            var result = await httpClient.PostAsync($"/Host", JsonContent.Create(data));
                            result.EnsureSuccessStatusCode();
                            _logger.LogDebug("Done");
                        }
                        catch (Exception exception)
                        {
                            _logger.LogError(exception, "Unable to post host update");
                        }
                    }, _shutdownCancellationToken);
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning("Shutdown requested while sending host update");
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Unexpected exception posting host update");
                }
                return Task.CompletedTask;
            });
            try
            {
                await Task.WhenAll(updateTasks);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error sending host updates");
            }
        }

        private void OnApplicationStopping()
        {
            _shutdownCancellationTokenSource.Cancel();
            _shutdownEvent.WaitOne(TimeSpan.FromSeconds(5));
        }
    }
}
