using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

/// <summary>Owns the original collector image prerequisite inside the native TUnit session.</summary>
internal static class NativeCoverageTUnitSession
{
    private const string RunnerName = "tests-rf3";
    private const string ArgumentsEnvironment = "KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS";
    private static DistributedApplication? application;
    private static CancellationTokenSource? outputLifetime;
    private static Task? output;
    private static readonly Dictionary<string, string?> previousEnvironment = new(StringComparer.Ordinal);

    [Before(HookType.TestSession)]
    public static async Task PrepareAsync(CancellationToken cancellationToken)
    {
        var json = Environment.GetEnvironmentVariable(ArgumentsEnvironment);
        if (json is null)
        { return; }
        var arguments = JsonSerializer.Deserialize<string[]>(json)
            ?? throw new InvalidOperationException("Native TUnit coverage preparation selection is missing.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(60), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try
        {
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(arguments, deadline.Token)
                .ConfigureAwait(false);
            var runner = builder.Resources.OfType<ExecutableResource>().Single(resource => resource.Name == RunnerName);
            var configuration = await ExecutionConfigurationBuilder.Create(runner).WithEnvironmentVariablesConfig()
                .BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                    NullLogger.Instance, deadline.Token).ConfigureAwait(false);
            if (configuration.Exception is not null)
            { throw configuration.Exception; }
            builder.Resources.Remove(runner);
            application = await builder.BuildAsync(deadline.Token).ConfigureAwait(false);
            outputLifetime = new();
            output = TestSuiteOutput.ForwardAsync(application, NativeCoverageRf3Prerequisite.ResourceName, outputLifetime.Token);
            var exit = await AspireResourceCompletion.RunToExitAsync(application, NativeCoverageRf3Prerequisite.ResourceName,
                deadline.Token).ConfigureAwait(false);
            if (exit != 0)
            { throw new InvalidOperationException("Original RF3 coverage image preparation failed."); }
            foreach (var (key, value) in configuration.EnvironmentVariables)
            {
                previousEnvironment.Add(key, Environment.GetEnvironmentVariable(key));
                Environment.SetEnvironmentVariable(key, value);
            }
        }
        catch (Exception primary)
        {
            try
            { await CleanupAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    [After(HookType.TestSession)]
    public static async Task CleanupAsync()
    {
        var owned = application;
        application = null;
        var failures = new List<Exception>();
        try
        {
            if (owned is not null)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2), TimeProvider.System);
                await CollectAsync(() => owned.StopAsync(deadline.Token), failures).ConfigureAwait(false);
                if (outputLifetime is not null)
                {
                    await CollectAsync(outputLifetime.CancelAsync, failures).ConfigureAwait(false);
                }
                if (output is not null)
                { await CollectAsync(() => output, failures).ConfigureAwait(false); }
                var imageCleanup = owned.Services.GetRequiredService<NativeCoverageRf3Cleanup>();
                await CollectAsync(() => owned.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
                await CollectAsync(() => imageCleanup.CleanupAsync(deadline.Token), failures).ConfigureAwait(false);
            }
        }
        finally
        {
            outputLifetime?.Dispose();
            outputLifetime = null;
            output = null;
            foreach (var (key, value) in previousEnvironment)
            { Environment.SetEnvironmentVariable(key, value); }
            previousEnvironment.Clear();
        }
        if (failures.Count > 0)
        { throw new AggregateException("Native TUnit coverage prerequisite cleanup failed.", failures); }
    }

    private static Task CollectAsync(Func<Task> action, List<Exception> failures)
        => ServerFailureObserver.ObserveAsync(action, failures);
}
