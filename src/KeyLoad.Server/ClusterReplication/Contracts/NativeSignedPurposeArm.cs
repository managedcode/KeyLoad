namespace KeyLoad.Server;

internal readonly record struct NativeSignedPurposeArm(int Version, string SessionId, Guid ArmId,
    string SourceVoter, string Route, Guid CommandId);
