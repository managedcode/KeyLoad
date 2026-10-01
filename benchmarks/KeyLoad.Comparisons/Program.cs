using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text;

var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
var options = ComparisonOptions.Read(configuration);
var runId = Guid.NewGuid().ToString("N");
string Required(string name) => configuration[name] ?? throw new InvalidOperationException("Missing benchmark setting: " + name);
string Image(string name) => Required("Benchmarks:Images:" + name);
var keyLoad = new HttpClient { BaseAddress = new Uri(Required("Benchmarks:KeyLoadEndpoint")), Timeout = Timeout.InfiniteTimeSpan };
var qdrant = new HttpClient { BaseAddress = new Uri(Required("Benchmarks:QdrantEndpoint")), Timeout = Timeout.InfiniteTimeSpan };
qdrant.DefaultRequestHeaders.Add("api-key", Required("Benchmarks:QdrantApiKey"));
var neo4j = new HttpClient { BaseAddress = new Uri(Required("Benchmarks:Neo4jEndpoint")), Timeout = Timeout.InfiniteTimeSpan };
neo4j.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("neo4j:" + Required("Benchmarks:Neo4jPassword"))));
IComparisonTarget[] targets = [new KeyLoadTarget(keyLoad, Required("Benchmarks:AdminKey"), runId),
    new PostgresTarget(Required("ConnectionStrings:benchmark-postgres"), runId, Image("Postgres")),
    new QdrantTarget(qdrant, runId, Image("Qdrant")),
    new RabbitTarget(Required("ConnectionStrings:benchmark-rabbit"), runId, Image("Rabbit")),
    new RedisTarget(Required("ConnectionStrings:benchmark-redis"), runId, Image("Redis")),
    new Neo4jTarget(neo4j, runId, Image("Neo4j"))];
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
