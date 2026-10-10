namespace KeyLoad;

/// <summary>The original known failed transfer stage selected for bounded source repair.</summary>
public enum QueueTransferRepairStage
{
    /// <summary>Repair the original target acceptance failure.</summary>
    Accept = 0,
    /// <summary>Repair the original source completion failure.</summary>
    Complete = 1
}
