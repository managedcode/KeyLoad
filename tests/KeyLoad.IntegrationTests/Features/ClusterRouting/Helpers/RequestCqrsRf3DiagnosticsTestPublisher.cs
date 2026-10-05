using KeyLoad.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal static class RequestCqrsRf3DiagnosticsTestPublisher
{
    private const string MethodToolsCall = "tools/call";
    private const string RawMessageTemplate = "{Line}";
    private const string MessagePrefix = "MCP transport rejected at ";
    private const string Separator = " for ";
    private const string SensitiveSentinel = "private-diagnostic-sentinel-7fe26d";
    private const int MaximumLineCharacters = 4_096;
    private const int ValidRecordsPerNode = 40;
    private static readonly Action<ILogger, string, Exception?> LogMalformedLine = LoggerMessage.Define<string>(
        LogLevel.Warning, default, RawMessageTemplate);

    internal static void EmitMalformed(ILogger logger)
    {
        var valid = FixedMessage(McpTransportStage.ProtocolRevisionValue);
        LogMalformedLine(logger, valid + new string('X', MaximumLineCharacters + 1) + SensitiveSentinel, null);
        LogMalformedLine(logger, valid + " " + MessagePrefix + "ProtocolRevisionValue for ToolsCall. " + SensitiveSentinel, null);
        LogMalformedLine(logger, MessagePrefix + "1" + Separator + "2.", null);
        LogMalformedLine(logger, MessagePrefix + SensitiveSentinel + Separator + "ToolsCall.", null);
        LogMalformedLine(logger, MessagePrefix + "ProtocolRevisionValue" + Separator + SensitiveSentinel + ".", null);
        LogMalformedLine(logger, valid + " trailing " + SensitiveSentinel, null);
    }

    internal static void EmitValid(ILogger logger)
        => EmitRejectedTransport(logger, McpTransportStage.ProtocolRevisionValue);

    internal static void EmitFortyValid(ILogger logger)
    {
        for (var index = 0; index < ValidRecordsPerNode; index++)
        { EmitValid(logger); }
    }

    private static void EmitRejectedTransport(ILogger logger, McpTransportStage stage)
    {
        var error = Errors.Fail(ErrorCode.Validation, McpTransportProtocol.InvalidTransport);
        error.Data[McpTransportDiagnostics.StageMetadataKey] = stage;
        var headers = new HeaderDictionary { [McpTransportProtocol.MethodHeader] = MethodToolsCall };
        McpTransportDiagnostics.Log(logger, error, headers);
    }

    private static string FixedMessage(McpTransportStage stage)
        => MessagePrefix + stage + Separator + nameof(McpTransportMethodCategory.ToolsCall) + ".";
}
