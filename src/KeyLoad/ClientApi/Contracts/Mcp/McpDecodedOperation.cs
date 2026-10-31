using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Canonical typed operation and caller write identity, before trusted gateway authority is attached.</summary>
/// <param name="ReadKind">Public read capability, mutually exclusive with the command capability.</param>
/// <param name="CommandKind">Public command capability, mutually exclusive with the read capability.</param>
/// <param name="CommandId">Caller stable write identity; empty for read-dispatch operations.</param>
/// <param name="Payload">The typed operation input, independent of the public argument document.</param>
internal sealed record McpDecodedOperation(GrainReadKind? ReadKind, OperationKind? CommandKind,
    Guid CommandId, object Payload);
