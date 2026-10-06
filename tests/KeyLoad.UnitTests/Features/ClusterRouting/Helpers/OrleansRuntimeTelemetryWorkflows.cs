using KeyLoad.Orleans;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class OrleansRuntimeTelemetryWorkflows
{
    internal static async Task RunAsync(RequestCqrsClusterFixture fixture)
    {
        await using var telemetry = await OrleansRuntimeTelemetryFixture.StartAsync();
        fixture.Database.Configure(OrleansRuntimeTelemetryTokens.Collection, ResourceKind.Collection);
        var first = PersistPrincipal(fixture, "first");
        var second = PersistPrincipal(fixture, "second");
        var third = PersistPrincipal(fixture, "third");
        await WriteWithSentinelAsync(fixture, telemetry, first, OrleansRuntimeTelemetryMutation.Event);
        await WriteWithSentinelAsync(fixture, telemetry, first, OrleansRuntimeTelemetryMutation.Baggage);
        await WriteWithSentinelAsync(fixture, telemetry, first, OrleansRuntimeTelemetryMutation.Link);
        await WriteWithSentinelAsync(fixture, telemetry, first, OrleansRuntimeTelemetryMutation.ErrorStatus);
        var parentage = new List<OrleansTelemetryOperationParent>(await ReadRealDocumentAsync(fixture, telemetry, first));
        var concurrent = await Task.WhenAll(WriteWithParentAsync(fixture, telemetry, second),
            WriteWithParentAsync(fixture, telemetry, third));
        await Assert.That(concurrent[0].Reply.Error).IsNull();
        await Assert.That(concurrent[1].Reply.Error).IsNull();
        await Assert.That(concurrent[0].Parent.TraceId).IsNotEqualTo(concurrent[1].Parent.TraceId);
        parentage.AddRange(concurrent.Select(static operation => operation.Parent));
        await FailRevokedWriteAsync(fixture, first);
        telemetry.Flush();
        var traces = telemetry.Activities.Snapshot();
        var metrics = telemetry.Metrics.Snapshot();
        await Assert.That(telemetry.Activities.WasTruncated).IsFalse();
        await Assert.That(telemetry.Metrics.WasTruncated).IsFalse();
        await OrleansRuntimeTelemetryAssertions.AssertTracePrivacyAsync(traces, first, second, third, parentage,
            telemetry.Options);
        await OrleansRuntimeTelemetryAssertions.AssertMetricPrivacyAsync(metrics);
    }

    private static async Task WriteWithSentinelAsync(RequestCqrsClusterFixture fixture,
        OrleansRuntimeTelemetryFixture telemetry, PrincipalRecord principal, OrleansRuntimeTelemetryMutation mutation)
    {
        using var sentinel = new OrleansRuntimeTelemetrySentinel(mutation,
            telemetry.Options.MaximumBaggageItems);
        using var parent = telemetry.StartParentActivity()
            ?? throw new InvalidOperationException("The telemetry provider did not sample the operation parent.");
        var documentId = NewDocumentId();
        var requestId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var signed = SignWrite(fixture, principal, requestId, commandId, documentId);
        var reply = await RequestCqrsNativeCommandStream.InvokeAsync(fixture, signed, principal, requestId,
            commandId, OrleansRuntimeTelemetryTokens.CompletedTerminalSequence);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(sentinel.WasInjected).IsTrue();
        await AssertDocumentRevisionAsync(fixture, documentId, 1);
    }

    private static async Task<OrleansTelemetryOperationParent[]> ReadRealDocumentAsync(RequestCqrsClusterFixture fixture,
        OrleansRuntimeTelemetryFixture telemetry, PrincipalRecord principal)
    {
        var documentId = NewDocumentId();
        var createReply = await WriteWithParentAsync(fixture, telemetry, principal, documentId);
        await Assert.That(createReply.Reply.Error).IsNull();
        await AssertDocumentRevisionAsync(fixture, documentId, 1);
        using var parent = telemetry.StartParentActivity()
            ?? throw new InvalidOperationException("The telemetry provider did not sample the read parent.");
        var requestId = Guid.NewGuid();
        var signed = fixture.Codec.CreateRead(requestId, principal.Id, GrainReadKind.Document,
            NativeSerialization.Serialize(new GetDocumentRequest(new(fixture.Database.Partition,
                OrleansRuntimeTelemetryTokens.Collection, documentId))));
        var reply = await ReadAsync(fixture, principal, requestId, signed);
        await Assert.That(reply.Payload.IsEmpty).IsFalse();
        var record = NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as DocumentRecord;
        await Assert.That(record).IsNotNull();
        await Assert.That(record!.Reference.Id).IsEqualTo(documentId);
        await Assert.That(record.Json).IsEqualTo(OrleansRuntimeTelemetryTokens.DocumentJson);
        return [createReply.Parent, new OrleansTelemetryOperationParent(parent.TraceId, parent.SpanId)];
    }

    private static async Task<(GrainOperationReply Reply, OrleansTelemetryOperationParent Parent)> WriteWithParentAsync(
        RequestCqrsClusterFixture fixture, OrleansRuntimeTelemetryFixture telemetry, PrincipalRecord principal,
        string? documentId = null)
    {
        using var parent = telemetry.StartParentActivity()
            ?? throw new InvalidOperationException("The telemetry provider did not sample the concurrent parent.");
        var requestId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var id = documentId ?? NewDocumentId();
        var signed = SignWrite(fixture, principal, requestId, commandId, id);
        var reply = await RequestCqrsNativeCommandStream.InvokeAsync(fixture, signed, principal, requestId,
            commandId, OrleansRuntimeTelemetryTokens.CompletedTerminalSequence);
        await AssertDocumentRevisionAsync(fixture, id, 1);
        return (reply, new OrleansTelemetryOperationParent(parent.TraceId, parent.SpanId));
    }

    private static async Task FailRevokedWriteAsync(RequestCqrsClusterFixture fixture, PrincipalRecord principal)
    {
        var documentId = NewDocumentId();
        var requestId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var signed = SignWrite(fixture, principal, requestId, commandId, documentId);
        var revoked = principal with { Revoked = true, PolicyEpoch = principal.PolicyEpoch + 1 };
        fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked));
        var reply = await RequestCqrsNativeCommandStream.InvokeAsync(fixture, signed, principal, requestId,
            commandId, OrleansRuntimeTelemetryTokens.FailedTerminalSequence,
            expectedError: ErrorCode.Unauthenticated);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.Unauthenticated);
        await AssertDocumentMissingAsync(fixture, documentId);
    }

    private static async Task<GrainOperationReply> ReadAsync(RequestCqrsClusterFixture fixture,
        PrincipalRecord principal, Guid requestId, string signed)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRoutingTests.InvocationBound);
        using var identity = new GrainRequestIdentityScope(fixture.Cluster.ServiceProvider, principal,
            requestId, Guid.Empty, deadline.Token);
        var request = fixture.Cluster.Client.GetGrain<IRequestGrain>(requestId);
        var stream = request.ExecuteStreamAsync(signed, deadline.Token).WithBatchSize(GrainRequestStreamProtocol.BatchSize);
        await using var iterator = stream.GetAsyncEnumerator(deadline.Token);
        if (!await iterator.MoveNextAsync())
        {
            throw new InvalidOperationException("The native read ended before its Started chunk.");
        }

        await Assert.That(iterator.Current.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(iterator.Current.ProgressResult?.Value?.RequestId).IsEqualTo(requestId);
        if (!await iterator.MoveNextAsync())
        {
            throw new InvalidOperationException("The native read ended before its terminal chunk.");
        }

        var terminal = iterator.Current;
        await Assert.That(terminal.Kind).IsEqualTo(CqrsStreamChunkKind.Completed);
        await Assert.That(terminal.Final?.IsSuccess).IsTrue();
        await Assert.That(await iterator.MoveNextAsync()).IsFalse();
        return terminal.Final?.Value ?? throw new InvalidOperationException("The native read has no terminal reply.");
    }

    private static PrincipalRecord PersistPrincipal(RequestCqrsClusterFixture fixture, string suffix)
    {
        var id = OrleansRuntimeTelemetryTokens.SubjectPrefix + suffix + Guid.NewGuid().ToString("N");
        var record = new PrincipalRecord(id, fixture.Database.Partition.TenantId,
            [new ScopeGrant(fixture.Database.Partition.DatabaseId, OrleansRuntimeTelemetryTokens.Collection, Capability.All)], []);
        return fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record))
            .Get<PrincipalRecord>();
    }

    private static string SignWrite(RequestCqrsClusterFixture fixture, PrincipalRecord principal,
        Guid requestId, Guid commandId, string documentId)
    {
        var command = new CommandRequest(commandId, fixture.Database.Partition,
            [new PutDocument(OrleansRuntimeTelemetryTokens.Collection, documentId, OrleansRuntimeTelemetryTokens.DocumentJson)]);
        return fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.Batch, commandId,
            NativeSerialization.Serialize(command));
    }

    private static string NewDocumentId()
        => OrleansRuntimeTelemetryTokens.DocumentPrefix + Guid.NewGuid().ToString("N");

    private static async Task AssertDocumentRevisionAsync(RequestCqrsClusterFixture fixture, string documentId, long revision)
    {
        var document = fixture.Database.Database.GetDocument("root",
            new EntityRef(fixture.Database.Partition, OrleansRuntimeTelemetryTokens.Collection, documentId));
        await Assert.That(document?.Revision).IsEqualTo(revision);
    }

    private static async Task AssertDocumentMissingAsync(RequestCqrsClusterFixture fixture, string documentId)
    {
        var document = fixture.Database.Database.GetDocument("root",
            new EntityRef(fixture.Database.Partition, OrleansRuntimeTelemetryTokens.Collection, documentId));
        await Assert.That(document).IsNull();
    }
}
