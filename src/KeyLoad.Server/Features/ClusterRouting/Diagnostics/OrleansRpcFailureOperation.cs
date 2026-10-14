namespace KeyLoad.Server;

/// <summary>Call-local primitive correlation copied only after native signed-envelope verification.</summary>
internal readonly record struct OrleansRpcFailureOperation(Guid CommandId,
    global::KeyLoad.Clustering.GrainReadKind? ReadKind, global::KeyLoad.OperationKind? CommandKind);
