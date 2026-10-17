namespace KeyLoad;

/// <summary>Confirms the creation of a backup and the committed position it contains.</summary>
/// <param name="Id">The backup identifier.</param>
/// <param name="Position">The committed position captured by the backup.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BackupReceipt)]
public sealed record BackupReceipt([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] long Position);
