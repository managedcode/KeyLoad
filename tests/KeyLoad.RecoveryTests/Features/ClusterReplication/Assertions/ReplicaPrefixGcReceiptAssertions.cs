using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaPrefixGcReceiptAssertions
{
    private const string PutKind = "putDocument";
    private const string Collection = "orders";
    private const string DocumentId = "document";
    private const int SingleMutation = 1;
    private const int ResourceCreationCut = 1;
    private const int FirstDocumentCut = 2;
    private const int RevisionOffset = 1;
    private const long OwnershipEpoch = 1;

    internal static async Task DocumentAsync(ReplicaCrashNode node, int cut)
    {
        var reference = ExpectedDocument();
        var document = node.Database.GetDocument(ReplicaCrashModel.PrincipalId, reference);
        var expected = new DocumentResult(reference, cut - RevisionOffset, ReplicaCrashModel.JsonAt(cut), false, []);
        await Assert.That(document).IsNotNull();
        await Assert.That(JsonDefaults.Serialize(document!).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(node.Database.LastApplied).IsEqualTo((long)cut);
    }

    internal static EntityRef ExpectedDocument()
    {
        // The child applies this deterministic original before either snapshot or tail commands.
        var original = ReplicaCrashModel.Operation(ResourceCreationCut);
        var request = JsonDefaults.Deserialize<ConfigureResourceRequest>(original.PayloadJson);
        var lifetime = ResourceLifetimeIdentity.Create(original, request.TenantId,
            request.DatabaseId, request.Definition);
        return ReplicaCrashModel.Document with
        {
            Lifetime = new(lifetime.Incarnation.Value)
        };
    }

    internal static async Task AllOriginalAsync(ReplicaCrashNode node, int lastCut)
    {
        for (var cut = FirstDocumentCut; cut <= lastCut; cut++)
        {
            await VerifyAsync(node, cut);
        }
    }

    internal static async Task VerifyAsync(ReplicaCrashNode node, int cut)
        => await VerifyOperationAsync(node, ReplicaCrashModel.Operation(cut), cut);

    internal static async Task VerifyOperationAsync(ReplicaCrashNode node, ReplicatedOperation operation, int cut)
    {
        var original = OutcomeStoreOracle.Read(node.Canonical, operation)!.Get<CommitReceipt>();
        var expected = new MutationReceipt(PutKind, Collection, DocumentId, cut - RevisionOffset);
        await Assert.That(original.CommandId).IsEqualTo(operation.Id);
        await Assert.That(original.Token.Incarnation).IsEqualTo(node.Configuration.Incarnation);
        await Assert.That(original.Token.AtomicPartitionId).IsEqualTo(ReplicaCrashModel.Document.Partition.AtomicPartitionId);
        await Assert.That(original.Token.Position).IsEqualTo((long)cut);
        await Assert.That(original.Token.OwnershipEpoch).IsEqualTo(OwnershipEpoch);
        await Assert.That(original.Durability).IsEqualTo(node.Canonical.Identity.Durability);
        await Assert.That(original.Mutations.Length).IsEqualTo(SingleMutation);
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(original.Mutations.Single())))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(expected)));
        var before = ReplicaPrefixGcRecoveryOracle.CanonicalBytes(node);
        var position = node.Canonical.Position;
        var replay = node.Database.Apply(operation).Get<CommitReceipt>();
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(replay)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(original)));
        await Assert.That(node.Canonical.Position).IsEqualTo(position);
        await Assert.That(ReplicaPrefixGcRecoveryOracle.CanonicalBytes(node).SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
    }
}
