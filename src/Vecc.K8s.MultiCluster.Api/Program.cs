using Destructurama;
using k8s.Models;
using KubeOps.KubernetesClient;
using KubeOps.Operator;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Serilog;
using Vecc.Dns.Server;
using Vecc.K8s.MultiCluster.Api.Controllers;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;
using Vecc.K8s.MultiCluster.Api.Services;
using Vecc.K8s.MultiCluster.Api.Services.Authentication;
using Vecc.K8s.MultiCluster.Api.Services.Default;
const string OperatorFlag = "--operator";
const string OrchestratorFlag = "--orchestrator";
const string DnsServerFlag = "--dns-server";
const string FrontEndFlag = "--front-end";
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.logging.json")
                      .AddEnvironmentVariables()
                      .AddCommandLine(args);

builder.Services.Configure<DnsServerOptions>(builder.Configuration.GetSection("DnsServer"));
builder.Services.Configure<ApiAuthenticationHandlerOptions>(builder.Configuration.GetSection("Authentication"));

builder.Services.Configure<MultiClusterOptions>(builder.Configuration);
var options = new MultiClusterOptions();
var dnsOptions = new DnsServerOptions();

builder.Configuration.Bind(options);
builder.Configuration.GetSection("DnsServer").Bind(dnsOptions);

builder.Host.UseSerilog((context, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration)
                 .Destructure.UsingAttributes();
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var securityScheme = new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Type = SecuritySchemeType.ApiKey,
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = ApiAuthenticationHandlerOptions.DefaultScheme
        },
    };
    options.AddSecurityDefinition(ApiAuthenticationHandlerOptions.DefaultScheme, securityScheme);
    options.OperationFilter<SwaggerOperationFilter>();
});

if (args.Contains(OperatorFlag))
{
    builder.Services.AddSingleton<OperatorLeader>();
    builder.Services.AddKubernetesOperator((operatorSettings) =>
    {
        operatorSettings.Name = "operator";
        operatorSettings.EnableLeaderElection = true;
    })
        .AddController<K8sChangedController, V1Ingress>()
        .AddController<K8sChangedController, V1Service>()
        .AddController<K8sChangedController, V1Endpoints>()
        .AddController<K8sChangedController, V1Gslb>();
}
else if (args.Contains(OrchestratorFlag))
{
    builder.Services.AddSingleton<OrchestratorLeader>();
    builder.Services.AddKubernetesOperator((operatorSettings) =>
    {
        operatorSettings.Name = "orchestrator";
        operatorSettings.Namespace = options.Namespace;
        operatorSettings.EnableLeaderElection = true;
    })
        .AddController<K8sClusterCacheController, V1ClusterCache>();
}
else if (args.Contains(DnsServerFlag))
{
    builder.Services.AddKubernetesOperator((operatorSettings) =>
    {
        operatorSettings.Name = "dnsserver";
        operatorSettings.Namespace = options.Namespace;
    })
        .AddController<K8sHostnameCacheController, V1HostnameCache>();

    builder.Services.AddSingleton<IKubernetesClient, KubernetesClient>();
}
else if (args.Contains(FrontEndFlag))
{
    builder.Services.AddSingleton<IKubernetesClient, KubernetesClient>();
}
else
{
    throw new Exception($"Expected one of {OperatorFlag}, {OrchestratorFlag}, {DnsServerFlag} or {FrontEndFlag}");
}


