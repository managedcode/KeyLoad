using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>An authentic source denial cannot become a controlled effect after genuine policy repair.</summary>
internal static class ResourceLifetimeGrantAdmissionDenial
{
    private const string Subject = "lifetime-denied-subject";
    private const string Administrator = "root";
    private const long InitialEpoch = 1;
    private const long RepairedEpoch = 2;
    private const long Step = 1;
    private const int OneEntry = 1;
    private static readonly Guid OriginalId = Guid.Parse("1733528d-ddb6-4781-a021-9c6bcf79e77c");

    internal static async Task ExecuteAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        var partition = state.Context.Control.Partition;
        var subject = new PrincipalRecord(Subject, partition.TenantId,
            [new(partition.DatabaseId, ControlledPartitionMovementCorpus.Collection, Capability.DocumentsRead)], [])
        {
            PolicyEpoch = InitialEpoch
        };
        await ConfigureAsync(state, subject, token);
        var request = NativeCommandPayload.Read<CommandRequest>(state.Original) with
        {
            CommandId = OriginalId
        };
        var original = state.Source.Database.CreateNativeOperation(OperationKind.Batch, OriginalId, Subject,
            state.Source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var admission = ResourceEffectAdmissionValidation.Read(original, state.Source.Database.Limits);
        await Assert.That(admission.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await ConfigureAsync(state, subject with
        {
            ClusterAdministrator = true,
            PolicyEpoch = RepairedEpoch
        }, token);
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var effectId = ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.Effect);
        var commandId = ControlledDocumentTechnicalIdentity.Derive(original, ControlledDocumentTechnicalIdentity.Admit);
        var result = await state.Phases.SubmitEncodedAsync(commandId, state.Context,
            PartitionMovePeerStage.ControlAdmitCommand, NativeSerialization.Serialize(new PartitionControlAdmitBody(
                state.Context.OperatorPrincipalId, state.Context.Control, original, effectId, state.Expiry)),
            state.Expiry, null, null, token);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(result.SafeDetail).IsEqualTo(admission.SafeDetail);
        await Assert.That(result.NativeValue).IsNull();
        await Assert.That(result.Json).IsNull();
        var entry = state.Source.Journal.Log.Read(before.LastIndex + Step, OneEntry,
            state.Source.Database.Limits.MaxBatchBytes).Single();
        var phase = entry.Operation ?? throw new InvalidOperationException(ResourceEffectAdmissionProtocol.Invalid);
        await Assert.That(phase.Id).IsEqualTo(commandId);
        await Assert.That(state.Source.Journal.Log.State.LastIndex).IsEqualTo(before.LastIndex + Step);
        await Assert.That(state.Source.Database.LastApplied).IsEqualTo(before.Applied + Step);
        await AssertSourceImageAsync(state, before, phase, entry.Index, result);
        await target.AssertUnchangedAsync(state.Target);
        await ResourceLifetimeGrantAssertions.EqualAsync(state.Proof,
            state.Source.Database.CaptureControlledCommandLifetimeProof(state.Request.Envelope, token));
    }

    private static async Task ConfigureAsync(ResourceLifetimeGrantState state, PrincipalRecord principal,
        CancellationToken token)
    {
        var database = state.Source.Database;
        var operation = database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(), Administrator,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
        var result = state.Source.Journal.Submit(operation, token);
        await Assert.That(result.Error).IsNull();
        await ResourceLifetimeGrantAssertions.EqualAsync(principal, result.Get<PrincipalRecord>());
    }

    private static async Task AssertSourceImageAsync(ResourceLifetimeGrantState state,
        ControlledPartitionMovementExpiryOwnerState before, ReplicatedOperation operation, long index, OperationResult result)
    {
        var partition = state.Context.Control.Partition;
        var expected = before.Image.ToDictionary(row => row[..row.IndexOf(':', StringComparison.Ordinal)], row => row[(row.IndexOf(':', StringComparison.Ordinal) + OneEntry)..],
            StringComparer.Ordinal);
        var principal = state.Source.Store.Read(view => state.Source.Database.Principal(view, Administrator, operation.EvaluatedAt));
        var outcome = new StoredOutcome(NativeOperationFingerprint.Compute(operation), state.Source.Store.Identity.Incarnation,
            principal.PolicyEpoch, new OperationResult(null, ErrorCode.PermissionDenied, result.SafeDetail))
        {
            ScopeKind = CommandOutcomeScopeKind.Partition,
            Partition = partition
        };
        var outcomeKey = KeySpace.PartitionOutcome(partition, Administrator, operation.Id);
        expected[Convert.ToHexString(outcomeKey)] = Convert.ToHexString(NativeSerialization.Serialize(outcome));
        expected[Convert.ToHexString(KeySpace.OutcomeLocatorV2(partition, Administrator, operation.Id))]
            = Convert.ToHexString(outcomeKey);
        expected[Convert.ToHexString(KeySpace.AppliedBytes)] = Convert.ToHexString(NativeSerialization.Serialize(index));
        var clockKey = Convert.ToHexString(KeySpace.ClockBytes);
        if (!expected.TryGetValue(clockKey, out var previousClock)
            || NativeSerialization.Deserialize<DateTimeOffset>(Convert.FromHexString(previousClock)) <= operation.EvaluatedAt)
        {
            expected[clockKey] = Convert.ToHexString(NativeSerialization.Serialize(operation.EvaluatedAt));
        }
        var image = expected.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + pair.Value).ToArray();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(state.Source.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(state.Source.Store.Position).IsEqualTo(before.Position + Step);
    }
}
