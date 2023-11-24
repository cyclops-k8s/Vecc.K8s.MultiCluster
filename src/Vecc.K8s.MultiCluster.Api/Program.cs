using Destructurama;
using KubeOps.KubernetesClient;
using KubeOps.Operator;
using KubeOps.Operator.Leadership;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Data;
using StackExchange.Redis;
using StackExchange.Redis.Maintenance;
using System.Net;
using System.Net.NetworkInformation;
using Vecc.Dns.Server;
using Vecc.K8s.MultiCluster.Api.Services;
using Vecc.K8s.MultiCluster.Api.Services.Authentication;
using Vecc.K8s.MultiCluster.Api.Services.Default;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.logging.json");

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

if (args.Any(arg => arg == "--orchestrator"))
{
    builder.Services.AddKubernetesOperator((operatorSettings) =>
    {
        operatorSettings.EnableLeaderElection = true;
        operatorSettings.OnlyWatchEventsWhenLeader = true;
    });
    builder.Services.AddSingleton<DefaultLeaderStateChangeObserver>();
}
else
{
    builder.Services.AddSingleton<IKubernetesClient, KubernetesClient>();
}
builder.Services.AddSingleton<LeaderStatus>();
builder.Services.AddSingleton<DefaultLeaderStateChangeObserver>();
builder.Services.AddSingleton<DefaultDnsResolver>();
builder.Services.AddSingleton<IIngressManager, DefaultIngressManager>();
builder.Services.AddSingleton<INamespaceManager, DefaultNamespaceManager>();
builder.Services.AddSingleton<IServiceManager, DefaultServiceManager>();
builder.Services.AddSingleton<IHostnameSynchronizer, DefaultHostnameSynchronizer>();
builder.Services.AddSingleton<ICache, RedisCache>();
builder.Services.AddSingleton<IDnsHost, DefaultDnsHost>();
builder.Services.AddSingleton<IDateTimeProvider, DefaultDateTimeProvider>();
builder.Services.AddSingleton<IRandom, DefaultRandom>();
builder.Services.AddSingleton<IDnsServer, DnsServer>();
builder.Services.AddSingleton<Vecc.Dns.ILogger, DefaultDnsLogging>();
builder.Services.AddSingleton<IDnsResolver>(sp => sp.GetRequiredService<DefaultDnsResolver>());
builder.Services.AddScoped<ApiAuthenticationHandler>();
builder.Services.AddSingleton<ApiAuthenticationHasher>();
builder.Services.AddSingleton<IQueue, RedisQueue>();
builder.Services.AddSingleton<DnsServerOptions>(sp => sp.GetRequiredService<IOptions<DnsServerOptions>>().Value);
builder.Services.AddHttpClient();
builder.Services.Configure<DnsServerOptions>(builder.Configuration.GetSection("DnsServer"));
builder.Services.Configure<ApiAuthenticationHandlerOptions>(builder.Configuration.GetSection("Authentication"));
builder.Services.Configure<MultiClusterOptions>(builder.Configuration);

builder.Services.AddAuthentication(ApiAuthenticationHandlerOptions.DefaultScheme)
    .AddScheme<ApiAuthenticationHandlerOptions, ApiAuthenticationHandler>(ApiAuthenticationHandlerOptions.DefaultScheme, null);

var options = new MultiClusterOptions();
var dnsOptions = new DnsServerOptions();
builder.Configuration.Bind(options);
builder.Configuration.GetSection("DnsServer").Bind(dnsOptions);

foreach (var peer in options.Peers)
{
    builder.Services.AddHttpClient(peer.Url, client =>
    {
        client.BaseAddress = new Uri(peer.Url);
        client.DefaultRequestHeaders.Add("X-Api-Key", peer.Key);
    });
}
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Redis");
    ConfigurationOptions configurationOptions;
    var logger = sp.GetRequiredService<ILogger<Program>>();
    if (connectionString == null && builder.Environment.IsDevelopment())
    {
        var endpoints = new EndPointCollection { new IPEndPoint(IPAddress.Loopback, 6379) };
        configurationOptions = new ConfigurationOptions { EndPoints = endpoints };
    }
    else
    {
        if (connectionString == null)
        {
            logger.LogError("Redis connection string must be set.");
            throw new Exception("Redis connection string must be set.");
        }

        configurationOptions = ConfigurationOptions.Parse(connectionString);
        configurationOptions.ReconnectRetryPolicy = new LinearRetry(int.MaxValue);
    }

    var multiplexer = ConnectionMultiplexer.Connect(configurationOptions);

    multiplexer.ConfigurationChanged += (sender, e) => { Multiplexer_ConfigurationChanged(logger, sender as IConnectionMultiplexer, sp, e); };
    multiplexer.ConfigurationChangedBroadcast += (sender, e) => { Multiplexer_ConfigurationChangedBroadcast(logger, sender as IConnectionMultiplexer, sp, e); };
    multiplexer.ConnectionFailed += (sender, e) => { Multiplexer_ConnectionFailed(logger, sender as IConnectionMultiplexer, sp, e); };
    multiplexer.ConnectionRestored += (sender, e) => { Multiplexer_ConnectionRestored(logger, sender as IConnectionMultiplexer, sp, e); };
    multiplexer.ErrorMessage += (sender, e) => { Multiplexer_ErrorMessage(logger, sender as IConnectionMultiplexer, sp, e); };
    multiplexer.HashSlotMoved += (sender, e) => { Multiplexer_HashSlotMoved(logger, sender as IConnectionMultiplexer, sp, e); };
    multiplexer.InternalError += (sender, e) => { Multiplexer_InternalError(logger, sender as IConnectionMultiplexer, sp, e); };
    multiplexer.ServerMaintenanceEvent += (sender, e) => { Multiplexer_ServerMaintenanceEvent(logger, sender as IConnectionMultiplexer, sp, e); };

    return multiplexer;
});

