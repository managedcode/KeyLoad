namespace KeyLoad;

/// <summary>Requests a non-consuming administrator view of one queue lane.</summary>
/// <param name="Lane">Full atomic queue identity.</param>
/// <param name="AfterId">Exclusive metadata identifier continuation.</param>
/// <param name="Limit">Maximum returned metadata records.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.AdminQueueRequest)]
public sealed record AdminQueueRequest([property: Orleans.Id(0)] QueueLaneRef Lane, [property: Orleans.Id(1)] string? AfterId = null,
    [property: Orleans.Id(2)] int Limit = AdminDashboardProtocol.DefaultPageSize);
