namespace KeyLoad;

/// <summary>Requests a bounded number of messages from one queue lane.</summary>
/// <param name="RequestId">Identifies this receive request.</param>
/// <param name="Lane">Identifies the queue lane.</param>
/// <param name="MaxMessages">Limits deliveries returned by the request.</param>
/// <param name="MaxBytes">Limits returned payload bytes.</param>
/// <param name="LeaseSeconds">Requests the message lease duration.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReceiveRequest)]
public sealed record ReceiveRequest([property: Orleans.Id(0)] Guid RequestId, [property: Orleans.Id(1)] QueueLaneRef Lane, [property: Orleans.Id(2)] int MaxMessages = ReceiveRequest.DefaultMaxMessages,
    [property: Orleans.Id(3)] int MaxBytes = ReceiveRequest.DefaultMaxBytes, [property: Orleans.Id(4)] int LeaseSeconds = ReceiveRequest.DefaultLeaseSeconds)
{
    private const int DefaultMaxMessages = 1;
    private const int DefaultMaxBytes = 1_048_576;
    private const int DefaultLeaseSeconds = 30;
}
