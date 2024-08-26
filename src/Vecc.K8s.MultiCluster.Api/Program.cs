using Destructurama;
using k8s.Models;
using KubeOps.KubernetesClient;
using KubeOps.Operator;
using KubeOps.Transpiler;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Reflection;
using Vecc.Dns.Server;
using Vecc.K8s.MultiCluster.Api.Controllers;
using Vecc.K8s.MultiCluster.Api.Models.K8sEntities;
using Vecc.K8s.MultiCluster.Api.Services;
using Vecc.K8s.MultiCluster.Api.Services.Authentication;
using Vecc.K8s.MultiCluster.Api.Services.Default;

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

if (args.Any(arg => arg == "--orchestrator"))
{
    builder.Services.AddKubernetesOperator((operatorSettings) =>
    {
        operatorSettings.EnableLeaderElection = true;
    })
        .AddController<K8sChangedController, V1Ingress>()
        .AddController<K8sChangedController, V1Service>()
        .AddController<K8sChangedController, V1Endpoints>();
}
else if (args.Any(arg => arg == "--dns-server"))
{
    builder.Services.AddKubernetesOperator((c) => { c.Namespace = options.Namespace; })
        .AddController<K8sHostnameCacheController, V1HostnameCache>();

    builder.Services.AddSingleton<IKubernetesClient, KubernetesClient>();
}
else if (args.Any(arg => arg == "--front-end"))
{
    builder.Services.AddSingleton<IKubernetesClient, KubernetesClient>();
}


builder.Services.AddSingleton<LeaderStatus>();
builder.Services.AddSingleton<LeaderStateChanged>();
builder.Services.AddSingleton<DefaultDnsResolver>();
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
builder.Services.AddSingleton<LeaderStateChanged>();
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

//watches cluster events and keeps the local cluster config in sync and sends updates to other nodes
//also keeps track of remote clusters and whether they are up or not
if (args.Contains("--orchestrator"))
{
    logger.LogInformation("Running the orchestrator");

    var hostnameSynchronizer = app.Services.GetRequiredService<IHostnameSynchronizer>();

    processTasks.Add(Task.Run(() =>
    {
        logger.LogInformation("Starting the operator");
        var leaderStateChanged = app.Services.GetRequiredService<LeaderStateChanged>();

        return app.RunAsync()
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
else if (args.Contains("--dns-server"))
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
else if (args.Contains("--front-end"))
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
