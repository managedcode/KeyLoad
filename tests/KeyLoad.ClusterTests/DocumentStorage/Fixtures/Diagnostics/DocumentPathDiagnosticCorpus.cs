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
    private static readonly string UpdatedPayload = new('y', PayloadCharacters);

    internal static string Id(int ordinal) => ordinal.ToString("D10", CultureInfo.InvariantCulture);

    internal static string Json(int ordinal)
        => Body(ordinal, Payload);

    internal static string UpdatedJson(int ordinal) => Body(ordinal, UpdatedPayload);

    private static string Body(int ordinal, string payload)
        => string.Create(CultureInfo.InvariantCulture, $"{{\"number\":{ordinal},\"payload\":\"{payload}\"}}");

    internal static PartitionRef Partition() => new("diagnostic-" + Guid.NewGuid().ToString("N"),
        "documents", "documents", Guid.NewGuid().ToString("N"));

    internal static void Require(string id, string json, long revision, int ordinal, bool updated)
    {
        if (id != Id(ordinal) || json != (updated ? UpdatedJson(ordinal) : Json(ordinal))
            || revision != (updated ? 2L : 1L))
        {
            throw new InvalidOperationException("The diagnostic operation returned a different document identity or complete body.");
        }
    }
}
