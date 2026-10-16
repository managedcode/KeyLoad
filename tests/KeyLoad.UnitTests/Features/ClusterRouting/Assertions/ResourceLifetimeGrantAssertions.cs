using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ResourceLifetimeGrantAssertions
{
    private const long SequenceStep = 1;
    private const long EmptyCount = 0;
    private const long CommandAndGrantPins = 2;
    private const long CommandOnlyPin = 1;
    private const long ActiveGrantPin = 1;
    private const string LiteralPayload = "{\"tenantId\":\"tenant\",\"databaseId\":\"database\",\"definition\":{\"name\":\"identity-records\",\"kind\":\"Collection\",\"transactionDomainId\":\"orders\"}}";
    private const string LiteralFingerprint = "6582b583af7321db4006d04a19849fc57216bfa68bd44dd6ddb48b2ad4430d50";
    private static readonly Guid LiteralId = new("01234567-89ab-cdef-0123-456789abcdef");

    internal static async Task CaptureAsync(ResourceLifetimeGrantState state)
    {
        var literal = new ReplicatedOperation(LiteralId, OperationKind.ConfigureResource, "root",
            state.Original.EvaluatedAt, LiteralPayload);
        await Assert.That(NativeOperationFingerprint.Compute(literal)).IsEqualTo(LiteralFingerprint);
        await EqualAsync(state.Record.Delegation!, state.Proof.ControlProof.OriginalDelegation);
        await EqualAsync(state.Authorization.Grant! with
        {
            Settlement = null,
            AbortDisposition = null
        }, state.Proof.OriginalGrant);
        await Assert.That(state.Proof.ControlProof.Coverage.Partition).IsEqualTo(state.Context.Control.Partition);
        var lifetimes = state.Proof.ControlProof.Coverage.Lifetimes;
        await Assert.That(lifetimes.Length).IsEqualTo(state.Record.Delegation!.Resources.Length);
        await Assert.That(lifetimes.IsDefaultOrEmpty).IsFalse();
        foreach (var definition in state.Record.Delegation.Resources)
        {
            var actual = lifetimes.Single(lifetime => lifetime.ResourceName == definition.Name);
            var source = state.Source.Store.Read(view => ResourceLifetimeCatalog.Read(view, actual.TenantId,
                actual.DatabaseId, actual.ResourceName, definition));
            await EqualAsync(source!, actual);
            await Assert.That(state.Target.Store.Read(view => view.GetRecord<ResourceDefinition>(
                KeySpace.Resource(actual.TenantId, actual.DatabaseId, actual.ResourceName)))).IsNull();
        }
        await PinnedAsync(state);
    }

    internal static async Task PinnedAsync(ResourceLifetimeGrantState state)
    {
        var current = state.Source.Store.Read(view => PartitionControlCommandStorage.Read(view,
            state.Record.Identity, state.Source.Database.Limits.MaxBatchBytes));
        await Assert.That(current!.Phase).IsEqualTo(PartitionControlCommandPhase.Admitted);
        await CountersAsync(state, CommandAndGrantPins, ActiveGrantPin);
    }

    internal static Task CommandPinnedAsync(ResourceLifetimeGrantState state)
        => CountersAsync(state, CommandOnlyPin, EmptyCount);

    private static async Task CountersAsync(ResourceLifetimeGrantState state, long commandAndGrant, long moveGrants)
    {
        var partition = state.Context.Control.Partition;
        await Assert.That(state.Source.Store.Read(view => PartitionMoveGrantStorage.Outstanding(view,
            PartitionMoveGrantStorage.DatabaseKey(partition.TenantId, partition.DatabaseId)))).IsEqualTo(commandAndGrant);
        await Assert.That(state.Source.Store.Read(view => PartitionMoveGrantStorage.Outstanding(view,
            state.Context.OperatorPrincipalId))).IsEqualTo(commandAndGrant);
        await Assert.That(state.Source.Store.Read(view => PartitionMoveGrantStorage.Outstanding(view,
            PartitionMoveGrantStorage.MoveCountKey(partition, state.Context.Control.MoveId)))).IsEqualTo(moveGrants);
    }

    internal static async Task CompletedAsync(ResourceLifetimeGrantState state, PartitionControlEffectPayload effect, CancellationToken token)
    {
        var partition = state.Context.Control.Partition;
        var expected = new CommitReceipt(state.Original.Id,
            new(state.Target.Store.Identity.Incarnation, partition.AtomicPartitionId,
                checked(state.TargetIndex + SequenceStep), state.Context.Control.PublishedPlacement!.PlacementEpoch),
            [new("putDocument", ControlledPartitionMovementCorpus.Collection, ControlledPartitionMovementCorpus.DocumentId,
                ResourceLifetimeGrantStages.UpdatedRevision)], DurabilityProfile.ProcessDurable);
        await EqualAsync(expected, effect.OriginalResult.Get<CommitReceipt>());
        await EqualAsync(expected with
        {
            CommandId = state.Record.EffectId
        }, effect.Receipt);
        var resolved = Resolve(state.Source, state.Original, token);
        await EqualAsync(effect.OriginalResult, resolved);
        await DocumentAsync(state.Source, state.Target, expected.Token, token);
        await Assert.That(state.Source.Store.Read(view => PartitionMoveGrantStorage.Outstanding(view,
            PartitionMoveGrantStorage.DatabaseKey(partition.TenantId, partition.DatabaseId)))).IsEqualTo(EmptyCount);
        await Assert.That(state.Source.Store.Read(view => PartitionMoveGrantStorage.Outstanding(view,
            state.Context.OperatorPrincipalId))).IsEqualTo(EmptyCount);
        await Assert.That(state.Source.Store.Read(view => PartitionMoveGrantStorage.Outstanding(view,
            PartitionMoveGrantStorage.MoveCountKey(partition, state.Context.Control.MoveId)))).IsEqualTo(EmptyCount);
    }

    internal static OperationResult Resolve(ControlledPartitionMovementNode source, ReplicatedOperation original, CancellationToken token)
        => source.Database.ResolveControlledDocumentOutcome(original.PrincipalId, original,
            new ReadExecutionBudget(Options.Create(source.Database.Limits), source.Database.EvaluationClock, token));

    internal static async Task EqualAsync<T>(T expected, T actual)
    {
        await Assert.That(JsonDefaults.Serialize(actual)).IsEquivalentTo(JsonDefaults.Serialize(expected),
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        if (expected is PartitionMoveLifetimeProofV1 expectedProof && actual is PartitionMoveLifetimeProofV1 actualProof)
        {
            var expectedOriginal = expectedProof.ControlProof.OriginalDelegation.OriginalOperation!;
            var actualOriginal = actualProof.ControlProof.OriginalDelegation.OriginalOperation!;
            await Assert.That(actualOriginal.NativePayload.Span.SequenceEqual(expectedOriginal.NativePayload.Span)).IsTrue();
        }
        if (expected is OperationResult expectedResult && actual is OperationResult actualResult)
        {
            await Assert.That(actualResult.NativeValue?.GetType()).IsEqualTo(expectedResult.NativeValue?.GetType());
            await Assert.That(JsonDefaults.Serialize(actualResult.NativeValue))
                .IsEquivalentTo(JsonDefaults.Serialize(expectedResult.NativeValue),
                    global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
    }

    internal static async Task DocumentAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        CommitToken minimum, CancellationToken token)
    {
        var reference = new EntityRef(ControlledPartitionMovementCorpus.Partition,
            ControlledPartitionMovementCorpus.Collection, ControlledPartitionMovementCorpus.DocumentId);
        var parent = new ReadExecutionBudget(Options.Create(source.Database.Limits), source.Database.EvaluationClock, token);
        var frame = source.Database.TryCaptureControlledDocumentReadV2("root", reference, minimum,
            source.Database.EvaluationClock.GetUtcNow() + TimeSpan.FromSeconds(source.Database.Limits.QueryDeadlineSeconds), parent)
            ?? throw new InvalidOperationException("The authenticated source lifetime frame is absent.");
        var bytes = Math.Min(target.Database.Limits.MaxBatchBytes, target.Database.Limits.MaxQueryReadBytes);
        var records = Math.Min(target.Database.Limits.MaxBatchMutations, target.Database.Limits.MaxScanRecords);
        var originalGrant = parent.CreateReadGrant(bytes, records);
        var receiver = new ReadExecutionBudget(Options.Create(target.Database.Limits), target.Database.EvaluationClock, token);
        var grant = receiver.CreateReadGrant(bytes, records);
        var actual = target.Database.ReadControlledDocumentV2("root", frame, receiver, grant, token);
        parent.ImportReadGrant(originalGrant, grant.ReadBytes, grant.ExaminedRecords);
        source.Database.ValidateControlledDocumentReadV2(frame, parent);
        await EqualAsync(new DocumentResult(reference, ResourceLifetimeGrantStages.UpdatedRevision,
            ResourceLifetimeGrantStages.UpdatedJson, false, []), actual);
    }
}
