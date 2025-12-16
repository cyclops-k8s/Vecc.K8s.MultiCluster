using k8s.Models;
using KubeOps.Operator;
using Vecc.IngressOperator;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKubernetesOperator(c => {
        c.AutoAttachFinalizers = false;
        c.AutoDetachFinalizers = false;
        c.LeaderElectionType = KubeOps.Abstractions.Builder.LeaderElectionType.None;
    })
    .AddController<IngressModifier, V1Ingress>()
    .AddController<IngressModifier, V1Service>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("Healthy"));
app.MapGet("/ready", () => Results.Ok("Ready"));

await app.RunAsync();
