using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Rejects unsafe early header comparisons while the official SDK remains the protocol implementation.</summary>
internal static class McpTransportGuard
{
    /// <summary>Checks the configured revision and bounds optional routing values without retaining raw failures.</summary>
    /// <param name="headers">The actual transport header dictionary.</param>
    /// <param name="executionOptions">The centrally validated transport admission bounds.</param>
    /// <returns>Checked optional routing hints for field comparison only.</returns>
    internal static McpTransportHeaders ReadHeaders(IHeaderDictionary headers, IOptions<McpExecutionOptions> executionOptions)
    {
        const int EmptyRevisionCount = 1;
        const int IndexEmptyCount = 0;

        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(executionOptions);
        var bounds = executionOptions.Value;
        bounds.Validate();
        var revision = headers[McpTransportProtocol.RevisionHeader];
        if (revision.Count != EmptyRevisionCount)
        {
            throw InvalidTransport(McpTransportStage.ProtocolRevisionCount);
        }
        if (revision[IndexEmptyCount] != McpTransportProtocol.Revision)
        {
            throw InvalidTransport(McpTransportStage.ProtocolRevisionValue);
        }
        if (headers.ContainsKey(McpTransportProtocol.SessionHeader))
        {
            throw InvalidTransport(McpTransportStage.SessionHeaderPresence);
        }
        if (headers.ContainsKey(McpTransportProtocol.LastEventHeader))
        {
            throw InvalidTransport(McpTransportStage.LastEventHeaderPresence);
        }
        return new((ReadRoutingHeader(headers, McpTransportProtocol.MethodHeader, bounds.MaximumTransportMethodCharacters,
                McpTransportStage.MethodHeaderShape, McpTransportStage.MethodHeaderEncoding)).Value,
            (ReadTargetHeader(headers, bounds.MaximumTransportNameCharacters)).Value);
    }

