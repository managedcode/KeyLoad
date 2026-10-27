using System.Net.Http.Headers;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.ComparisonHost.BenchmarkOrchestration;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

internal static class AdditionalDatabaseResources
{
    private const string DirectoryName = "database-adapters", Format = "N", Profile = "session";
    private const string Enabled = "--Benchmarks:Enabled=true", Session = "--Benchmarks:Session=true";
    private const string Evidence = "--Benchmarks:EvidenceProfile=" + Profile, Node = "--Benchmarks:NodeCount=";
    private const string Root = "--Benchmarks:DataRoot=", Output = "--Benchmarks:Output=", Identity = "--Benchmarks:SessionId=";
    private const string Target = "--Benchmarks:Target=";
    private const string Invalid = "AdditionalDatabaseSelectionRejected";

    internal static async Task RunAsync(string target, Func<IComparisonTarget, CancellationToken, Task> verify, CancellationToken token, int nodes = 1)
    {
        if (target is not (BenchmarkAdditionalTargets.Timescale or BenchmarkAdditionalTargets.SqlServer
            or BenchmarkAdditionalTargets.MySql or BenchmarkAdditionalTargets.ClickHouse or BenchmarkAdditionalTargets.InfluxDb
            or NativeDatabaseTests.Redis or NativeDatabaseTests.OpenSearch or NativeDatabaseTests.HelixDb or NativeDatabaseTests.MongoDb))
        {
            throw new InvalidOperationException(Invalid);
        }
        var identity = Guid.NewGuid().ToString(Format);
        var root = Path.Combine(AppContext.BaseDirectory, DirectoryName, identity);
        Directory.CreateDirectory(root);
        string[] arguments = [Enabled, Session, Evidence, Node + nodes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Root + root, Output + root, Identity + identity, Target + target];
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_LoadTests_AppHost>(arguments, token);
        await using var application = await builder.BuildAsync(token);
        var failures = new List<Exception>();
        var resources = application.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().Select(resource => resource.Name).ToArray();
        await using var capture = new ComparisonTestLogCapture(application, ExecutionPolicyFixture.Harness(), resources);
        var observations = new NativeDatabaseResourceObservation(application, root, token);
        try
        {
            await RunNativeAsync(application, target, verify, observations, token);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
        await SettleAsync(observations.CompleteAsync, failures);
        await SettleAsync(capture.StopAsync, failures);
        await SettleAsync(() => capture.WriteResourcesToAsync(root), failures);
        await SettleAsync(() => capture.DisposeAsync().AsTask(), failures);
        await SettleAsync(() => application.StopAsync(CancellationToken.None), failures);
        await SettleAsync(() => application.DisposeAsync().AsTask(), failures);
        if (failures.Count > NoFailures)
        {
            throw new AggregateException(failures);
        }
    }
    private const int NoFailures = 0;

    private static async Task RunNativeAsync(DistributedApplication application, string target,
        Func<IComparisonTarget, CancellationToken, Task> verify, NativeDatabaseResourceObservation observations, CancellationToken token)
    {
        await application.StartAsync(token);
        var settings = await BenchmarkSessionClientRuntime.WaitForReadinessAsync(application, token);
        await observations.VerifyReadyAsync(token);
        var values = await settings.ResolveAsync(token);
        using var configuration = new ConfigurationManager();
        configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, ComparisonExecutionRegistration.ConfigurationPath), optional: false);
        var policy = ComparisonExecutionRegistration.Read(configuration);
        await using var database = Create(target, values, policy, configuration);
        await verify(database, token);
    }

    private const string Image = "Benchmarks__Execution__Image", Connection = "Benchmarks__Execution__ConnectionString";
    private const string Endpoint = "Benchmarks__Execution__Endpoints__0", User = "Benchmarks__Execution__User", Password = "Benchmarks__Execution__Password";
    private const string Basic = "Basic", Separator = ":";

    private static IComparisonTarget Create(string target, IReadOnlyDictionary<string, string> values,
        Microsoft.Extensions.Options.IOptions<ComparisonExecutionOptions> options, IConfiguration configuration)
    {
        var runId = Guid.NewGuid().ToString(Format);
        if (target == NativeDatabaseTests.Redis)
        {
            return NativeDatabaseTargets.Redis(values, runId, options, configuration);
        }
        if (target == NativeDatabaseTests.MongoDb)
        {
            return NativeDatabaseTargets.Mongo(values, runId, options, configuration);
        }
        if (target is BenchmarkAdditionalTargets.Timescale or BenchmarkAdditionalTargets.SqlServer or BenchmarkAdditionalTargets.MySql)
        {
            return RelationalComparisonTargets.Create(target, values[Connection], runId, values[Image], options);
        }
        HttpClient? http = new();
        try
        {
            http.BaseAddress = new(values[Endpoint]);
            http.Timeout = Timeout.InfiniteTimeSpan;
            if (values.TryGetValue(User, out var user))
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(Basic,
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(user + Separator + values[Password])));
            }
            var targetOwner = target switch
            {
                NativeDatabaseTests.OpenSearch => new OpenSearchTarget(http, runId, values[Image], NativeDatabaseTargets.Topology(values),
                    ComparisonExecutionRegistration.ReadLifecycle(configuration), options),
                NativeDatabaseTests.HelixDb => new HelixDbTarget(http, runId, values[Image], options),
                _ => HttpComparisonTargets.Create(target, http, runId, values[Image], options)
            };
            http = null;
            return targetOwner;
        }
        finally
        {
            http?.Dispose();
        }
    }

    private static async Task SettleAsync(Func<Task> operation, List<Exception> failures)
    {
        try
        {
            await operation();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
    }
}
