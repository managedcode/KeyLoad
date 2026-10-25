namespace KeyLoad.FunctionalTests.ClientApi;

internal static class SdkProblemBoundaryProtocol
{
    internal const string Root = "root";
    internal const string Allowed = "problem-boundary-allowed";
    internal const string Document = "original";
    internal const string Reader = "problem-boundary-caller";
    internal const string KeyId = "problem-boundary-key";
    internal const string Secret = "problem-boundary-key.execution-private-credential-canary";
    internal const string SeedJson = "{\"value\":\"original-private-body\"}";
    internal const string HealthyJson = "{\"value\":\"healthy-private-body\"}";
    internal const string AttackJson = "{\"value\":\"refused-change\"}";
    internal const string Denied = "The principal cannot perform this operation in this scope.";
    internal const string Unavailable = "The server response is unavailable.";
    internal const string Failure = "The real execution problem-boundary operation violated its complete fixture contract.";
    internal const string Commands = "/commands";
    internal const string Documents = "/documents/get";
    internal const string Authorization = "Authorization";
    internal const string Bearer = "Bearer ";
    internal const string CommandHeader = "X-KeyLoad-Command-Id";
    internal const string ProblemType = "application/problem+json";
    internal const string JsonType = "application/json";
    internal const string RowSeparator = ":";
    internal const string Scheme = "Bearer";
    internal const string Put = "putDocument";
    internal const int FirstOrdinal = 0;
    internal const int SecondOrdinal = 1;
    internal const int Lookahead = 1;
    internal const int MaximumRows = 1024;
    internal const long FirstRevision = 1;
    internal const long HealthyRevision = 2;
    internal const long InitialRevision = 0;
    internal const long FirstSequence = 1;
    internal const long SecondSequence = 2;
    internal const long HealthySequence = 3;
    internal const long InitialPolicy = 1;
    internal const int Empty = 0;
    internal const int PlacementVersion = 1;
}
