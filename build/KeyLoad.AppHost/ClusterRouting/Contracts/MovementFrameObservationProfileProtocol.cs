namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class MovementFrameObservationProfileProtocol
{
    internal const string Section = "KeyLoadTests:MovementFrameObservation";
    internal const string FourthVoter = "node4";
    internal const string FifthVoter = "node5";
    internal const string SixthVoter = "node6";
    internal static IReadOnlyList<string> Voters { get; } = Array.AsReadOnly(new[] { FourthVoter, FifthVoter, SixthVoter });
    internal const int OwnerVersion = 1;
    internal const string OwnerKind = "movement-frame-owner";
    internal const string Root = "/movement-frame-observation";
    internal const string EnabledEnvironment = "KeyLoad__MovementFrameObservation__Enabled";
    internal const string RootEnvironment = "KeyLoad__MovementFrameObservation__Root";
    internal const string SessionEnvironment = "KeyLoad__MovementFrameObservation__SessionId";
    internal const string EnabledValue = "true";
    internal const string Invalid = "The protected movement frame observation profile is invalid.";
}
