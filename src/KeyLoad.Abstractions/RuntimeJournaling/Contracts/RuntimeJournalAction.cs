namespace KeyLoad;

/// <summary>Closed private infrastructure actions; these are not public database tools.</summary>
public enum RuntimeJournalAction
{
    /// <summary>Creates the protected journal identity under current administrator authority.</summary>
    BootstrapIdentity,
    /// <summary>Creates a journal if its name is absent.</summary>
    Create,
    /// <summary>Changes bounded execution metadata using full ETag comparison.</summary>
    UpdateMetadata,
    /// <summary>Appends opaque bytes at a captured owner and content revision.</summary>
    Append,
    /// <summary>Replaces opaque bytes at a captured owner and content revision.</summary>
    Replace,
    /// <summary>Deletes a captured journal instance and releases its capacity.</summary>
    Delete
}
