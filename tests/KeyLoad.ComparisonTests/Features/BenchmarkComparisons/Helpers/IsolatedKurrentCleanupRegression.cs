using System.Runtime.ExceptionServices;
using Aspire.Hosting;
using KeyLoad.Comparisons.Targets;
using KurrentDB.Client;
using KurrentEventData = KurrentDB.Client.EventData;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedKurrentCleanupRegression
{
    /// <summary>AC-ISO-005/006: actual bounded deletion preserves an independently owned native stream.</summary>
    internal static async Task VerifyAsync(DistributedApplication app, int nodeCount, CancellationToken token)
    {
        using var deadlineTimeout = new CancellationTokenSource(NativeExecutionPolicyFixture.Lifecycle().Value.KurrentCleanupHostTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, deadlineTimeout.Token);
        var endpoint = await IsolatedKurrentCleanupRegressionGossip.ReadLeaderAsync(app, nodeCount, deadline.Token);
        var owner = new NativeProbeOwner(endpoint);
        ExceptionDispatchInfo? primary = null;
        try
        {
            await SeedAsync(owner, deadline.Token);
            await DeleteAndVerifyAsync(owner, deadline.Token);
        }
        catch (Exception error)
        {
            primary = ExceptionDispatchInfo.Capture(error);
            throw;
        }
        finally
        {
            await CleanupRemainingAsync(owner, primary);
        }
    }

    private static async Task SeedAsync(NativeProbeOwner owner, CancellationToken token)
    {
        owner.Writer = owner.CreateClient();
        await owner.Writer.AppendToStreamAsync(owner.Foreign, StreamState.NoStream, [owner.Expected], cancellationToken: token);
        owner.ForeignOwned = true;
        for (var index = 0; index < IsolatedKurrentCleanupRegressionNative.PrivateCount; index++)
        {
            var stream = IsolatedKurrentCleanupRegressionNative.PrivatePrefix + Guid.NewGuid().ToString(KurrentConstants.GuidFormat);
            await owner.Writer.AppendToStreamAsync(stream, StreamState.NoStream,
                [IsolatedKurrentCleanupRegressionNative.CreateEvent()], cancellationToken: token);
            owner.PrivateStreams.Add(stream);
        }
    }

    private static async Task DeleteAndVerifyAsync(NativeProbeOwner owner, CancellationToken token)
    {
        var writer = owner.Writer ?? throw new InvalidOperationException(KurrentConstants.NotInitialized);
        var streams = owner.PrivateStreams.ToArray();
        owner.PrivateDeletionStarted = true;
        owner.Clients.Remove(writer);
        // Ownership transfers to the actual production cleanup, even when native deletion fails.
        var diagnostic = await KurrentOwnedStreamCleanup.RunAsync(writer, streams, [writer], [], NativeExecutionPolicyFixture.Lifecycle(), NativeExecutionPolicyFixture.ReadDiagnostics(), TimeProvider.System, token);
        await IsolatedKurrentCleanupRegressionNative.RequireCompleteAsync(diagnostic, streams.Length);
        var reader = owner.CreateClient();
        await IsolatedKurrentCleanupRegressionNative.RequireDeletedAsync(reader, streams, token);
        await KurrentReplicaProbe.RequireOriginalEventAsync(reader, owner.Foreign, owner.Expected, token);
    }

    private static async Task CleanupRemainingAsync(NativeProbeOwner owner, ExceptionDispatchInfo? primary)
    {
        // Unknown effects after an attempted deletion are never retried by the fixture.
        var streams = owner.PrivateDeletionStarted ? [] : owner.PrivateStreams.ToArray();
        if (owner.ForeignOwned)
        {
            streams = [.. streams, owner.Foreign];
        }
        KurrentDBClient writer;
        try
        {
            writer = owner.CreateClient();
        }
        catch (Exception error)
        {
            var original = primary ?? ExceptionDispatchInfo.Capture(error);
            var untransferred = owner.Clients.ToArray();
            owner.Clients.Clear();
            try
            {
                await KurrentOwnedStreamCleanup.RunAsync(null, streams, untransferred, [], NativeExecutionPolicyFixture.Lifecycle(), NativeExecutionPolicyFixture.ReadDiagnostics(), TimeProvider.System, CancellationToken.None);
            }
            finally
            {
                original.Throw();
            }
            throw;
        }
        var clients = owner.Clients.ToArray();
        owner.Clients.Clear();
        try
        {
            await KurrentOwnedStreamCleanup.RunAsync(writer, streams, clients, [], NativeExecutionPolicyFixture.Lifecycle(), NativeExecutionPolicyFixture.ReadDiagnostics(), TimeProvider.System, CancellationToken.None);
        }
        catch (Exception)
        {
            primary?.Throw();
            throw;
        }
    }

    private sealed class NativeProbeOwner(Uri endpoint)
    {
        internal List<KurrentDBClient> Clients { get; } = [];
        internal List<string> PrivateStreams { get; } = [];
        internal KurrentDBClient? Writer { get; set; }
        internal bool ForeignOwned { get; set; }
        internal bool PrivateDeletionStarted { get; set; }
        internal string Foreign { get; } = IsolatedKurrentCleanupRegressionNative.ForeignPrefix + Guid.NewGuid().ToString(KurrentConstants.GuidFormat);
        internal KurrentEventData Expected { get; } = IsolatedKurrentCleanupRegressionNative.CreateEvent();

        internal KurrentDBClient CreateClient()
        {
            var client = IsolatedKurrentCleanupRegressionNative.CreateClient(endpoint);
            Clients.Add(client);
            return client;
        }
    }
}
