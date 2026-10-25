using System.Text;
using System.Text.Json;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Core.InternalSerialization;

/// <summary>Canonicalizes only a first-issued typed analytics batch; retained execution operations remain untouched.</summary>
internal static class AnalyticsIdentity
{
    private const int FirstByte = 0;
    private const string AnalyticsRequired = "The command has no typed analytics mutation.";

    internal static bool HasAnalyticsMutation(CommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return !request.Mutations.IsDefaultOrEmpty
            && request.Mutations.Any(mutation => mutation is UpsertAnalyticsEvents or DeleteAnalyticsEvents);
    }

    internal static string Canonicalize(CommandRequest request, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        if (!HasAnalyticsMutation(request))
        {
            throw new ArgumentException(AnalyticsRequired, nameof(request));
        }

        using var document = JsonSerializer.SerializeToDocument(request, JsonDefaults.Options);
        work.Check();
        using var bytes = new MemoryStream();
        WriteCanonical(document.RootElement, bytes);
        work.Check();
        var identity = Encoding.UTF8.GetString(bytes.GetBuffer().AsSpan(FirstByte, checked((int)bytes.Length)));
        work.Check();
        return identity;
    }

    private static void WriteCanonical(JsonElement element, Stream output)
    {
        using var writer = new Utf8JsonWriter(output);
        CanonicalJsonWriter.Write(writer, element, decimalNumbers: false);
        writer.Flush();
    }
}
