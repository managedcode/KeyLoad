using System.Net;
using System.Text;

namespace KeyLoad.Orleans;

internal static class ReplicaMembershipAuthorityValidation
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const string InvalidRequest = "The membership authority request is invalid.";
    private const string InvalidReply = "The membership authority reply is invalid.";

    internal static void Call(ReplicaMembershipAuthorityCallV1 call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call.Version != ReplicaMembershipAuthorityProtocol.Version)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, InvalidRequest); }
        if (string.IsNullOrWhiteSpace(call.ClusterId) || !Bounded(call.ClusterId)
            || call.AuthorityPhysicalShardId == Guid.Empty || call.AuthorityIncarnation == Guid.Empty
            || call.CallerPhysicalShardId == Guid.Empty || call.CallerIncarnation == Guid.Empty || call.RequestId == Guid.Empty
            || !Bounded(call.CallerVoterId) || !CanonicalAddress(call.CallerSiloAddress)
            || !Enum.IsDefined((ReplicaMembershipAuthorityOperation)call.Operation))
        { throw Errors.Fail(ErrorCode.Validation, InvalidRequest); }

        var operation = (ReplicaMembershipAuthorityOperation)call.Operation;
        ValidateOperation(operation, call);
        if (call.CandidateEntry is { } entry)
        { Entry(entry); }
        if (call.TargetSiloAddress is { } target && !CanonicalAddress(target))
        { throw Errors.Fail(ErrorCode.Validation, InvalidRequest); }
    }

    internal static void Entry(ReplicaMembershipAuthorityEntryV1 entry)
    {
        if (entry is null || !CanonicalAddress(entry.Address) || !Bounded(entry.Host) || !Bounded(entry.Name)
            || !Enum.IsDefined(entry.Status) || entry.ProxyPort is < 0 or > IPEndPoint.MaxPort
            || !ValidRowEtag(entry.RowETag)
            || entry.Suspects.IsDefault || entry.Suspects.Length > ReplicaMembershipAuthorityProtocol.MaximumSuspects)
        { throw Errors.Fail(ErrorCode.Validation, InvalidRequest); }
        foreach (var suspect in entry.Suspects)
        {
            if (suspect is null || !CanonicalAddress(suspect.Address))
            { throw Errors.Fail(ErrorCode.Validation, InvalidRequest); }
        }
        if (NativeSerialization.Measure(entry) > ReplicaMembershipAuthorityProtocol.MaximumRowBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidRequest); }
    }

    internal static void Reply(ReplicaMembershipAuthorityReplyV1 reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.Version != ReplicaMembershipAuthorityProtocol.Version || reply.AuthorityPhysicalShardId == Guid.Empty
            || reply.AuthorityIncarnation == Guid.Empty || reply.RequestId == Guid.Empty
            || !ValidNonceText(reply.RequestNonce) || !Enum.IsDefined((ReplicaMembershipAuthorityResultKind)reply.ResultKind)
            || reply.ErrorDetailCode is < 0 or > 12
            || reply.ErrorCode is { } error && !Enum.IsDefined(error)
            || reply.TableVersionETag is null
            || reply.Rows.IsDefault || reply.Rows.Length > ReplicaMembershipAuthorityProtocol.MaximumRows)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidReply); }
        foreach (var row in reply.Rows)
        { Entry(row); }
        var failed = (ReplicaMembershipAuthorityResultKind)reply.ResultKind == ReplicaMembershipAuthorityResultKind.Failed;
        if (failed && (reply.ErrorCode is null || reply.ErrorDetailCode == 0 || reply.Applied || !reply.Rows.IsEmpty
                || reply.TableVersion != 0 || reply.TableVersionETag.Length != 0)
            || !failed && (reply.ErrorCode is not null || reply.ErrorDetailCode != 0))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidReply); }
        if (NativeSerialization.Measure(reply) > ReplicaMembershipAuthorityProtocol.MaximumReplyBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidReply); }
    }

    private static void ValidateOperation(ReplicaMembershipAuthorityOperation operation, ReplicaMembershipAuthorityCallV1 call)
    {
        var defaults = call.ExpectedTableVersion >= 0 && call.CleanupBeforeUtcTicks >= 0;
        var valid = defaults && (operation switch
        {
            ReplicaMembershipAuthorityOperation.ReadAll => call.TargetSiloAddress is null && call.CandidateEntry is null
                && call.ExpectedTableVersion == 0 && call.ExpectedTableVersionETag is null && call.ExpectedRowETag is null
                && call.CleanupBeforeUtcTicks == 0,
            ReplicaMembershipAuthorityOperation.ReadRow => call.TargetSiloAddress is not null && call.CandidateEntry is null
                && call.ExpectedTableVersion == 0 && call.ExpectedTableVersionETag is null && call.ExpectedRowETag is null
                && call.CleanupBeforeUtcTicks == 0,
            ReplicaMembershipAuthorityOperation.InsertRow => call.TargetSiloAddress is null && call.CandidateEntry is not null
                && ValidTableVersion(call) && call.ExpectedRowETag is null && call.CleanupBeforeUtcTicks == 0,
            ReplicaMembershipAuthorityOperation.UpdateRow => call.TargetSiloAddress is not null && call.CandidateEntry is not null
                && ValidTableVersion(call) && call.ExpectedRowETag is not null
                && call.ExpectedRowETag == call.CandidateEntry.RowETag && call.CleanupBeforeUtcTicks == 0,
            ReplicaMembershipAuthorityOperation.UpdateIAmAlive => call.TargetSiloAddress is null && call.CandidateEntry is not null
                && call.ExpectedTableVersion == 0 && call.ExpectedTableVersionETag is null && call.ExpectedRowETag is null
                && call.CleanupBeforeUtcTicks == 0,
            ReplicaMembershipAuthorityOperation.CleanupDefunct => call.CandidateEntry is null && call.TargetSiloAddress is null
                && call.ExpectedTableVersion == 0 && call.ExpectedTableVersionETag is null && call.ExpectedRowETag is null
                && IsValidCleanupCutoff(call.CleanupBeforeUtcTicks),
            _ => false
        });
        if (!valid)
        { throw Errors.Fail(ErrorCode.Validation, InvalidRequest); }
        if ((operation is ReplicaMembershipAuthorityOperation.InsertRow or ReplicaMembershipAuthorityOperation.UpdateIAmAlive)
            && call.CandidateEntry!.RowETag != "0")
        { throw Errors.Fail(ErrorCode.Validation, InvalidRequest); }
    }

    private static bool ValidTableVersion(ReplicaMembershipAuthorityCallV1 call)
        => call.ExpectedTableVersionETag is { Length: > 0 } value
            && long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 0 && parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) == value;

    private static bool ValidRowEtag(string value)
        => long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
            out var parsed) && parsed >= 0 && parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) == value;

    private static bool IsValidCleanupCutoff(long ticks)
        => ticks > 0 && ticks <= DateTime.MaxValue.Ticks;

    internal static bool Bounded(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        { return false; }
        try
        { return Utf8.GetByteCount(value) <= ReplicaMembershipAuthorityProtocol.MaximumAddressBytes; }
        catch (EncoderFallbackException) { return false; }
    }

    internal static bool CanonicalAddress(string? value)
    {
        if (!Bounded(value))
        { return false; }
        try
        {
            var address = SiloAddress.FromParsableString(value!);
            return string.Equals(address.ToParsableString(), value, StringComparison.Ordinal);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        { return false; }
    }

    internal static bool ValidNonce(string? value)
    {
        if (value is not { Length: ReplicaMembershipAuthorityProtocol.NonceCharacters }
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        { return false; }
        Span<byte> bytes = stackalloc byte[ReplicaMembershipAuthorityProtocol.NonceBytes];
        var encoded = value.Replace('-', '+').Replace('_', '/') + "==";
        return Convert.TryFromBase64String(encoded, bytes, out var count) && count == bytes.Length
            && string.Equals(Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                value, StringComparison.Ordinal);
    }

    private static bool ValidNonceText(string? value) => ValidNonce(value);
}
