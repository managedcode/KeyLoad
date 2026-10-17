namespace KeyLoad;

/// <summary>Specifies where a change feed begins when no cursor is supplied.</summary>
public enum ChangeFeedStart
{
    /// <summary>Read from the earliest retained change.</summary>
    Beginning,
    /// <summary>Begin at the change feed tail observed when the request is processed.</summary>
    Now
}
