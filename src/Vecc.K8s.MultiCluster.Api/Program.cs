using KubeOps.Operator;
using KubeOps.Operator.Leadership;
using Vecc.K8s.MultiCluster.Api.Services;
using Vecc.K8s.MultiCluster.Api.Services.Default;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddKubernetesOperator((operatorSettings) =>
{
    operatorSettings.EnableLeaderElection = false;
    operatorSettings.OnlyWatchEventsWhenLeader = true;
    operatorSettings.OnlyWatchEventsWhenLeader = true;
});
builder.Services.AddSingleton<DefaultLeaderStateChangeObserver>();
builder.Services.AddSingleton<IIngressManager, DefaultIngressManager>();
builder.Services.AddSingleton<INamespaceManager, DefaultNamespaceManager>();
builder.Services.AddSingleton<IServiceManager, DefaultServiceManager>();

var app = builder.Build();

var defaultLeaderStateChangeObserver = app.Services.GetRequiredService<DefaultLeaderStateChangeObserver>();
app.Services.GetRequiredService<ILeaderElection>().LeadershipChange.Subscribe(defaultLeaderStateChangeObserver);

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthorization();

app.MapControllers();
app.UseKubernetesOperator();

await app.RunOperatorAsync(args);
