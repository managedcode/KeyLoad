using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Text.Json;
using KeyLoad.Orleans;
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
    }

    private static (GrainOperationReply Reply, string Output) RejectAndCapture(Exception error, Guid requestId, GrainFailureStage stage)
    {
        using var capture = new EventSourceLogCapture();
        GrainOperationReply reply;
        using (var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger()))
        {
            reply = GrainReplyFactory.Failure(error, false, factory.CreateLogger(nameof(GrainFailureDiagnosticsTests)), UnitRoutingOptions.Routing(), requestId, stage);
        }

        return (reply, capture.Text);
    }
}

/// <summary>Observes the real Microsoft logging EventSource provider without replacing process output.</summary>
internal sealed class EventSourceLogCapture : EventListener
{
    private const string LoggingEventSourceName = "Microsoft-Extensions-Logging";
    private const string FilterSpecsKey = "FilterSpecs";
    private const string LoggerFilter = "GrainFailureDiagnosticsTests:Warning;McpTransportDiagnosticsTests:Warning";
    private const string FormattedMessageEvent = "FormattedMessage";
    private readonly ConcurrentQueue<string> messages = new();

    /// <summary>Gets all formatted log event payload fields captured during the observation.</summary>
    internal string Text => string.Join(" ", messages);

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
