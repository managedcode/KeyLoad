namespace KeyLoad.Server;

internal readonly record struct NativeDiscoveryOmissionArm(int Version, string SessionId, Guid ArmId,
    string SourceVoter, string TargetVoter, string Route);

internal readonly record struct NativeDiscoveryOmissionWitness(int Version, string SessionId, Guid ArmId,
    string SourceVoter, string TargetVoter, string Route, string HttpTraceId, Guid ConnectionId,
    Guid ChildRequestId, Guid CommandId, Guid Nonce, string Stage, string? ProducerVoter,
    int? RuntimeJournalReaderContract);
