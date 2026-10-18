using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.ResourceExecution;
using D = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionPrefixPrepareData;
using R = KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

// Genuine six independent LOCAL native stores/phases; no consensus or CLUSTER qualification is represented.
internal static class CompositionPrefixPreparePrivacy
{
    internal static Task RunAsync() => ResourceRetirementPreparationValidationFixture.RunAsync(async (state, token) =>
    {
        var source = state.Nodes[R.SourceIndex];
        var sources = state.Nodes.Take(R.Voters).ToArray();
        var issued = await IssueThenRevokeAsync(sources, state.Preparation.Control.Lifetime, token);
        var expectedBeginPosition = source.Journal.Log.State.LastIndex + R.One;
        foreach (var owner in sources)
        {
            await Assert.That(owner.Journal.Log.State.LastIndex + R.One).IsEqualTo(expectedBeginPosition);
        }
        var statuses = ResourceRetirementPreparationValidationSeed.ReadStatuses(state.Nodes, state.Preparation.Control, token);
        await ResourceRetirementAttemptAdmissionFlow.ExecuteAsync(state with
        {
            Statuses = statuses
        }, token);
        var current = source.Database.ReadResourceRetirementState(D.Root, state.Preparation.Control.Request,
            ResourceRetirementPreparationValidationFixture.Work(source, token));
        await Assert.That(current.Stage).IsEqualTo(ResourceRetirementStage.Retiring);
        await Assert.That(current.Control.BeginReceipt.AppliedPosition).IsEqualTo(expectedBeginPosition);
        await Assert.That(current.Control.Lifetime).IsEqualTo(state.Preparation.Control.Lifetime);
        await RefusalAndReplayAsync(source, issued.Original, issued.Bytes, token);
    });

