using System.Diagnostics;
using KeyLoad.Core;
using KeyLoad.Orleans;
using ManagedCode.Communication.CQRS;
using OpenTelemetry.Metrics;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsDataSource]
[NotInParallel]
internal sealed class OrleansRuntimeTelemetryTests(RequestCqrsClusterFixture fixture)
{
    [Test]
    public async Task AcOrl012RealSignedOperationsExportBoundedPrivateNativeTelemetry()
    {
        await using var telemetry = await OrleansRuntimeTelemetryFixture.StartAsync();
        fixture.Database.Configure(OrleansRuntimeTelemetryTokens.Collection, ResourceKind.Collection);
        var first = PersistPrincipal(OrleansRuntimeTelemetryTokens.SubjectPrefix + Guid.NewGuid().ToString("N"));
        var second = PersistPrincipal(OrleansRuntimeTelemetryTokens.SubjectPrefix + Guid.NewGuid().ToString("N"));
        var third = PersistPrincipal(OrleansRuntimeTelemetryTokens.SubjectPrefix + Guid.NewGuid().ToString("N"));

        string firstDocument;
        Guid firstRequest;
        ActivityTraceId firstTrace;
        ActivitySpanId firstParent;
        using (var sentinel = new OrleansRuntimeTelemetrySentinel())
        using (var parent = telemetry.StartParentActivity()
            ?? throw new InvalidOperationException("The telemetry provider did not sample the parent activity."))
        {
            firstTrace = parent.TraceId;
            firstParent = parent.SpanId;
            firstRequest = Guid.NewGuid();
            firstDocument = OrleansRuntimeTelemetryTokens.DocumentPrefix + Guid.NewGuid().ToString("N");
            var reply = await WriteAsync(first, firstDocument, firstRequest);
            await Assert.That(reply.Error).IsNull();
            await Assert.That(sentinel.WasInjected).IsTrue();
            await AssertDocumentRevisionAsync(firstDocument, 1);
        }

        ActivityTraceId readTrace;
        ActivitySpanId readParent;
        using (var parent = telemetry.StartParentActivity()
            ?? throw new InvalidOperationException("The telemetry provider did not sample the read parent."))
        {
            readTrace = parent.TraceId;
            readParent = parent.SpanId;
            var readRequest = Guid.NewGuid();
            var signed = fixture.Codec.CreateRead(readRequest, first.Id, GrainReadKind.Document,
                NativeSerialization.Serialize(new GetDocumentRequest(new(fixture.Database.Partition,
                    OrleansRuntimeTelemetryTokens.Collection, firstDocument))));
            var reply = await ReadAsync(first, readRequest, signed);
            await Assert.That(reply.Payload.IsEmpty).IsFalse();
            await Assert.That(NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value)
                .IsTypeOf<DocumentRecord>();
        }

        var secondTask = WriteWithParentAsync(telemetry, second,
            OrleansRuntimeTelemetryTokens.DocumentPrefix + Guid.NewGuid().ToString("N"));
        var thirdTask = WriteWithParentAsync(telemetry, third,
            OrleansRuntimeTelemetryTokens.DocumentPrefix + Guid.NewGuid().ToString("N"));
        var concurrent = await Task.WhenAll(secondTask, thirdTask);
        await Assert.That(concurrent[0].Reply.Error).IsNull();
        await Assert.That(concurrent[1].Reply.Error).IsNull();
        await Assert.That(concurrent[0].TraceId).IsNotEqualTo(concurrent[1].TraceId);
        await AssertDocumentRevisionAsync(concurrent[0].DocumentId, 1);
        await AssertDocumentRevisionAsync(concurrent[1].DocumentId, 1);

        var deniedDocument = OrleansRuntimeTelemetryTokens.DocumentPrefix + Guid.NewGuid().ToString("N");
        var deniedCommand = Guid.NewGuid();
        var deniedRequest = Guid.NewGuid();
        var deniedSigned = fixture.Codec.CreateCommand(deniedRequest, first.Id, OperationKind.Batch, deniedCommand,
            NativeSerialization.Serialize(Command(deniedCommand, deniedDocument)));
        Revoke(first);
        var failed = await RequestCqrsNativeCommandStream.InvokeAsync(fixture, deniedSigned, first,
            deniedRequest, deniedCommand, OrleansRuntimeTelemetryTokens.FailedTerminalSequence,
            expectedError: ErrorCode.Unauthenticated);
        await Assert.That(failed.Error).IsEqualTo(ErrorCode.Unauthenticated);
        await AssertDocumentMissingAsync(deniedDocument);

        telemetry.Flush();
        var nativeSpans = telemetry.Activities.Snapshot()
            .Where(span => span.SourceName is OrleansRuntimeTelemetryTokens.ApplicationSource
                or OrleansRuntimeTelemetryTokens.LifecycleSource).ToArray();
        await Assert.That(nativeSpans.Length).IsGreaterThan(0);
        await AssertParentageAsync(nativeSpans, firstTrace, firstParent);
        await AssertParentageAsync(nativeSpans, readTrace, readParent);
        foreach (var item in concurrent)
        {
            await AssertParentageAsync(nativeSpans, item.TraceId, item.ParentSpanId);
        }

        var exportedText = string.Join("\n", nativeSpans.Select(Format));
        await Assert.That(exportedText).DoesNotContain(OrleansRuntimeTelemetryTokens.SentinelValue);
        await Assert.That(exportedText).DoesNotContain(first.Id);
        await Assert.That(exportedText).DoesNotContain(second.Id);
        await Assert.That(exportedText).DoesNotContain(third.Id);
        await Assert.That(exportedText).DoesNotContain(firstRequest.ToString("N"));
        await Assert.That(exportedText).DoesNotContain(OrleansRuntimeTelemetryTokens.DocumentJson);
        foreach (var span in nativeSpans)
        {
            await Assert.That(span.DisplayName).IsEqualTo(span.SourceName == OrleansRuntimeTelemetryTokens.ApplicationSource
                ? OrleansRuntimeTelemetryTokens.ApplicationDisplayName : OrleansRuntimeTelemetryTokens.LifecycleDisplayName);
            await Assert.That(span.StatusDescription).IsNullOrEmpty();
            await Assert.That(span.TraceState).IsNullOrEmpty();
            await Assert.That(span.Baggage.Length).IsEqualTo(0);
            await Assert.That(span.Tags.Length).IsLessThanOrEqualTo(telemetry.Options.MaximumTags);
            await Assert.That(span.Events.Length).IsLessThanOrEqualTo(telemetry.Options.MaximumEvents);
            await Assert.That(span.Events.Any(activityEvent => activityEvent.Name == OrleansRuntimeTelemetryTokens.SentinelEvent)).IsFalse();
        }

        var nativeMetrics = telemetry.Metrics.Snapshot()
            .Where(metric => metric.MeterName == OrleansRuntimeTelemetryTokens.MetricMeter).ToArray();
        await Assert.That(nativeMetrics.Length).IsGreaterThan(0);
        foreach (var point in nativeMetrics)
        {
            await Assert.That(point.Tags.Length).IsEqualTo(0);
            await Assert.That(point.ExemplarTags.Length).IsEqualTo(0);
        }

        var suppression = telemetry.Metrics.Snapshot().Where(metric =>
            metric.MeterName == OrleansRuntimeTelemetryTokens.PrivacyMeter
            && metric.MetricName == OrleansRuntimeTelemetryTokens.SuppressionCounter).ToArray();
        await Assert.That(suppression.Any(point => point.Tags.Any(tag =>
            tag.Key == OrleansRuntimeTelemetryTokens.SuppressionReasonTag
            && tag.Value == OrleansRuntimeTelemetryTokens.SafeSuppressionReason))).IsTrue();
    }

