using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchProbe
{
    internal static async Task VerifyAsync(HttpClient client, string index, ImmutableArray<float> vector, int expectedCopies,
        CancellationToken cancellationToken)
    {
        var id = OpenSearchNames.ProbePrefix + Guid.NewGuid().ToString(OpenSearchNames.GuidFormat);
        var json = JsonSerializer.Serialize(new Dictionary<string, object> { [OpenSearchNames.Id] = id, [OpenSearchNames.PayloadProbe] = true });
        var source = OpenSearchDocument.Create(id, json, vector);
        using (var write = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Put,
            OpenSearchNames.PathSeparator + index + OpenSearchNames.CreateDocumentPath + Uri.EscapeDataString(id) + OpenSearchNames.CreateIndexSuffix,
            source, cancellationToken))
        {
            OpenSearchWriteAcknowledgement.Verify(write.RootElement, expectedCopies);
        }

        using (var read = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Get,
            OpenSearchNames.PathSeparator + index + OpenSearchNames.DocumentPath + Uri.EscapeDataString(id) + OpenSearchNames.RealtimeQuery, null, cancellationToken))
        {
            var document = OpenSearchDocument.Read(read.RootElement.GetProperty(OpenSearchNames.Source));
            if (document.Id != id || !BenchmarkDataset.SameJson(document.Json, json))
            {
                throw new ComparisonFailureException(OpenSearchNames.ProbeReadbackMismatch);
            }
        }

        using var delete = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Delete,
            OpenSearchNames.PathSeparator + index + OpenSearchNames.DocumentPath + Uri.EscapeDataString(id) + OpenSearchNames.CreateIndexSuffix,
            null, cancellationToken);
        OpenSearchWriteAcknowledgement.Verify(delete.RootElement, expectedCopies);
    }
}
