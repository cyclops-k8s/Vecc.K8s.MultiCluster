using KubeOps.Operator;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddKubernetesOperator((operatorSettings) =>
{
    operatorSettings.EnableLeaderElection = true;
    operatorSettings.OnlyWatchEventsWhenLeader = true;
});
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthorization();

app.MapControllers();
app.UseKubernetesOperator();

app.RunOperatorAsync(args);
