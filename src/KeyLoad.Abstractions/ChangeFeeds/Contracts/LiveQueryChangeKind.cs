namespace KeyLoad;

/// <summary>Specifies how a row changed in a live query result.</summary>
public enum LiveQueryChangeKind
{
    /// <summary>The row is present or has changed.</summary>
    Upsert,
    /// <summary>The row is no longer present in the result.</summary>
    Remove
}
