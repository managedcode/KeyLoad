using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

/// <summary>Stores closed failure-stage metadata and writes privacy-safe internal diagnostics.</summary>
internal static partial class GrainFailureDiagnostics
{
    private const int FailureEventId = 3;
    private const int MaximumAggregateDepth = 4;
    private const int InitialAggregateDepth = 0;
    private const int NoInnerExceptions = 0;
    private const int PrimaryExceptionIndex = 0;
    private const string FailureMessage = "Orleans request rejected: {RequestId} at {Stage} ({Category}) with {ErrorCode}. Closed cause {CauseKind}.";
    private const string StageMetadataKeyValue = "KeyLoad.Orleans.ClusterRouting.GrainFailureStage";
    private const string PartitionQueryLifetimeRequest = "The partition query lifetime request is invalid.";
    private const string PartitionQueryReceivingOwner = "The partition query receiving owner changed.";
    private const string PartitionQueryPlacementOwner = "The partition query owner no longer matches this server.";
    private const string PartitionQueryLeafOwner = "Partition query leaves do not share one owner and policy epoch.";
    private const string PartitionQueryIncomplete = "A partition query leaf did not complete.";
    private const string PartitionQuerySourceMetadata = "The query source metadata is invalid.";
    private const string PartitionQueryPhysicalOwner = "The server physical owner identity is invalid.";
    private const string PhysicalStorageOwner = "The operation targets a different physical storage owner.";
    private const string EntityResourceLifetime = "The entity reference belongs to another resource lifetime.";
    [LoggerMessage(EventId = FailureEventId, Message = FailureMessage)]
    private static partial void LogFailure(ILogger logger, LogLevel level, Guid requestId,
        GrainFailureStage stage, GrainFailureCategory category, ErrorCode errorCode,
        GrainFailureCauseKind causeKind, Exception? exception);

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
    /// <param name="callSiteStage">The call-site stage used when metadata is absent or invalid.</param>
    /// <param name="code">The public typed error code.</param>
    internal static void Log(ILogger? logger, Exception error, Guid requestId, GrainFailureStage callSiteStage, ErrorCode code)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (logger is null)
        {
            return;
        }

        var stage = error.Data[StageMetadataKey] is GrainFailureStage marked && Enum.IsDefined(marked) ? marked : ValidStage(callSiteStage);
        var category = Category(error, stage);
        var safeCode = Enum.IsDefined(code) ? code : ErrorCode.OwnershipLost;
        var causeKind = CauseKind(error, category);
        var level = category == GrainFailureCategory.Unexpected ? LogLevel.Error : LogLevel.Warning;
        LogFailure(logger, level, requestId, stage, category, safeCode, causeKind, null);
    }

    private static GrainFailureCauseKind CauseKind(Exception error, GrainFailureCategory category)
    {
        if (category == GrainFailureCategory.Domain && error is KeyLoadException { Code: ErrorCode.OwnershipLost })
        {
            return OwnershipCause(error.Message);
        }
        if (category != GrainFailureCategory.Unexpected)
        {
            return GrainFailureCauseKind.Unobserved;
        }
        var depth = InitialAggregateDepth;
        while (error is AggregateException aggregate)
        {
            if (depth >= MaximumAggregateDepth)
            {
                return GrainFailureCauseKind.AggregateDepthLimit;
            }
            if (aggregate.InnerExceptions.Count == NoInnerExceptions)
            {
                return GrainFailureCauseKind.Other;
            }
            error = aggregate.InnerExceptions[PrimaryExceptionIndex];
            depth++;
        }
        return error switch
        {
            ObjectDisposedException => GrainFailureCauseKind.ReleasedState,
            InvalidOperationException => GrainFailureCauseKind.State,
            NullReferenceException => GrainFailureCauseKind.Reference,
            InvalidCastException or FormatException or OverflowException => GrainFailureCauseKind.Conversion,
            TypeInitializationException => GrainFailureCauseKind.Initialization,
            TypeLoadException or ReflectionTypeLoadException or MissingMemberException => GrainFailureCauseKind.TypeResolution,
            NotSupportedException => GrainFailureCauseKind.Unsupported,
            SerializationException => GrainFailureCauseKind.Serialization,
            IOException => GrainFailureCauseKind.StorageIo,
            _ => GrainFailureCauseKind.Other
        };
    }

    private static GrainFailureCauseKind OwnershipCause(string message) => message switch
    {
        PartitionQueryLifetimeRequest => GrainFailureCauseKind.PartitionQueryLifetimeRequest,
        PartitionQueryReceivingOwner => GrainFailureCauseKind.PartitionQueryReceivingOwner,
        PartitionQueryPlacementOwner => GrainFailureCauseKind.PartitionQueryPlacementOwner,
        PartitionQueryLeafOwner => GrainFailureCauseKind.PartitionQueryLeafOwner,
        PartitionQueryIncomplete => GrainFailureCauseKind.PartitionQueryIncomplete,
        PartitionQuerySourceMetadata => GrainFailureCauseKind.PartitionQuerySourceMetadata,
        PartitionQueryPhysicalOwner => GrainFailureCauseKind.PartitionQueryPhysicalOwner,
        PhysicalStorageOwner => GrainFailureCauseKind.PhysicalStorageOwner,
        EntityResourceLifetime => GrainFailureCauseKind.EntityResourceLifetime,
        _ => GrainFailureCauseKind.Unobserved
    };

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
