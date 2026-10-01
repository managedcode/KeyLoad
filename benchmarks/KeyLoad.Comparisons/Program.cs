using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
var options = ComparisonOptions.Read(configuration);
var runId = Guid.NewGuid().ToString("N");
string Required(string name) => configuration[name] ?? throw new InvalidOperationException("Missing benchmark setting: " + name);
string Image(string name) => Required("Benchmarks:Images:" + name);
var keyLoad = new HttpClient { BaseAddress = new Uri(Required("Benchmarks:KeyLoadEndpoint")), Timeout = Timeout.InfiniteTimeSpan };
var qdrant = new HttpClient { BaseAddress = new Uri(Required("Benchmarks:QdrantEndpoint")), Timeout = Timeout.InfiniteTimeSpan };
qdrant.DefaultRequestHeaders.Add("api-key", Required("Benchmarks:QdrantApiKey"));
IComparisonTarget[] targets = [new KeyLoadTarget(keyLoad, Required("Benchmarks:AdminKey"), runId),
    new PostgresTarget(Required("ConnectionStrings:benchmark-postgres"), runId, Image("Postgres")),
    new QdrantTarget(qdrant, runId, Image("Qdrant")),
    new RabbitTarget(Required("ConnectionStrings:benchmark-rabbit"), runId, Image("Rabbit")),
    new RedisTarget(Required("ConnectionStrings:benchmark-redis"), runId, Image("Redis"))];
using var lifetime = new CancellationTokenSource(TimeSpan.FromHours(2));
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; lifetime.Cancel(); };
try
{
    var report = await new ComparisonRunner(options, Console.WriteLine).RunAsync(targets, configuration["Benchmarks:SourceRevision"], lifetime.Token, Required("Benchmarks:Storage"));
    var directory = Path.GetFullPath(Required("Benchmarks:Output"));
    await ReportWriter.WriteAsync(report, directory, lifetime.Token);
    Console.WriteLine(ReportWriter.Markdown(report));
    Console.WriteLine("Reports: " + directory);
    return report.Cases.Any(item => item.Status == "failed") ? 1 : 0;
}
finally
{
    foreach (var target in targets)
    {
        try { await target.DisposeAsync(); }
        catch (Exception error) { Console.Error.WriteLine($"{target.Profile.Name} cleanup failed: {error.GetType().Name}"); }
    }
}
