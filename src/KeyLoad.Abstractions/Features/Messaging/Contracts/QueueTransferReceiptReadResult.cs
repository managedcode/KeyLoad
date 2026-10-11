namespace KeyLoad;

/// <summary>Contains the authorized target receipt, or successful absence.</summary>
/// <param name="Receipt">The retained genuine target receipt; null means no matching receipt exists.</param>
[Orleans.GenerateSerializer, Orleans.Alias(PublicReadResultAliases.Receipt)]
public sealed record QueueTransferReceiptReadResult([property: Orleans.Id(0)] QueueTransferReceiptInspection? Receipt);
