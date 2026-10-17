namespace KeyLoad;

/// <summary>Selects the expected existence condition for an event stream.</summary>
public enum ExpectedStreamState
{
    /// <summary>Require the exact supplied revision.</summary>
    Exact,
    /// <summary>Require that no stream currently exists.</summary>
    NoStream,
    /// <summary>Accept any current stream state.</summary>
    Any
}
