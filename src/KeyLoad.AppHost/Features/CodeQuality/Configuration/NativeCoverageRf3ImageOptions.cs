using System.Buffers;
using System.Globalization;

namespace KeyLoad.AppHost.Features.CodeQuality;

/// <summary>One centrally bound snapshot of optional RF3 coverage image identity and context.</summary>
[ConfigurationOptions]
internal sealed class NativeCoverageRf3ImageOptions
{
    internal const string ValidationMessage = "The original-node coverage selection is invalid.";
    private const int EmptyStringLength = 0;
    private const int MinimumPositiveDecimalExclusive = 0;
    private const int NoCharactersFound = -1;
    private const int MaximumToolVersionLabelCharacters = 64;
    private const int MillisecondsPerSecond = 1_000;
    private const long MaximumStartupSeconds = long.MaxValue / MillisecondsPerSecond;
    private static readonly SearchValues<char> ShaCharacters = SearchValues.Create("0123456789abcdef");
    private static readonly SearchValues<char> LabelCharacters = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._-");

    public string? ServerMode { get; set; }
    public string? SourceManifest { get; set; }
    public string? ImageReference { get; set; }
    public string? RunId { get; set; }
    public string? OutputRoot { get; set; }
    public string? SourceReceiptSha256 { get; set; }
    public string? ContextManifestSha256 { get; set; }
    public string? ImageId { get; set; }
    public string? ServerDllSha256 { get; set; }
    public string? ServerPdbSha256 { get; set; }
    public string? ServerMvid { get; set; }
    public string? ToolVersion { get; set; }
    public string? ToolClosureSha256 { get; set; }
    public string? SettingsSha256 { get; set; }
    public string? ShutdownSeconds { get; set; }
    public string? SettlementSeconds { get; set; }
    public string? MaximumReportBytes { get; set; }
    public string? StartupPollMilliseconds { get; set; }
    public string? RunManifest { get; set; }

    internal bool IsValid()
    {
        if (!HasSelection)
        {
            return !HasContextValues;
        }
        if (IsOuterSelection)
        {
            return !HasNestedIdentityValues && Path.IsPathFullyQualified(SourceManifest!);
        }
        var startupMilliseconds = StartupMilliseconds(ShutdownSeconds);
        return IsNestedSelection && IsCanonicalRunId(RunId)
            && string.Equals(ImageReference, ExpectedImageReference(RunId!), StringComparison.Ordinal)
            && HasValidContext(startupMilliseconds);
    }

    internal bool HasSelection => ServerMode is not null || SourceManifest is not null
        || ImageReference is not null || RunId is not null;

    internal bool IsOuterSelection => ServerMode == NativeCoverageRf3Protocol.Mode
        && !string.IsNullOrWhiteSpace(SourceManifest) && ImageReference is null && RunId is null;

    internal bool IsNestedSelection => ServerMode is { Length: EmptyStringLength }
        && SourceManifest is { Length: EmptyStringLength } && ImageReference is not null && RunId is not null;

    internal string ExpectedImageTag => ImageReference![NativeCoverageRf3Protocol.CoverageImagePrefix.Length..];

    internal string GetFullOutputRoot() => Path.GetFullPath(OutputRoot!);

    internal string GetStartupTimeoutMilliseconds() => StartupMilliseconds(ShutdownSeconds);

    private bool HasContextValues => HasNestedIdentityValues || ShutdownSeconds is not null
        || SettlementSeconds is not null
        || StartupPollMilliseconds is not null;

    private bool HasNestedIdentityValues => OutputRoot is not null || SourceReceiptSha256 is not null
        || ContextManifestSha256 is not null || ImageId is not null || ServerDllSha256 is not null
        || ServerPdbSha256 is not null || ServerMvid is not null || ToolVersion is not null
        || ToolClosureSha256 is not null || SettingsSha256 is not null || RunManifest is not null;

    private bool HasValidContext(string startupMilliseconds)
        => !string.IsNullOrEmpty(OutputRoot) && Directory.Exists(OutputRoot)
            && IsSha256(SourceReceiptSha256) && IsSha256(ContextManifestSha256)
            && IsSha256(ServerDllSha256) && IsSha256(ServerPdbSha256)
            && IsSha256(ToolClosureSha256) && IsSha256(SettingsSha256)
            && IsImageId(ImageId) && IsCanonicalGuid(ServerMvid!) && IsLabel(ToolVersion)
            && IsPositiveDecimal(ShutdownSeconds) && IsPositiveDecimal(SettlementSeconds)
            && IsPositiveDecimal(MaximumReportBytes) && IsPositiveDecimal(StartupPollMilliseconds)
            && IsPositiveDecimal(startupMilliseconds)
            && IsAtMost(StartupPollMilliseconds!, startupMilliseconds);

    private static string ExpectedImageReference(string runId)
        => NativeCoverageRf3Protocol.CoverageImagePrefix
            + Guid.ParseExact(runId, NativeCoverageRf3Protocol.GuidFormat)
                .ToString(NativeCoverageRf3Protocol.GuidCompactFormat);

    private static bool IsCanonicalRunId(string? runId)
        => Guid.TryParseExact(runId, NativeCoverageRf3Protocol.GuidFormat, out var parsed)
            && string.Equals(runId, parsed.ToString(NativeCoverageRf3Protocol.GuidFormat), StringComparison.Ordinal);

    private static string StartupMilliseconds(string? seconds)
    {
        if (!long.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value <= MinimumPositiveDecimalExclusive || value > MaximumStartupSeconds)
        {
            return string.Empty;
        }
        return checked(value * MillisecondsPerSecond)
            .ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsCanonicalGuid(string value)
        => Guid.TryParseExact(value, NativeCoverageRf3Protocol.GuidFormat, out var parsed)
            && string.Equals(value, parsed.ToString(NativeCoverageRf3Protocol.GuidFormat), StringComparison.Ordinal);

    private static bool IsSha256(string? value)
        => value is { Length: NativeCoverageRf3Protocol.JsonShaHexLength }
            && value.AsSpan().IndexOfAnyExcept(ShaCharacters) == NoCharactersFound;

    private static bool IsImageId(string? value)
        => value is not null && value.StartsWith(NativeCoverageRf3Protocol.JsonShaPrefix, StringComparison.Ordinal)
            && value.Length == NativeCoverageRf3Protocol.JsonShaPrefix.Length
                + NativeCoverageRf3Protocol.JsonShaHexLength
            && IsSha256(value[NativeCoverageRf3Protocol.JsonShaPrefix.Length..]);

    private static bool IsLabel(string? value)
        => value is { Length: > EmptyStringLength and <= MaximumToolVersionLabelCharacters }
            && value.AsSpan().IndexOfAnyExcept(LabelCharacters) == NoCharactersFound;

    private static bool IsPositiveDecimal(string? value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed > MinimumPositiveDecimalExclusive
            && string.Equals(parsed.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal);

    private static bool IsAtMost(string left, string right)
        => long.Parse(left, NumberStyles.None, CultureInfo.InvariantCulture)
            <= long.Parse(right, NumberStyles.None, CultureInfo.InvariantCulture);
}
