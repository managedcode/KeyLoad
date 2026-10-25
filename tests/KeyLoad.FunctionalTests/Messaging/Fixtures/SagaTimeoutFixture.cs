using KeyLoad.Clustering;
using KeyLoad.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.DurableJobs;
using IAsyncInitializer = TUnit.Core.Interfaces.IAsyncInitializer;

namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class SagaTimeoutFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string InvalidProfileDetail = "The execution runtime test timing profile is invalid.";
    private readonly RequestCqrsHostedRuntimeFixture runtime;
    private readonly Lock disposalGate = new();
    private Task? disposal;

    internal SagaTimeoutFixture()
    {
        if (!TestProfile.IsValid())
        {
            throw new InvalidOperationException(InvalidProfileDetail);
        }
        var jobs = Options.Create(new RuntimeDurableJobOptions
        {
            ShardCheckInterval = TestProfile.PollInterval,
            JobStatusPollInterval = TestProfile.PollInterval,
            RetryDelay = TestProfile.RetryDelay
        });
        runtime = new(configureServices: (services, _) =>
            services.Replace(ServiceDescriptor.Singleton(jobs)));
    }

    internal RequestCqrsHostedDatabase Database => runtime.Database;
    internal GrainRequestCodec Codec => runtime.Codec;
    internal ICommitCoordinator Coordinator => Database.Coordinator;
    internal RequestWorkOwner RequestWork => runtime.RequestWork;
    internal IServiceProvider SiloServices => runtime.Services;
    internal IOptions<GrainRoutingOptions> Routing => runtime.RoutingOptions;
    internal RuntimeTestOptions TestProfile { get; } = new();
    internal SagaTimeoutJobHarness JobHarness { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(TestProfile.StartupTimeout, TimeProvider.System);
        try
        {
            await runtime.InitializeAsync(deadline.Token);
            Database.Configure("jobs", ResourceKind.WorkQueue);
            Database.Configure("timeouts", ResourceKind.WorkQueue);
            SagaTimeoutTestData.ConfigureSagaPrincipal(this);
            BindJobs();
        }
        catch (Exception startupFailure)
        {
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(startupFailure, cleanupFailure);
            }
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            return new(disposal ??= runtime.DisposeAsync().AsTask());
        }
    }

    internal Task<DurableJob> DispatchAsync(DueWorkHint hint, CancellationToken cancellationToken)
        => JobHarness.ScheduleAsync(hint, TimeProvider.System.GetUtcNow(), cancellationToken);

    internal async Task RestartRuntimeAsync(CancellationToken cancellationToken)
    {
        await runtime.RestartAsync(cancellationToken);
        BindJobs();
    }

    private void BindJobs() => JobHarness = new(runtime.Grains, runtime.Services, TestProfile);
}
