namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record PhysicalOwnerRegistrationObservation(Guid? InvocationCommandId, bool Verified);