    /// <summary>Compares bounded hints and version fields without changing business arguments or execution message identity.</summary>
    /// <param name="checkedBody">Already framing-checked bytes held under the ingress memory lease.</param>
    /// <param name="headers">Previously checked routing hints.</param>
    internal static void Inspect(ReadOnlyMemory<byte> checkedBody, McpTransportHeaders headers)
    {
        using var document = JsonDocument.Parse(checkedBody);
        var root = document.RootElement;
        var hasMethod = root.TryGetProperty(McpTransportProtocol.Method, out var method);
        if (hasMethod && method.ValueKind != JsonValueKind.String)
        {
            throw InvalidTransport(McpTransportStage.BodyMethodShape);
        }
        if (headers.Method is { } expectedMethod && (!hasMethod || !method.ValueEquals(expectedMethod)))
        {
            throw InvalidTransport(McpTransportStage.BodyMethodMismatch);
        }
        if (!root.TryGetProperty(McpTransportProtocol.Parameters, out var parameters)
            || parameters.ValueKind != JsonValueKind.Object)
        {
            if (headers.Name is not null && hasMethod && (TargetField(method)).Value is not null)
            {
                throw InvalidTransport(McpTransportStage.MissingTarget);
            }
            return;
        }
        CheckRevision(parameters, McpTransportProtocol.RevisionField, McpTransportStage.ParameterRevision);
        if (parameters.TryGetProperty(McpTransportProtocol.Meta, out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            CheckRevision(meta, MetaKeys.ProtocolVersion, McpTransportStage.MetadataRevision);
        }
        if (headers.Name is { } expectedName && hasMethod)
        {
            CheckTarget(parameters, method, expectedName);
        }
    }

    private static global::ManagedCode.Communication.Result<string?> ReadRoutingHeader(IHeaderDictionary headers, string name, int maximumCharacters,
        McpTransportStage shapeStage, McpTransportStage encodingStage)
    {
        const int EmptyValuesCount = 1;
        const int IndexEmptyCount = 0;
        const int EmptyTrimmedLength = 0;
        const char TabCharacter = '\t';
        const char SpaceCharacter = ' ';
        const char ReadRoutingHeaderCharacterToken = '~';

        if (!headers.TryGetValue(name, out var values))
        {
            return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ReadRoutingHeader));
        }
        if (values.Count != EmptyValuesCount || values[IndexEmptyCount] is not { } value)
        {
            throw InvalidTransport(shapeStage);
        }
        var trimmed = value.Trim();
        if (trimmed.Length == EmptyTrimmedLength || trimmed.Length > maximumCharacters)
        {
            throw InvalidTransport(shapeStage);
        }
        foreach (var character in trimmed)
        {
            if (character != TabCharacter && (character < SpaceCharacter || character > ReadRoutingHeaderCharacterToken))
            {
                throw InvalidTransport(encodingStage);
            }
        }
        return global::ManagedCode.Communication.Result<string?>.Succeed(((string?)(trimmed)));
    }

    private static void CheckRevision(JsonElement fields, string name, McpTransportStage stage)
    {
        if (fields.TryGetProperty(name, out var revision)
            && (revision.ValueKind != JsonValueKind.String || !revision.ValueEquals(McpTransportProtocol.Revision)))
        {
            throw InvalidTransport(stage);
        }
    }

    private static global::ManagedCode.Communication.Result<string?> ReadTargetHeader(IHeaderDictionary headers, int maximumCharacters)
    {
        const int EmptyDecodedLength = 0;
        const char TabCharacter = '\t';
        const char SpaceCharacter = ' ';
        const char ReadTargetHeaderCharacterToken = '\u007F';

        var encoded = (ReadRoutingHeader(headers, McpTransportProtocol.NameHeader, maximumCharacters,
            McpTransportStage.NameHeaderShape, McpTransportStage.NameHeaderEncoding)).Value;
        if (encoded is null)
        {
            return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ReadTargetHeader));
        }
        if (encoded.StartsWith(McpTransportProtocol.EncodedHeaderPrefix, StringComparison.Ordinal)
            && !encoded.EndsWith(McpTransportProtocol.EncodedHeaderSuffix, StringComparison.Ordinal))
        {
            throw InvalidTransport(McpTransportStage.NameHeaderEncoding);
        }
        var decoded = McpHeaderEncoder.DecodeValue(encoded);
        if (decoded is null || decoded.Length == EmptyDecodedLength || decoded.Length > maximumCharacters)
        {
            throw InvalidTransport(McpTransportStage.NameHeaderEncoding);
        }
        foreach (var character in decoded)
        {
            if (character != TabCharacter && (character < SpaceCharacter || character == ReadTargetHeaderCharacterToken))
            {
                throw InvalidTransport(McpTransportStage.NameHeaderEncoding);
            }
        }
        return global::ManagedCode.Communication.Result<string?>.Succeed(((string?)(decoded)));
    }

    private static void CheckTarget(JsonElement parameters, JsonElement method, string expectedName)
    {
        var name = (TargetField(method)).Value;
        if (name is null)
        {
            return;
        }
        if (!parameters.TryGetProperty(name, out var target))
        {
            throw InvalidTransport(McpTransportStage.MissingTarget);
        }
        if (target.ValueKind != JsonValueKind.String || !target.ValueEquals(expectedName))
        {
            throw InvalidTransport(McpTransportStage.TargetMismatch);
        }
    }

    private static global::ManagedCode.Communication.Result<string?> TargetField(JsonElement method) => method.ValueEquals(RequestMethods.ResourcesRead) ? global::ManagedCode.Communication.Result<string?>.Succeed(((string?)(McpTransportProtocol.Uri)))
        : method.ValueEquals(RequestMethods.ToolsCall) || method.ValueEquals(RequestMethods.PromptsGet) ? global::ManagedCode.Communication.Result<string?>.Succeed(((string?)(McpTransportProtocol.Name))) : global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(TargetField));

    private static KeyLoadException InvalidTransport(McpTransportStage stage)
    {
        var error = Errors.Fail(ErrorCode.Validation, McpTransportProtocol.InvalidTransport);
        error.Data[McpTransportDiagnostics.StageMetadataKey] = stage;
        return error;
    }
}
