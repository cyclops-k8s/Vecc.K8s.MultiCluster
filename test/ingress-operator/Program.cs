using k8s.Models;
using KubeOps.Operator;
using Vecc.IngressOperator;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKubernetesOperator(c => {
        c.AutoAttachFinalizers = false;
        c.AutoDetachFinalizers = false;
        c.LeaderElectionType = KubeOps.Abstractions.Builder.LeaderElectionType.None;
    })
    .AddController<IngressModifier, V1Ingress>();

var app = builder.Build();

await app.RunAsync();