builder.Services.AddSingleton<IDatabase>(sp =>
{
    var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
    var database = multiplexer.GetDatabase();
    return database;
});

builder.Services.AddSingleton<ISubscriber>(sp =>
{
    var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
    var subscriber = multiplexer.GetSubscriber();
    return subscriber;
});

var app = builder.Build();

app.UseWhen(context => !context.Request.Path.StartsWithSegments("/Healthz"), appBuilder => appBuilder.UseSerilogRequestLogging());
app.UseSwagger();
app.UseSwaggerUI();

var controllerPaths = new string[] { "/Heartbeat", "/Authentication", "/Host", "/Healthz", "/swagger" };
if (args.Contains("--orchestrator"))
{
    app.UseWhen(context => !controllerPaths.Any(path => context.Request.Path.StartsWithSegments(path)), appBuilder => appBuilder.UseKubernetesOperator());
    app.UseKubernetesOperator();
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

var logger = app.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Starting");
logger.LogInformation("Configured Options {@options} {@dnsServerOptions}", options, dnsOptions);

var processTasks = new List<Task>();

//watches cluster events and keeps the local cluster config in sync and sends updates to other nodes
//also keeps track of remote clusters and whether they are up or not
if (args.Contains("--orchestrator"))
{
    logger.LogInformation("Running the orchestrator");
    var defaultLeaderStateChangeObserver = app.Services.GetRequiredService<DefaultLeaderStateChangeObserver>();
    app.Services.GetRequiredService<ILeaderElection>().LeadershipChange.Subscribe(defaultLeaderStateChangeObserver);
    var hostnameSynchronizer = app.Services.GetRequiredService<IHostnameSynchronizer>();

    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Starting the operator");
        return app.RunOperatorAsync(args.Where(a => a != "--dns-server" && a != "--front-end" && a != "--orchestrator").ToArray())
            .ContinueWith(_ => logger.LogInformation("Operator stopped"));
    }));

    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Starting cluster heartbeat");
        return hostnameSynchronizer.ClusterHeartbeatAsync().ContinueWith(_ =>
        {
            logger.LogInformation("Cluster heartbeat stopped");
        });
    }));

    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Starting cluster heartbeat watcher");
        return hostnameSynchronizer.WatchClusterHeartbeatsAsync().ContinueWith(_ => logger.LogInformation("Cluster heartbeat watcher stopped"));
    }));
}

//starts the dns server to respond to dns queries for the respective hosts
if (args.Contains("--dns-server"))
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
}

//starts the api server
if (!args.Contains("--orchestrator") && (args.Contains("--dns-server") || args.Contains("--front-end")))
{
    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Running API Server");
        return app.RunAsync().ContinueWith(_ => logger.LogInformation("API Server stopped"));
    }));
}

logger.LogInformation("Waiting on process tasks");

await Task.WhenAll(processTasks);

logger.LogInformation("Terminated");

void Multiplexer_ServerMaintenanceEvent(ILogger<Program> logger, IConnectionMultiplexer? sender, IServiceProvider serviceProvider, ServerMaintenanceEvent e)
{
    logger.LogError("Redis server maintenance: {@event}", e);
}

void Multiplexer_InternalError(ILogger<Program> logger, IConnectionMultiplexer? sender, IServiceProvider serviceProvider, InternalErrorEventArgs e)
{
    logger.LogError("Redis internal error: {@event}", e);
}

void Multiplexer_HashSlotMoved(ILogger<Program> logger, IConnectionMultiplexer? sender, IServiceProvider serviceProvider, HashSlotMovedEventArgs e)
{
    logger.LogDebug("Redis hash slot moved: {@event}", e);
}

void Multiplexer_ErrorMessage(ILogger<Program> logger, IConnectionMultiplexer? sender, IServiceProvider serviceProvider, RedisErrorEventArgs e)
{
    logger.LogError("Redis error: {@event}", e);
}

void Multiplexer_ConfigurationChangedBroadcast(ILogger<Program> logger, IConnectionMultiplexer? sender, IServiceProvider serviceProvider, EndPointEventArgs e)
{
    logger.LogInformation("Redis configuration changed broadcast: {@event}", e);
}

void Multiplexer_ConfigurationChanged(ILogger<Program> logger, IConnectionMultiplexer? sender, IServiceProvider serviceProvider, EndPointEventArgs e)
{
    logger.LogInformation("Redis configuration changed: {@event}", e);
}

async void Multiplexer_ConnectionRestored(ILogger<Program> logger, IConnectionMultiplexer? sender, IServiceProvider serviceProvider, ConnectionFailedEventArgs e)
{
    logger.LogInformation("Redis connection restored: {@event}", e);
    if (e.ConnectionType == ConnectionType.Subscription)
    {
        try
        {
            logger.LogInformation("Redis connection is a subscription, it is restored, initiating dns resolver resync");
            var dnsResolver = serviceProvider.GetRequiredService<DefaultDnsResolver>();
            await dnsResolver.InitializeAsync();
            logger.LogInformation("Resync complete");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error handling connection restored event.");
        }
    }
}

void Multiplexer_ConnectionFailed(ILogger<Program> logger, IConnectionMultiplexer? sender, IServiceProvider serviceProvider, ConnectionFailedEventArgs e)
{
    logger.LogError("Redis connection failed: {@event}", e);
}