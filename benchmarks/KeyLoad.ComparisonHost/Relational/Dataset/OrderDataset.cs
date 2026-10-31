namespace KeyLoad.ComparisonHost.Relational;

/// <summary>A row of the <c>orders</c> table.</summary>
/// <param name="Id">Primary key, <c>BIGINT</c>.</param>
/// <param name="Customer"><c>INT</c>, 10,000 distinct customers.</param>
/// <param name="Amount"><c>DECIMAL(12,2)</c>.</param>
/// <param name="Status"><c>VARCHAR(16)</c>: new, paid, shipped or closed.</param>
/// <param name="Created"><c>TIMESTAMP</c> in UTC, one second apart.</param>
internal sealed record OrderRow(long Id, int Customer, decimal Amount, string Status, DateTimeOffset Created);

/// <summary>The standard relational dataset: one <c>orders</c> table with typed columns.</summary>
internal static class OrderDataset
{
    internal const string Table = "orders";
    internal const int Customers = 10_000;
    private const int AmountCents = 100_000;
    private const decimal Cents = 100m;
    private static readonly string[] Statuses = ["new", "paid", "shipped", "closed"];
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static OrderRow Create(long id) => new(id, (int)(id % Customers), Amount(id),
        Statuses[id % Statuses.Length], Epoch.AddSeconds(id));

    internal static decimal Amount(long id) => (id % AmountCents) / Cents;

    /// <summary>Checks the typed columns of a returned row; <c>Created</c> is not compared because engines differ in time zones.</summary>
    internal static bool Matches(OrderRow row, OrderRow expected, decimal amount)
        => row.Id == expected.Id && row.Customer == expected.Customer && row.Amount == amount
            && string.Equals(row.Status, expected.Status, StringComparison.Ordinal);
}
