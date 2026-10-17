namespace KeyLoad;

/// <summary>Identifies the only saga transitions supported by this protocol.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.SagaPhase)]
public enum SagaPhase
{
    /// <summary>The saga may be revised or completed before its deadline.</summary>
    Waiting,
    /// <summary>The saga completed through an explicit revision transition.</summary>
    Completed,
    /// <summary>The saga was cancelled through an explicit revision transition.</summary>
    Cancelled,
    /// <summary>The expected waiting revision expired and committed its timeout message.</summary>
    TimedOut
}
