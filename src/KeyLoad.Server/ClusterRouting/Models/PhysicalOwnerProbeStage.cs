namespace KeyLoad.Server.Features.ClusterRouting;

internal enum PhysicalOwnerProbeStage
{
    Unobserved, Framing, Mac, Decoding, CallValidation, ReplayAdmission, CallerAddress, CallerPinning,
    InitialMembership, Authentication, NativeStatus, StatusGuard, ReadBarrier, Consensus, Discovery,
    FinalMembership, FinalGuard, CurrentAdministrator, ReplyEncoding, ReplyWrite, Reply, NativeStartup
}
