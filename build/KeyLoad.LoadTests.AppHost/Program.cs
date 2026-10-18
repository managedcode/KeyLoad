using KeyLoad;
using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;
using Microsoft.Extensions.DependencyInjection;

_ = SerializationExecutionRegistration.Process.Value;
var builder = DistributedApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
IsolatedBenchmarkResources.Add(builder);
await using var application = builder.Build();
await application.RunAsync();
