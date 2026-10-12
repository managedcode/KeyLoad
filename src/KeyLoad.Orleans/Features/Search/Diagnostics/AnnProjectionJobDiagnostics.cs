using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

internal static partial class AnnProjectionJobDiagnostics
{
    private const string FailureMessage = "Native ANN autonomous work refused with bounded error code {Code}.";
    [LoggerMessage(Level = LogLevel.Warning, Message = FailureMessage)]
    internal static partial void Failed(ILogger logger, ErrorCode code);
}
