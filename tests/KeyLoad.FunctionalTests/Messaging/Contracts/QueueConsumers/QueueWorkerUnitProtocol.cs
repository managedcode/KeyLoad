namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueWorkerUnitProtocol
{
    internal const int Initial = 0;
    internal const int ExceedsUncertainty = 2;
    internal const int ExceedsLease = 301;
    internal const int ExtraByte = 1;
    internal const string UnusedCredential = "unused-execution-client-no-io";
    internal const string CallbackMustNotRun = "Invalid or pre-cancelled worker admission must not run a handler.";
    internal const string Tenant = "worker-policy";
    internal const string Database = "database";
    internal const string Domain = "messaging";
    internal const string Partition = "options";
    internal const string Queue = "jobs";
    internal static readonly QueueLaneRef Lane = new(new(Tenant, Database, Domain, Partition), Queue);
}
