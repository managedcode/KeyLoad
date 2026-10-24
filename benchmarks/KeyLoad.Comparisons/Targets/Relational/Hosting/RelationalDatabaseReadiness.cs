using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace KeyLoad.Comparisons.Targets;

public static partial class RelationalComparisonTargets
{
    private const string ReadinessProbe = "SELECT 1";

    /// <summary>Tests an actual native connection before the measured client starts; no corpus work runs here.</summary>
    public static async Task<bool> IsReadyAsync(string target, string connectionString,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var seconds = checked((int)Math.Ceiling(timeout.TotalSeconds));
        await using DbConnection connection = target switch
        {
            SqlServer => new SqlConnection(new SqlConnectionStringBuilder(connectionString) { Pooling = false, ConnectTimeout = seconds }.ConnectionString),
            MySql => new MySqlConnection(new MySqlConnectionStringBuilder(connectionString) { Pooling = false, ConnectionTimeout = checked((uint)seconds) }.ConnectionString),
            Timescale => new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, Timeout = seconds }.ConnectionString),
            _ => throw new InvalidOperationException(InvalidEngine)
        };
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = ReadinessProbe;
            command.CommandTimeout = seconds;
            _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
