namespace KeyLoad.Comparisons;

/// <summary>Actual terminal native operation observation; carries no identifiers, credentials or payloads.</summary>
public sealed record DocumentComparisonProgress(int Repetition, long Attempts, long Acknowledged, long Planned,
    long Failed, long Canceled, double ElapsedSeconds);
