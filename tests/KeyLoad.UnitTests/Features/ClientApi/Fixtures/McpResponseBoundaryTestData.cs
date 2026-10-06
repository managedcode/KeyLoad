using System.Text;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Creates genuine serialized or explicitly encoded JSON for response and transport boundary regressions.</summary>
internal static class McpResponseBoundaryTestData
{
    internal const char Padding = 'a';
    private const char Utf8Character = 'é';
    private const char ArrayStart = '[';
    private const char ArrayEnd = ']';
    private const char ScalarZero = '0';
    private const string ObjectValuePrefix = "{\"value\":";
    private const char ObjectEnd = '}';
    private const string IdentifierPrefix = "{\"id\":\"";
    private const string EscapedIdentifierPrefix = "{\"i\\u0064\":\"";
    private const string IdentifierSuffix = "\"}";
    private const string UnicodeEscape = "\\u00E9";
    private const string QuoteEscape = "\\\"";
    private const int Utf8CharacterBytes = 2;
    private const string ParametersField = "params";
    private const int MaximumWireBytes = 65_536;

    /// <summary>Creates a complete array value with an exact maximum container depth.</summary>
    /// <param name="depth">The positive number of nested arrays.</param>
    /// <returns>Owned actual UTF-8 JSON bytes.</returns>
    internal static byte[] NestedArrays(int depth)
        => Encoding.UTF8.GetBytes(new StringBuilder().Append(ArrayStart, depth).Append(ScalarZero).Append(ArrayEnd, depth).ToString());

    /// <summary>Creates an inbound object whose total object and array depth is exact.</summary>
    /// <param name="depth">The positive total number of containers.</param>
    /// <returns>Owned actual UTF-8 JSON bytes.</returns>
    internal static byte[] NestedObject(int depth)
        => Encoding.UTF8.GetBytes(new StringBuilder(ObjectValuePrefix).Append(ArrayStart, depth - 1)
            .Append(ScalarZero).Append(ArrayEnd, depth - 1).Append(ObjectEnd).ToString());

    /// <summary>Creates valid encoded JSON string contents with the exact requested byte count.</summary>
    /// <param name="encoding">The encoded spelling, independent of decoded character count.</param>
    /// <param name="bytes">The encoded content byte count excluding quotes.</param>
    /// <returns>Raw valid string contents suitable for a handcrafted JSON token.</returns>
    internal static string EncodedIdentifier(McpIdentifierEncoding encoding, int bytes)
    {
        if (encoding == McpIdentifierEncoding.Ascii)
        { return new string(Padding, bytes); }
        if (encoding == McpIdentifierEncoding.Utf8)
        { return new string(Utf8Character, bytes / Utf8CharacterBytes) + new string(Padding, bytes % Utf8CharacterBytes); }
        var escape = encoding == McpIdentifierEncoding.UnicodeEscape ? UnicodeEscape : QuoteEscape;
        var builder = new StringBuilder();
        for (var index = 0; index < bytes / escape.Length; index++)
        { builder.Append(escape); }
        return builder.Append(Padding, bytes % escape.Length).ToString();
    }

    /// <summary>Frames encoded string contents without normalizing their actual wire spelling.</summary>
    /// <param name="encoded">Valid already encoded JSON string contents.</param>
    /// <param name="escapedKey">Whether the identifier property itself uses a Unicode escape.</param>
    /// <returns>A complete actual UTF-8 object frame.</returns>
    internal static byte[] IdentifierFrame(string encoded, bool escapedKey = false)
        => Encoding.UTF8.GetBytes((escapedKey ? EscapedIdentifierPrefix : IdentifierPrefix) + encoded + IdentifierSuffix);

    /// <summary>Serializes missing or wrong native parameter fields using the actual canonical serializer.</summary>
    /// <param name="method">The body method, omitted when null.</param>
    /// <param name="parameters">The concrete JSON parameter value, including null or a nonobject.</param>
    /// <param name="omitParameters">Whether to omit the property completely.</param>
    /// <returns>A structurally checked owned wire frame.</returns>
    internal static byte[] TransportFrame(string? method, object? parameters, bool omitParameters = false)
    {
        var envelope = new Dictionary<string, object?>();
        if (method is not null)
        { envelope[McpTransportGuardTestData.MethodField] = method; }
        if (!omitParameters)
        { envelope[ParametersField] = parameters; }
        var wire = JsonDefaults.Serialize(envelope);
        _ = McpFrameBounds.Inspect(wire, MaximumWireBytes, UnitMcpOptions.Execution());
        return wire;
    }

    /// <summary>Creates one concrete missing-target or invalid-parameter condition.</summary>
    /// <param name="method">The routed native body method.</param>
    /// <param name="field">The native target parameter key.</param>
    /// <param name="fault">The precise absence or type failure.</param>
    /// <returns>The real serialized frame used directly by the guard.</returns>
    internal static byte[] InvalidTargetFrame(string method, string field, McpMissingTargetFault fault)
    {
        object? parameters = fault switch
        {
            McpMissingTargetFault.NullParameters => null,
            McpMissingTargetFault.ScalarParameters => true,
            McpMissingTargetFault.ArrayParameters => Array.Empty<object>(),
            McpMissingTargetFault.NullTarget => new Dictionary<string, object?> { [field] = null },
            McpMissingTargetFault.WrongTarget => new Dictionary<string, object?> { [field] = false },
            _ => new Dictionary<string, object?>()
        };
        return TransportFrame(method, parameters, fault == McpMissingTargetFault.MissingParameters);
    }
}

/// <summary>Names real encoded JSON string representations with different byte and character counts.</summary>
internal enum McpIdentifierEncoding { Ascii, Utf8, UnicodeEscape, QuoteEscape }

/// <summary>Names native target omissions and concrete JSON type failures.</summary>
internal enum McpMissingTargetFault
{
    MissingParameters, NullParameters, ScalarParameters, ArrayParameters, MissingTarget, NullTarget, WrongTarget
}
