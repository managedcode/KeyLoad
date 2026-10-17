namespace KeyLoad;

/// <summary>Initial position policy for genuinely new covered sources.</summary>
public enum EventFeedStartPolicy
{
    /// <summary>Begin at the current retained floor.</summary>
    FromBeginning = 0,
    /// <summary>Begin after the current captured tail.</summary>
    FromNow = 1
}
