namespace KeyLoad.Clustering;

/// <summary>Sizes projection replay pages by the grain reply ceiling that carries each page back to its parent.</summary>
internal static class ProjectionReplayPage
{
    /// <summary>The page record count; the reply byte ceiling alone ends a page.</summary>
    internal const int Records = int.MaxValue;
}
