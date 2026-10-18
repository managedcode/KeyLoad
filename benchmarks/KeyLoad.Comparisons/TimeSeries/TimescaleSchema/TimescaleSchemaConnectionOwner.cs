using System.Runtime.ExceptionServices;
using Npgsql;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal sealed class TimescaleSchemaConnectionOwner : IAsyncDisposable
{
    internal TimescaleSchemaConnectionOwner(string? connectionString, NpgsqlDataSource? dataSource)
    {
        Connection = dataSource is null ? new NpgsqlConnection(connectionString) : dataSource.CreateConnection();
    }

    internal NpgsqlConnection Connection
    {
        get;
    }
    internal ExceptionDispatchInfo? Primary
    {
        get; set;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            Primary?.Throw();
            throw;
        }
    }
}
