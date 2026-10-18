using KeyLoad.UnitTests.Features.ResourceExecution;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25ColdInvalidationTrial
{
    internal const string Collection = "orders";
    internal const string Field = "/text";
    internal const string Json = "{\"text\":\"needle repaired\"}";
    internal const string ReadGrant = "cold.corpus.read";
    internal const string UseGrant = "cold.corpus.use";

    internal static async Task RunAsync(Bm25OriginalPhaseRecoveryFlow flow)
    {
        var original = flow.Capture(null);
        var result = flow.Publish(original);
        await flow.RequireHealthyAsync(original, result);
        var entry = NativeSerialization.Serialize(flow.Owner.Entry(original.CommandId));
        var head = NativeSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((original).Value!.Input.Span).Current;
        var rawHead = Bm25ColdInvalidationAssertions.HeadBytes(flow.Owner, head);
        var tail = Tail(flow);
        var mutations = new (Command<DatabaseCommand> Operation, Result<object> Result, byte[] Entry)[4];
        mutations[0] = await MutationAsync(flow, new PatchDocument(Collection, "first",
            [new(Field, PatchKind.Set, "\"needle changed\"")], 1), tail + 1);
        mutations[1] = await MutationAsync(flow, new PutDocument(Collection, "first", "{\"text\":\"needle extra extra\"}", 2,
            new RowAccess(OwnerId: Bm25OriginalPhaseRecoveryFlow.Subject), ExplicitReplacement: true), tail + 2);
        mutations[2] = await MutationAsync(flow, new DeleteDocument(Collection, "first", 3), tail + 3);
        mutations[3] = await MutationAsync(flow, new PutDocument(Collection, "first", Json), tail + 4);
        await NativeReplayResultAssertions.BytesAsync(rawHead, Bm25ColdInvalidationAssertions.HeadBytes(flow.Owner, head));
        await Bm25ColdInvalidationAssertions.ColdAsync(flow, original, entry);
        await NativeReplayResultAssertions.BytesAsync(rawHead, Bm25ColdInvalidationAssertions.HeadBytes(flow.Owner, head));
        await Bm25ColdInvalidationAssertions.MutationReplayAsync(flow, mutations);
        await Bm25ColdInvalidationAssertions.SearchAsync(flow, expectedCurrent: false);
        await Bm25ColdInvalidationAssertions.ReplayAsync(flow, original, result, entry);
        await PolicyAsync(flow, original, tail + 4);
    }

    private static async Task<(Command<DatabaseCommand> Operation, Result<object> Result, byte[] Entry)> MutationAsync(Bm25OriginalPhaseRecoveryFlow flow, Mutation mutation, long tail)
    {
        var owner = flow.Owner;
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, owner.Source.Partition, [mutation]);
        var operation = Issue(flow, OperationKind.Batch, id, request);
        var result = owner.Submit(operation);
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        await Assert.That(result.Get<CommitReceipt>().CommandId).IsEqualTo(id);
        var entry = NativeSerialization.Serialize(owner.Entry(id));
        var before = Bm25OriginalPhaseRecoveryState.Read(owner);
        await NativeReplayResultAssertions.SameAsync(result, owner.Submit(operation));
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
        await NativeReplayResultAssertions.BytesAsync(entry, NativeSerialization.Serialize(owner.Entry(id)));
        await Assert.That(Tail(flow)).IsEqualTo(tail);
        return (operation, result, entry);
    }

    private static async Task PolicyAsync(Bm25OriginalPhaseRecoveryFlow flow, Command<DatabaseCommand> original, long tail)
    {
        var owner = flow.Owner;
        var revoked = flow.Configure(flow.Reader with
        {
            PolicyEpoch = 2,
            Grants = [new(owner.Source.Partition.DatabaseId, Collection, Capability.DocumentsRead)]
        });
        await Assert.That(revoked.PolicyEpoch).IsEqualTo(2L);
        var resource = owner.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId, Collection)))!;
        var changed = owner.Submit(Issue(flow, OperationKind.ConfigureResource, Guid.NewGuid(),
            new ConfigureResourceRequest(owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId,
                resource with
                {
                    SchemaVersion = resource.SchemaVersion + 1,
                    FieldPolicies = [new(Field, "cold-private", ReadGrant, UseGrant, "cold.corpus.write")]
                })
            {
                ExpectedSchemaVersion = resource.SchemaVersion
            })).Get<ResourceDefinition>();
        await Assert.That(changed.SchemaVersion).IsEqualTo(resource.SchemaVersion + 1);
        await Assert.That(Tail(flow)).IsEqualTo(tail);
        var before = Bm25OriginalPhaseRecoveryState.Read(owner);
        owner.Reopen();
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
        await Bm25ColdInvalidationAssertions.DeniedAsync(flow, original);
        var repaired = flow.Configure(flow.Reader with
        {
            PolicyEpoch = 3
        });
        await Assert.That(repaired.PolicyEpoch).IsEqualTo(3L);
        await Bm25ColdInvalidationAssertions.DeniedAsync(flow, original);
        repaired = flow.Configure(repaired with
        {
            PolicyEpoch = 4,
            FieldGrants = [ReadGrant, UseGrant]
        });
        await Assert.That(repaired.PolicyEpoch).IsEqualTo(4L);
        await Assert.That(Tail(flow)).IsEqualTo(tail);
        await Bm25ColdInvalidationAssertions.SearchAsync(flow, expectedCurrent: false);
        await Bm25ColdInvalidationAssertions.PhaseDeniedAsync(flow, original);
        var fresh = flow.Capture(original.CommandId);
        var result = flow.Publish(fresh);
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        await Assert.That(result.Get<CommitReceipt>().CommandId).IsEqualTo(fresh.CommandId);
        var phase = NativeSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((fresh).Value!.Input.Span);
        await Assert.That(phase.Current.PolicyEpoch).IsEqualTo(4L);
        await Assert.That(phase.Current.Statistics.DocumentCount).IsEqualTo(1);
        await Assert.That(phase.Current.Statistics.TotalLength).IsEqualTo(2L);
        await Assert.That(phase.Current.Statistics.Terms.Single()).IsEqualTo("needle");
        await Assert.That(phase.Current.Statistics.DocumentFrequencies.Single()).IsEqualTo(1);
        await Assert.That(phase.Current.SchemaVersion).IsEqualTo(changed.SchemaVersion);
        await Assert.That(phase.Current.SourceOutboxTail).IsEqualTo(tail);
        await Bm25ColdInvalidationAssertions.SearchAsync(flow, expectedCurrent: true);
        await Bm25ColdInvalidationAssertions.ReplayAsync(flow, fresh, result,
            NativeSerialization.Serialize(owner.Entry(fresh.CommandId)));
    }

    private static Command<DatabaseCommand> Issue<T>(Bm25OriginalPhaseRecoveryFlow flow, OperationKind kind, Guid id, T payload)
        => flow.Owner.Database.CreateCommand(kind, id, "root",
            flow.Owner.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(payload));

    private static long Tail(Bm25OriginalPhaseRecoveryFlow flow)
        => flow.Owner.Database.GetOutboxStatus("root", flow.Owner.Source.Partition).Head.Tail;
}
