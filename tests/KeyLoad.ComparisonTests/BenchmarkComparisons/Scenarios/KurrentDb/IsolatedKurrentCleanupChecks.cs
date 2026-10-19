using System.Runtime.ExceptionServices;
using Aspire.Hosting;
using KeyLoad.Comparisons.Targets;
using KurrentDB.Client;
using KurrentEventData = KurrentDB.Client.EventData;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedKurrentCleanupChecks
{
    /// <summary>AC-ISO-005/006: actual bounded deletion preserves an independently owned execution stream.</summary>
    internal static async Task VerifyAsync(DistributedApplication app, int nodeCount, CancellationToken token)
    {
        using var deadlineTimeout = new CancellationTokenSource(ExecutionPolicyFixture.Lifecycle().Value.KurrentCleanupHostTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, deadlineTimeout.Token);
        var endpoint = await IsolatedKurrentCleanupChecksGossip.ReadLeaderAsync(app, nodeCount, deadline.Token);
        var owner = new ProbeOwner(endpoint);
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

    private static async Task SeedAsync(ProbeOwner owner, CancellationToken token)
    {
        owner.Writer = owner.CreateClient();
        await owner.Writer.AppendToStreamAsync(owner.Foreign, StreamState.NoStream, [owner.Expected], cancellationToken: token);
        owner.ForeignOwned = true;
        for (var index = 0; index < RuntimeIsolatedKurrentCleanupChecks.PrivateCount; index++)
        {
            var stream = RuntimeIsolatedKurrentCleanupChecks.PrivatePrefix + Guid.NewGuid().ToString(KurrentConstants.GuidFormat);
            await owner.Writer.AppendToStreamAsync(stream, StreamState.NoStream,
                [RuntimeIsolatedKurrentCleanupChecks.CreateEvent()], cancellationToken: token);
            owner.PrivateStreams.Add(stream);
        }
    }

    private static async Task DeleteAndVerifyAsync(ProbeOwner owner, CancellationToken token)
    {
        var writer = owner.Writer ?? throw new InvalidOperationException(KurrentConstants.NotInitialized);
        var streams = owner.PrivateStreams.ToArray();
        owner.PrivateDeletionStarted = true;
        owner.Clients.Remove(writer);
        // Ownership transfers to the actual production cleanup, even when execution deletion fails.
        var diagnostic = await KurrentOwnedStreamCleanup.RunAsync(writer, streams, [writer], [], ExecutionPolicyFixture.Lifecycle(), ExecutionPolicyFixture.ReadDiagnostics(), TimeProvider.System, token);
        await RuntimeIsolatedKurrentCleanupChecks.RequireCompleteAsync(diagnostic, streams.Length);
        var reader = owner.CreateClient();
        await RuntimeIsolatedKurrentCleanupChecks.RequireDeletedAsync(reader, streams, token);
        await KurrentReplicaProbe.RequireOriginalEventAsync(reader, owner.Foreign, owner.Expected, token);
    }

    private static async Task CleanupRemainingAsync(ProbeOwner owner, ExceptionDispatchInfo? primary)
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
                await KurrentOwnedStreamCleanup.RunAsync(null, streams, untransferred, [], ExecutionPolicyFixture.Lifecycle(), ExecutionPolicyFixture.ReadDiagnostics(), TimeProvider.System, CancellationToken.None);
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
            await KurrentOwnedStreamCleanup.RunAsync(writer, streams, clients, [], ExecutionPolicyFixture.Lifecycle(), ExecutionPolicyFixture.ReadDiagnostics(), TimeProvider.System, CancellationToken.None);
        }
        catch (Exception)
        {
            primary?.Throw();
            throw;
        }
    }

    private sealed class ProbeOwner(Uri endpoint)
    {
        internal List<KurrentDBClient> Clients { get; } = [];
        internal List<string> PrivateStreams { get; } = [];
        internal KurrentDBClient? Writer
        {
            get; set;
        }
        internal bool ForeignOwned
        {
            get; set;
        }
        internal bool PrivateDeletionStarted
        {
            get; set;
        }
        internal string Foreign { get; } = RuntimeIsolatedKurrentCleanupChecks.ForeignPrefix + Guid.NewGuid().ToString(KurrentConstants.GuidFormat);
        internal KurrentEventData Expected { get; } = RuntimeIsolatedKurrentCleanupChecks.CreateEvent();

        internal KurrentDBClient CreateClient()
        {
            var client = RuntimeIsolatedKurrentCleanupChecks.CreateClient(endpoint);
            Clients.Add(client);
            return client;
        }
    }
}
