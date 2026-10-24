using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class RelationalDocumentSession(DbConnection connection, RelationalDocumentSql sql,
    IOptions<ComparisonExecutionOptions> options) : IDocumentComparisonSession
{
    private readonly ComparisonExecutionOptions policy = ComparisonExecutionOptions.Require(options).Value;
    private const string IdParameter = "@id", BodyParameter = "@body", NumberParameter = "@number";
    private const int IdSize = 64, BodySize = 1024, IdColumn = 0, BodyColumn = 1, SingleRow = 1;
    private const string UnexpectedRows = "RelationalDocumentRowCountMismatch";
    public Guid ClientIdentity { get; } = Guid.NewGuid();
    public bool SingleOperationTiming => true;

    public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
    {
        if (scenario == Scenario.PointRead)
        {
            return new(Document: await ReadAsync(document, cancellationToken).ConfigureAwait(false));
        }
        var operation = scenario switch
        {
            Scenario.DocumentWrite => RelationalDocumentCommand.Insert,
            Scenario.DocumentUpdate => RelationalDocumentCommand.Update,
            Scenario.DocumentDelete => RelationalDocumentCommand.Delete,
            _ => throw new NotSupportedException()
        };
        await using var command = sql.Command(connection, operation, policy.OperationTimeout);
        Bind(command, document);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != SingleRow)
        {
            throw new ComparisonFailureException(UnexpectedRows);
        }
        return new();
    }

    public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        await using var command = sql.Command(connection, RelationalDocumentCommand.Read, policy.OperationTimeout);
        Bind(command, document);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await ReadOneAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<FoundDocument> ReadCorpusAsync(CancellationToken cancellationToken)
        => ReadDocumentsAsync(cancellationToken);

    // One bulk operation keeps the single create's autocommit, primary-key rejection and exact acknowledged-row check.
    public async Task CreateDocumentsAsync(IReadOnlyList<BenchmarkDocument> documents, CancellationToken cancellationToken)
    {
        var acknowledged = await sql.Batch.CreateAsync(connection, documents, policy.OperationTimeout, cancellationToken).ConfigureAwait(false);
        if (acknowledged != documents.Count)
        {
            throw new ComparisonFailureException(UnexpectedRows);
        }
    }

    public async IAsyncEnumerable<FoundDocument> ReadDocumentsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = sql.Command(connection, RelationalDocumentCommand.Enumerate, policy.OperationTimeout);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new(reader.GetString(IdColumn), reader.GetString(BodyColumn));
        }
    }

    internal static void Bind(DbCommand command, BenchmarkDocument document)
    {
        Add(command, IdParameter, DbType.String, document.Id, IdSize);
        Add(command, BodyParameter, DbType.String, document.Json, BodySize);
        Add(command, NumberParameter, DbType.Int32, document.Number);
    }

    private static void Add(DbCommand command, string name, DbType type, object value, int? size = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        if (size is { } bound)
        {
            parameter.Size = bound;
        }
        command.Parameters.Add(parameter);
    }

    internal static async Task<FoundDocument?> ReadOneAsync(DbDataReader reader, CancellationToken token)
    {
        var found = await reader.ReadAsync(token).ConfigureAwait(false)
            ? new FoundDocument(reader.GetString(IdColumn), reader.GetString(BodyColumn)) : null;
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw new ComparisonFailureException(UnexpectedRows);
        }
        return found;
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
