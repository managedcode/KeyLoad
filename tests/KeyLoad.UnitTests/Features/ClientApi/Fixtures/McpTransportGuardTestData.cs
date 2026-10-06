using System.Text.Json;
using KeyLoad.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Owns concrete native headers and serialized wire data for transport-boundary regressions.</summary>
internal static class McpTransportGuardTestData
{
    internal const string RevisionHeader = "MCP-Protocol-Version";
    internal const string MethodHeader = "Mcp-Method";
    internal const string NameHeader = "Mcp-Name";
    internal const string SessionHeader = "Mcp-Session-Id";
    internal const string LastEventHeader = "Last-Event-ID";
    internal const string Revision = "2026-07-28";
    internal const string LegacyRevision = "2025-11-25";
    internal const string Marker = "private-transport-marker";
    internal const string Target = "keyload_get_document";
    internal const string Resource = "keyload://documents/order-1";
    internal const string MethodField = "method";
    internal const string NameField = "name";
    internal const string UriField = "uri";
    internal const string VersionField = "protocolVersion";
    internal const string MetaField = "_meta";
    internal const string ArgumentsField = "arguments";
    internal const string RequestField = "request";
    internal const string Whitespace = " \t";
    internal const string TabbedMethod = "tools/\tcall";
    internal const string TabbedName = "keyload_\tget_document";
    private const string ParametersField = "params";
    private const string JsonRpcField = "jsonrpc";
    private const string JsonRpcVersion = "2.0";
    private const string IdentifierField = "id";
    private const string RequestIdentifier = "transport-boundary-request";
    private const string NonAscii = "é";
    private const string ControlCharacter = "\n";
    private const string DeleteCharacter = "\u007F";
    private const int MaximumWireBytes = 65_536;
    private const int WrongNumericField = 7;

    /// <summary>Creates actual case-insensitive ASP.NET headers with one exact native revision.</summary>
    /// <param name="method">The optional HTTP routing method.</param>
    /// <param name="name">The optional HTTP target name.</param>
    /// <returns>The concrete header dictionary used directly by the product guard.</returns>
    internal static HeaderDictionary Headers(string? method = RequestMethods.ToolsCall, string? name = Target)
    {
        var headers = new HeaderDictionary { [RevisionHeader] = Revision };
        if (method is not null)
        { headers[MethodHeader] = method; }
        if (name is not null)
        { headers[NameHeader] = name; }
        return headers;
    }

