namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ConnectionNativeProtocol
{
    internal const string Root = "root";
    internal const string Collection = "connection-acceptance";
    internal const string FirstJson = "{\"value\":1}";
    internal const string SecondJson = "{\"value\":2}";
    internal const string SubjectPrefix = "connection-principal-";
    internal const string OrdinaryFailure = "The selected native connection operation failed before submission.";
    internal const string DuplicateObservation = "The native operation was observed more than once.";
    internal const string MissingObservation = "The native connection operation has no observation.";
    internal const string IdentityChanged = "The native connection activation or signed operation identity changed.";
    internal const string UnexpectedReply = "The native connection operation returned an unexpected reply.";
    internal const string BodyFailure = "body-failure";
    internal const string CleanupFailure = "cleanup-failure";
    internal const int EmptyFailures = 0;
    internal const int AbsentActivations = 0;
    internal const int OneConnection = 1;
}
