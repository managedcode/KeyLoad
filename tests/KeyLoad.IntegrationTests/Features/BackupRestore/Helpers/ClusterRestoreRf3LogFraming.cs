using System.Globalization;
using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Decodes exact pinned Aspire display framing while retaining the original records unchanged.</summary>
internal static class ClusterRestoreRf3LogFraming
{
    private const char JsonObjectStart = '{';
    private const char TimestampSeparator = ' ';
    private const string NativeTimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffffffK";
    private const int MissingSeparator = -1;
    private const int SeparatorCharacters = 1;

    internal static T Read<T>(LogLine original)
    {
        var content = original.Content;
        if (content.StartsWith(JsonObjectStart))
        { return JsonDefaults.Deserialize<T>(content); }
        var separator = content.IndexOf(TimestampSeparator, StringComparison.Ordinal);
        if (separator == MissingSeparator || !DateTime.TryParseExact(content.AsSpan(0, separator),
            NativeTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        return JsonDefaults.Deserialize<T>(content[(separator + SeparatorCharacters)..]);
    }
}
