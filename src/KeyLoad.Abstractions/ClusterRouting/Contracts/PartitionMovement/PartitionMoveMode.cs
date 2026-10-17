namespace KeyLoad;

/// <summary>Selects the current administrator-controlled partition movement action.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveAliases.Mode)]
public enum PartitionMoveMode
{
    /// <summary>No movement action was selected; admission rejects this value.</summary>
    None = 0,
    /// <summary>Begins the exact bounded transfer and monotonic owner transition.</summary>
    Transfer = 1,
    /// <summary>Reconciles and continues the same durable move identity.</summary>
    Resume = 2,
    /// <summary>Abandons an unpublished move after its actual owners settle.</summary>
    Abort = 3
}
