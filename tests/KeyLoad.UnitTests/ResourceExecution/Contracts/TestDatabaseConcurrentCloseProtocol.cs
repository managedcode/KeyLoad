namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class TestDatabaseConcurrentCloseProtocol
{
    internal const string Principal = "root";
    internal const string Collection = "fixture-close-documents";
    internal const string Document = "acknowledged";
    internal const string Body = """{"value":"durable-before-close"}""";
    internal const string MutationKind = "putDocument";
    internal const string ReplicaDirectory = "ann-native-replica";
    internal const int Callers = 4;
    internal const int CallerParity = 2;
    internal const int FirstCaller = 0;
    internal const int PageLimit = 2;
    internal const int OneRecord = 1;
    internal const long MissingRevision = 0;
    internal const long Revision = 1;
    internal const long OwnershipEpoch = 1;
    internal const long IndexStep = 1;
    internal static readonly Guid OriginalId = Guid.Parse("c484385b-dc74-41b9-bf91-7def6d94b0c1");
    internal static readonly Guid HealthyId = Guid.Parse("7a00838c-6201-4e8e-a1cc-6c0046fa7101");
}