    private PrincipalRecord PersistPrincipal(string id)
    {
        var record = new PrincipalRecord(id, fixture.Database.Partition.TenantId,
            [new ScopeGrant(fixture.Database.Partition.DatabaseId, OrleansRuntimeTelemetryTokens.Collection, Capability.All)], []);
        return fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record))
            .Get<PrincipalRecord>();
    }

    private void Revoke(PrincipalRecord principal)
    {
        var revoked = principal with { Revoked = true, PolicyEpoch = principal.PolicyEpoch + 1 };
        fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked));
    }

    private async Task<GrainOperationReply> WriteAsync(PrincipalRecord principal, string documentId, Guid requestId)
    {
        var commandId = Guid.NewGuid();
        return await RequestCqrsNativeCommandStream.InvokeAsync(fixture, principal, requestId, commandId,
            Command(commandId, documentId));
    }

    private async Task<(GrainOperationReply Reply, string DocumentId, ActivityTraceId TraceId, ActivitySpanId ParentSpanId)>
        WriteWithParentAsync(OrleansRuntimeTelemetryFixture telemetry, PrincipalRecord principal, string documentId)
    {
        using var parent = telemetry.StartParentActivity()
            ?? throw new InvalidOperationException("The telemetry provider did not sample a concurrent parent activity.");
        var traceId = parent.TraceId;
        var parentId = parent.SpanId;
        var reply = await WriteAsync(principal, documentId, Guid.NewGuid());
        return (reply, documentId, traceId, parentId);
    }

    private async Task<GrainOperationReply> ReadAsync(PrincipalRecord principal, Guid requestId, string signedRequest)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRoutingTests.InvocationBound);
        using var identity = new GrainRequestIdentityScope(fixture.Cluster.ServiceProvider, principal,
            requestId, Guid.Empty, deadline.Token);
        var request = fixture.Cluster.Client.GetGrain<IRequestGrain>(requestId);
        var stream = request.ExecuteStreamAsync(signedRequest, deadline.Token)
            .WithBatchSize(GrainRequestStreamProtocol.BatchSize);
        await using var iterator = stream.GetAsyncEnumerator(deadline.Token);
        if (!await iterator.MoveNextAsync())
        {
            throw new InvalidOperationException("The native read ended before its Started chunk.");
        }

        var started = iterator.Current;
        await Assert.That(started.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(started.ProgressResult?.Value?.RequestId).IsEqualTo(requestId);
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

    private CommandRequest Command(Guid commandId, string documentId)
        => new(commandId, fixture.Database.Partition,
            [new PutDocument(OrleansRuntimeTelemetryTokens.Collection, documentId, OrleansRuntimeTelemetryTokens.DocumentJson)]);

    private async Task AssertDocumentRevisionAsync(string id, long revision)
    {
        var document = fixture.Database.Database.GetDocument("root",
            new EntityRef(fixture.Database.Partition, OrleansRuntimeTelemetryTokens.Collection, id));
        await Assert.That(document?.Revision).IsEqualTo(revision);
    }

    private async Task AssertDocumentMissingAsync(string id)
    {
        var document = fixture.Database.Database.GetDocument("root",
            new EntityRef(fixture.Database.Partition, OrleansRuntimeTelemetryTokens.Collection, id));
        await Assert.That(document).IsNull();
    }

    private static async Task AssertParentageAsync(OrleansActivityCapture[] spans, ActivityTraceId traceId,
        ActivitySpanId parentSpanId)
    {
        var trace = traceId.ToHexString();
        var parent = parentSpanId.ToHexString();
        await Assert.That(spans.Any(span => span.TraceId == trace && span.ParentSpanId == parent)).IsTrue();
    }

    private static string Format(OrleansActivityCapture activity)
        => string.Join("|", activity.SourceName, activity.DisplayName, activity.Status.ToString(),
            activity.StatusDescription, activity.TraceState, string.Join(",", activity.Baggage),
            string.Join(",", activity.Tags.Select(tag => tag.Key + "=" + tag.Value)),
            string.Join(",", activity.Events.Select(item => item.Name + ":" + string.Join(",",
                item.Tags.Select(tag => tag.Key + "=" + tag.Value)))));
}