builder.Services.AddSingleton<LeaderStatus>();
builder.Services.AddSingleton<DefaultDnsResolver>();
builder.Services.AddSingleton<IGslbManager, DefaultGslbManager>();
builder.Services.AddSingleton<IIngressManager, DefaultIngressManager>();
builder.Services.AddSingleton<INamespaceManager, DefaultNamespaceManager>();
builder.Services.AddSingleton<IServiceManager, DefaultServiceManager>();
builder.Services.AddSingleton<IHostnameSynchronizer, DefaultHostnameSynchronizer>();
builder.Services.AddSingleton<ICache, KubernetesApiCache>();
builder.Services.AddSingleton<IDnsHost, DefaultDnsHost>();
builder.Services.AddSingleton<IDateTimeProvider, DefaultDateTimeProvider>();
builder.Services.AddSingleton<IRandom, DefaultRandom>();
builder.Services.AddSingleton<IDnsServer, DnsServer>();
builder.Services.AddSingleton<Vecc.Dns.ILogger, DefaultDnsLogging>();
builder.Services.AddSingleton<IDnsResolver>(sp => sp.GetRequiredService<DefaultDnsResolver>());
builder.Services.AddSingleton<KubernetesQueue>();
builder.Services.AddSingleton<IQueue>((s) => s.GetRequiredService<KubernetesQueue>());

builder.Services.AddScoped<ApiAuthenticationHandler>();
builder.Services.AddSingleton<ApiAuthenticationHasher>();
builder.Services.AddSingleton<DnsServerOptions>(sp => sp.GetRequiredService<IOptions<DnsServerOptions>>().Value);
builder.Services.AddHttpClient();

builder.Services.AddAuthentication(ApiAuthenticationHandlerOptions.DefaultScheme)
    .AddScheme<ApiAuthenticationHandlerOptions, ApiAuthenticationHandler>(ApiAuthenticationHandlerOptions.DefaultScheme, null);

foreach (var peer in options.Peers)
{
    builder.Services.AddHttpClient(peer.Url, client =>
    {
        client.BaseAddress = new Uri(peer.Url);
        client.DefaultRequestHeaders.Add("X-Api-Key", peer.Key);
    });
}

var app = builder.Build();

app.UseWhen(context => !context.Request.Path.StartsWithSegments("/Healthz"), appBuilder => appBuilder.UseSerilogRequestLogging());
app.UseSwagger();
app.UseSwaggerUI();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

var logger = app.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Starting");
logger.LogInformation("Configured Options {@options} {@dnsServerOptions}", options, dnsOptions);

var processTasks = new List<Task>();

// watches the cluster caches and updates the host cache, also expires old cluster caches
if (args.Contains(OperatorFlag))
{
    logger.LogInformation("Running the operator");

    var hostnameSynchronizer = app.Services.GetRequiredService<IHostnameSynchronizer>();

    processTasks.Add(Task.Run(async () =>
    {
        logger.LogInformation("Starting the operator leader watcher");
        var leaderStateChanged = app.Services.GetRequiredService<OperatorLeader>();
        var lifecycle = app.Lifetime;

        while (!lifecycle.ApplicationStopping.IsCancellationRequested)
        {
            await Task.Yield();
            await Task.Delay(1000);
        };
    }).ContinueWith(_ => logger.LogInformation("Operator leader watcher stopped")));

    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Starting cluster heartbeat");
        return hostnameSynchronizer.ClusterHeartbeatAsync().ContinueWith(_ =>
        {
            logger.LogInformation("Cluster heartbeat stopped");
        });
    }));

    if (options.PerioidRefreshInterval <= 0)
    {
        logger.LogInformation("Perioid refresh interval is {interval} which is <= 0, disabling periodic refresher.", options.PerioidRefreshInterval);
    }
    else
    {
        processTasks.Add(Task.Run(async () =>
        {
            logger.LogInformation("Starting the periodic refresher");
            var lifecycle = app.Lifetime;
            var leaderStatus = app.Services.GetRequiredService<LeaderStatus>();

            while (!lifecycle.ApplicationStopping.IsCancellationRequested)
            {
                await Task.Yield();
                await Task.Delay(options.PerioidRefreshInterval * 1000);
                using var scope = logger.BeginScope(new { PeriodicRefreshId = Guid.NewGuid() });

                if (leaderStatus.IsLeader)
                {
                    logger.LogInformation("Initiating periodic refresh");
                    try
                    {
                        await hostnameSynchronizer.SynchronizeLocalClusterAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error during periodic refresh");
                    }
                }
                else
                {
                    logger.LogTrace("Not the leader, skipping periodic refresh");
                }
            };
        }).ContinueWith(_ => logger.LogInformation("Periodic refresher stopped")));
    }

    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Running API Server for health checks");
        return app.RunAsync().ContinueWith(_ => logger.LogInformation("API Server stopped"));
    }));
}

