using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Canonical owned operation bytes and caller write identity, before trusted gateway authority is attached.</summary>
/// <param name="ReadKind">Public read capability, mutually exclusive with the command capability.</param>
/// <param name="CommandKind">Public command capability, mutually exclusive with the read capability.</param>
/// <param name="CommandId">Caller stable write identity; empty for read-dispatch operations.</param>
/// <param name="Payload">Owned canonical UTF8 JSON, independent of the native argument document.</param>
internal sealed record McpDecodedOperation(GrainReadKind? ReadKind, OperationKind? CommandKind,
    Guid CommandId, ReadOnlyMemory<byte> Payload);
