using KubeOps.Operator;
using KubeOps.Operator.Leadership;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Serilog;
using StackExchange.Redis;
using System.Net;
using Vecc.Dns.Server;
using Vecc.K8s.MultiCluster.Api.Services;
using Vecc.K8s.MultiCluster.Api.Services.Authentication;
using Vecc.K8s.MultiCluster.Api.Services.Default;
using Destructurama;
using k8s.Models;
using System.Diagnostics.Eventing.Reader;
using Microsoft.Extensions.DependencyInjection.Extensions;
using k8s;
using KubeOps.KubernetesClient;

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
builder.Configuration.Bind(options);
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

    if (connectionString == null && builder.Environment.IsDevelopment())
    {
        var endpoints = new EndPointCollection { new IPEndPoint(IPAddress.Loopback, 6379) };
        configurationOptions = new ConfigurationOptions { EndPoints = endpoints };
    }
    else
    {
        if (connectionString == null)
        {
            var logger = sp.GetRequiredService<ILogger<Program>>();
            throw new Exception("Redis connection string must be set.");
        }
        configurationOptions = ConfigurationOptions.Parse(connectionString);
    }

    var multiplexer = ConnectionMultiplexer.Connect(configurationOptions);
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
app.MapWhen(context => !context.Request.Path.StartsWithSegments("/Healthz"), appBuilder => appBuilder.UseSerilogRequestLogging());
app.UseSwagger();
app.UseSwaggerUI();
var controllerPaths = new string[] { "/Heartbeat", "/Authentication", "/Host", "/Healthz" };
foreach (var path in controllerPaths)
{
    app.MapWhen(context => context.Request.Path.StartsWithSegments(path), appBuilder =>
    {
        appBuilder.UseRouting();
        appBuilder.UseAuthentication();
        appBuilder.UseAuthorization();
        appBuilder.UseEndpoints(endpointBuilder => endpointBuilder.MapControllers());
    });
}
if (args.Any(arg => arg == "--orchestrator"))
{
    app.MapWhen(context => !controllerPaths.Any(path => context.Request.Path.StartsWithSegments(path)), appBuilder => appBuilder.UseKubernetesOperator());
}

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting");
logger.LogInformation("Configured Options {@options}", options);

var processTasks = new List<Task>();

//watches cluster events and keeps the local cluster config in sync and sends updates to other nodes
//also keeps track of remote clusters and whether they are up or not
if (args.Contains("--orchestrator"))
{
    logger.LogInformation("Running the orchestrator");
    var defaultLeaderStateChangeObserver = app.Services.GetRequiredService<DefaultLeaderStateChangeObserver>();
    app.Services.GetRequiredService<ILeaderElection>().LeadershipChange.Subscribe(defaultLeaderStateChangeObserver);
    var hostnameSynchronizer = app.Services.GetRequiredService<IHostnameSynchronizer>();

    processTasks.Add(new Task(async () =>
    {
        logger.LogInformation("Starting the operator");
        await app.RunOperatorAsync(args.Where(a => a != "--dns-server" && a != "--front-end" && a != "--orchestrator").ToArray());
        logger.LogInformation("Operator stopped");
    }, TaskCreationOptions.LongRunning));

    processTasks[0].Start();

    processTasks.Add(new Task(async () =>
    {
        logger.LogInformation("Starting cluster heartbeat");
        await hostnameSynchronizer.ClusterHeartbeatAsync();
        logger.LogInformation("Cluster heartbeat stopped");
    }, TaskCreationOptions.LongRunning));

    processTasks.Add(new Task(async () =>
    {
        logger.LogInformation("Starting cluster heartbeat watcher");
        await hostnameSynchronizer.WatchClusterHeartbeatsAsync();
        logger.LogInformation("Cluster heartbeat watcher stopped");
    }, TaskCreationOptions.LongRunning));
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

    logger.LogInformation("Starting the dns server");
    processTasks.Add(new Task(async () => await dnsHost.StartAsync(), TaskCreationOptions.LongRunning));
}

//starts the api server
if (!args.Contains("--orchestrator") && (args.Contains("--dns-server") || args.Contains("--front-end")))
{
    logger.LogInformation("Running the front end api");
    processTasks.Add(Task.Run(async () =>
    {
        await app.RunAsync();
    }));
}

//if (!addedOperator)
//{
//    logger.LogInformation("Running underlying KubeOps");

//}

logger.LogInformation("Waiting on process tasks");
//await app.RunOperatorAsync(args.Where(a => a != "--dns-server" && a != "--front-end" && a != "--orchestrator").ToArray());
await Task.WhenAll(processTasks);

logger.LogInformation("Terminated");



