namespace KeyLoad.Clustering;

/// <summary>Canonical addressing for the database's execution Orleans commit grain.</summary>
public static class DatabaseGrainIdentity
{
    /// <summary>Uses the persisted database incarnation without changing user entity casing.</summary>
    public static Guid For(Guid incarnation)
        => incarnation != Guid.Empty ? incarnation : throw new ArgumentException(StorageCopyContract.InvalidIncarnation, nameof(incarnation));
}
