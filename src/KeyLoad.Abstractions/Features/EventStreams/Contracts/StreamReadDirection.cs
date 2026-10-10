namespace KeyLoad;

/// <summary>The order of an exclusive retained-stream traversal.</summary>
public enum StreamReadDirection
{
    /// <summary>Traverses retained events in increasing revision order.</summary>
    Forward = 0,
    /// <summary>Traverses retained events in decreasing revision order.</summary>
    Backward = 1
}
