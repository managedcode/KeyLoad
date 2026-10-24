using KeyLoad.QueryExecution;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Server;

internal static class ScriptDomExecOperation
{
    private const int EmptyCount = 0;
    private const int SingleArgument = 1;
    private const int FirstIndex = 0;
    private const int ParameterPrefixLength = 1;
    private const string ParameterPrefix = "@";
    /// <summary>Cluster administration and credential tools stay outside the SQL surface; the SDK and MCP own them.</summary>
    private static readonly HashSet<string> AdministrationTools = new(StringComparer.Ordinal)
    {
        McpToolNames.AdminBackup, McpToolNames.AdminStatus, McpToolNames.AdminDispatch,
        McpToolNames.PrincipalsConfigure, McpToolNames.CredentialsConfigure
    };

    internal static McpDecodedOperation Compile(SqlOperationRequest request, ExecuteStatement statement,
        int maximumPayloadBytes, int? positionalParameterCount, Action checkBudget, CancellationToken token)
    {
        checkBudget();
        token.ThrowIfCancellationRequested();
        if (request.AllowFullScan || request.Cursor is not null)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        if (request.QueryDialectVersion != SqlOperationProtocol.DefaultQueryDialectVersion)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        var specification = statement.ExecuteSpecification;
        if (statement.Options.Count != EmptyCount || specification.Variable is not null
            || specification.LinkedServer is not null || specification.ExecuteContext is not null
            || specification.ExecutableEntity is not ExecutableProcedureReference procedure)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        var name = ProcedureName(procedure);
        if (procedure.Parameters.Count != SingleArgument)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        var parameter = procedure.Parameters[FirstIndex];
        if (parameter.IsOutput || parameter.Variable is not null)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        var binding = ParameterName(parameter.ParameterValue, positionalParameterCount);
        var arguments = BindArguments(request.Parameters, binding, checkBudget, token);
        if (name == SqlOperationProtocol.ToolName || AdministrationTools.Contains(name)
            || !McpOperationCatalog.TryGet(name, out var descriptor)
            || descriptor.ReadKind.HasValue == descriptor.CommandKind.HasValue)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        checkBudget();
        token.ThrowIfCancellationRequested();
        var operation = descriptor.Decode(arguments, maximumPayloadBytes);
        checkBudget();
        token.ThrowIfCancellationRequested();
        return operation;
    }

    private static string ProcedureName(ExecutableProcedureReference procedure)
    {
        var reference = procedure.ProcedureReference;
        if (procedure.AdHocDataSource is not null || reference.ProcedureVariable is not null
            || reference.ProcedureReference is not { Number: null } target
            || target.Name.Identifiers.Count != SingleArgument)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        return target.Name.Identifiers[FirstIndex].Value;
    }

    private static string ParameterName(ScalarExpression value, int? positionalParameterCount)
    {
        if (positionalParameterCount is { } count)
        {
            if (value is MoneyLiteral slot && ScriptDomSyntax.TryGetSessionSlot(slot, count, out var name))
            {
                return name;
            }
            throw ScriptDomOperationErrors.Invalid();
        }
        if (value is VariableReference variable
            && variable.Name.Length > ParameterPrefixLength
            && variable.Name.StartsWith(ParameterPrefix, StringComparison.Ordinal))
        {
            return variable.Name[ParameterPrefixLength..];
        }
        throw ScriptDomOperationErrors.Unsupported();
    }

    private static Dictionary<string, global::System.Text.Json.JsonElement> BindArguments(Dictionary<string, global::System.Text.Json.JsonElement>? parameters,
        string binding, Action checkBudget, CancellationToken token)
    {
        if (parameters is not { Count: SingleArgument }
            || !parameters.Keys.Contains(binding, StringComparer.Ordinal)
            || !parameters.TryGetValue(binding, out var envelope) || envelope.ValueKind != global::System.Text.Json.JsonValueKind.Object)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        var arguments = new Dictionary<string, global::System.Text.Json.JsonElement>(StringComparer.Ordinal);
        foreach (var property in envelope.EnumerateObject())
        {
            checkBudget();
            token.ThrowIfCancellationRequested();
            if (!arguments.TryAdd(property.Name, property.Value))
            {
                throw ScriptDomOperationErrors.Invalid();
            }
        }
        return arguments;
    }
}
