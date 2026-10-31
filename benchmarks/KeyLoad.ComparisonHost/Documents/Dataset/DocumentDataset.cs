using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KeyLoad.ComparisonHost.Documents;

/// <summary>A JSON document of the standard documents dataset.</summary>
internal sealed record DocumentRecord(string Id, long Number, string Json);

/// <summary>The stored body: <c>{"id","number","version","text","padding"}</c>, padded to 1 KiB.</summary>
internal sealed record DocumentBody(string Id, long Number, int Version, string Text, string Padding);

/// <summary>The standard documents dataset every document database and KeyLoad load.</summary>
internal static class DocumentDataset
{
    internal const int PayloadBytes = 1024;
    internal const string NumberProperty = "number";
    private const string IdPrefix = "d";
    private const string IdFormat = "D9";
    private const string Text = "KeyLoad standard documents dataset";
    private const char Padding = 'x';

    internal static string Id(int number) => IdPrefix + number.ToString(IdFormat, CultureInfo.InvariantCulture);

    internal static DocumentRecord Create(int number, int version = 0)
    {
        var id = Id(number);
        var unpadded = Serialize(new(id, number, version, Text, string.Empty));
        var padding = new string(Padding, PayloadBytes - Encoding.UTF8.GetByteCount(unpadded));
        return new(id, number, Serialize(new(id, number, version, Text, padding)));
    }

    /// <summary>Checks that a returned body is the document with this number.</summary>
    internal static bool Matches(string json, int number)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(NumberProperty, out var value)
            && value.TryGetInt64(out var found) && found == number;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Serialize(DocumentBody body) => JsonSerializer.Serialize(body, Json);
}
