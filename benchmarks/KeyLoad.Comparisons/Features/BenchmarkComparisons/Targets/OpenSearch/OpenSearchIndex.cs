using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchIndex
{
    private const int BulkBatchSize = 64;
    private const string KeywordSubfield = OpenSearchNames.KeywordSubfield;
    private const string ShardsSetting = OpenSearchNames.ShardsParameter;
    private const string ReplicasSetting = OpenSearchNames.ReplicasParameter;
    private const string TranslogSetting = OpenSearchNames.TranslogParameter;
    private const string DurabilitySetting = OpenSearchNames.DurabilityParameter;
    private const string KnnSetting = OpenSearchNames.KnnParameter;

    internal static async Task CreateAsync(HttpClient client, string index, int dimension, int replicas, CancellationToken cancellationToken)
    {
        var body = new
        {
            settings = new Dictionary<string, object>
            {
                [ShardsSetting] = OpenSearchNames.PrimaryShardCount,
                [ReplicasSetting] = replicas,
                [TranslogSetting] = new { durability = OpenSearchNames.RequestDurability },
                [KnnSetting] = false
            },
            mappings = new
            {
                properties = new Dictionary<string, object>
                {
                    [OpenSearchNames.Id] = new { type = OpenSearchNames.Text, fields = new Dictionary<string, object> { [KeywordSubfield] = new { type = KeywordSubfield } } },
                    [OpenSearchNames.Payload] = new { type = OpenSearchNames.Object, enabled = false },
                    [OpenSearchNames.Vector] = new { type = OpenSearchNames.KNearestNeighbors, dimension }
                }
            }
        };
        using var response = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Put, OpenSearchNames.PathSeparator + index + OpenSearchNames.CreateIndexSuffix, body, cancellationToken);
        _ = response.RootElement;
    }

    internal static async Task SeedAsync(HttpClient client, string index, ImmutableArray<BenchmarkDocument> documents, int expectedCopies,
        CancellationToken cancellationToken)
    {
        foreach (var batch in documents.Chunk(BulkBatchSize))
        {
            var lines = new List<string>(batch.Length * 2);
            foreach (var document in batch)
            {
                lines.Add(JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    [OpenSearchNames.Indexing] = new Dictionary<string, object>
                    {
                        [OpenSearchNames.IndexActionName] = index,
                        [OpenSearchNames.DocumentActionId] = document.Id
                    }
                }, OpenSearchHttp.JsonOptions));
                lines.Add(JsonSerializer.Serialize(OpenSearchDocument.Create(document.Id, document.Json, document.Vector), OpenSearchHttp.JsonOptions));
            }
            var body = string.Join(OpenSearchNames.LineFeed, lines) + OpenSearchNames.LineFeed;
            using var response = await OpenSearchHttp.SendNdjsonAsync(client, OpenSearchNames.PathSeparator + index + OpenSearchNames.BulkSuffix, body, cancellationToken);
            VerifyBulk(response.RootElement, batch.Length, expectedCopies);
        }
    }

    internal static async Task<string> VerifySettingsAsync(HttpClient client, string index, int expectedReplicas, CancellationToken cancellationToken)
    {
        using var response = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Get, OpenSearchNames.PathSeparator + index + OpenSearchNames.SettingsSuffix, null, cancellationToken);
        var indexSettings = OpenSearchJson.RequiredPath(response.RootElement, index, OpenSearchNames.Settings);
        var shards = Setting(indexSettings, OpenSearchNames.NumberOfShards, OpenSearchNames.Index, ShardsSetting);
        var replicas = Setting(indexSettings, OpenSearchNames.NumberOfReplicas, OpenSearchNames.Index, ReplicasSetting);
        var durability = Setting(indexSettings, OpenSearchNames.TranslogDurability, OpenSearchNames.Index, TranslogSetting, DurabilitySetting);
        var knn = Setting(indexSettings, OpenSearchNames.KnnEnabled, OpenSearchNames.Index, KnnSetting);
        if (ReadInteger(shards) != OpenSearchNames.PrimaryShardCount || ReadInteger(replicas) != expectedReplicas
            || ReadString(durability) != OpenSearchNames.RequestDurability || ReadBoolean(knn))
        {
            throw new ComparisonFailureException(OpenSearchNames.IndexSettingsMismatch);
        }

        return string.Join(OpenSearchNames.SettingSeparator,
            OpenSearchNames.NumberOfShards + OpenSearchNames.SettingAssignment + ReadInteger(shards),
            OpenSearchNames.NumberOfReplicas + OpenSearchNames.SettingAssignment + ReadInteger(replicas),
            OpenSearchNames.TranslogDurability + OpenSearchNames.SettingAssignment + ReadString(durability),
            OpenSearchNames.KnnEnabled + OpenSearchNames.SettingAssignment + ReadBoolean(knn));
    }

    private static int ReadInteger(JsonElement setting)
        => setting.ValueKind == JsonValueKind.Number ? setting.GetInt32()
            : int.TryParse(setting.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value : throw new ComparisonFailureException(OpenSearchNames.IndexSettingsMismatch);

    private static string ReadString(JsonElement setting)
        => setting.ValueKind == JsonValueKind.String ? setting.GetString()!
            : throw new ComparisonFailureException(OpenSearchNames.IndexSettingsMismatch);

    private static bool ReadBoolean(JsonElement setting)
        => setting.ValueKind is JsonValueKind.True or JsonValueKind.False ? setting.GetBoolean()
            : setting.ValueKind == JsonValueKind.String && bool.TryParse(setting.GetString(), out var value)
                ? value : throw new ComparisonFailureException(OpenSearchNames.IndexSettingsMismatch);

    private static JsonElement Setting(JsonElement settings, string flatName, params string[] nestedPath)
    {
        if (settings.TryGetProperty(flatName, out var flat))
        {
            return flat;
        }

        foreach (var part in nestedPath)
        {
            if (!settings.TryGetProperty(part, out var nested))
            {
                throw new ComparisonFailureException(OpenSearchNames.IndexSettingsMismatch);
            }

            settings = nested;
        }
        return settings;
    }

    private static void VerifyBulk(JsonElement response, int expectedItems, int expectedCopies)
    {
        if (OpenSearchJson.RequiredBoolean(response, OpenSearchNames.BulkErrors))
        {
            throw new ComparisonFailureException(OpenSearchNames.BulkItemFailure);
        }

        var items = OpenSearchJson.RequiredArray(response, OpenSearchNames.Items);
        if (items.GetArrayLength() != expectedItems)
        {
            throw new ComparisonFailureException(OpenSearchNames.BulkItemFailure);
        }

        foreach (var item in items.EnumerateArray())
        {
            var result = OpenSearchJson.RequiredObject(item, OpenSearchNames.Indexing);
            if (OpenSearchJson.RequiredInt32(result, OpenSearchNames.StatusCode) is < OpenSearchNames.SuccessStatusMinimum or >= OpenSearchNames.SuccessStatusMaximumExclusive)
            {
                throw new ComparisonFailureException(OpenSearchNames.BulkItemFailure);
            }

            if (OpenSearchJson.RequiredString(result, OpenSearchNames.Result) != OpenSearchNames.Created)
            {
                throw new ComparisonFailureException(OpenSearchNames.BulkItemFailure);
            }

            OpenSearchWriteAcknowledgement.Verify(result, expectedCopies);
        }
    }
}
