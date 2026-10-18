namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerClusterByteProtocol
{
    internal const int SourceCharacters = 2048;
    internal const int BudgetBytesPerSourceCharacter = 4;
    internal const int CompetingDeliveries = 2;
    internal const char SourceCharacter = 'Ж';
    internal const string EscapedCharacter = "\\u0416";
    internal const string BodyPrefix = "{\"body\":\"";
    internal const string RawSuffix = "\",\"secret\":\"omit\"}";
    internal const string ProjectedSuffix = "\"}";
    internal const string PrivatePath = "secret";
    internal const string Classification = "worker-private";
    internal const string WriteGrant = "pii.write";
    internal static string Payload => BodyPrefix + new string(SourceCharacter, SourceCharacters) + RawSuffix;
    internal static string Projected => BodyPrefix + string.Concat(Enumerable.Repeat(EscapedCharacter, SourceCharacters)) + ProjectedSuffix;
    internal const int BudgetBytes = SourceCharacters * BudgetBytesPerSourceCharacter;
}
