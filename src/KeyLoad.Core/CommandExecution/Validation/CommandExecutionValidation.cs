using System.Text.Json;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes CommandExecutionValidation operations against the caller's database context.</summary>
public static class CommandExecutionValidation
{
    internal const string UnsupportedDatabaseCommand = "The operation is unsupported.";
    internal const string InvalidDatabaseCommandJson = "The operation contains invalid protocol JSON.";
    /// <summary>Parses public JSON once at the trusted boundary and creates an owned execution command.</summary>
    /// <param name = "kind">The supported database operation.</param>
    /// <param name = "id">Original stable command identity.</param>
    /// <param name = "principalId">The server-verified subject.</param>
    /// <param name = "evaluatedAt">The server-evaluated business time.</param>
    /// <param name = "payloadJson">Exact original public input.</param>
    /// <returns>The execution communication command containing generated database input.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static Command<DatabaseCommand> CreateCommand(this DatabaseEngine engine, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(engine) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(payloadJson) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        if (kind is OperationKind.FileMetadata or OperationKind.Bm25CorpusPublicationPhase or OperationKind.OnlineTextPublicationPhase)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedDatabaseCommand);
        }

        var /* KLD0041: This native command admission receives the exact Kind-selected business input before storing it in the existing persisted envelope. */ input = DatabaseCommandInputTypes.Admit(kind, Normalize(payloadJson, DatabaseCommandInputTypes.For(kind)) /* KLD0041: JSON admission selects a closed business contract from the operation kind before the native persisted envelope is created. */) /* KLD0041: The closed operation-kind map validates the exact runtime contract held by the existing persisted input slot. */;
        return engine.CreatePreparedCommand(kind, id, principalId, evaluatedAt, input, null) /* KLD0041: This admission preserves the Kind-selected native payload in the existing persisted command envelope. */;
    }

    private static object /* KLD0041: This native command admission receives the exact Kind-selected business input before storing it in the existing persisted envelope. */ Normalize(string json, Type type)
    {
        try
        {
            return JsonDefaults.Deserialize(json, type) /* KLD0041: This JSON boundary uses the closed operation-kind Type selected by the validated command map. */;
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidDatabaseCommandJson);
        }
    }
}
