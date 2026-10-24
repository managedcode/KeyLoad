using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Npgsql;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Creates benchmark-only native SQL targets with bounded, namespace-specific provider pools.</summary>
public static partial class RelationalComparisonTargets
{
    private const string SqlServer = BenchmarkAdditionalTargets.SqlServer, MySql = BenchmarkAdditionalTargets.MySql, Timescale = BenchmarkAdditionalTargets.Timescale;
    private const string InvalidEngine = "RelationalComparisonEngineRejected";
    private const string PublicSearchPath = ",public";
    private const int MinimumPoolSize = 0;

    /// <summary>Creates a genuine SQL Server, MySQL/InnoDB or TimescaleDB document session.</summary>
    public static IComparisonTarget Create(string target, string connectionString, string runId,
        string image, IOptions<ComparisonExecutionOptions> options)
    {
        var policy = ComparisonExecutionOptions.Require(options).Value;
        var name = PostgresSchemaIdentity.FromRunId(runId).Name;
        var connections = Connections(target, connectionString, name, policy);
        return new RelationalDocumentTarget(connections.Admin, connections.Worker, connections.Clear,
            new(target), name, image, options);
    }

    private static (Func<DbConnection> Admin, Func<DbConnection> Worker, Func<CancellationToken, Task> Clear)
        Connections(string target, string connection, string name, ComparisonExecutionOptions policy)
    {
        return target switch
        {
            SqlServer => SqlServerConnections(connection, name, policy),
            MySql => MySqlConnections(connection, name, policy),
            Timescale => PostgresConnections(connection, name, policy),
            _ => throw new InvalidOperationException(InvalidEngine)
        };
    }

    private static (Func<DbConnection> Admin, Func<DbConnection> Worker, Func<CancellationToken, Task> Clear)
        SqlServerConnections(string connection, string name, ComparisonExecutionOptions policy)
    {
        var settings = new SqlConnectionStringBuilder(connection)
        {
            MaxPoolSize = policy.PostgresMaxPoolSize,
            MinPoolSize = MinimumPoolSize,
            ConnectTimeout = checked((int)Math.Ceiling(policy.OperationTimeout.TotalSeconds)),
            Pooling = false
        };
        var admin = settings.ConnectionString;
        settings.InitialCatalog = name;
        settings.Pooling = true;
        var worker = settings.ConnectionString;
        return (() => new SqlConnection(admin), () => new SqlConnection(worker), token =>
        {
            token.ThrowIfCancellationRequested();
            using var pool = new SqlConnection(worker);
            SqlConnection.ClearPool(pool);
            return Task.CompletedTask;
        }
        );
    }

    private static (Func<DbConnection> Admin, Func<DbConnection> Worker, Func<CancellationToken, Task> Clear)
        MySqlConnections(string connection, string name, ComparisonExecutionOptions policy)
    {
        var settings = new MySqlConnectionStringBuilder(connection)
        {
            MaximumPoolSize = checked((uint)policy.PostgresMaxPoolSize),
            MinimumPoolSize = MinimumPoolSize,
            ConnectionTimeout = checked((uint)Math.Ceiling(policy.OperationTimeout.TotalSeconds)),
            DefaultCommandTimeout = checked((uint)Math.Ceiling(policy.OperationTimeout.TotalSeconds)),
            CancellationTimeout = checked((int)Math.Ceiling(policy.OperationTimeout.TotalSeconds)),
            AllowUserVariables = true,
            Pooling = false
        };
        var admin = settings.ConnectionString;
        settings.Database = name;
        settings.Pooling = true;
        var worker = settings.ConnectionString;
        return (() => new MySqlConnection(admin), () => new MySqlConnection(worker), async token =>
        {
            await using var pool = new MySqlConnection(worker);
            await MySqlConnection.ClearPoolAsync(pool, token).ConfigureAwait(false);
        }
        );
    }

    private static (Func<DbConnection> Admin, Func<DbConnection> Worker, Func<CancellationToken, Task> Clear)
        PostgresConnections(string connection, string name, ComparisonExecutionOptions policy)
    {
        var settings = new NpgsqlConnectionStringBuilder(connection)
        {
            MaxPoolSize = policy.PostgresMaxPoolSize,
            MinPoolSize = MinimumPoolSize,
            Timeout = checked((int)Math.Ceiling(policy.OperationTimeout.TotalSeconds)),
            CommandTimeout = checked((int)Math.Ceiling(policy.OperationTimeout.TotalSeconds)),
            Pooling = false
        };
        var admin = settings.ConnectionString;
        settings.SearchPath = name + PublicSearchPath;
        settings.Pooling = true;
        var worker = settings.ConnectionString;
        return (() => new NpgsqlConnection(admin), () => new NpgsqlConnection(worker), token =>
        {
            token.ThrowIfCancellationRequested();
            using var pool = new NpgsqlConnection(worker);
            NpgsqlConnection.ClearPool(pool);
            return Task.CompletedTask;
        }
        );
    }
}
