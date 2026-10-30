using System.Globalization;

namespace KeyLoad.ClusterTests.DocumentStorage;

internal static class DocumentPathDiagnosticCorpus
{
    internal const string Collection = "diagnostic_documents";
    internal const int Clients = 16;
    internal const int BatchRecords = 256;
    internal const int ReadStride = 7919;
    private const int PayloadCharacters = 900;
    private static readonly string Payload = new('x', PayloadCharacters);

    internal static string Id(int ordinal) => ordinal.ToString("D10", CultureInfo.InvariantCulture);

    internal static string Json(int ordinal)
        => string.Create(CultureInfo.InvariantCulture, $"{{\"number\":{ordinal},\"payload\":\"{Payload}\"}}");

    internal static PartitionRef Partition() => new("diagnostic-" + Guid.NewGuid().ToString("N"),
        "documents", "documents", Guid.NewGuid().ToString("N"));

    internal static void Require(string id, string json, int ordinal)
    {
        if (id != Id(ordinal) || json != Json(ordinal))
        {
            throw new InvalidOperationException("The diagnostic operation returned a different document identity or complete body.");
        }
    }
}
