using System.Collections.Immutable;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Starts only the selected real database with the production AppHost resource composer.</summary>
internal sealed class NativeDatabaseFlowFixture(DistributedApplication application, string root, string target) : IAsyncDisposable
{
    private const string HttpEndpoint = "http";
    private const string Surreal = "SurrealDB";
    private const string SurrealNode = "isolated-surrealdb";
    private const string HelixNode = "isolated-helixdb";
    private const string PasswordParameter = "isolated-surrealdb-password";
    private const string Basic = "Basic";
    private const string User = "root:";
    private const string ComparisonRunner = "comparisons";
    private static TimeSpan Deadline => NativeExecutionPolicyFixture.Harness().Value.NativeDatabaseFlowTimeout;

    internal static IOptions<NativeComparisonExecutionOptions> ExecutionOptions => NativeExecutionPolicyFixture.Read();
    internal string Image => target == Surreal ? "docker.io/surrealdb/surrealdb:v3.2.4@" + BenchmarkResources.SurrealDbDigest : "ghcr.io/helixdb/helixdb:v0.0.10@" + BenchmarkResources.HelixDbDigest;

    internal static async Task<NativeDatabaseFlowFixture> CreateAsync(string target, CancellationToken token)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-native-flow-" + Guid.NewGuid().ToString("N"));
        try
        {
            return await StartNativeApplicationAsync(target, root, token);
        }
        catch (Exception failure)
        {
            await CleanupNativeFlowAsync(null, root, failure);
            throw;
        }
    }

    private static async Task<DistributedApplication> BuildNativeApplicationAsync(string target, string root, CancellationToken token)
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(
            ["--Benchmarks:Enabled=true", "--" + ComparisonWorkerSelection.TargetSetting + "=" + target,
                "--" + ComparisonWorkerSelection.NodeCountSetting + "=1", "--" + ComparisonWorkerSelection.ScenarioSetting + "=" + Scenario.PointRead,
                "--" + ComparisonWorkerSelection.ProfileSetting + "=" + IsolatedComparisonContract.Current.Profile,
                "--Benchmarks:DataRoot=" + root, "--Benchmarks:Output=" + Path.Combine(root, "reports")], token);
        try
        {
            var runner = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == ComparisonRunner);
            builder.Resources.Remove(runner);
            return await builder.BuildAsync(token);
        }
        catch (Exception failure)
        {
            await CleanupNativeFlowAsync(builder, null, failure);
            throw;
        }
    }

    private static async Task<NativeDatabaseFlowFixture> StartNativeApplicationAsync(string target, string root, CancellationToken token)
    {
        var app = await BuildNativeApplicationAsync(target, root, token);
        try
        {
            await app.StartAsync(token);
            await app.ResourceNotifications.WaitForResourceHealthyAsync(target == Surreal ? SurrealNode : HelixNode, token).WaitAsync(Deadline, TimeProvider.System, token);
            return new(app, root, target);
        }
        catch (Exception failure)
        {
            await CleanupNativeFlowAsync(app, null, failure);
            throw;
        }
    }
    internal async Task<HttpClient> ClientAsync(CancellationToken token)
    {
        var client = application.CreateHttpClient(target == Surreal ? SurrealNode : HelixNode, HttpEndpoint);
        client.Timeout = Deadline;
        if (target == Surreal)
        {
            var password = await application.Services.GetRequiredService<DistributedApplicationModel>().Resources
                .OfType<ParameterResource>().Single(parameter => parameter.Name == PasswordParameter).GetValueAsync(token);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(Basic, Convert.ToBase64String(Encoding.UTF8.GetBytes(User + password)));
        }
        return client;
    }
    public async ValueTask DisposeAsync() => await CleanupNativeFlowAsync(application, root, null);

    private static async Task CleanupNativeFlowAsync(IAsyncDisposable? owner, string? directory, Exception? primary)
    {
        var failures = new List<Exception>();
        if (owner is not null)
        {
            await IsolatedNativeTeardownNativeSupport.CollectFailureAsync(() => owner.DisposeAsync().AsTask(), failures);
        }
        await IsolatedNativeTeardownNativeSupport.CollectFailureAsync(() =>
        {
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
            return Task.CompletedTask;
        }, failures);
        if (OpenLoopFailure.Combine(primary, failures.ToImmutableArray()) is { } failure)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
