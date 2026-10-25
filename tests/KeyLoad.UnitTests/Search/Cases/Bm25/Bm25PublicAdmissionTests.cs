using KeyLoad.UnitTests.ResourceExecution;
namespace KeyLoad.UnitTests.Search;

internal sealed class Bm25PublicAdmissionTests
{
    [Test]
    public async Task PrivatePublicationRejectsGenericFactoriesAndCatalogWithoutEffectsThenHealthyCommandCommits()
    {
        await Bm25CorpusPublicationFlow.RunAsync(async flow =>
        {
            var id = Guid.NewGuid();
            var original = flow.Database.Database.CreateCommand(OperationKind.Batch, id, Bm25CorpusPublicationFlow.Root,
                flow.Database.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(new CommandRequest(id,
                    flow.Database.Partition, [new PutDocument(Bm25CorpusPublicationFlow.Collection, "bm25-boundary", "{\"value\":1}")])));
            var payload = OrleansSerialization.Serialize(new CommandRequest(original.CommandId, flow.Database.Partition,
                [new PutDocument(Bm25CorpusPublicationFlow.Collection, "bm25-boundary", "{\"value\":1}")]));
            var before = CanonicalStoreCut.Read(flow.Database);
            var execution = Assert.ThrowsExactly<KeyLoadException>(() => flow.Database.Database.CreateCommand(
                OperationKind.Bm25CorpusPublicationPhase, original.CommandId, original.UserId!, new DateTimeOffset(original.Timestamp), payload));
            await Assert.That(execution.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
            var codec = new KeyLoad.Clustering.GrainRequestCodec(flow.Database.Database.Store.Identity.Incarnation, TimeProvider.System, UnitRoutingOptions.Routing());
            var generic = Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateCommand(Guid.NewGuid(),
                original.UserId!, OperationKind.Bm25CorpusPublicationPhase, original.CommandId, payload));
            await Assert.That(generic.Code).IsEqualTo(ErrorCode.TokenInvalidated);
            var privateWrongKind = Assert.ThrowsExactly<KeyLoadException>(() =>
                codec.CreateBm25CorpusPublication(Guid.NewGuid(), original, TimeProvider.System.GetUtcNow().AddSeconds(1)));
            await Assert.That(privateWrongKind.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(KeyLoad.Server.McpOperationCatalog.Entries.Any(entry =>
                entry.CommandKind == OperationKind.Bm25CorpusPublicationPhase)).IsFalse();
            var descriptor = KeyLoad.Server.McpOperationCatalog.Entries.First(entry => entry.CommandKind == OperationKind.Batch);
            var catalog = Assert.ThrowsExactly<ArgumentException>(() => CopyDescriptor(descriptor,
                OperationKind.Bm25CorpusPublicationPhase));
            await Assert.That(catalog.ParamName).IsEqualTo("commandKind");
            await before.RequireAsync(CanonicalStoreCut.Read(flow.Database));
            var healthyDescriptor = CopyDescriptor(descriptor, OperationKind.Batch);
            await Assert.That(healthyDescriptor.CreateTool().Name).IsEqualTo(descriptor.Name);
            var envelope = codec.CreateCommand(Guid.NewGuid(), original.UserId!, OperationKind.Batch, original.CommandId, payload);
            await Assert.That(codec.Decode(envelope).Envelope.CommandKind).IsEqualTo(OperationKind.Batch);
            var result = flow.Database.SubmitIssued(original);
            await Assert.That(Errors.Code(result.Problem).Value).IsNull();
            await ReplayResultAssertions.SameAsync(result,
                flow.Database.Database.ResolveOutcome(original));
            await Assert.That(flow.Database.Database.GetDocument(Bm25CorpusPublicationFlow.Root,
                new(flow.Database.Partition, Bm25CorpusPublicationFlow.Collection, "bm25-boundary"))!.Json)
                .IsEqualTo("{\"value\":1}");
        });
    }

    [Test]
    public Task GenuineCaptureUsesPrivateRouteVerifiedSubmitReplayAndChangedIdentityRefusalThenHealthyPublication()
        => Bm25PrivateAdmissionFlow.RunAsync();

    private static KeyLoad.Server.McpOperationDescriptor CopyDescriptor(KeyLoad.Server.McpOperationDescriptor original, OperationKind kind)
        => new(original.Name, original.Route, original.ReadKind, kind, original.Description,
            original.InputSchema, original.OutputSchema, KeyLoad.Server.McpToolHints.ForCommand(OperationKind.Batch), original.Decode);
}
