namespace KeyLoad.Comparisons;

/// <summary>The canonical document-v1 schedules, independent of native adapter operation names.</summary>
public enum DocumentComparisonScenario
{
    /// <summary>Reads identities in ascending order.</summary>
    SequentialRead,
    /// <summary>Reads a deterministic seed-bound permutation.</summary>
    RandomRead,
    /// <summary>Creates distinct new identities above the initial corpus.</summary>
    Create,
    /// <summary>Replaces each selected existing identity exactly once.</summary>
    Update,
    /// <summary>Deletes each selected existing identity exactly once.</summary>
    Delete,
    /// <summary>Reads stable identities and updates distinct identities at a 50/50 ratio.</summary>
    ReadUpdate50,
    /// <summary>Reads stable identities and updates distinct identities at a 95/5 ratio.</summary>
    ReadUpdate95,
    /// <summary>Executes four disjoint read/create/update/delete lanes at equal ratios.</summary>
    MixedCrud,
    /// <summary>Creates a million total distinct records in an empty configured namespace.</summary>
    Ingest
}
