namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Physical control ownership; this does not select an Orleans membership provider.</summary>
internal enum PhysicalGroupTrustRole
{
    Disabled,
    Control,
    Destination
}