    /// <summary>Creates an invalid native header set without replacing the production request type.</summary>
    /// <param name="fault">The exact invalid transport-header condition.</param>
    /// <returns>Headers containing only the selected mutation.</returns>
    internal static HeaderDictionary InvalidHeaders(McpTransportHeaderFault fault)
    {
        var headers = Headers();
        (string Field, StringValues? Value) mutation = fault switch
        {
            McpTransportHeaderFault.MissingRevision => (RevisionHeader, (StringValues?)null),
            McpTransportHeaderFault.EmptyRevision => (RevisionHeader, StringValues.Empty),
            McpTransportHeaderFault.LegacyRevision => (RevisionHeader, new StringValues(LegacyRevision)),
            McpTransportHeaderFault.RevisionWhitespace => (RevisionHeader, new StringValues(Whitespace + Revision)),
            McpTransportHeaderFault.MarkerRevision => (RevisionHeader, new StringValues(Marker)),
            McpTransportHeaderFault.DuplicateRevision => (RevisionHeader, new StringValues([Revision, Revision])),
            McpTransportHeaderFault.Session => (SessionHeader, new StringValues(Marker)),
            McpTransportHeaderFault.EmptySession => (SessionHeader, new StringValues(string.Empty)),
            McpTransportHeaderFault.LastEvent => (LastEventHeader, new StringValues(Marker)),
            McpTransportHeaderFault.EmptyLastEvent => (LastEventHeader, new StringValues(string.Empty)),
            McpTransportHeaderFault.DuplicateMethod => (MethodHeader, new StringValues([RequestMethods.ToolsCall, Marker])),
            McpTransportHeaderFault.DuplicateName => (NameHeader, new StringValues([Target, Marker])),
            McpTransportHeaderFault.EmptyMethod => (MethodHeader, new StringValues(Whitespace)),
            McpTransportHeaderFault.EmptyName => (NameHeader, new StringValues(Whitespace)),
            McpTransportHeaderFault.EmptyMethodValues => (MethodHeader, new StringValues(string.Empty)),
            McpTransportHeaderFault.EmptyNameValues => (NameHeader, new StringValues(string.Empty)),
            McpTransportHeaderFault.NonAsciiMethod => (MethodHeader, new StringValues(NonAscii + Marker)),
            McpTransportHeaderFault.NonAsciiName => (NameHeader, new StringValues(NonAscii + Marker)),
            McpTransportHeaderFault.ControlMethod => (MethodHeader, new StringValues(Marker + ControlCharacter + Marker)),
            McpTransportHeaderFault.ControlName => (NameHeader, new StringValues(Marker + ControlCharacter + Marker)),
            McpTransportHeaderFault.DeleteMethod => (MethodHeader, new StringValues(Marker + DeleteCharacter)),
            McpTransportHeaderFault.DeleteName => (NameHeader, new StringValues(Marker + DeleteCharacter)),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        if (mutation.Value is { } value)
        {
            headers[mutation.Field] = value;
        }
        else
        { headers.Remove(mutation.Field); }
        return headers;
    }

    /// <summary>Serializes actual UTF-8 JSON and verifies the guard's framing precondition.</summary>
    /// <param name="method">The native body method, omitted when null and including genuine wrong-type cases.</param>
    /// <param name="parameters">The exact request parameters.</param>
    /// <returns>Owned, structurally checked wire bytes.</returns>
    internal static byte[] Frame(object? method, Dictionary<string, object?> parameters)
    {
        var envelope = new Dictionary<string, object?>
        {
            [JsonRpcField] = JsonRpcVersion,
            [IdentifierField] = RequestIdentifier,
            [ParametersField] = parameters
        };
        if (method is not null)
        {
            envelope[MethodField] = method;
        }
        var bytes = JsonDefaults.Serialize(envelope);
        _ = McpFrameBounds.Inspect(bytes, MaximumWireBytes, UnitMcpOptions.Execution());
        return bytes;
    }

    /// <summary>Creates a method, target or protocol mismatch in an otherwise framing-valid request.</summary>
    /// <param name="fault">The exact unsafe early-mismatch field.</param>
    /// <returns>Actual UTF-8 JSON supplied directly to the product guard.</returns>
    internal static byte[] InvalidFrame(McpTransportFrameFault fault)
    {
        var parameters = new Dictionary<string, object?> { [NameField] = Target };
        object? method = RequestMethods.ToolsCall;
        switch (fault)
        {
            case McpTransportFrameFault.MethodMismatch:
                method = Marker;
                break;
            case McpTransportFrameFault.MethodWrongType:
                method = WrongNumericField;
                break;
            case McpTransportFrameFault.NameMismatch:
                parameters[NameField] = Marker;
                break;
            case McpTransportFrameFault.NameWrongType:
                parameters[NameField] = true;
                break;
            case McpTransportFrameFault.VersionMismatch:
                parameters[VersionField] = Marker;
                break;
            case McpTransportFrameFault.VersionWrongType:
                parameters[VersionField] = WrongNumericField;
                break;
            case McpTransportFrameFault.VersionNull:
                parameters[VersionField] = null;
                break;
            case McpTransportFrameFault.MetaMismatch:
                parameters[MetaField] = new Dictionary<string, object?> { [MetaKeys.ProtocolVersion] = Marker };
                break;
            case McpTransportFrameFault.MetaWrongType:
                parameters[MetaField] = new Dictionary<string, object?> { [MetaKeys.ProtocolVersion] = true };
                break;
            case McpTransportFrameFault.MetaNull:
                parameters[MetaField] = new Dictionary<string, object?> { [MetaKeys.ProtocolVersion] = null };
                break;
        }
        return Frame(method, parameters);
    }

    /// <summary>Reads the exact JSON target bytes independently of the transport guard.</summary>
    /// <param name="wire">The actual serialized request.</param>
    /// <param name="field">The target parameter key.</param>
    /// <returns>The caller-provided target value.</returns>
    internal static string? ReadTarget(byte[] wire, string field)
    {
        using var document = JsonDocument.Parse(wire);
        return document.RootElement.GetProperty(ParametersField).GetProperty(field).GetString();
    }
}

/// <summary>Names concrete invalid HTTP header conditions.</summary>
internal enum McpTransportHeaderFault
{
    MissingRevision, EmptyRevision, LegacyRevision, RevisionWhitespace, MarkerRevision, DuplicateRevision,
    Session, EmptySession, LastEvent, EmptyLastEvent, DuplicateMethod, DuplicateName, EmptyMethod, EmptyName,
    EmptyMethodValues, EmptyNameValues, NonAsciiMethod, NonAsciiName, ControlMethod, ControlName, DeleteMethod, DeleteName
}

/// <summary>Names concrete invalid JSON transport fields without changing business arguments.</summary>
internal enum McpTransportFrameFault
{
    MethodMismatch, MethodWrongType, NameMismatch, NameWrongType, VersionMismatch, VersionWrongType, VersionNull,
    MetaMismatch, MetaWrongType, MetaNull
}
