namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateProtocol
{
    internal const int ResourceCount = 3;
    internal const int UserPairs = 4;
    internal const int ModelCount = 10;
    internal const int MinimumOperations = 10_000;
    internal const int MaximumWaves = 256;
    internal const string AnchorCollection = "gate_docs_0";
    internal const string OpenDocumentCollection = "gate_docs_1";
    internal const string AnchorId = "anchor";
    internal const string TenantPrefix = "functional-gate-";
    internal const string Database = "models";
    internal const string TransactionDomain = "models";
    internal const string GuidFormat = "N";
}
