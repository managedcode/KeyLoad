using System.Runtime.ExceptionServices;
using Aspire.Hosting;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using KurrentDB.Client;
using KurrentEventData = KurrentDB.Client.EventData;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedKurrentOwnershipRegression
{
    /// <summary>AC-KO-002: real different-ID rejection excludes foreign data while actual owned cleanup still runs.</summary>
    internal static async Task VerifyAsync(DistributedApplication app, int nodeCount, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(NativeExecutionPolicyFixture.Lifecycle().Value.KurrentCleanupHostTimeout);
        var endpoint = await IsolatedKurrentCleanupRegressionGossip.ReadLeaderAsync(app, nodeCount, deadline.Token);
        var owner = new NativeOwner(endpoint);
        ExceptionDispatchInfo? primary = null;
        try
        {
            await SeedAndRejectAsync(owner, deadline.Token);
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

    private static async Task SeedAndRejectAsync(NativeOwner owner, CancellationToken token)
    {
        var fixture = owner.CreateClient();
        var result = await fixture.AppendToStreamAsync(owner.Foreign, StreamState.NoStream, [owner.Expected], cancellationToken: token);
        if (result is not SuccessResult)
        {
            throw new ComparisonFailureException(KurrentConstants.AppendAcknowledgementMissing);
        }
        owner.ForeignOwned = true;
        owner.Before = await IsolatedKurrentOwnershipRegressionNative.ReadOriginalAsync(owner.CreateClient(), owner.Foreign, token);
        var writer = owner.CreateClient();
        owner.Writer = writer;
        var conflicting = IsolatedKurrentOwnershipRegressionNative.CreateEvent();
        await Assert.That(conflicting.EventId != owner.Expected.EventId).IsTrue();
        await Assert.That(async () =>
        {
            await KurrentOwnedStreamAppend.AppendAsync(writer, owner.Ownership, owner.Foreign, conflicting, token);
        }).Throws<WrongExpectedVersionException>();
        await Assert.That(owner.Ownership.SnapshotAcknowledged().Length).IsEqualTo(0);
        await KurrentOwnedStreamAppend.AppendAsync(writer, owner.Ownership, owner.Private,
            IsolatedKurrentOwnershipRegressionNative.CreateEvent(), token);
        await Assert.That(owner.Ownership.SnapshotAcknowledged().SequenceEqual([owner.Private])).IsTrue();
    }

    private static async Task DeleteAndVerifyAsync(NativeOwner owner, CancellationToken token)
    {
        var writer = owner.Writer ?? throw new InvalidOperationException(KurrentConstants.NotInitialized);
        var streams = owner.Ownership.SnapshotAcknowledged();
        owner.PrivateDeletionStarted = true;
        owner.Clients.Remove(writer);
        // Transfer only ACKed candidate names and this original writer to the frozen production cleanup.
        var diagnostic = await KurrentOwnedStreamCleanup.RunAsync(writer, streams, [writer], [], NativeExecutionPolicyFixture.Lifecycle(), token);
        await IsolatedKurrentCleanupRegressionNative.RequireCompleteAsync(diagnostic, streams.Length);
        await IsolatedKurrentCleanupRegressionNative.RequireDeletedAsync(owner.CreateClient(), streams, token);
        var after = await IsolatedKurrentOwnershipRegressionNative.ReadOriginalAsync(owner.CreateClient(), owner.Foreign, token);
        await IsolatedKurrentOwnershipRegressionNative.RequireUnchangedAsync(
            owner.Before ?? throw new InvalidOperationException(KurrentConstants.NotInitialized), after);
    }

    private static async Task CleanupRemainingAsync(NativeOwner owner, ExceptionDispatchInfo? primary)
    {
        // No deletion retry or authority is granted to rejected/unknown reservations.
        var streams = owner.PrivateDeletionStarted ? [] : owner.Ownership.SnapshotAcknowledged();
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
                await KurrentOwnedStreamCleanup.RunAsync(null, streams, untransferred, [], NativeExecutionPolicyFixture.Lifecycle(), CancellationToken.None);
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
            await KurrentOwnedStreamCleanup.RunAsync(writer, streams, clients, [], NativeExecutionPolicyFixture.Lifecycle(), CancellationToken.None);
        }
        catch (Exception)
        {
            primary?.Throw();
            throw;
        }
    }

    private sealed class NativeOwner(Uri endpoint)
    {
        private readonly string prefix = KurrentConstants.StreamPrefix + Guid.NewGuid().ToString(KurrentConstants.GuidFormat) + KurrentConstants.StreamSeparator;
        internal List<KurrentDBClient> Clients { get; } = [];
        internal KurrentStreamOwnership Ownership { get; } = new(NativeExecutionPolicyFixture.Workload(new ComparisonOptions
        { Documents = 1, TopK = 1, Operations = 1, Warmup = 0, Repetitions = 1 }));
        internal KurrentDBClient? Writer { get; set; }
        internal bool ForeignOwned { get; set; }
        internal bool PrivateDeletionStarted { get; set; }
        internal string Foreign => prefix + IsolatedKurrentOwnershipRegressionNative.ForeignSuffix;
        internal string Private => prefix + IsolatedKurrentOwnershipRegressionNative.PrivateSuffix;
        internal KurrentEventData Expected { get; } = IsolatedKurrentOwnershipRegressionNative.CreateEvent();
        internal IsolatedKurrentOwnershipRegressionNative.Snapshot? Before { get; set; }

        internal KurrentDBClient CreateClient()
        {
            var client = IsolatedKurrentCleanupRegressionNative.CreateClient(endpoint);
            Clients.Add(client);
            return client;
        }
    }
}
