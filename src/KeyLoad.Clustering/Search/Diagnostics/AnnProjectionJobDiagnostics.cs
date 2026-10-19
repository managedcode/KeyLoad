using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

internal static partial class AnnProjectionJobDiagnostics
{
    private const string FailureMessage = "Execution ANN autonomous work refused with bounded error code {Code}.";
    [LoggerMessage(Level = LogLevel.Warning, Message = FailureMessage)]
    internal static partial void Failed(ILogger logger, ErrorCode code);
}
