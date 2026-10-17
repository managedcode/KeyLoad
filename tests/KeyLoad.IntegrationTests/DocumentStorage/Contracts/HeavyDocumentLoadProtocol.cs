using System.Globalization;
using System.Text;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Frozen AC-METH-005 functional load bounds, distinct from benchmark measurements.</summary>
internal static class HeavyDocumentLoadProtocol
{
    internal const int Records = 1_000_000;
    internal const int Writers = 10;
    internal const int RecordsPerWriter = Records / Writers;
    internal const int ReaderLanes = 3;
    internal const int PageSize = 128;
    internal const int SnapshotThreshold = 2_000_000;
    internal const long QueryReadBytes = 2_147_483_648;
    internal const long CreatedRevision = 1;
    internal const int PayloadCharacters = 960;
    internal const string Category = "HeavyLoad";
    internal const string PutKind = "putDocument";
    internal const string RecordPrefix = "heavy-";
    internal const string IdFormat = "D7";
    internal const string JsonPrefix = "{\"ordinal\":";
    internal const string JsonPayload = ",\"payload\":\"";
    internal const string JsonSuffix = "\"}";
    internal static readonly TimeSpan Deadline = TimeSpan.FromMinutes(120);
    private static readonly string Payload = new('x', PayloadCharacters);

    internal static string Id(int ordinal) => RecordPrefix + ordinal.ToString(IdFormat, CultureInfo.InvariantCulture);
    internal static string Json(int ordinal) => JsonPrefix + ordinal.ToString(CultureInfo.InvariantCulture)
        + JsonPayload + Payload + JsonSuffix;
    internal static byte[] DigestRecord(string id, string json) => Encoding.UTF8.GetBytes(id + "\n" + json + "\n");
    internal static CommandRequest Command(PartitionRef partition, int ordinal) => new(Guid.NewGuid(), partition,
        [new PutDocument(McpDocumentProtocol.Collection, Id(ordinal), Json(ordinal), ExpectedRevision: 0)]);
}
