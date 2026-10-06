using KeyLoad.Orleans;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsRoutingCases
{
    private const int EarlyFailedSequence = 1;
    private const int PostStartFailedSequence = 2;
    private const string WriterPrefix = "request-route-writer-";
    private const string ForgedPrefix = "request-route-forged-";

    internal static async Task AcCrs001NativeRequestRoutesAStableBatchThroughItsCommandGrain(
        RequestCqrsClusterFixture fixture)
    {
        fixture.Database.Configure(RequestCqrsRoutingTests.Collection, ResourceKind.Collection);
        var writer = PersistWriter(fixture, WriterPrefix);
        var documentId = DocumentId("first");
        var commandId = Guid.NewGuid();
        var command = Command(commandId, fixture.Database.Partition, documentId);
        var firstRequest = Guid.NewGuid();
        var beforePosition = fixture.Database.Store.Position;
        var firstReply = await RequestCqrsNativeCommandStream.InvokeAsync(fixture, writer, firstRequest, commandId,
            command, async () =>
            {
                await AssertDocumentMissingAsync(fixture, documentId);
                await Assert.That(fixture.Database.Store.Position).IsEqualTo(beforePosition);
            });

        await Assert.That(firstReply.Error).IsNull();
        var firstReceipt = ReadReceipt(firstReply);
        await Assert.That(firstReceipt.CommandId).IsEqualTo(commandId);
        await Assert.That(firstReceipt.Mutations.Length).IsEqualTo(1);
        var firstReceiptBytes = NativeSerialization.Serialize(firstReceipt);
        await AssertDocumentRevisionAsync(fixture, documentId, 1);
        var firstPayload = firstReply.Payload.ToArray();
        var committedPosition = fixture.Database.Store.Position;

        var retryRequest = Guid.NewGuid();
        await Assert.That(retryRequest).IsNotEqualTo(firstRequest);
        var retryCommand = Command(commandId, fixture.Database.Partition, documentId);
        var retryReply = await RequestCqrsNativeCommandStream.InvokeAsync(fixture, writer, retryRequest, commandId,
            retryCommand, async () =>
            {
                await AssertDocumentRevisionAsync(fixture, documentId, 1);
                await Assert.That(fixture.Database.Store.Position).IsEqualTo(committedPosition);
            });

        await Assert.That(retryReply.Error).IsNull();
        await Assert.That(retryReply.Payload.Span.SequenceEqual(firstPayload)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(ReadReceipt(retryReply))
            .SequenceEqual(firstReceiptBytes)).IsTrue();
        await AssertDocumentRevisionAsync(fixture, documentId, 1);
        var durableReceipt = OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Partition, writer.Id, commandId)?.Get<CommitReceipt>()
            ?? throw new InvalidOperationException("The stable command outcome has no durable receipt.");
        await Assert.That(NativeSerialization.Serialize(durableReceipt).SequenceEqual(firstReceiptBytes)).IsTrue();
    }

    internal static async Task AcCrs003RevokedCommandFailsSafelyAndFollowingRequestSucceeds(
        RequestCqrsClusterFixture fixture)
    {
        fixture.Database.Configure(RequestCqrsRoutingTests.Collection, ResourceKind.Collection);
        var writer = PersistWriter(fixture, WriterPrefix);
        var documentId = DocumentId("revoked");
        var healthyDocumentId = DocumentId("healthy");
        var commandId = Guid.NewGuid();
        var command = Command(commandId, fixture.Database.Partition, documentId);
        var requestId = Guid.NewGuid();
        var signed = fixture.Codec.CreateCommand(requestId, writer.Id, OperationKind.Batch, commandId,
            NativeSerialization.Serialize(command));
        var revoked = writer with { Revoked = true, PolicyEpoch = writer.PolicyEpoch + 1 };
        fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked));
        var deniedPosition = fixture.Database.Store.Position;

        var denied = await RequestCqrsNativeCommandStream.InvokeAsync(fixture, signed, writer, requestId, commandId,
            PostStartFailedSequence, expectedError: ErrorCode.Unauthenticated);
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(deniedPosition);
        await Assert.That(OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Partition, writer.Id, commandId)).IsNull();
        await AssertDocumentMissingAsync(fixture, documentId);

        var root = GrainRequestAuthority.Reload(fixture.Database.Database, RequestCqrsRoutingTests.RootPrincipalId,
            TimeProvider.System);
        var healthyId = Guid.NewGuid();
        var healthyRequest = Guid.NewGuid();
        var healthyCommand = Command(healthyId, fixture.Database.Partition, healthyDocumentId);
        var healthy = await RequestCqrsNativeCommandStream.InvokeAsync(fixture, root, healthyRequest, healthyId,
            healthyCommand);
        await Assert.That(healthy.Error).IsNull();
        await Assert.That(ReadReceipt(healthy).CommandId).IsEqualTo(healthyId);
        await AssertDocumentRevisionAsync(fixture, healthyDocumentId, 1);
    }

    internal static async Task AcCrs006MissingAndForgedContextFailBeforeCommandEffects(
        RequestCqrsClusterFixture fixture)
    {
        fixture.Database.Configure(RequestCqrsRoutingTests.Collection, ResourceKind.Collection);
        var writer = PersistWriter(fixture, WriterPrefix);
        var documentId = DocumentId("missing");
        var forgedDocumentId = DocumentId("forged");
        var commandId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var command = Command(commandId, fixture.Database.Partition, documentId);
        var signed = fixture.Codec.CreateCommand(requestId, writer.Id, OperationKind.Batch, commandId,
            NativeSerialization.Serialize(command));
        var beforeMissing = fixture.Database.Store.Position;
        var missing = await RequestCqrsNativeCommandStream.InvokeWithoutContextAsync(fixture, signed, requestId,
            commandId, EarlyFailedSequence, ErrorCode.TokenInvalidated);
        await Assert.That(missing.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(beforeMissing);
        await Assert.That(OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Partition, writer.Id, commandId)).IsNull();
        await AssertDocumentMissingAsync(fixture, documentId);

        var forged = PersistWriter(fixture, ForgedPrefix);
        var forgedRequest = Guid.NewGuid();
        var forgedCommandId = Guid.NewGuid();
        var forgedCommand = Command(forgedCommandId, fixture.Database.Partition,
            forgedDocumentId);
        var forgedToken = fixture.Codec.CreateCommand(forgedRequest, writer.Id, OperationKind.Batch, forgedCommandId,
            NativeSerialization.Serialize(forgedCommand));
        var beforeForged = fixture.Database.Store.Position;
        var rejected = await RequestCqrsNativeCommandStream.InvokeAsync(fixture, forgedToken, forged,
            forgedRequest, forgedCommandId, EarlyFailedSequence, expectedError: ErrorCode.Unauthenticated);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(beforeForged);
        await Assert.That(OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Partition, writer.Id, forgedCommandId)).IsNull();
        await AssertDocumentMissingAsync(fixture, forgedDocumentId);
    }

    private static string DocumentId(string purpose)
        => purpose + "-" + Guid.NewGuid().ToString("N");

    private static PrincipalRecord PersistWriter(RequestCqrsClusterFixture fixture, string idPrefix)
    {
        var record = new PrincipalRecord(idPrefix + Guid.NewGuid().ToString("N"), fixture.Database.Partition.TenantId,
            [new ScopeGrant(fixture.Database.Partition.DatabaseId, RequestCqrsRoutingTests.Collection,
                Capability.DocumentsWrite)], []);
        return fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record))
            .Get<PrincipalRecord>();
    }

    private static CommandRequest Command(Guid commandId, PartitionRef partition, string documentId)
        => new(commandId, partition,
            [new PutDocument(RequestCqrsRoutingTests.Collection, documentId, RequestCqrsRoutingTests.DocumentJson)]);

    private static CommitReceipt ReadReceipt(GrainOperationReply reply)
        => NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as CommitReceipt
            ?? throw new InvalidOperationException("The command reply did not contain its canonical CommitReceipt.");

    private static async Task AssertDocumentMissingAsync(RequestCqrsClusterFixture fixture, string id)
    {
        var document = fixture.Database.Database.GetDocument(RequestCqrsRoutingTests.RootPrincipalId,
            new EntityRef(fixture.Database.Partition, RequestCqrsRoutingTests.Collection, id));
        await Assert.That(document).IsNull();
    }

    private static async Task AssertDocumentRevisionAsync(RequestCqrsClusterFixture fixture, string id,
        long expectedRevision)
    {
        var document = fixture.Database.Database.GetDocument(RequestCqrsRoutingTests.RootPrincipalId,
            new EntityRef(fixture.Database.Partition, RequestCqrsRoutingTests.Collection, id));
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Revision).IsEqualTo(expectedRevision);
    }
}

