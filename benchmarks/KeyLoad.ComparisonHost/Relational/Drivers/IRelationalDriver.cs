using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

namespace KeyLoad.ComparisonHost.Relational;

/// <summary>The only operations a relational database implements; thread-safe, shared by all clients.</summary>
internal interface IRelationalDriver : IAsyncDisposable
{
    /// <summary>Rows per <see cref="InsertAsync"/> call during the load.</summary>
    int BatchSize { get; }

    /// <summary>Creates <c>orders(id BIGINT PRIMARY KEY, customer INT, amount DECIMAL(12,2), status VARCHAR(16), created TIMESTAMP)</c>.</summary>
    Task PrepareAsync(CancellationToken token);

    /// <summary>Inserts rows with one statement; returns after commit.</summary>
    Task InsertAsync(IReadOnlyList<OrderRow> rows, CancellationToken token);

    /// <summary>Selects one row by primary key; throws <see cref="KeyNotFoundException"/> when missing.</summary>
    Task<OrderRow> SelectAsync(long id, CancellationToken token);

    /// <summary>Selects the ids of all rows with <c>id BETWEEN from AND to</c>, in any order.</summary>
    Task<IReadOnlyList<long>> SelectRangeAsync(long from, long to, CancellationToken token);

    /// <summary>Sets <c>amount</c> of one existing row.</summary>
    Task UpdateAmountAsync(long id, decimal amount, CancellationToken token);

    /// <summary>Deletes one existing row.</summary>
    Task DeleteAsync(long id, CancellationToken token);
}

/// <summary>Creates the relational driver of the deployed database.</summary>
internal static class RelationalDrivers
{
    internal static IRelationalDriver Create(BenchmarkConnection connection) => connection.Database switch
    {
        BenchmarkDatabases.KeyLoad => new KeyLoadRelationalDriver(connection),
        BenchmarkDatabases.Postgres => new PostgresRelationalDriver(connection),
        BenchmarkDatabases.SqlServer => new SqlServerRelationalDriver(connection),
        BenchmarkDatabases.MySql => new MySqlRelationalDriver(connection),
        _ => throw new NotSupportedException(BenchmarkTypes.NoDriver + BenchmarkTypes.Relational + BenchmarkTypes.ForDatabase + connection.Database)
    };
}
