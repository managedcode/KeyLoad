namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class EarlyAdmissionQualificationProtocol
{
    internal const string Collection = "early_sql_admission";
    internal const string TenantPrefix = "early-sql-";
    internal const string Database = "database";
    internal const string Domain = "domain";
    internal const string First = "a";
    internal const string Second = "b";
    internal const string FirstJson = """{"value":1}""";
    internal const string SecondJson = """{"value":2}""";
    internal const string Query = "SELECT * FROM early_sql_admission ORDER BY id";
    internal const string InvalidQuery = "SELECT FROM";
    internal const string QueryRoute = "/v1/query";
    internal const string Scan = "bounded-full-scan";
    internal const string Put = "putDocument";
    internal const string InvalidDetail = "The SQL syntax or query arguments are invalid.";
    internal const string BusyDetail = "The node, tenant or principal command admission budget is exhausted.";
    internal const string PublicBusyDetail = "The operation exceeds the available resource capacity.";
    internal const string PublicInvalidDetail = "The database operation could not be completed.";
    internal const string ScopeDetail = "The principal cannot perform this operation in this scope.";
    internal const int Version = 1;
    internal const long Revision = 1;
    internal const int ColdCount = 2;
    internal const int QuietCommands = 1;
    internal const int HeldCommands = 2;
    internal const int EmptyControls = 0;
    internal const long InvalidCut = 0;
    internal const long DeniedEpoch = 2;
    internal const long RepairedEpoch = 3;
    internal const long HeavyBytes = 65_536;
    internal const long EnvelopeBytes = 4_096;
    internal const long BodyScale = 4;
    internal static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(10);
}
