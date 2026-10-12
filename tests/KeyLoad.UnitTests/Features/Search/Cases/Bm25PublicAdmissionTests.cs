namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25PublicAdmissionTests
{
    [Test]
    public async Task PrivatePublicationRejectsGenericFactoriesAndCatalogWithoutEffectsThenHealthyCommandCommits()
    {
        await ReplicaSubmissionOutcomeFlow.RunAsync(async flow =>
        {
            var original = flow.Create("bm25-boundary", "{\"value\":1}");
            var payload = NativeSerialization.Serialize(new CommandRequest(original.Id, flow.Database.Partition,
                [new PutDocument(ReplicaSubmissionOutcomeFlow.Resource, "bm25-boundary", "{\"value\":1}")]));
            var before = ReplicaSubmissionOutcomeState.Read(flow.Database);
            var native = Assert.ThrowsExactly<KeyLoadException>(() => flow.Database.Database.CreateNativeOperation(
                OperationKind.Bm25CorpusPublicationPhase, original.Id, original.PrincipalId, original.EvaluatedAt, payload));
            await Assert.That(native.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
            var codec = new KeyLoad.Orleans.GrainRequestCodec(flow.Database.Database, TimeProvider.System, UnitRoutingOptions.Routing());
            var generic = Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateCommand(Guid.NewGuid(),
                original.PrincipalId, OperationKind.Bm25CorpusPublicationPhase, original.Id, payload));
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
            await before.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
            var healthyDescriptor = CopyDescriptor(descriptor, OperationKind.Batch);
            await Assert.That(healthyDescriptor.CreateTool().Name).IsEqualTo(descriptor.Name);
            var signed = codec.CreateCommand(Guid.NewGuid(), original.PrincipalId, OperationKind.Batch, original.Id, payload);
            await Assert.That(codec.Verify(signed).Envelope.CommandKind).IsEqualTo(OperationKind.Batch);
            var result = flow.Submit(original);
            await Assert.That(result.Error).IsNull();
            await ReplicaSubmissionOutcomeFlow.RequireResultAsync(result,
                flow.Database.Database.ResolveSubmissionOutcome(original));
            await Assert.That(flow.Document("bm25-boundary").Length).IsGreaterThan(0);
        });
    }

    [Test]
    public Task GenuineCaptureUsesPrivateRouteVerifiedSubmitReplayAndChangedIdentityRefusalThenHealthyPublication()
        => Bm25PrivateAdmissionFlow.RunAsync();

    private static KeyLoad.Server.McpOperationDescriptor CopyDescriptor(KeyLoad.Server.McpOperationDescriptor original, OperationKind kind)
        => new(original.Name, original.Route, original.ReadKind, kind, original.Description,
            original.InputSchema, original.OutputSchema, KeyLoad.Server.McpToolHints.ForCommand(OperationKind.Batch), original.Decode);
}
