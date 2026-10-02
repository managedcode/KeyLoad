using System.Text.Json;
using KeyLoad.Orleans;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Immutable version-one metadata and typed decoder for one canonical public operation.</summary>
internal sealed record McpOperationDescriptor
{
    private readonly Func<IDictionary<string, JsonElement>?, int, McpDecodedOperation> decoder;

    internal McpOperationDescriptor(string name, string route, GrainReadKind? readKind, OperationKind? commandKind,
        string description, JsonElement inputSchema, JsonElement outputSchema, McpToolHints hints,
        Func<IDictionary<string, JsonElement>?, int, McpDecodedOperation> decoder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(decoder);
        if (readKind.HasValue == commandKind.HasValue)
        {
            throw new ArgumentException(McpCatalogProtocol.InvalidOperation, nameof(readKind));
        }
        Name = name;
        Route = route;
        ReadKind = readKind;
        CommandKind = commandKind;
        Description = description;
        InputSchema = inputSchema;
        OutputSchema = outputSchema;
        ReadOnly = hints.ReadOnly;
        Idempotent = hints.Idempotent;
        Destructive = hints.Destructive;
        this.decoder = decoder;
    }

    /// <summary>Stable version-one native tool name.</summary>
    internal string Name { get; }
    /// <summary>Existing canonical HTTP admission route.</summary>
    internal string Route { get; }
    /// <summary>Read-dispatch capability, absent for commands.</summary>
    internal GrainReadKind? ReadKind { get; }
    /// <summary>Command-dispatch capability, absent for reads.</summary>
    internal OperationKind? CommandKind { get; }
    /// <summary>Agent guidance including pagination and retry behavior.</summary>
    internal string Description { get; }
    /// <summary>Owned immutable root-object argument schema.</summary>
    internal JsonElement InputSchema { get; }
    /// <summary>Owned immutable structured success/error envelope schema.</summary>
    internal JsonElement OutputSchema { get; }
    /// <summary>Explicit advisory read-only hint; backup has physical side effects.</summary>
    internal bool ReadOnly { get; }
    /// <summary>Advisory retry hint; command retries require the same identity and payload.</summary>
    internal bool Idempotent { get; }
    /// <summary>Advisory hint for operations that can remove or consume existing data.</summary>
    internal bool Destructive { get; }

    /// <summary>Decodes strict outer arguments using the actual canonical JSON contract.</summary>
    /// <param name="arguments">Native SDK arguments, inspected without mutation.</param>
    /// <param name="maximumPayloadBytes">The actual canonical lane ceiling, reserved before decode.</param>
    /// <returns>Owned canonical operation bytes and caller write identity.</returns>
    internal McpDecodedOperation Decode(IDictionary<string, JsonElement>? arguments,
        int maximumPayloadBytes = ServerProtocol.KestrelMaximumBodyBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPayloadBytes);
        return decoder(arguments, maximumPayloadBytes);
    }

    /// <summary>Creates fresh native mutable discovery metadata without exposing shared mutable tools.</summary>
    /// <returns>A new official SDK tool and annotation object.</returns>
    internal Tool CreateTool() => new()
    {
        Name = Name,
        Description = Description,
        InputSchema = InputSchema,
        OutputSchema = OutputSchema,
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = ReadOnly,
            IdempotentHint = Idempotent,
            DestructiveHint = Destructive,
            OpenWorldHint = false
        }
    };
}
