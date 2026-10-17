using KeyLoad.Storage;
using System.Text.Json;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaRetainedReplayRestartAssertions
{
    private const long FirstIndex = 1;
    private const string Separator = ":";
    private const string MissingEntry = "The original acknowledged native replica prefix is missing.";
    private static readonly string AppliedPrefix = Convert.ToHexString(KeySpace.AppliedBytes) + Separator;

    internal static ReplicaRetainedReplayRestartCut Capture(RequestCqrsCohortNativeNode node)
    {
        var journal = node.Journal;
        var state = journal.Log.State;
        if (state.CommittedIndex < FirstIndex || state.LastIndex > journal.Configuration.MaxAppendEntries)
        {
            throw new InvalidOperationException(MissingEntry);
        }
        var entries = new List<byte[]>();
        for (var index = FirstIndex; index <= state.CommittedIndex; index++)
        {
            entries.Add(NativeSerialization.Serialize(journal.Log.ReadEntry(index)
                ?? throw new InvalidOperationException(MissingEntry)));
        }
        return new(journal.Configuration.Directory, journal.CanonicalDirectoryPath,
            JsonSerializer.Serialize(journal.Configuration, JsonDefaults.Options),
            journal.Database.Store.Identity with
            {
                SigningKey = ReadOnlyMemory<byte>.Empty
            },
            journal.ReplicaStore.Identity with
            {
                SigningKey = ReadOnlyMemory<byte>.Empty
            }, state,
            journal.Database.LastApplied, journal.Database.Store.Position,
            QueueWholeFlowStorage.Bytes((KeyLoad.Storage.ZoneTree.ZoneTreeStore)journal.Database.Store),
            entries.ToArray(), SHA256.HashData(journal.Database.Store.Identity.SigningKey.Span),
            SHA256.HashData(journal.ReplicaStore.Identity.SigningKey.Span));
    }

    internal static async Task RequireAsync(RequestCqrsCohortNativeNode node, ReplicaRetainedReplayRestartCut expected)
    {
        var journal = node.Journal;
        var current = Capture(node);
        await Assert.That(current.Directory).IsEqualTo(expected.Directory);
        await Assert.That(current.CanonicalDirectory).IsEqualTo(expected.CanonicalDirectory);
        await Assert.That(current.ConfigurationJson).IsEqualTo(expected.ConfigurationJson);
        await IdentityAsync(expected.CanonicalIdentity, current.CanonicalIdentity);
        await Assert.That(CryptographicOperations.FixedTimeEquals(expected.CanonicalSigningDigest, current.CanonicalSigningDigest)).IsTrue();
        await IdentityAsync(expected.ReplicaIdentity, current.ReplicaIdentity);
        await Assert.That(CryptographicOperations.FixedTimeEquals(expected.ReplicaSigningDigest, current.ReplicaSigningDigest)).IsTrue();
        await Assert.That(current.State.Version).IsEqualTo(expected.State.Version);
        await Assert.That(current.State.Incarnation).IsEqualTo(expected.State.Incarnation);
        await Assert.That(current.State.Term).IsGreaterThanOrEqualTo(expected.State.Term);
        if (current.State.Term == expected.State.Term)
        {
            await Assert.That(current.State.VotedFor).IsEqualTo(expected.State.VotedFor);
        }
        if (current.State.VotedFor is { } votedFor)
        {
            await Assert.That(journal.Configuration.VoterIds.Contains(votedFor, StringComparer.Ordinal)).IsTrue();
        }
        await Assert.That(current.State.LastIndex).IsGreaterThanOrEqualTo(expected.State.LastIndex);
        await Assert.That(current.State.CommittedIndex).IsGreaterThanOrEqualTo(expected.State.CommittedIndex);
        await Assert.That(current.Applied).IsGreaterThanOrEqualTo(expected.Applied);
        await Assert.That(current.Position).IsGreaterThanOrEqualTo(expected.Position);
        await Assert.That(current.State.Snapshot).IsEqualTo(expected.State.Snapshot);
        await Assert.That(WithoutApplied(current.Rows).SequenceEqual(WithoutApplied(expected.Rows), StringComparer.Ordinal)).IsTrue();
        await PrefixAsync(node, expected, current.State.LastIndex);
    }

    private static async Task PrefixAsync(RequestCqrsCohortNativeNode node,
        ReplicaRetainedReplayRestartCut expected, long through)
    {
        for (var index = FirstIndex; index <= expected.State.CommittedIndex; index++)
        {
            var entry = node.Journal.Log.ReadEntry(index) ?? throw new InvalidOperationException(MissingEntry);
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(expected.Entries[checked((int)(index - FirstIndex))],
                NativeSerialization.Serialize(entry));
        }
        for (var index = expected.State.CommittedIndex + FirstIndex; index <= through; index++)
        {
            var entry = node.Journal.Log.ReadEntry(index) ?? throw new InvalidOperationException(MissingEntry);
            await Assert.That(entry.Operation).IsNull();
            await Assert.That(entry.Index).IsEqualTo(index);
        }
    }

    private static async Task IdentityAsync(StoreIdentity expected, StoreIdentity current)
    {
        await Assert.That(current.ReadGeneration).IsGreaterThanOrEqualTo(expected.ReadGeneration);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(
            NativeSerialization.Serialize(expected with
            {
                ReadGeneration = current.ReadGeneration
            }),
            NativeSerialization.Serialize(current));
    }

    private static IEnumerable<string> WithoutApplied(string[] rows)
        => rows.Where(row => !row.StartsWith(AppliedPrefix, StringComparison.Ordinal));
}
