using System.Net.Http.Headers;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
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
    private static TimeSpan Deadline => NativeExecutionPolicyFixture.Harness().Value.NativeDatabaseFlowTimeout;

    internal static IOptions<NativeComparisonExecutionOptions> ExecutionOptions => NativeExecutionPolicyFixture.Read();
    internal string Image => target == Surreal ? "docker.io/surrealdb/surrealdb:v3.2.4@" + BenchmarkResources.SurrealDbDigest : "ghcr.io/helixdb/helixdb:v0.0.10@" + BenchmarkResources.HelixDbDigest;

    internal static async Task<NativeDatabaseFlowFixture> CreateAsync(string target, CancellationToken token)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-native-flow-" + Guid.NewGuid().ToString("N"));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(["--KeyLoadTests:Suite=comparison"], token);
        foreach (var runner in builder.Resources.OfType<ExecutableResource>().ToArray())
        {
            builder.Resources.Remove(runner);
        }

        var unusedRunner = builder.AddContainer("native-flow-anchor", "mcr.microsoft.com/dotnet/runtime", "10.0");
        var selection = new ComparisonWorkerSelection(target, 1, Scenario.PointRead, IsolatedComparisonContract.Current.Profile);
        var context = new IsolatedResourceContext(builder, selection, unusedRunner, root);
        if (target == Surreal)
        {
            IsolatedSurrealDbResources.Add(context);
        }
        else
        {
            IsolatedHelixDbResources.Add(context);
        }

        builder.Resources.Remove(unusedRunner.Resource);
        var app = await builder.BuildAsync(token);
        try
        {
            await app.StartAsync(token);
            await app.ResourceNotifications.WaitForResourceHealthyAsync(target == Surreal ? SurrealNode : HelixNode, token).WaitAsync(Deadline, token);
            return new(app, root, target);
        }
        catch (Exception)
        {
            await app.DisposeAsync();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

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
    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
