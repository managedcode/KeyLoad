namespace KeyLoad;

/// <summary>Describes a local acceptance; this value is neither persisted nor transmitted.</summary>
/// <param name="Revision">Accepted strictly increasing receiver sequence.</param>
/// <param name="PreviousRevision">Last accepted sequence, including after withdrawal; zero before the first acceptance.</param>
/// <param name="Continuous">Whether the prior local lease was eligible at acceptance.</param>
public readonly record struct CacheReadPermitAcceptance(long Revision, long PreviousRevision, bool Continuous);
