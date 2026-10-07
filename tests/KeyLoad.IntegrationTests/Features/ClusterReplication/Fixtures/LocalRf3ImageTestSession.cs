using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Owns the actual local image prerequisite for one explicitly selected native membership case.</summary>
internal sealed class LocalRf3ImageTestSession : IAsyncDisposable
{
    private const string RunnerName = "tests-rf3";
    private const string RunnerConfigurationFailure = "The local RF3 image runner configuration is invalid.";
    private const string PreparationFailure = "The local RF3 image preparation did not complete successfully.";
    private const string UnexpectedPreparationResources = "The local RF3 image session contains an unexpected resource.";
    private const string CleanupFailure = "Local RF3 image session cleanup failed.";

    private DistributedApplication? application;
    private IDistributedApplicationTestingBuilder? testingBuilder;
    private LocalRf3ImageCleanup? imageCleanup;
    private CancellationTokenSource? outputLifetime;
    private Task? output;
    private LocalRf3PreparationDiagnostics? preparationDiagnostics;
    private readonly string repositoryRoot;
    private LocalRf3ImageSelection.Selection? PreparedSelection { get; set; }

    private LocalRf3ImageTestSession(string repositoryRoot) => this.repositoryRoot = repositoryRoot;

    internal LocalRf3ImageSelection.Selection Selection => PreparedSelection
        ?? throw new InvalidOperationException(RunnerConfigurationFailure);

    internal static async Task<LocalRf3ImageTestSession?> StartIfSelectedAsync(CancellationToken cancellationToken)
    {
        var arguments = LocalRf3ImageSelection.ReadNativeArgumentsIfSelected();
        if (arguments is null)
        {
            return null;
        }

        var root = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        var session = new LocalRf3ImageTestSession(root);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.testingBuilder = FixtureOwnedImagePreparationComposition.Create(() =>
                session.testingBuilder = DistributedApplicationTestingBuilder.Create(arguments));
            var builder = session.testingBuilder;
            var runner = builder.Resources.OfType<ExecutableResource>().Single(resource => resource.Name == RunnerName);
            var runnerConfiguration = await ExecutionConfigurationBuilder.Create(runner).WithEnvironmentVariablesConfig()
                .BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                    NullLogger.Instance, cancellationToken).ConfigureAwait(false);
            if (runnerConfiguration.Exception is not null)
            {
                throw new InvalidOperationException(RunnerConfigurationFailure, runnerConfiguration.Exception);
            }

            session.PreparedSelection = LocalRf3ImageSelection.FromRunnerConfiguration(runnerConfiguration.EnvironmentVariables
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            await session.PrepareAsync(builder, runner, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch (Exception primary)
        {
            await session.DisposeAfterFailureAsync(primary, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task PrepareAsync(IDistributedApplicationTestingBuilder builder, ExecutableResource runner,
        CancellationToken cancellationToken)
    {
        const int SuccessExitCode = 0;

        builder.Resources.Remove(runner);
        VerifyPreparationResources(builder);
        preparationDiagnostics = new();
        builder.Services.AddSingleton<ILoggerProvider>(_ => new LocalRf3PreparationLogger(preparationDiagnostics));
        application = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        preparationDiagnostics.Attach(application);
        imageCleanup = application.Services.GetRequiredService<LocalRf3ImageCleanup>();
        outputLifetime = new CancellationTokenSource();
        output = TestSuiteOutput.ForwardAsync(application, LocalRf3ImagePrerequisite.ResourceName,
            outputLifetime.Token);
        var exitCode = await AspireResourceCompletion.RunToExitAsync(application,
            LocalRf3ImagePrerequisite.ResourceName, cancellationToken).ConfigureAwait(false);
        if (exitCode != SuccessExitCode)
        {
            throw new InvalidOperationException(PreparationFailure);
        }
        _ = await LocalRf3ImageIdentity.ReadVerifiedAsync(repositoryRoot, Selection, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException(PreparationFailure);
    }

    internal CancellationTokenSource CreateApplicationCleanupCancellation()
    {
        var owned = application ?? throw new InvalidOperationException(PreparationFailure);
        var policy = owned.Services.GetRequiredService<IOptions<TestExecutionOptions>>().Value;
        return new CancellationTokenSource(policy.ApplicationCleanupTimeout,
            owned.Services.GetRequiredService<TimeProvider>());
    }

    public ValueTask DisposeAsync() => DisposeAsync(removeImage: true);

    internal async ValueTask DisposeAsync(bool removeImage)
    {
        var failures = new List<Exception>();
        var owned = application;
        var ownedCleanup = imageCleanup;
        var timeProvider = owned?.Services.GetRequiredService<TimeProvider>() ?? TimeProvider.System;
        var policy = owned?.Services.GetRequiredService<IOptions<TestExecutionOptions>>().Value;
        if (owned is not null)
        {
            using var cleanup = new CancellationTokenSource(policy!.ApplicationCleanupTimeout, timeProvider);
            await ServerFailureObserver.ObserveAsync(() => owned.StopAsync(cleanup.Token), failures)
                .ConfigureAwait(false);
        }
        await StopOutputAsync(failures).ConfigureAwait(false);
        if (testingBuilder is { } ownedBuilder)
        {
            var beforeDispose = failures.Count;
            await ServerFailureObserver.ObserveAsync(() => ownedBuilder.DisposeAsync().AsTask(), failures)
                .ConfigureAwait(false);
            if (failures.Count == beforeDispose)
            {
                testingBuilder = null;
                application = null;
                imageCleanup = null;
            }
        }
        if (removeImage && failures.Count == 0 && ownedCleanup is not null)
        {
            using var imageDeadline = new CancellationTokenSource(policy!.ImageCleanupTimeout, timeProvider);
            await ServerFailureObserver.ObserveAsync(() => ownedCleanup.CleanupAsync(imageDeadline.Token), failures)
                .ConfigureAwait(false);
        }
        if (failures.Count > 0)
        {
            throw new AggregateException(CleanupFailure, failures);
        }
    }

    private static void VerifyPreparationResources(IDistributedApplicationTestingBuilder builder)
    {
        var executables = builder.Resources.OfType<ExecutableResource>().ToArray();
        if (executables.Length != 1 || executables[0].Name != LocalRf3ImagePrerequisite.ResourceName
            || builder.Resources.OfType<ContainerResource>().Any())
        {
            throw new InvalidOperationException(UnexpectedPreparationResources);
        }
    }

    private async Task StopOutputAsync(List<Exception> failures)
    {
        if (outputLifetime is not null)
        {
            await ServerFailureObserver.ObserveAsync(outputLifetime.CancelAsync, failures).ConfigureAwait(false);
        }
        if (output is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => output, failures).ConfigureAwait(false);
        }
        if (preparationDiagnostics is not null)
        {
            var diagnosticsDisposal = preparationDiagnostics.DisposeAsync().AsTask();
            await ServerFailureObserver.ObserveAsync(() => diagnosticsDisposal, failures)
                .ConfigureAwait(false);
        }
        preparationDiagnostics = null;
        output = null;
        outputLifetime?.Dispose();
        outputLifetime = null;
    }

    private async Task DisposeAfterFailureAsync(Exception primary, CancellationToken caller)
    {
        var failures = new List<Exception> { primary };
        if (preparationDiagnostics is not null)
        {
            ServerFailureObserver.Observe(() => preparationDiagnostics.SaveFailure(primary, caller), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
