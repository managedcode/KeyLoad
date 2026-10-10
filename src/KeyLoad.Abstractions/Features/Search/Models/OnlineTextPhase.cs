namespace KeyLoad;

/// <summary>The actual completed online text-maintenance phase.</summary>
public enum OnlineTextPhase
{
    /// <summary>The committed document cut and native pin were captured.</summary>
    Captured = 0,
    /// <summary>The admitted native generation was seeded off the apply gate.</summary>
    Seeded = 1,
    /// <summary>All admitted ordered source pages and canonical acknowledgements settled.</summary>
    CaughtUp = 2,
    /// <summary>The complete native candidate and current authority were validated.</summary>
    Validated = 3,
    /// <summary>The canonical publication and exact local catalog reconciled.</summary>
    Published = 4,
    /// <summary>The replaced generation awaits or completes its final reader release.</summary>
    RetirementMarked = 5
}
