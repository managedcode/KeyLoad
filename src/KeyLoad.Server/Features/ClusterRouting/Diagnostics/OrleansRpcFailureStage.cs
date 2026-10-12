namespace KeyLoad.Server;

/// <summary>Closed content-free observations of the original native request failure.</summary>
internal enum OrleansRpcFailureStage
{
    /// <summary>The original failure was observed as Unobserved.</summary>
    Unobserved,
    /// <summary>The original failure was observed as Preparation.</summary>
    Preparation,
    /// <summary>The original failure was observed as Consumption.</summary>
    Consumption
}
