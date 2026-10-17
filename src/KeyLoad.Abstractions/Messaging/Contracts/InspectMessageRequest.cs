namespace KeyLoad;

/// <summary>Identifies a queued message to inspect.</summary>
/// <param name="Lane">The queue lane containing the message.</param>
/// <param name="Id">The message identifier.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.InspectMessageRequest)]
public sealed record InspectMessageRequest([property: Orleans.Id(0)] QueueLaneRef Lane, [property: Orleans.Id(1)] string Id);
