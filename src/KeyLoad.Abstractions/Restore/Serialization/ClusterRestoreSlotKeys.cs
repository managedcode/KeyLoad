using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Current operation-owned native stage rows, never public database authority.</summary>
public static class ClusterRestoreSlotKeys
{
    private const string Space = "cluster-restore-slot";
    private const string ReconciledStage = "reconciled";
    private const string ResetStage = "authority-reset";
    /// <summary>Actual reconciliation transaction evidence key.</summary>
    public static byte[] Reconciled() => KeyCodec.Encode(Space, ReconciledStage);
    /// <summary>Actual atomic authority-reset transaction evidence key.</summary>
    public static byte[] AuthorityReset() => KeyCodec.Encode(Space, ResetStage);
}
