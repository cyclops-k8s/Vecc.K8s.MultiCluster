using KubeOps.Operator;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKubernetesOperator(c => c.EnableLeaderElection = false);

var app = builder.Build();
app.UseKubernetesOperator();

await app.RunOperatorAsync(args);
