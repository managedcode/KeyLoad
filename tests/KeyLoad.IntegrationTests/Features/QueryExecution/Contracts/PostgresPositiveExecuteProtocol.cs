namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPositiveExecuteProtocol
{
    internal const string Statement = "positive-execute";
    internal const string Portal = "positive-execute-row";
    internal const int OneRow = 1;
    internal const int TwoRows = 2;
    internal const int AllRows = 0;
    internal const int InvalidRows = -1;
    internal const int MaximumRows = int.MaxValue;
    internal const int ColdCuts = 2;
    internal const long DenialAdvance = 1;
    internal const long RepairAdvance = 2;
    internal const string Unsupported = "SERROR\0C0A000\0MUnsupportedCapability\0\0";
    internal static readonly int[] Limits = [AllRows, OneRow, TwoRows, MaximumRows];
}
