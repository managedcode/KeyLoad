namespace KeyLoad;

/// <summary>Selects which traversal result is materialized after the same authorized graph walk.</summary>
public enum GraphTraversalProjection
{
    /// <summary>Returns visible vertices and projected edges.</summary>
    Full = 0,
    /// <summary>Returns visible vertex references without edge payloads.</summary>
    Vertices = 1,
    /// <summary>Returns the distinct visited vertex count without vertex or edge arrays.</summary>
    Count = 2
}
