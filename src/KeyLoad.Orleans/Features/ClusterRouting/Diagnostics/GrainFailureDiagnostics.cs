using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

/// <summary>Closed categories emitted by internal rejected-request diagnostics.</summary>
internal enum GrainFailureCategory
{
    /// <summary>A domain exception with a typed KeyLoad error code.</summary>
    Domain,
    /// <summary>A malformed or incompatible JSON payload.</summary>
    Json,
    /// <summary>An invalid operation argument.</summary>
    Argument,
    /// <summary>A cancelled operation.</summary>
    Cancellation,
    /// <summary>An unexpected failure without a public exception classification.</summary>
    Unexpected
}

/// <summary>Stores closed failure-stage metadata and writes privacy-safe internal diagnostics.</summary>
internal static class GrainFailureDiagnostics
{
    private const int FailureEventId = 3;
    private const string FailureMessage = "Orleans request rejected: {RequestId} at {Stage} ({Category}) with {ErrorCode}.";
    private const string StageMetadataKeyValue = "KeyLoad.Orleans.ClusterRouting.GrainFailureStage";
    private static readonly Action<ILogger, Guid, GrainFailureStage, GrainFailureCategory, ErrorCode, Exception?> LogRejected =
        LoggerMessage.Define<Guid, GrainFailureStage, GrainFailureCategory, ErrorCode>(LogLevel.Warning,
            new EventId(FailureEventId), FailureMessage);
    private static readonly Action<ILogger, Guid, GrainFailureStage, GrainFailureCategory, ErrorCode, Exception?> LogUnexpected =
        LoggerMessage.Define<Guid, GrainFailureStage, GrainFailureCategory, ErrorCode>(LogLevel.Error,
            new EventId(FailureEventId), FailureMessage);

    /// <summary>Names the private exception data entry used for an optional closed stage marker.</summary>
    internal const string StageMetadataKey = StageMetadataKeyValue;

    /// <summary>Adds a stage marker unless the exception already has a valid one.</summary>
    /// <param name="error">The failure to tag.</param>
    /// <param name="stage">The closed stage observed by the caller.</param>
    /// <returns>The same exception for use in a throw expression.</returns>
    internal static Exception Mark(Exception error, GrainFailureStage stage)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.Data[StageMetadataKey] is GrainFailureStage marked && Enum.IsDefined(marked))
        {
            return error;
        }

        error.Data[StageMetadataKey] = ValidStage(stage);
        return error;
    }

    /// <summary>Creates a canonical safe validation failure tagged with its decoding stage.</summary>
    /// <param name="stage">The closed payload stage that rejected the request.</param>
    /// <returns>A typed validation failure carrying only private enum metadata.</returns>
    internal static KeyLoadException InvalidPayload(GrainFailureStage stage)
    {
        var error = Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        Mark(error, stage);
        return error;
    }

    /// <summary>Emits only a request identifier and sanitized closed enum values.</summary>
    /// <param name="logger">The optional application logger.</param>
    /// <param name="error">The rejected request exception; its object and text are never logged.</param>
    /// <param name="requestId">The request's non-secret correlation identifier.</param>
    /// <param name="fallback">The call-site stage used when metadata is absent or invalid.</param>
    /// <param name="code">The public typed error code.</param>
    internal static void Log(ILogger? logger, Exception error, Guid requestId, GrainFailureStage fallback, ErrorCode code)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (logger is null)
        {
            return;
        }

        var stage = error.Data[StageMetadataKey] is GrainFailureStage marked && Enum.IsDefined(marked) ? marked : ValidStage(fallback);
        var category = Category(error, stage);
        var safeCode = Enum.IsDefined(code) ? code : ErrorCode.OwnershipLost;
        var write = category == GrainFailureCategory.Unexpected ? LogUnexpected : LogRejected;
        write(logger, requestId, stage, category, safeCode, null);
    }

    private static GrainFailureStage ValidStage(GrainFailureStage stage) => Enum.IsDefined(stage) ? stage : GrainFailureStage.EnvelopeVerification;

    private static GrainFailureCategory Category(Exception error, GrainFailureStage stage)
    {
        return error switch
        {
            JsonException => GrainFailureCategory.Json,
            ArgumentException => GrainFailureCategory.Argument,
            OperationCanceledException => GrainFailureCategory.Cancellation,
            KeyLoadException when stage is GrainFailureStage.PayloadSyntax or GrainFailureStage.TypedPayloadDecode or GrainFailureStage.RequiredNullPayload
                => GrainFailureCategory.Json,
            KeyLoadException => GrainFailureCategory.Domain,
            _ => GrainFailureCategory.Unexpected
        };
    }
}
