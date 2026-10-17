namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeLiveProtocol
{
    internal const string ChallengeKind = "ActivationChallenge";
    internal const string AckKind = "ActivationLiveAck";
    internal const string ChallengePrefix = "activation-challenge-";
    internal const string AckPrefix = "activation-live-";
    internal const int BeforeReplacement = 1;
    internal const int AfterReplacement = 2;
    internal const int NameParts = 3;
    internal const int StepPart = 2;
    internal const string BeforeSuffix = "1";
    internal const string AfterSuffix = "2";
    internal const string EmptyDigest = "";
}
