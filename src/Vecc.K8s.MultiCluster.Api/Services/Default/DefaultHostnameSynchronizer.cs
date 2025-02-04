using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NewRelic.Api.Agent;
using System.Net;
using Vecc.K8s.MultiCluster.Api.Models.Core;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;

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
        private readonly IGslbManager _gslbManager;
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
            IHttpClientFactory clientFactory,
            IGslbManager gslbManager)
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
            _gslbManager = gslbManager;
            _lifetime.ApplicationStopping.Register(OnApplicationStopping);
            _shutdownCancellationTokenSource = new CancellationTokenSource();
            _shutdownEvent = new ManualResetEvent(true);
            _shutdownCancellationToken = _shutdownCancellationTokenSource.Token;
            _synchronizeLocalClusterHolder = new AutoResetEvent(true);
        }

        [Trace]
        public async Task SynchronizeLocalClusterAsync()
        {
            _synchronizeLocalClusterHolder.WaitOne();
            using var scope = _logger.BeginScope(new { SyncId = Guid.NewGuid() });
            try
            {
                _logger.LogInformation("Waiting a second for the cluster to settle.");
                await Task.Delay(1000); //wait one second to allow resources to fully complete.
                _logger.LogInformation("Synchronizing local cluster");
                var ipAddresses = new Dictionary<string, List<HostIP>>();
                var localClusterIdentifier = _multiClusterOptions.Value.ClusterIdentifier;
                var validIngressHosts = new Dictionary<string, V1Ingress>();
                var invalidHostnames = new List<string>();
                IList<V1Ingress>? ingresses = null;
                IList<V1Service>? services = null;
                IList<V1Endpoints>? endpoints = null;
                IList<V1Service>? loadBalancerServices = null;
                V1Gslb[]? gslbs = null;

                Dictionary<string, IList<V1Ingress>>? ingressHosts = null;
                var myHosts = Array.Empty<Models.Core.Host>();
                _logger.LogTrace("Initiating namespace getter");
                //var namespaces = await _namespaceManager.GetNamsepacesAsync();
                _logger.LogTrace("Got the namespaces");

                _logger.LogTrace("Getting ingresses, services, endpoints and gslbs");
                await Task.WhenAll(
                    Task.Run(async () => myHosts = (await _cache.GetHostsAsync(_multiClusterOptions.Value.ClusterIdentifier) ?? Array.Empty<Models.Core.Host>())),
                    Task.Run(async () => ingresses = await _ingressManager.GetIngressesAsync()),
                    Task.Run(async () => services = await _serviceManager.GetServicesAsync()),
                    Task.Run(async () => endpoints = await _serviceManager.GetEndpointsAsync()),
                    Task.Run(async () => gslbs = await _gslbManager.GetGslbsAsync()));

                _logger.LogTrace("Got ingresses, services, endpoints and gslbs");

                _logger.LogDebug("Counts: ingress-{@ingress} services-{@services} endpoints-{@endpoints} gslbs-{@gslbs}", ingresses!.Count, services!.Count, endpoints!.Count, gslbs!.Length);
                _logger.LogTrace("Current hostnames: {@hostnames}", (object)myHosts);

                _logger.LogTrace("Getting available hostnames and load balancer services");
                await Task.WhenAll(
                    Task.Run(async () => ingressHosts = await _ingressManager.GetAvailableHostnamesAsync(ingresses, services, endpoints)),
                    Task.Run(async () => loadBalancerServices = await _serviceManager.GetLoadBalancerServicesAsync(services, endpoints))
                    );
                _logger.LogTrace("Done getting available hostnames and load balancer services");

                _logger.LogTrace("Done getting available hostnames from services");

                _logger.LogDebug("Counts validingresses-{@ingress} valid load balancer services-{@lbservices}",
                    ingressHosts!.Count, loadBalancerServices!.Count);

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

                foreach (var ingressHost in ingressHosts)
                {
                    using var ingressHostScope = _logger.BeginScope("{hostname}", ingressHost.Key);

                    _logger.LogDebug("Checking ingress for validity");

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

                var gslbHostnames = gslbs.SelectMany(gslb => gslb.Hostnames.Select(host => new KeyValuePair<string, V1Gslb>(host, gslb)));
                var gslbToHostnames = gslbHostnames.GroupBy(g => g.Key).ToDictionary(g => g.Key, g => g.Select(x => x.Value).ToArray());
                foreach (var gslb in gslbToHostnames)
                {
                    using var gslbScope = _logger.BeginScope("{hostname}", gslb.Key);

                    try
                    {
                        if (invalidHostnames.Contains(gslb.Key))
                        {
                            _logger.LogWarning("GSLB hostname is in the list of invalid hostname, skipping");
                            continue;
                        }

                        if (gslb.Value.All(x => x.ObjectReference.Kind == V1Gslb.V1ObjectReference.ReferenceType.Service))
                        {
                            _logger.LogDebug("GSLB reference type is a service");

                            if (gslb.Value.Length > 1)
                            {
                                _logger.LogWarning("GSLB hostname {hostname} has multiple services, skipping. Expected to find only one service: {@services}", gslb.Key, gslb.Value.Select(x => x.Metadata.NamespaceProperty + "/" + x.ObjectReference.Name));
                                continue;
                            }

                            var gslbServices = loadBalancerServices.Where(s =>
                                gslb.Value.Any(g =>
                                    g.Metadata.NamespaceProperty == s.Metadata.NamespaceProperty &&
                                    g.ObjectReference.Name == s.Metadata.Name)).ToArray();

                            if (gslbServices.Length == 0)
                            {
                                _logger.LogWarning("GSLB hostname {hostname} has no valid service, skipping. Expected to find valid services: {namespace}/{name} {@validServices}",
                                    gslb.Key,
                                    gslb.Value[0].Metadata.NamespaceProperty,
                                    gslb.Value[0].ObjectReference.Name,
                                    loadBalancerServices.Select(s => s.Metadata.NamespaceProperty + "/" + s.Metadata.Name));

                                continue;
                            }

                            _logger.LogTrace("Found valid services: {@services}", gslbServices.Select(s => s.Metadata.NamespaceProperty + "/" + s.Metadata.Name));

                            var service = gslbServices[0]!;
                            var endpoint = endpoints.FirstOrDefault(e => e.Namespace() == service.Namespace() && e.Name() == service.Name());

                            if (endpoint == null)
                            {
                                _logger.LogWarning("Endpoints not found for {service}. Skipping", service.Namespace() + "/" + service.Name());
                                continue;
                            }

                            if ((endpoint.Subsets?.Count ?? 0) == 0)
                            {
                                _logger.LogWarning("Service has no backend endpoints. Skipping.");
                                continue;
                            }

                            ipAddresses[gslb.Key] = gslbServices.Select(s =>
                                new HostIP
                                {
                                    IPAddress = s.Status.LoadBalancer.Ingress.First().Ip,
                                    Priority = gslb.Value.Max(x => x.Priority),
                                    Weight = gslb.Value.Max(x => x.Weight),
                                    ClusterIdentifier = _multiClusterOptions.Value.ClusterIdentifier
                                }).ToList();
                        }
                        else if (gslb.Value.All(x => x.ObjectReference.Kind == V1Gslb.V1ObjectReference.ReferenceType.Ingress))
                        {
                            _logger.LogDebug("GSLB reference type is an ingress");
                            var gslbIngresses = validIngressHosts.Values.Where(s => gslb.Value.Any(g => g.Metadata.NamespaceProperty == s.Metadata.NamespaceProperty && g.ObjectReference.Name == s.Metadata.Name)).ToArray();
                            if (gslbIngresses.Count() == 0)
                            {
                                _logger.LogWarning("GSLB hostname {hostname} has no valid ingresses, skipping. Expected to find valid ingress: {@validIngresses}", gslb.Key, gslb.Value.Select(x => x.Metadata.NamespaceProperty + "/" + x.ObjectReference.Name));
                                continue;
                            }
                            _logger.LogTrace("Found valid ingresses: {@ingresses}", gslbIngresses.Select(s => s.Metadata.NamespaceProperty + "/" + s.Metadata.Name));

                            ipAddresses[gslb.Key] = gslbIngresses.Select(s =>
                                new HostIP
                                {
                                    IPAddress = s.Status.LoadBalancer.Ingress.First().Ip,
                                    Priority = gslb.Value.Max(x => x.Priority),
                                    Weight = gslb.Value.Max(x => x.Weight),
                                    ClusterIdentifier = _multiClusterOptions.Value.ClusterIdentifier
                                }).ToList();
                        }
                        else
                        {
                            _logger.LogWarning("GSLB hostname {hostname} has mixed references, skipping", gslb.Key);
                            continue;
                        }
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Error processing gslb hostname {hostname}", gslb.Key);
                    }
                }

                var hostnameCache = ipAddresses.Select(x => new Models.Core.Host
                {
                    Hostname = x.Key,
                    HostIPs = x.Value.ToArray()
                }).ToArray();

                await _cache.SetClusterCacheAsync(localClusterIdentifier, hostnameCache);

                // send host changes to peers
                foreach (var host in ipAddresses)
                {
                    await SendHostUpdatesAsync(host.Key, host.Value.ToArray());
                }

                // remove old hosts from peers
                foreach (var host in myHosts)
                {
                    _logger.LogDebug("Checking if {host} is removed.", host.Hostname);
                    if (!ipAddresses.ContainsKey(host.Hostname))
                    {
                        _logger.LogInformation("Removing old host {host}", host.Hostname);
                        await SendHostUpdatesAsync(host.Hostname, Array.Empty<HostIP>());
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

                    await _cache.SetClusterCacheAsync(peer.Identifier, hosts.Select(h =>
                        new Models.Core.Host
                        {
                            Hostname = h.Hostname,
                            HostIPs = h.HostIPs.Select(ip => new HostIP
                            {
                                IPAddress = ip.IPAddress,
                                Priority = ip.Priority,
                                Weight = ip.Weight,
                                ClusterIdentifier = peer.Identifier
                            }).ToArray()
                        }).ToArray());
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
            _shutdownEvent.Reset();

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
                    _logger.LogInformation("Pruncing clusters that haven't checked in since {timeout}", timeout);

                    foreach (var clusterIdentifier in clusterIdentifiers)
                    {
                        var clusterHeartbeat = await _cache.GetClusterHeartbeatTimeAsync(clusterIdentifier);
                        _logger.LogInformation("Checking cluster heartbeat for identifier {clusterIdentifier} with last heartbeat of {clusterHeartbeat}", clusterIdentifier, clusterHeartbeat);

                        if (clusterHeartbeat == null)
                        {
                            _logger.LogWarning("Cluster heartbeat not set for identifier {clusterIdentifier}", clusterIdentifier);
                        }
                        else if (clusterHeartbeat < timeout)
                        {
                            _logger.LogWarning("Cluster heartbeat is stale for identifier {clusterIdentifier}", clusterIdentifier);
                            await _cache.RemoveClusterCacheAsync(clusterIdentifier);
                        }
                        else
                        {
                            _logger.LogTrace("Cluster heartbeat is valid for identifier {clusterIdentifier}", clusterIdentifier);
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
