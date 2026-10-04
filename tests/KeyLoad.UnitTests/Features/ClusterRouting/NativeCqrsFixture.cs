using System.Diagnostics;
using ManagedCode.Communication.CQRS;
using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Metadata;
using Orleans.TestingHost;
using IAsyncInitializer = TUnit.Core.Interfaces.IAsyncInitializer;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeCqrsClusterFixture : IAsyncInitializer, IAsyncDisposable
{
    private static readonly TimeSpan StartupBound = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownBound = TimeSpan.FromSeconds(30);
    private bool disposed;

    public NativeCqrsClusterFixture()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NativeCqrsSiloConfigurator>();
        builder.AddClientBuilderConfigurator<NativeCqrsClientConfigurator>();
        Cluster = builder.Build();
    }

    public TestCluster Cluster { get; }

    public IGrainFactory GrainFactory => Cluster.Client;

    public async Task InitializeAsync()
    {
        using var deadline = new CancellationTokenSource(StartupBound);
        try
        {
            await Cluster.DeployAsync(deadline.Token);
        }
        catch (Exception startupError)
        {
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException(startupError, cleanupError);
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        using var deadline = new CancellationTokenSource(ShutdownBound);
        await NativeCqrsTestSupport.RunWithCleanupAsync(
            () => Cluster.StopAllSilosAsync(deadline.Token),
            async () => await Cluster.DisposeAsync());
        GC.SuppressFinalize(this);
    }
}

internal sealed class NativeCqrsSiloConfigurator : ISiloConfigurator
{
    public NativeCqrsSiloConfigurator()
    {
    }

    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.Services.AddSingleton<IConfigureGrainTypeComponents>(services =>
            new NativeCqrsGrainComponentConfigurator(
                services.GetRequiredService<GrainClassMap>(),
                services));
        siloBuilder
            .AddOrleansGraph()
            .UseOrleansCommunication();
    }
}

internal sealed class NativeCqrsClientConfigurator : IClientBuilderConfigurator
{
    public NativeCqrsClientConfigurator()
    {
    }

    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder
            .AddOrleansGraph()
            .UseOrleansCommunication();
    }
}

internal static class NativeCqrsTestSupport
{
    private const int MaximumChunks = 8;
    internal static readonly TimeSpan WaitBound = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private const string WaitExpired = "The native CQRS actor did not reach the expected state before the test deadline.";

    internal static async Task<List<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>>> ReadAllAsync(
        IAsyncEnumerable<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> stream,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(WaitBound);
        var chunks = new List<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>>(4);
        await foreach (var chunk in stream.WithBatchSize(NativeCqrsProtocol.BatchSize)
                           .WithCancellation(timeout.Token))
        {
            chunks.Add(chunk);
            if (chunks.Count > MaximumChunks)
            {
                throw new InvalidOperationException(WaitExpired);
            }
        }
        return chunks;
    }

    internal static async Task<NativeCqrsObservationSnapshot> WaitForEventAsync(
        IGrainFactory grainFactory, Guid requestId, string expectedEvent)
    {
        var started = Stopwatch.GetTimestamp();
        var reader = grainFactory.GetGrain<INativeCqrsObservationReaderGrain>(requestId);
        while (true)
        {
            var remaining = Remaining(started);
            var snapshot = await reader.ReadAsync().WaitAsync(remaining);
            if (snapshot.Events.Contains(expectedEvent, StringComparer.Ordinal))
            {
                return snapshot;
            }
            await Task.Delay(PollInterval).WaitAsync(Remaining(started));
        }
    }

    internal static async Task AssertTerminalSequenceAsync(
        IReadOnlyList<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> chunks,
        CqrsStreamChunkKind expectedTerminal)
    {
        await Assert.That(chunks.Count).IsGreaterThan(0);
        var terminalCount = 0;
        for (var index = 0; index < chunks.Count; index++)
        {
            await Assert.That(chunks[index].Sequence).IsEqualTo(index + NativeCqrsProtocol.FirstSequence);
            if (chunks[index].IsTerminal)
            {
                terminalCount++;
                await Assert.That((index == chunks.Count - 1)).IsTrue();
                await Assert.That(chunks[index].Kind).IsEqualTo(expectedTerminal);
            }
        }
        await Assert.That(terminalCount).IsEqualTo(1);
    }

    internal static async Task RunWithCleanupAsync(Func<Task> body, Func<Task> cleanup)
    {
        try
        {
            await body();
        }
        catch (Exception bodyFailure)
        {
            try
            {
                await cleanup();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(bodyFailure, cleanupFailure);
            }
            throw;
        }
        await cleanup();
    }

    internal static TimeSpan Remaining(long started)
    {
        var remaining = WaitBound - Stopwatch.GetElapsedTime(started);
        if (remaining <= TimeSpan.Zero)
        {
            throw new TimeoutException(WaitExpired);
        }
        return remaining;
    }
}
