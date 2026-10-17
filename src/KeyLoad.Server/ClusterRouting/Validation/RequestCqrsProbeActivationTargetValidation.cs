namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Closed private diagnostic origins; matching the actual configured voter occurs at native claim.</summary>
internal static class RequestCqrsProbeActivationTargetValidation
{
    private const string First = "http://node1:8080";
    private const string Second = "http://node2:8080";
    private const string Third = "http://node3:8080";
    internal static bool Valid(string target) => target is First or Second or Third;
}
