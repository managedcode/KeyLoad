namespace KeyLoad;

/// <summary>Reports queue storage and in-flight usage and the next ready sequence.</summary>
/// <param name="StoredMessages">Specifies the stored messages value.</param>
/// <param name="StoredBytes">Specifies the stored bytes value.</param>
/// <param name="InFlightMessages">Specifies the in flight messages value.</param>
/// <param name="InFlightBytes">Specifies the in flight bytes value.</param>
/// <param name="NextReadySequence">Specifies the next ready sequence value.</param>
/// <param name="DeadLetterMessages">The retained parked-message count charged to the dead-letter grant.</param>
/// <param name="DeadLetterBytes">The retained parked-body bytes charged to the dead-letter grant.</param>
/// <param name="NextParkedSequence">The next monotonically increasing dead-letter ordering sequence.</param>
/// <param name="NextOrderSequence">The next monotonically increasing strict-order admission or redrive sequence.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.QueueCounters)]
public sealed record QueueCounters([property: Orleans.Id(0)] long StoredMessages, [property: Orleans.Id(1)] long StoredBytes, [property: Orleans.Id(2)] long InFlightMessages, [property: Orleans.Id(3)] long InFlightBytes, [property: Orleans.Id(4)] long NextReadySequence,
    [property: Orleans.Id(5)] long DeadLetterMessages = QueueCounterDefaults.NoUsage, [property: Orleans.Id(6)] long DeadLetterBytes = QueueCounterDefaults.NoUsage,
    [property: Orleans.Id(7)] long NextParkedSequence = QueueCounterDefaults.NoUsage,
    [property: Orleans.Id(8)] long NextOrderSequence = QueueCounterDefaults.NoUsage);
