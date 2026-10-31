using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;
using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
BenchmarkTopology.Add(builder);
await using var application = builder.Build();
await application.RunAsync();
