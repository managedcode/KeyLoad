using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Rejects unsafe early header comparisons while the official SDK remains the protocol implementation.</summary>
internal static class McpTransportGuard
{
    /// <summary>Checks the configured revision and bounds optional routing values without retaining raw failures.</summary>
    /// <param name="headers">The actual transport header dictionary.</param>
    /// <returns>Checked optional routing hints for field comparison only.</returns>
    internal static McpTransportHeaders ReadHeaders(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var revision = headers[McpTransportProtocol.RevisionHeader];
        if (revision.Count != 1)
        { throw InvalidTransport(McpTransportStage.ProtocolRevisionCount); }
        if (revision[0] != McpTransportProtocol.Revision)
        { throw InvalidTransport(McpTransportStage.ProtocolRevisionValue); }
        if (headers.ContainsKey(McpTransportProtocol.SessionHeader))
        { throw InvalidTransport(McpTransportStage.SessionHeaderPresence); }
        if (headers.ContainsKey(McpTransportProtocol.LastEventHeader))
        { throw InvalidTransport(McpTransportStage.LastEventHeaderPresence); }
        return new(ReadRoutingHeader(headers, McpTransportProtocol.MethodHeader, McpTransportProtocol.MaximumMethodCharacters,
                McpTransportStage.MethodHeaderShape, McpTransportStage.MethodHeaderEncoding),
            ReadTargetHeader(headers));
    }

    /// <summary>Compares bounded hints and version fields without changing business arguments or native message identity.</summary>
    /// <param name="checkedBody">Already framing-checked bytes held under the ingress memory lease.</param>
    /// <param name="headers">Previously checked routing hints.</param>
    internal static void Inspect(ReadOnlyMemory<byte> checkedBody, McpTransportHeaders headers)
    {
        using var document = JsonDocument.Parse(checkedBody);
        var root = document.RootElement;
        var hasMethod = root.TryGetProperty(McpTransportProtocol.Method, out var method);
        if (hasMethod && method.ValueKind != JsonValueKind.String)
        { throw InvalidTransport(McpTransportStage.BodyMethodShape); }
        if (headers.Method is { } expectedMethod && (!hasMethod || !method.ValueEquals(expectedMethod)))
        { throw InvalidTransport(McpTransportStage.BodyMethodMismatch); }
        if (!root.TryGetProperty(McpTransportProtocol.Parameters, out var parameters)
            || parameters.ValueKind != JsonValueKind.Object)
        {
            if (headers.Name is not null && hasMethod && TargetField(method) is not null)
            { throw InvalidTransport(McpTransportStage.MissingTarget); }
            return;
        }
        CheckRevision(parameters, McpTransportProtocol.RevisionField, McpTransportStage.ParameterRevision);
        if (parameters.TryGetProperty(McpTransportProtocol.Meta, out var meta) && meta.ValueKind == JsonValueKind.Object)
        { CheckRevision(meta, MetaKeys.ProtocolVersion, McpTransportStage.MetadataRevision); }
        if (headers.Name is { } expectedName && hasMethod)
        { CheckTarget(parameters, method, expectedName); }
    }

    private static string? ReadRoutingHeader(IHeaderDictionary headers, string name, int maximumCharacters,
        McpTransportStage shapeStage, McpTransportStage encodingStage)
    {
        if (!headers.TryGetValue(name, out var values))
        { return null; }
        if (values.Count != 1 || values[0] is not { } value)
        { throw InvalidTransport(shapeStage); }
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > maximumCharacters)
        { throw InvalidTransport(shapeStage); }
        foreach (var character in trimmed)
        {
            if (character != '\t' && (character < ' ' || character > '~'))
            { throw InvalidTransport(encodingStage); }
        }
        return trimmed;
    }

    private static void CheckRevision(JsonElement fields, string name, McpTransportStage stage)
    {
        if (fields.TryGetProperty(name, out var revision)
            && (revision.ValueKind != JsonValueKind.String || !revision.ValueEquals(McpTransportProtocol.Revision)))
        { throw InvalidTransport(stage); }
    }

    private static string? ReadTargetHeader(IHeaderDictionary headers)
    {
        var encoded = ReadRoutingHeader(headers, McpTransportProtocol.NameHeader, McpTransportProtocol.MaximumNameCharacters,
            McpTransportStage.NameHeaderShape, McpTransportStage.NameHeaderEncoding);
        if (encoded is null)
        { return null; }
        if (encoded.StartsWith(McpTransportProtocol.EncodedHeaderPrefix, StringComparison.Ordinal)
            && !encoded.EndsWith(McpTransportProtocol.EncodedHeaderSuffix, StringComparison.Ordinal))
        { throw InvalidTransport(McpTransportStage.NameHeaderEncoding); }
        var decoded = McpHeaderEncoder.DecodeValue(encoded);
        if (decoded is null || decoded.Length == 0 || decoded.Length > McpTransportProtocol.MaximumNameCharacters)
        { throw InvalidTransport(McpTransportStage.NameHeaderEncoding); }
        foreach (var character in decoded)
        {
            if (character != '\t' && (character < ' ' || character == '\u007F'))
            { throw InvalidTransport(McpTransportStage.NameHeaderEncoding); }
        }
        return decoded;
    }

    private static void CheckTarget(JsonElement parameters, JsonElement method, string expectedName)
    {
        var name = TargetField(method);
        if (name is null)
        { return; }
        if (!parameters.TryGetProperty(name, out var target))
        { throw InvalidTransport(McpTransportStage.MissingTarget); }
        if (target.ValueKind != JsonValueKind.String || !target.ValueEquals(expectedName))
        { throw InvalidTransport(McpTransportStage.TargetMismatch); }
    }

    private static string? TargetField(JsonElement method) => method.ValueEquals(RequestMethods.ResourcesRead) ? McpTransportProtocol.Uri
        : method.ValueEquals(RequestMethods.ToolsCall) || method.ValueEquals(RequestMethods.PromptsGet) ? McpTransportProtocol.Name : null;

    private static KeyLoadException InvalidTransport(McpTransportStage stage)
    {
        var error = Errors.Fail(ErrorCode.Validation, McpTransportProtocol.InvalidTransport);
        error.Data[McpTransportDiagnostics.StageMetadataKey] = stage;
        return error;
    }
}
