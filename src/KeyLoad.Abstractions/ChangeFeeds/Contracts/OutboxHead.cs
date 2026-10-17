namespace KeyLoad;

/// <summary>Reports the retained range and storage usage of the system outbox.</summary>
/// <param name="Tail">The latest outbox sequence.</param>
/// <param name="FirstAvailable">The earliest retained sequence.</param>
/// <param name="StoredRecords">The number of retained records.</param>
/// <param name="StoredBytes">The bytes occupied by retained records.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.OutboxHead)]
public sealed record OutboxHead([property: Orleans.Id(0)] long Tail, [property: Orleans.Id(1)] long FirstAvailable, [property: Orleans.Id(2)] long StoredRecords, [property: Orleans.Id(3)] long StoredBytes);
