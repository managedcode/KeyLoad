namespace KeyLoad.Server.Features.ClusterRouting;

internal enum PhysicalOwnerRegistrationStage
{
    MembershipPrerequisite,
    TargetProbes,
    Authentication,
    MembershipRevalidation,
    Registration,
    DirectoryVerification
}

internal enum PhysicalOwnerRegistrationFailureCategory
{
    Domain,
    Cancellation,
    Protocol,
    Unexpected
}
