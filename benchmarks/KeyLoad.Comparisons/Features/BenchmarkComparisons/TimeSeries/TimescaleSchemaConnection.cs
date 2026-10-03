using System.Runtime.ExceptionServices;
using Npgsql;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimescaleSchemaConnection
{
    internal static async Task<T> ExecuteAsync<T>(string? connectionString, NpgsqlDataSource? dataSource,
        Func<NpgsqlConnection, Task<T>> operation)
    {
        await using var owner = new TimescaleSchemaConnectionOwner(connectionString, dataSource);
        try
        {
            return await operation(owner.Connection).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            owner.Primary = ExceptionDispatchInfo.Capture(error);
            throw;
        }
    }
}

internal sealed class TimescaleSchemaConnectionOwner : IAsyncDisposable
{
    internal TimescaleSchemaConnectionOwner(string? connectionString, NpgsqlDataSource? dataSource)
    {
        Connection = dataSource is null ? new NpgsqlConnection(connectionString) : dataSource.CreateConnection();
    }

    internal NpgsqlConnection Connection { get; }
    internal ExceptionDispatchInfo? Primary { get; set; }

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
