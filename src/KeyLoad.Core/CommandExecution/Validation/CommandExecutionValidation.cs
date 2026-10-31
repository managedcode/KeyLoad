using System.Text.Json;
using KeyLoad.Core.InternalSerialization;
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
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(payloadJson);
        if (kind is OperationKind.FileMetadata or OperationKind.Bm25CorpusPublicationPhase or OperationKind.OnlineTextPublicationPhase)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedDatabaseCommand);
        }

        var input = DatabaseCommandInputTypes.Require(kind, Normalize(payloadJson, DatabaseCommandInputTypes.For(kind)));
        return engine.CreatePreparedCommand(kind, id, principalId, evaluatedAt, input, DatabaseCommandFingerprint.Compute(id, kind, principalId, payloadJson), null);
    }

    private static object Normalize(string json, Type type)
    {
        try
        {
            return JsonDefaults.Deserialize(json, type);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidDatabaseCommandJson);
        }
    }
}
