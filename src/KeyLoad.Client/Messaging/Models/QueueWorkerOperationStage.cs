namespace KeyLoad.Client;

/// <summary>Local observation stages; prepared never claims that a server operation committed.</summary>
public enum QueueWorkerOperationStage
{
    /// <summary>Original identity and payload were retained before native invocation.</summary>
    Prepared,
    /// <summary>Original native call returned its actual typed result after reader settlement.</summary>
    Returned,
    /// <summary>Original native invocation threw; its exception is retained without an invented result.</summary>
    Threw
}