    private static async Task<(ReplicatedOperation Original, byte[] Bytes)> IssueThenRevokeAsync(
        ControlledPartitionMovementNode[] sources, ResourceLifetimeV1 documents, CancellationToken token)
    {
        var source = sources[R.SourceIndex];
        var partition = ControlledPartitionMovementCorpus.Partition;
        var expectedLifetimes = new HashSet<ResourceLifetimeV1> { documents };
        foreach (var (name, kind) in new[] { (D.Graph, ResourceKind.Graph), (D.Queue, ResourceKind.WorkQueue) })
        {
            var definition = new ResourceDefinition(name, kind, partition.TransactionDomainId);
            var operation = source.Database.CreateNativeOperation(OperationKind.ConfigureResource, Guid.NewGuid(), D.Root,
                source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new ConfigureResourceRequest(
                    partition.TenantId, partition.DatabaseId, definition)));
            var lifetime = ResourceLifetimeIdentity.Create(operation, partition.TenantId, partition.DatabaseId, definition);
            expectedLifetimes.Add(lifetime);
            await CompositionPrefixPreparePrivacySeed.SubmitAsync(sources, operation, definition, token);
            foreach (var owner in sources)
            {
                await CompositionPrefixPrepareAssertions.EqualAsync(lifetime, owner.Store.Read(view => ResourceLifetimeCatalog.Read(
                    view, partition.TenantId, partition.DatabaseId, definition.Name, definition)));
            }
        }
        var actor = Principal(partition, documents.ResourceName, allowed: true);
        await ConfigureAsync(sources, actor, token);
        var vertex = new EntityRef(partition, documents.ResourceName, ControlledPartitionMovementCorpus.DocumentId);
        var payload = JsonSerializer.Serialize(new QueueGraphLink(vertex, vertex, D.Label), JsonDefaults.Options);
        var seed = new CommandRequest(Guid.NewGuid(), partition, [new EnqueueMessage(D.Queue, D.Message, payload)]);
        var enqueue = source.Database.CreateNativeOperation(OperationKind.Batch, seed.CommandId, D.Root,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(seed));
        var expectedReceipt = new CommitReceipt(seed.CommandId,
            new(source.PhysicalOwner.Incarnation, partition.AtomicPartitionId,
                source.Journal.Log.State.LastIndex + R.One, source.PhysicalOwner.PlacementEpoch),
            [new(D.Enqueue, D.Queue, D.Message, D.FirstRevision)], DurabilityProfile.ProcessDurable);
        await CompositionPrefixPreparePrivacySeed.SubmitAsync(sources, enqueue, expectedReceipt, token);
        var request = new CommandRequest(Guid.NewGuid(), partition,
            [CompositionPrefixPrepareCommands.Forward(typed: false)]);
        var original = source.Database.CreateNativeOperation(OperationKind.Batch, request.CommandId, actor.Id,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request),
            ResourceRetirementPreparationValidationFixture.Work(source, token));
        var bytes = NativeSerialization.Serialize(original);
        var admission = ResourceEffectAdmissionValidation.Read(original, source.Database.Limits);
        await Assert.That(admission.Error).IsNull();
        await Assert.That(expectedLifetimes.SetEquals(admission.Bindings)).IsTrue();
        await ConfigureAsync(sources, Principal(partition, documents.ResourceName, allowed: false), token);
        await Assert.That(NativeSerialization.Serialize(original).SequenceEqual(bytes)).IsTrue();
        return (original, bytes);
    }

    private static PrincipalRecord Principal(PartitionRef partition, string collection, bool allowed)
    {
        var grants = new List<ScopeGrant>
        {
            new(partition.DatabaseId, D.Graph, Capability.GraphWrite),
            new(partition.DatabaseId, D.Queue, Capability.QueueInspect)
        };
        if (allowed)
        {
            grants.Add(new(partition.DatabaseId, collection, Capability.DocumentsRead));
        }
        return new(D.RevokedReader, partition.TenantId, [.. grants], [])
        {
            PolicyEpoch = allowed ? D.FirstRevision : D.CancelRevision
        };
    }

    private static async Task ConfigureAsync(ControlledPartitionMovementNode[] sources,
        PrincipalRecord actor, CancellationToken token)
    {
        var source = sources[R.SourceIndex];
        var original = source.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(), D.Root,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new ConfigurePrincipalRequest(actor)));
        await CompositionPrefixPreparePrivacySeed.SubmitAsync(sources, original, actor, token);
        foreach (var owner in sources)
        {
            await CompositionPrefixPrepareAssertions.EqualAsync(actor,
                owner.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(actor.Id))));
        }
    }

    private static async Task RefusalAndReplayAsync(ControlledPartitionMovementNode source, ReplicatedOperation original,
        byte[] bytes, CancellationToken token)
    {
        var partition = ControlledPartitionMovementCorpus.Partition;
        var modelImage = CompositionPrefixPrepareAssertions.ModelImage(source.Store, partition);
        var expectedIndex = source.Journal.Log.State.LastIndex + D.FirstRevision;
        var expected = new OperationResult(null, ErrorCode.PermissionDenied, D.ScopeDenied);
        var result = source.Journal.Submit(original, token);
        await CompositionPrefixPrepareAssertions.EqualAsync(expected, result);
        await Assert.That(result.NativeValue).IsNull();
        await Assert.That(CompositionPrefixPrepareAssertions.ModelImage(source.Store, partition)
            .SequenceEqual(modelImage, StringComparer.Ordinal)).IsTrue();
        var retained = source.Journal.Log.Read(expectedIndex, D.FirstRevision, source.Journal.Configuration.MaxAppendBytes).Single().Operation!;
        await Assert.That(NativeSerialization.Serialize(retained).SequenceEqual(bytes)).IsTrue();
        var outcome = OutcomeStoreOracle.ReadStored(source.Store, original)!;
        await Assert.That(outcome.PolicyEpoch).IsEqualTo((long)D.CancelRevision);
        await Assert.That(outcome.Fingerprint).IsEqualTo(NativeOperationFingerprint.Compute(original));
        await CompositionPrefixPrepareAssertions.EqualAsync(expected, outcome.Result);
        await Assert.That(outcome.Result.NativeValue).IsNull();
        var image = QueueWholeFlowStorage.Bytes(source.Store);
        var position = source.Store.Position;
        var applied = source.Database.LastApplied;
        var replay = source.Journal.Submit(original, token);
        await CompositionPrefixPrepareAssertions.EqualAsync(expected, replay);
        await Assert.That(replay.NativeValue).IsNull();
        var resolved = source.Database.ResolveOutcome(original);
        await CompositionPrefixPrepareAssertions.EqualAsync(expected, resolved);
        await Assert.That(resolved.NativeValue).IsNull();
        await Assert.That(NativeSerialization.Serialize(original).SequenceEqual(bytes)).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(source.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(expectedIndex);
    }
}