internal static class RequestCqrsNativeCommandStream
{
    private const int StartedSequence = 1;
    private const int CompletedSequence = 2;
    private const int EarlyFailedSequence = 1;
    private const int PostStartFailedSequence = 2;

    internal static Task<GrainOperationReply> InvokeAsync(RequestCqrsClusterFixture fixture, PrincipalRecord principal,
        Guid requestId, Guid commandId, CommandRequest command, Func<Task>? beforeFirstPull = null)
    {
        var signed = fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.Batch, commandId,
            NativeSerialization.Serialize(command));
        return InvokeAsync(fixture, signed, principal, requestId, commandId, CompletedSequence,
            beforeFirstPull: beforeFirstPull);
    }

    internal static Task<GrainOperationReply> InvokeAsync(RequestCqrsClusterFixture fixture, string signed,
        PrincipalRecord contextPrincipal, Guid requestId, Guid commandId, int terminalSequence,
        ErrorCode? expectedError = null, Func<Task>? beforeFirstPull = null)
        => InvokeCoreAsync(fixture, signed, contextPrincipal, true, requestId, commandId, terminalSequence,
            expectedError, beforeFirstPull);

    internal static Task<GrainOperationReply> InvokeWithoutContextAsync(RequestCqrsClusterFixture fixture,
        string signed, Guid requestId, Guid commandId, int terminalSequence, ErrorCode expectedError)
        => InvokeCoreAsync(fixture, signed, null, false, requestId, commandId, terminalSequence, expectedError);

    private static async Task<GrainOperationReply> InvokeCoreAsync(RequestCqrsClusterFixture fixture, string signed,
        PrincipalRecord? contextPrincipal, bool publishContext, Guid requestId, Guid commandId, int terminalSequence,
        ErrorCode? expectedError, Func<Task>? beforeFirstPull = null)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRoutingTests.InvocationBound);
        using IDisposable context = publishContext
            ? new GrainRequestIdentityScope(fixture.Cluster.ServiceProvider, contextPrincipal,
                requestId, commandId, deadline.Token)
            : RequestCqrsClientContext.Set(null, false, null, includeState: false);
        var actor = fixture.Cluster.Client.GetGrain<IRequestGrain>(requestId);
        var stream = actor.ExecuteStreamAsync(signed, deadline.Token)
            .WithBatchSize(GrainRequestStreamProtocol.BatchSize);
        if (beforeFirstPull is not null)
        {
            await beforeFirstPull();
        }

        await using var iterator = stream.GetAsyncEnumerator(deadline.Token);
        if (!await iterator.MoveNextAsync())
        {
            throw new InvalidOperationException("The request stream ended before its first native chunk.");
        }

        var first = iterator.Current;
        if (terminalSequence == EarlyFailedSequence)
        {
            await AssertFailedAsync(first, EarlyFailedSequence, expectedError!.Value);
            await Assert.That(await iterator.MoveNextAsync()).IsFalse();
            return FailedReply(first);
        }

        await AssertStartedAsync(first, requestId);
        if (!await iterator.MoveNextAsync())
        {
            throw new InvalidOperationException("The request stream ended before its terminal native chunk.");
        }

        var terminal = iterator.Current;
        if (expectedError is { } error)
        {
            await AssertFailedAsync(terminal, PostStartFailedSequence, error);
        }
        else
        {
            await AssertCompletedAsync(terminal, CompletedSequence);
        }

        await Assert.That(await iterator.MoveNextAsync()).IsFalse();
        return expectedError is null ? TerminalReply(terminal) : FailedReply(terminal);
    }

    private static async Task AssertStartedAsync(
        CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, Guid requestId)
    {
        await Assert.That(chunk.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(chunk.Sequence).IsEqualTo(StartedSequence);
        await Assert.That(chunk.EventType).IsEqualTo(
            CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(CqrsStreamChunkKind.Started));
        await Assert.That(chunk.ProgressResult?.Value?.RequestId).IsEqualTo(requestId);
        await Assert.That(chunk.Final).IsNull();
    }

    private static async Task AssertCompletedAsync(
        CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, int sequence)
    {
        await Assert.That(chunk.Kind).IsEqualTo(CqrsStreamChunkKind.Completed);
        await Assert.That(chunk.Sequence).IsEqualTo(sequence);
        await Assert.That(chunk.EventType).IsEqualTo(
            CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(CqrsStreamChunkKind.Completed));
        await Assert.That(chunk.ProgressResult).IsNull();
        await Assert.That(chunk.Final?.IsSuccess).IsTrue();
        await Assert.That(chunk.Final?.Problem).IsNull();
    }

    private static async Task AssertFailedAsync(
        CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, int sequence, ErrorCode expectedError)
    {
        await Assert.That(chunk.Kind).IsEqualTo(CqrsStreamChunkKind.Failed);
        await Assert.That(chunk.Sequence).IsEqualTo(sequence);
        await Assert.That(chunk.EventType).IsEqualTo(
            CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(CqrsStreamChunkKind.Failed));
        await Assert.That(chunk.ProgressResult).IsNull();
        await Assert.That(chunk.Final?.IsSuccess).IsFalse();
        await Assert.That(chunk.Final?.Value).IsNull();
        var problem = chunk.Final?.Problem ?? throw new InvalidOperationException("The failure terminal has no Problem.");
        await Assert.That(GrainRequestStreamProblem.ReadCode(problem, UnitRoutingOptions.Routing())).IsEqualTo(expectedError);
        await Assert.That((problem.Detail ?? string.Empty).Length).IsGreaterThan(0);
    }

    private static GrainOperationReply TerminalReply(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk)
        => chunk.Final?.Value ?? throw new InvalidOperationException("The completed request has no final value.");

    private static GrainOperationReply FailedReply(
        CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk)
    {
        var problem = chunk.Final?.Problem ?? throw new InvalidOperationException("The failed request has no Problem.");
        return new() { Error = GrainRequestStreamProblem.ReadCode(problem, UnitRoutingOptions.Routing()), SafeDetail = problem.Detail };
    }
}
