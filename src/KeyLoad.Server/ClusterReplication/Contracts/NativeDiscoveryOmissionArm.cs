namespace KeyLoad.Server;

internal readonly record struct NativeDiscoveryOmissionArm(int Version, string SessionId, Guid ArmId,
    string SourceVoter, string TargetVoter, string Route);
