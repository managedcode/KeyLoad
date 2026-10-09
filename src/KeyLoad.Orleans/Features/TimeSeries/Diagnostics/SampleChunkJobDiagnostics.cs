using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

internal static partial class SampleChunkJobDiagnostics
{
    private const string ChunkWorkFailureMessage = "Native chunk work failed with bounded error code {Code}.";
    [LoggerMessage(Level = LogLevel.Warning, Message = ChunkWorkFailureMessage)]
    internal static partial void Failed(ILogger logger, ErrorCode code);
    private const string ChunkAdmissionRefusalMessage =
        "Native chunk admission {CommandId} refused with bounded error code {Code}.";
    [LoggerMessage(Level = LogLevel.Warning, Message = ChunkAdmissionRefusalMessage)]
    internal static partial void AdmissionRefused(ILogger logger, Guid commandId, ErrorCode code);
    private const string ReturnedMessage = "Native chunk provider returned {CommandId} job {JobId} metadata {MetadataDigest}.";
    private const string ExecutingMessage = "Native chunk provider executing {CommandId} job {JobId} metadata {MetadataDigest}.";
    [LoggerMessage(Level = LogLevel.Warning, Message = ReturnedMessage)]
    internal static partial void ProviderReturned(ILogger logger, Guid commandId, string jobId, string metadataDigest);
    [LoggerMessage(Level = LogLevel.Warning, Message = ExecutingMessage)]
    internal static partial void ProviderExecuting(ILogger logger, Guid commandId, string jobId, string metadataDigest);
}
