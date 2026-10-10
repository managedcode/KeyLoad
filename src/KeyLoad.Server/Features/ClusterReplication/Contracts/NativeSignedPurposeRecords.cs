namespace KeyLoad.Server;

internal readonly record struct NativeSignedPurposeArm(int Version, string SessionId, Guid ArmId,
    string SourceVoter, string Route, Guid CommandId);

internal readonly record struct NativeSignedPurposeWitness(int Version, string SessionId, Guid ArmId,
    string SourceVoter, string Route, Guid ConnectionId, Guid RequestId, Guid CommandId,
    Guid Incarnation, string PrincipalId, DateTimeOffset ExpiresAt, string OriginalPurpose,
    string PayloadDigest, string Stage, string? ReceiverVoter);
