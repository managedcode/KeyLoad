using System.Data.Common;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class RelationalDocumentTarget(Func<DbConnection> admin, Func<DbConnection> connect,
    Func<CancellationToken, Task> clear, RelationalDocumentSql sql, string namespaceName,
    string image, IOptions<ComparisonExecutionOptions> options)
    : IDocumentComparisonTarget, IBenchmarkSessionQueries
{
    private const string Unobserved = "unobserved";
    private const string Topology = "one primary; no replicas";
    private const string ReadContract = "primary reads after acknowledged autocommit; ordinal document identity";
    private const string Transport = "parameterized SQL/TCP";
    private const string Authorization = "isolated benchmark namespace; database owner";
    private const string InvalidSettings = "RelationalPersistenceSettingsRejected";
    private const string RepeatedInitialization = "RelationalTargetAlreadyInitialized";
    private const int VersionColumn = 0, SettingColumn = 1, SingleCopy = 1;
    private const string NamespaceParameter = "@namespace";
    private bool initialized;
    private bool ownsNamespace;
    private readonly ComparisonExecutionOptions policy = ComparisonExecutionOptions.Require(options).Value;

    public TargetProfile Profile { get; private set; } = new(sql.Name, Unobserved, Topology,
        sql.Acknowledgement, ReadContract, Transport, Authorization, image);

    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite
        or Scenario.DocumentUpdate or Scenario.DocumentDelete;
    public int IngestionBatchRecords => sql.Batch.Records;

    public Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken token)
    {
        DocumentInitialization.Validate(initialization);
        return InitializeAsync(initialization.Corpus, token);
    }

    public async Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (initialized)
        {
            throw new InvalidOperationException(RepeatedInitialization);
        }
        initialized = true;
        await using (var administrator = await OpenAsync(cancellationToken, administration: true).ConfigureAwait(false))
        {
            await ExecuteAsync(administrator, RelationalDocumentCommand.CreateNamespace, cancellationToken).ConfigureAwait(false);
            ownsNamespace = true;
        }
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        if (sql.Name == BenchmarkAdditionalTargets.Timescale)
        {
            await ExecuteAsync(connection, RelationalDocumentCommand.InitializeExtension, cancellationToken).ConfigureAwait(false);
        }
        await using (var observe = Command(connection, RelationalDocumentCommand.Observe))
        await using (var reader = await observe.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                || reader.GetString(SettingColumn) != sql.RequiredSetting)
            {
                throw new ComparisonFailureException(InvalidSettings);
            }
            Profile = Profile with
            {
                Version = reader.GetString(VersionColumn),
                Cluster = new(SingleCopy, SingleCopy, Topology, [sql.Acknowledgement])
            };
        }
        await ExecuteAsync(connection, RelationalDocumentCommand.CreateTable, cancellationToken).ConfigureAwait(false);
        await using var session = new RelationalDocumentSession(connection, sql, options);
        foreach (var document in dataset.Documents)
        {
            await session.ExecuteAsync(Scenario.DocumentWrite, document, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => await OpenDocumentSessionAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken cancellationToken)
        => new RelationalDocumentSession(await OpenAsync(cancellationToken).ConfigureAwait(false), sql, options);

    public async Task BuildSessionIndexesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, RelationalDocumentCommand.Index);
        command.CommandTimeout = checked((int)Math.Ceiling(policy.IndexBuildTimeout.TotalSeconds));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken)
    {
        var expected = BenchmarkSessionQueryContract.Expected(ordinal, stableStart, stableCount);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, RelationalDocumentCommand.Query);
        RelationalDocumentSession.Bind(command, expected);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var found = await RelationalDocumentSession.ReadOneAsync(reader, cancellationToken).ConfigureAwait(false);
        BenchmarkSessionQueryContract.Require(found, expected);
    }

    public async ValueTask DisposeAsync()
    {
        if (!ownsNamespace)
        {
            return;
        }
        using var deadline = new CancellationTokenSource(policy.CleanupTimeout);
        await clear(deadline.Token).ConfigureAwait(false);
        await using var connection = await OpenAsync(deadline.Token, administration: true).ConfigureAwait(false);
        await ExecuteAsync(connection, RelationalDocumentCommand.DropNamespace, deadline.Token).ConfigureAwait(false);
        ownsNamespace = false;
    }

    private async Task<DbConnection> OpenAsync(CancellationToken token, bool administration = false)
    {
        var connection = administration ? admin() : connect();
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            return connection;
        }
        catch (Exception)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private DbCommand Command(DbConnection connection, RelationalDocumentCommand operation)
    {
        var command = sql.Command(connection, operation, policy.OperationTimeout);
        if (operation is RelationalDocumentCommand.CreateNamespace or RelationalDocumentCommand.DropNamespace)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = NamespaceParameter;
            parameter.Value = namespaceName;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private async Task ExecuteAsync(DbConnection connection, RelationalDocumentCommand operation, CancellationToken token)
    {
        await using var command = Command(connection, operation);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
