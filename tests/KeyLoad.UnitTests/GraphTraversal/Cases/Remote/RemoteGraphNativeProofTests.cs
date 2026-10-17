using KeyLoad.Core;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class RemoteGraphNativeProofTests
{
    [Test]
    [Arguments(OperationKind.RemoteGraphSource, typeof(RemoteGraphSourceMutationV1))]
    [Arguments(OperationKind.RemoteGraphReceive, typeof(RemoteGraphReceiverMutationV1))]
    [Arguments(OperationKind.RemoteGraphComplete, typeof(RemoteGraphCompletionMutationV1))]
    public Task PrivatePhaseHasTypedNativeContractButGenericJsonCannotPromoteIt(OperationKind kind, Type expected)
        => RemoteGraphNativeProofFlow.RunAsync(async database =>
        {
            await Assert.That(DatabaseEngine.NativeOperationPayloadType(kind)).IsEqualTo(expected);
            var id = Guid.NewGuid();
            var before = RemoteGraphNativeProofFlow.Image(database);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.CreateNativeOperation(
                kind, id, "root", database.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(true)));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
            await Assert.That(failure.Message).IsEqualTo("The operation is unsupported.");
            var normalized = database.Database.NormalizeOperation(new(id, kind, "root",
                database.Database.EvaluationClock.GetUtcNow(), "true"));
            var verified = database.Database.VerifyOperationAuthority(normalized);
            var payload = NativeSerialization.Deserialize<NativeCommandPayload>(verified.NativePayload.Span);
            await Assert.That(payload.Error).IsEqualTo(ErrorCode.UnsupportedCapability);
            await Assert.That(payload.SafeDetail).IsEqualTo("The operation is unsupported.");
            await Assert.That(payload.Value.IsEmpty).IsTrue();
            await Assert.That(RemoteGraphNativeProofFlow.Image(database)).IsEqualTo(before);
            database.Configure("native-graph-boundary-healthy", ResourceKind.WorkQueue);
            database.Commit(new EnqueueMessage("native-graph-boundary-healthy", "healthy", "{}", "{}"));
            await Assert.That(database.Database.InspectMessage("root",
                new(database.Partition, "native-graph-boundary-healthy"), "healthy")!.PayloadJson).IsEqualTo("{}");
        });
}
