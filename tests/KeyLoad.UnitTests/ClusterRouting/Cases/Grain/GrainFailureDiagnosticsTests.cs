using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.TestInfrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventSource;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>AC-ROUTE-008: internal rejection diagnostics stay typed and private.</summary>
[NotInParallel(LoggingEventSourceIsolation.Key)]
internal sealed class GrainFailureDiagnosticsTests
{
    private const string JsonCanary = "private-json-payload-canary";
    private const string ArgumentCanary = "private-argument-identity-canary";
    private const string MetadataCanary = "private-invalid-stage-canary";
    private const string UnavailableDetail = "The database request could not complete. Retry the same command ID for writes.";
    private const string ObservedEqualLength = "route=1 comparisonObserved=True phaseEqual=False canonicalLength=2 originalLength=2 firstDifference=1";
    private const string ObservedDifferentLength = "route=1 comparisonObserved=True phaseEqual=False canonicalLength=1 originalLength=2 firstDifference=1";
    private const string PropagatedOperands = "route=2 comparisonObserved=False phaseEqual=False canonicalLength=-1 originalLength=-1 firstDifference=-1";
    private const string UnobservedOperands = "unobserved";
    private const byte FirstOperandByte = 17;
    private const byte OriginalOperandByte = 34;
    private const byte ChangedOperandByte = 35;

    /// <summary>Malformed JSON and argument failures keep the public validation reply and omit private text.</summary>
    [Test]
    public async Task JsonAndArgumentRejectionsKeepSafeReplyAndPrivateLogs()
    {
        var requestId = Guid.NewGuid();
        foreach (var (error, category) in new (Exception Error, GrainFailureCategory Category)[]
        {
            (new JsonException(JsonCanary), GrainFailureCategory.Json),
            (new ArgumentException(ArgumentCanary), GrainFailureCategory.Argument)
        })
        {
            GrainFailureDiagnostics.Mark(error, GrainFailureStage.PayloadSyntax);
            GrainFailureDiagnostics.Mark(error, GrainFailureStage.PartitionResolution);
            var (reply, output) = RejectAndCapture(error, requestId, GrainFailureStage.EnvelopeVerification);

            await Assert.That(reply.Error).IsEqualTo(ErrorCode.Validation);
            await Assert.That(reply.SafeDetail).IsEqualTo(GrainRoutingProtocol.InvalidRequest);
            await Assert.That(output).Contains(requestId.ToString());
            await Assert.That(output).Contains(nameof(GrainFailureStage.PayloadSyntax));
            await Assert.That(output).Contains(category.ToString());
            await Assert.That(output).DoesNotContain(JsonCanary);
            await Assert.That(output).DoesNotContain(ArgumentCanary);
            await Assert.That(output).DoesNotContain(nameof(JsonException));
            await Assert.That(reply.SafeDetail).DoesNotContain(JsonCanary);
            await Assert.That(reply.SafeDetail).DoesNotContain(ArgumentCanary);
        }
    }

    /// <summary>A tagged typed-payload failure overrides the caller fallback with its closed stage metadata.</summary>
    [Test]
    public async Task TaggedTypedPayloadFailureReportsItsStageAndSafeValidationReply()
    {
        var requestId = Guid.NewGuid();
        var error = GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.TypedPayloadDecode);
        var (reply, output) = RejectAndCapture(error, requestId, GrainFailureStage.EnvelopeVerification);

        await Assert.That(reply.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(reply.SafeDetail).IsEqualTo(GrainRoutingProtocol.InvalidRequest);
        await Assert.That(output).Contains(requestId.ToString());
        await Assert.That(output).Contains(nameof(GrainFailureStage.TypedPayloadDecode));
        await Assert.That(output).Contains(nameof(GrainFailureCategory.Json));
        await Assert.That(output).Contains(nameof(ErrorCode.Validation));
        await Assert.That(output).DoesNotContain(JsonCanary);
    }

    /// <summary>Unknown exception metadata falls back to the supplied stage and cannot enter provider output.</summary>
    [Test]
    public async Task UnknownMetadataCannotInjectPrivateLogText()
    {
        var requestId = Guid.NewGuid();
        var error = new InvalidOperationException(MetadataCanary);
        error.Data[GrainFailureDiagnostics.StageMetadataKey] = (GrainFailureStage)int.MaxValue;
        var (reply, output) = RejectAndCapture(error, requestId, GrainFailureStage.PartitionResolution);

        await Assert.That(reply.Error).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(reply.SafeDetail).IsEqualTo(UnavailableDetail);
        await Assert.That(output).Contains(requestId.ToString());
        await Assert.That(output).Contains(nameof(GrainFailureStage.PartitionResolution));
        await Assert.That(output).Contains(nameof(GrainFailureCategory.Unexpected));
        await Assert.That(output).Contains(nameof(ErrorCode.OwnershipLost));
        await Assert.That(output).DoesNotContain(MetadataCanary);
        await Assert.That(output).DoesNotContain(nameof(InvalidOperationException));
        await ParentOperandsAsync();
    }

