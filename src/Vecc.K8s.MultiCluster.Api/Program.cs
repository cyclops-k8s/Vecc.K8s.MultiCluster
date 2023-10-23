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

builder.Services.AddKubernetesOperator((operatorSettings) =>
{
    operatorSettings.EnableLeaderElection = false;
    operatorSettings.OnlyWatchEventsWhenLeader = true;
});

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
        configurationOptions = new ConfigurationOptions{ EndPoints = endpoints };
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
var app2 = (IApplicationBuilder)app;
app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
var controllerPaths = new string[] { "/Heartbeat", "/Authentication", "/Host" };
app.MapWhen(context => controllerPaths.Any(path => context.Request.Path.StartsWithSegments(path)), appBuilder =>
{
    appBuilder.UseRouting();
    appBuilder.UseAuthentication();
    appBuilder.UseAuthorization();
    appBuilder.UseEndpoints(endpointBuilder => endpointBuilder.MapControllers());
});
app.MapWhen(context => !controllerPaths.Any(path => context.Request.Path.StartsWithSegments(path)), appBuilder => appBuilder.UseKubernetesOperator());

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting");
logger.LogInformation("Configured Options {@options}", options);

var processTasks = new List<Task>();
var addedOperator = false;

//watches cluster events and keeps the local cluster config in sync and sends updates to other nodes
//also keeps track of remote clusters and whether they are up or not
if (args.Contains("--orchestrator"))
{
    logger.LogInformation("Running the orchestrator");
    var defaultLeaderStateChangeObserver = app.Services.GetRequiredService<DefaultLeaderStateChangeObserver>();
    app.Services.GetRequiredService<ILeaderElection>().LeadershipChange.Subscribe(defaultLeaderStateChangeObserver);
    var hostnameSynchronizer = app.Services.GetRequiredService<IHostnameSynchronizer>();

    processTasks.Add(app.RunOperatorAsync(Array.Empty<string>()));
    processTasks.Add(hostnameSynchronizer.ClusterHeartbeatAsync());
    processTasks.Add(hostnameSynchronizer.WatchClusterHeartbeatsAsync());

    addedOperator = true;
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
    processTasks.Add(dnsHost.StartAsync());
    if (!addedOperator)
    {
        processTasks.Add(app.RunOperatorAsync(Array.Empty<string>()));
        addedOperator = true;
    }
}

//starts the api server
if (args.Contains("--front-end"))
{
    logger.LogInformation("Running the front end api");

    if (!addedOperator)
    {
        processTasks.Add(app.RunOperatorAsync(Array.Empty<string>()));
        addedOperator = true;
    }
}

if (!addedOperator)
{
    logger.LogInformation("Running underlying KubeOps");

    processTasks.Add(app.RunOperatorAsync(args));
}

logger.LogInformation("Waiting on process tasks");
await Task.WhenAll(processTasks);
logger.LogInformation("Terminated");


