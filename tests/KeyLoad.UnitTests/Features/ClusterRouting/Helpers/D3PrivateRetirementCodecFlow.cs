using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class D3PrivateRetirementCodecFlow
{
    private const string Root = "root";
    private const int Version = 1;
    private const int PageSize = 0;
    private const string Required = "The native private retirement boundary did not return its original value.";

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var listeners = new ControlledPartitionMovementLoopbackListeners();
            var corpus = new ControlledPartitionMovementLoopbackCorpus(listeners);
            using var source = new ControlledPartitionMovementNode(corpus.Control.Owner);
            using var target = new ControlledPartitionMovementNode(corpus.Destination.Owner);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(ControlledPartitionMovementSetup.SeedConfigured(source, target, corpus, out _, token).Error).IsNull();
                await ExecuteAsync(source, corpus, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementLoopbackCorpus corpus, CancellationToken token)
    {
        var request = Request(source);
        var phase = new ResourceRetirementPhaseCommandV1(Version, request.RetirementId,
            ResourceRetirementPhase.PrepareRetirement, NativeSerialization.Serialize(new ResourceRetirementPrepareBodyV1(request)));
        var operation = source.Database.CreateResourceRetirementOperation(ResourceRetirementIdentity.PreparationCommandId(request), Root,
            source.Database.EvaluationClock.GetUtcNow(), phase);
        var routing = UnitRoutingOptions.Routing();
        var codec = new GrainRequestCodec(source.Database, source.Database.EvaluationClock, routing);
        var expiry = source.Database.EvaluationClock.GetUtcNow() + routing.Value.RequestLifetime;
        var principal = GrainRequestAuthority.Reload(source.Database, Root, source.Database.EvaluationClock);
        var query = new ResourceRetirementReadRequestV1(Version, ResourceRetirementReadOperation.State,
            request, null, Guid.Empty, PageSize, expiry, corpus.Control, corpus.Control.Owner.VoterIds.First(),
            string.Empty, principal.PolicyEpoch, source.Database.Limits.MaxQueryReadBytes,
            source.Database.Limits.MaxScanRecords, source.Database.Limits.MaxBatchBytes);
        var work = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(source.Database.Limits),
            source.Database.EvaluationClock, token);
        await D3PrivateRetirementMetadataAdmission.RunAsync(work, expiry, async metadata =>
        {
            await D3PrivateRetirementCodecAssertions.RefusalsAsync(source, codec, query, operation, expiry, metadata);
            token.ThrowIfCancellationRequested();
            var signed = codec.CreateResourceRetirementCommand(Guid.NewGuid(), operation, expiry, metadata);
            var decoded = codec.Verify(signed);
            await Assert.That(GrainPartitionResolver.Resolve(decoded)).IsEqualTo(GrainRoutingProtocol.CatalogPartition);
            var result = source.Journal.Submit(GrainNativePayload.Read<ReplicatedOperation>(decoded.Payload), token);
            await Assert.That(result.Error).IsNull();
            var value = result.Get<ResourceRetirementPhaseResultV1>();
            await Assert.That(value.Receipt.CommandId).IsEqualTo(operation.Id);
            await Assert.That(value.Receipt.RetirementId).IsEqualTo(request.RetirementId);
            await Assert.That(value.Receipt.Phase).IsEqualTo(ResourceRetirementPhase.PrepareRetirement);
            await Assert.That(value.State).IsNull();
            await Assert.That(value.Fence).IsNull();
            await Assert.That(value.PreparationState!.Control.Lifetime.Incarnation).IsEqualTo(request.ExpectedIncarnation);
            await Assert.That(JsonSerializer.Serialize(value.PreparationState!.Control.Request, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(request, JsonDefaults.Options));
            await Assert.That(JsonSerializer.Serialize(value.Receipt.PhysicalOwner, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(source.PhysicalOwner, JsonDefaults.Options));
            var recovered = source.Database.ReadResourceRetirementPreparation(Root, request, work);
            await Assert.That(JsonSerializer.Serialize(recovered, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(value.PreparationState, JsonDefaults.Options));
            await Assert.That(work.ReadBytes > 0).IsTrue();
        });
    }

    private static ResourceRetirementRequestV1 Request(ControlledPartitionMovementNode source)
        => source.Store.Read(view =>
        {
            var partition = ControlledPartitionMovementCorpus.Partition;
            var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(partition.TenantId,
                partition.DatabaseId, ControlledPartitionMovementCorpus.Collection)) ?? throw new InvalidOperationException(Required);
            var lifetime = ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId,
                definition.Name, definition) ?? throw new InvalidOperationException(Required);
            return new ResourceRetirementRequestV1(Version, Guid.NewGuid(), partition.TenantId,
                partition.DatabaseId, definition.Name, lifetime.Incarnation);
        });
}