    private static async Task ParentOperandsAsync()
    {
        foreach (var (canonical, original, expected) in new (byte[] Canonical, byte[] Original, string Expected)[]
        {
            ([FirstOperandByte, OriginalOperandByte], [FirstOperandByte, ChangedOperandByte], ObservedEqualLength),
            ([FirstOperandByte], [FirstOperandByte, OriginalOperandByte], ObservedDifferentLength)
        })
        {
            var error = Errors.Fail(ErrorCode.OwnershipLost, UnavailableDetail);
            var same = global::KeyLoad.Core.PartitionMoveParentReadDiagnostic.PhaseMismatch(error, canonical, original);
            await Assert.That(ReferenceEquals(same, error)).IsTrue();
            var (reply, output) = RejectAndCapture(same, Guid.NewGuid(), GrainFailureStage.PartitionResolution);
            await Assert.That(reply.Error).IsEqualTo(ErrorCode.OwnershipLost);
            await Assert.That(reply.SafeDetail).IsEqualTo(UnavailableDetail);
            await Assert.That(output).Contains(expected);
            await Assert.That(output).DoesNotContain(MetadataCanary);
        }
        var propagated = Errors.Fail(ErrorCode.OwnershipLost, UnavailableDetail);
        await Assert.That(ReferenceEquals(propagated,
            global::KeyLoad.Core.PartitionMoveParentReadDiagnostic.Propagated(propagated))).IsTrue();
        var (propagatedReply, propagatedOutput) = RejectAndCapture(propagated, Guid.NewGuid(), GrainFailureStage.PartitionResolution);
        await Assert.That(propagatedReply.Error).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(propagatedReply.SafeDetail).IsEqualTo(UnavailableDetail);
        await Assert.That(propagatedOutput).Contains(PropagatedOperands);
        var invalid = Errors.Fail(ErrorCode.OwnershipLost, UnavailableDetail);
        invalid.Data[global::KeyLoad.Core.PartitionMoveParentReadDiagnostic.MetadataKey] = MetadataCanary;
        var (_, invalidOutput) = RejectAndCapture(invalid, Guid.NewGuid(), GrainFailureStage.PartitionResolution);
        await Assert.That(invalidOutput).Contains(UnobservedOperands);
        await Assert.That(invalidOutput).DoesNotContain(MetadataCanary);
        var equal = Errors.Fail(ErrorCode.OwnershipLost, UnavailableDetail);
        global::KeyLoad.Core.PartitionMoveParentReadDiagnostic.PhaseMismatch(equal,
            [FirstOperandByte], [FirstOperandByte]);
        await Assert.That(global::KeyLoad.Core.PartitionMoveParentReadDiagnostic.ReadText(equal)).IsEqualTo(UnobservedOperands);
        var unrelated = Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        await Assert.That(ReferenceEquals(unrelated,
            global::KeyLoad.Core.PartitionMoveParentReadDiagnostic.Propagated(unrelated))).IsTrue();
        await Assert.That(global::KeyLoad.Core.PartitionMoveParentReadDiagnostic.ReadText(unrelated)).IsEqualTo(UnobservedOperands);
    }

    private static (GrainOperationReply Reply, string Output) RejectAndCapture(Exception error, Guid requestId, GrainFailureStage stage)
    {
        using var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger());
        using var capture = new EventSourceLogCapture();
        var reply = GrainReplyFactory.Failure(error, false, factory.CreateLogger(nameof(GrainFailureDiagnosticsTests)), UnitRoutingOptions.Routing(), requestId, stage);
        return (reply, capture.Text);
    }
}

/// <summary>Observes the real Microsoft logging EventSource provider without replacing process output.</summary>
internal sealed class EventSourceLogCapture : EventListener
{
    private const string LoggingEventSourceName = "Microsoft-Extensions-Logging";
    private const string FilterSpecsKey = "FilterSpecs";
    private const string LoggerFilter = "GrainFailureDiagnosticsTests:Warning;McpTransportDiagnosticsTests:Warning;KeyLoad.Clustering.ConnectionGrain:Warning";
    private const string FormattedMessageEvent = "FormattedMessage";
    private readonly ConcurrentQueue<string> messages = new();

    /// <summary>Gets all formatted log event payload fields captured during the observation.</summary>
    internal string Text => string.Join(" ", messages);

    /// <summary>Gets complete original provider rows for request-specific correlation.</summary>
    internal IReadOnlyList<string> Messages => messages.ToArray();

    /// <inheritdoc />
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == LoggingEventSourceName)
        {
            var arguments = new Dictionary<string, string?> { [FilterSpecsKey] = LoggerFilter };
            EnableEvents(eventSource, EventLevel.Warning, LoggingEventSource.Keywords.FormattedMessage, arguments);
        }
    }

    /// <inheritdoc />
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventName == FormattedMessageEvent && eventData.Payload is { } payload)
        {
            messages.Enqueue(string.Join(" ", payload));
        }
    }
}
