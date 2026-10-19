namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class ConnectionProtocol
{
    internal const string Root = "root";
    internal const string Collection = "connection-acceptance";
    internal const string FirstJson = "{\"value\":1}";
    internal const string SecondJson = "{\"value\":2}";
    internal const string SubjectPrefix = "connection-principal-";
    internal const string OrdinaryFailure = "The selected execution connection operation failed before submission.";
    internal const string DuplicateObservation = "The execution operation was observed more than once.";
    internal const string MissingObservation = "The execution connection operation has no observation.";
    internal const string IdentityChanged = "The execution connection activation or signed operation identity changed.";
    internal const string UnexpectedReply = "The execution connection operation returned an unexpected reply.";
    internal const string BodyFailure = "body-failure";
    internal const string CleanupFailure = "cleanup-failure";
    internal const int EmptyFailures = 0;
    internal const int AbsentActivations = 0;
    internal const int OneConnection = 1;
}
