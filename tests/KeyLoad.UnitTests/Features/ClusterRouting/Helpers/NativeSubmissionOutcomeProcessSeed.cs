using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeSubmissionOutcomeProcessSeed(CommandRequest Command,
    ReplicatedOperation Operation, OperationResult Result, long PolicyEpoch, byte[] Key, byte[] OriginalBytes,
    C1SubmissionOutcomeInspectionRequest Request)
{
    internal static async Task<NativeSubmissionOutcomeProcessSeed> CreateAsync(C1OutcomeInspectionFixture fixture)
    {
        var principalId = "c1-probe-" + Guid.NewGuid().ToString("N");
        var principal = new PrincipalRecord(principalId, fixture.Partition.TenantId,
            [new(fixture.Partition.DatabaseId, C1OutcomeInspectionFixture.ResourceName,
                Capability.DocumentsRead | Capability.DocumentsWrite)], []) { PolicyEpoch = 1 };
        var configure = fixture.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            C1OutcomeInspectionAssertions.AdminId, fixture.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
        var configured = fixture.Database.Apply(configure).Get<PrincipalRecord>();
        await Assert.That(configured.Id).IsEqualTo(principalId);
        await Assert.That(configured.ClusterAdministrator).IsFalse();
        await Assert.That(configured.PolicyEpoch).IsEqualTo(principal.PolicyEpoch);
        var command = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new PutDocument(C1OutcomeInspectionFixture.ResourceName, "full-outcome-document", "{\"full\":true}")]);
        var operation = fixture.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId, principalId,
            fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(command));
        var result = fixture.Database.Apply(operation);
        await Assert.That(result.Error).IsNull();
        await Assert.That(NativeSerialization.Serialize(result).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(fixture.Database.ResolveOutcome(operation)))).IsTrue();
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, command.Partition);
        var key = CommandOutcomeKeyResolver.ForNew(principalId, command.CommandId, scope).Key;
        var raw = fixture.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(C1OutcomeInspectionFixture.SuccessMessage);
        var request = new C1SubmissionOutcomeInspectionRequest(C1SubmissionOutcomeInspectionProtocol.Version,
            fixture.DirectoryPath, fixture.Identity.NodeId, fixture.Incarnation, principalId, command.CommandId,
            command.Partition);
        return new(command, operation, result, configured.PolicyEpoch, key, raw, request);
    }

    internal async Task RequireOutcomeAsync(C1SubmissionOutcomeInspectionReceipt receipt)
    {
        var outcome = NativeSerialization.Deserialize<StoredOutcome>(receipt.NativeStoredOutcome);
        await Assert.That(receipt.OutcomeKey.AsSpan().SequenceEqual(Key)).IsTrue();
        await Assert.That(receipt.NativeStoredOutcome.AsSpan().SequenceEqual(OriginalBytes)).IsTrue();
        await Assert.That(outcome.Fingerprint).IsEqualTo(NativeOperationFingerprint.Compute(Operation));
        await Assert.That(outcome.PolicyEpoch).IsEqualTo(PolicyEpoch);
        await Assert.That(outcome.Incarnation).IsEqualTo(Request.Incarnation);
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(outcome.Partition).IsEqualTo(Command.Partition);
        await Assert.That(NativeSerialization.Serialize(outcome.Result).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(Result))).IsTrue();
        await Assert.That(outcome.BlobAuthority).IsNull();
        await Assert.That(outcome.CompositionAuthority).IsNull();
        await Assert.That(outcome.OnlineTextAuthority).IsNull();
        await Assert.That(outcome.RemoteTransferFailureAuthority).IsNull();
        await Assert.That(outcome.RemoteTransferAuthority).IsNull();
    }

    public override string ToString() => nameof(NativeSubmissionOutcomeProcessSeed);
}
