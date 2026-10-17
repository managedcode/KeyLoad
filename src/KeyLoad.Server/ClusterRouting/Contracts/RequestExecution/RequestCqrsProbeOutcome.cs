namespace KeyLoad.Server.Features.ClusterRouting;

internal enum RequestCqrsProbeOutcome
{
    Observed,
    Released,
    Cancelled,
    FaultRequested
}