//watches cluster events and keeps the local cluster config in sync and sends updates to other nodes
else if (args.Contains(OrchestratorFlag))
{
    logger.LogInformation("Running the orchestrator");

    processTasks.Add(Task.Run(() =>
    {
        var hostnameSynchronizer = app.Services.GetRequiredService<IHostnameSynchronizer>();
        logger.LogInformation("Starting cluster heartbeat watcher");
        return hostnameSynchronizer.WatchClusterHeartbeatsAsync().ContinueWith(_ => logger.LogInformation("Cluster heartbeat watcher stopped"));
    }));

    processTasks.Add(Task.Run(async () =>
    {
        logger.LogInformation("Starting the orchestrator leader watcher");
        var leaderStateChanged = app.Services.GetRequiredService<OrchestratorLeader>();
        while (true)
        {
            await Task.Yield();
            await Task.Delay(1000);
        }
    }).ContinueWith(_ => logger.LogInformation("Orchestrator leader watcher stopped")));


    if (options.PerioidRefreshInterval <= 0)
    {
        logger.LogInformation("Perioid refresh interval is {interval} which is <= 0, disabling periodic refresher.", options.PerioidRefreshInterval);
    }
    else
    {
        processTasks.Add(Task.Run(async () =>
        {
            logger.LogInformation("Starting the periodic refresher");
            var lifecycle = app.Lifetime;
            var leaderStatus = app.Services.GetRequiredService<LeaderStatus>();
            var cache = app.Services.GetRequiredService<ICache>();

            while (!lifecycle.ApplicationStopping.IsCancellationRequested)
            {
                await Task.Yield();
                await Task.Delay(options.PerioidRefreshInterval * 1000);
                using var scope = logger.BeginScope(new { PeriodicRefreshId = Guid.NewGuid() });

                if (leaderStatus.IsLeader)
                {
                    logger.LogInformation("Initiating periodic refresh");
                    try
                    {
                        await cache.SynchronizeCachesAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error during periodic refresh");
                    }
                }
                else
                {
                    logger.LogTrace("Not the leader, skipping periodic refresh");
                }
            };
        }).ContinueWith(_ => logger.LogInformation("Periodic refresher stopped")));
    }

    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Running API Server for health checks");
        return app.RunAsync().ContinueWith(_ => logger.LogInformation("API Server stopped"));
    }));
}

//starts the dns server to respond to dns queries for the respective hosts
else if (args.Contains(DnsServerFlag))
{
    logger.LogInformation("Running the dns server");
    var dnsHost = app.Services.GetRequiredService<IDnsHost>();
    var dnsResolver = app.Services.GetRequiredService<DefaultDnsResolver>();
    await dnsResolver.InitializeAsync();
    var queue = app.Services.GetRequiredService<IQueue>();

    queue.OnHostChangedAsync = dnsResolver.OnHostChangedAsync;

    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Starting the dns server");
        return dnsHost.StartAsync().ContinueWith(_ => logger.LogInformation("DNS Server stopped"));
    }));

    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Running API Server for health checks");
        return app.RunAsync().ContinueWith(_ => logger.LogInformation("API Server stopped"));
    }));
}

//starts the api server
else if (args.Contains(FrontEndFlag))
{
    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Running API Server");
        return app.RunAsync().ContinueWith(_ => logger.LogInformation("API Server stopped"));
    }));
}

logger.LogInformation("Waiting on process tasks");

await Task.WhenAny(processTasks);

logger.LogInformation("Terminated");
