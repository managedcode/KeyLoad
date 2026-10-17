namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresErrorReceiverProtocol
{
    internal const string Directory = "native-sql-error-probe";
    internal const string Configuration = "KeyLoadTests__PostgresErrorDelivery__Session";
    internal const string Statement = "error_receiver_statement";
    internal const string Portal = "error_receiver_portal";
    internal const int DenialAdvance = 1;
    internal const int RepairAdvance = 2;
    internal const int ColdWaves = 2;
    internal const int ExpectedFailures = 2;
    internal static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);
    internal const string Invalid = "Native SQL error receiver ownership is invalid.";
}
