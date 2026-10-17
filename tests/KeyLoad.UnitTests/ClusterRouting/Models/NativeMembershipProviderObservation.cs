namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeMembershipProviderObservation(
    NativeMembershipProviderRow[] Rows,
    int TableVersion,
    string TableVersionEtag);
