using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Server;
using KeyLoad.Storage;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class SafeProjectorOperationMutations
{
    private const string Separator = ":";
    private const string MissingOutcome = "The actual safe projector refusal outcome is absent.";
    private const string MissingClock = "The admitted safe projector refusal clock is absent.";
    private const long OnePosition = 1;

    internal static async Task<SafeProjectorOperationCommand> RefusedAsync(SafeProjectorOperationDatabase owner,
        ImmutableArray<Mutation> mutations, string expectedDetail, long policyEpoch)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, owner.Source.Partition, mutations);
        var before = owner.Store.Position;
        var journal = owner.ReadCanonicalCut();
        var domain = Domain(owner, id);
        var command = SafeProjectorOperationSeed.Issue(owner, OperationKind.Batch, id, request,
            SafeProjectorOperationProtocol.Reader, creatorEpoch: policyEpoch);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RefusalAsync(command.Result, expectedDetail), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(Domain(owner, id).SequenceEqual(domain, StringComparer.Ordinal)).IsTrue();
            await Assert.That(command.Outcome.CanonicalSequence).IsEqualTo(journal.AppliedPosition + OnePosition);
            await Assert.That(owner.ReadCanonicalCut()).IsEqualTo((command.Outcome.CanonicalSequence, before + OnePosition));
            await Assert.That(owner.Database.LastApplied).IsEqualTo(command.Outcome.CanonicalSequence);
            await Assert.That(owner.Store.Position).IsEqualTo(before + OnePosition);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => StoredRefusalAsync(owner, command, expectedDetail, policyEpoch), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return command;
    }

    private static async Task StoredRefusalAsync(SafeProjectorOperationDatabase owner,
        SafeProjectorOperationCommand command, string expectedDetail, long policyEpoch)
    {
        var key = KeySpace.PartitionOutcome(owner.Source.Partition, SafeProjectorOperationProtocol.Reader, command.Operation.CommandId);
        var stored = owner.Store.Read(view => view.GetRecord<StoredOutcome>(key))
            ?? throw new InvalidOperationException(MissingOutcome);
        await RefusalAsync(stored.Result, expectedDetail);
        await Assert.That(StringComparer.Ordinal.Equals(stored.Fingerprint, DatabaseCommandFingerprint.Compute(command.Operation))).IsTrue();
        await Assert.That(stored.PolicyEpoch).IsEqualTo(policyEpoch);
        await Assert.That(stored.Incarnation).IsEqualTo(command.ExpectedToken.Incarnation);
        await Assert.That(stored.Partition).IsEqualTo(owner.Source.Partition);
        await Assert.That(stored.ScopeKind).IsEqualTo(KeyLoad.Core.Features.CommandOutcomes.Contracts.CommandOutcomeScopeKind.Partition);
        await Assert.That(stored.CompositionAuthority is null).IsTrue();
        await Assert.That(stored.OnlineTextAuthority is null).IsTrue();
        await Assert.That(stored.RemoteTransferFailureAuthority is null).IsTrue();
        var encodedClock = owner.Store.Read(view => view.ReadOwnedValue(KeySpace.ClockBytes))
            ?? throw new InvalidOperationException(MissingClock);
        await Assert.That(OrleansSerialization.Deserialize<DateTimeOffset>(encodedClock)).IsEqualTo(new DateTimeOffset(command.Operation.Timestamp));
        var locator = owner.Store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocator(
            owner.Source.Partition, SafeProjectorOperationProtocol.Reader, command.Operation.CommandId)));
        await Assert.That(locator is not null && locator.AsSpan().SequenceEqual(key)).IsTrue();
    }

    internal static SafeProjectorOperationCommand Patch(SafeProjectorOperationDatabase owner,
        long previousRevision, string status, bool protectedWrite, long policyEpoch)
    {
        var fields = ImmutableArray.CreateBuilder<FieldPatch>();
        fields.Add(new(SafeProjectorOperationProtocol.StatusPath, PatchKind.Set,
            JsonSerializer.Serialize(status, JsonDefaults.Options)));
        if (protectedWrite)
        {
            fields.Add(new(SafeProjectorOperationProtocol.LiteralPath, PatchKind.Set,
                JsonSerializer.Serialize(SafeProjectorOperationProtocol.Canary, JsonDefaults.Options)));
        }
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, owner.Source.Partition,
            [new PatchDocument(SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.First,
                fields.ToImmutable(), previousRevision)]);
        var command = SafeProjectorOperationSeed.Issue(owner, OperationKind.Batch, id, request,
            SafeProjectorOperationProtocol.Reader, creatorEpoch: policyEpoch);
        return command;
    }

    internal static async Task PatchReceiptAsync(SafeProjectorOperationCommand command, long expectedRevision)
    {
        await SafeProjectorOperationSeed.EqualAsync(command.Result.Get<CommitReceipt>(), new CommitReceipt(command.Operation.CommandId, command.ExpectedToken, [new(SafeProjectorOperationProtocol.PatchKind, SafeProjectorOperationProtocol.Collection,
                SafeProjectorOperationProtocol.First, expectedRevision)], DurabilityProfile.ProcessDurable, 1));
    }

    internal static ImmutableArray<Mutation> Replacement(string actualProjectedJson, long revision, bool explicitReplacement)
        => [new PutDocument(SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.First,
            actualProjectedJson, revision, ExplicitReplacement: explicitReplacement)];

    internal static ImmutableArray<Mutation> ProtectedPatch(long revision)
        => [new PatchDocument(SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.First,
            [new(SafeProjectorOperationProtocol.LiteralPath, PatchKind.Set,
                JsonSerializer.Serialize(SafeProjectorOperationProtocol.Canary, JsonDefaults.Options))], revision)];

    internal static async Task RefusalAsync(Result<object> actual, string detail)
    {
        await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(StringComparer.Ordinal.Equals(actual.Problem?.Detail, detail)).IsTrue();
        await Assert.That(actual.Value is null).IsTrue();
        await Assert.That(actual.Problem?.Detail?.Contains(SafeProjectorOperationProtocol.Canary, StringComparison.Ordinal) is true).IsFalse();
    }

    private static string[] Domain(SafeProjectorOperationDatabase owner, Guid id)
    {
        var excluded = new[]
        {
            KeySpace.PartitionOutcome(owner.Source.Partition, SafeProjectorOperationProtocol.Reader, id),
            KeySpace.OutcomeLocator(owner.Source.Partition, SafeProjectorOperationProtocol.Reader, id),
            KeySpace.ClockBytes,
            KeySpace.AppliedBytes
        }.Select(key => Convert.ToHexString(key) + Separator).ToArray();
        return TeamRowPolicyAssertions.Image(owner.Store).Where(row =>
            !excluded.Any(key => row.StartsWith(key, StringComparison.Ordinal))).ToArray();
    }
}
