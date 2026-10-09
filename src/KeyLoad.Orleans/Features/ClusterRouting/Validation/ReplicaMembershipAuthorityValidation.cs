using System.Net;
using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class ReplicaMembershipAuthorityValidation
{
    private const int IsValidReadAllEmptyExpectedTableVersion = 0;
    private const int IsValidReadAllEmptyCleanupBeforeUtcTicks = 0;
    private const int IsValidReadRowEmptyExpectedTableVersion = 0;
    private const int IsValidReadRowEmptyCleanupBeforeUtcTicks = 0;
    private const int IsValidInsertRowEmptyCleanupBeforeUtcTicks = 0;
    private const int IsValidUpdateRowEmptyCleanupBeforeUtcTicks = 0;
    private const int IsValidUpdateIAmAliveEmptyExpectedTableVersion = 0;
    private const int IsValidUpdateIAmAliveEmptyCleanupBeforeUtcTicks = 0;
    private const int IsValidCleanupDefunctEmptyExpectedTableVersion = 0;
    private const int ValidTableVersionExpectedTableVersionETagEmptyCount = 0;
    private const int ValidTableVersionParsedValidationBoundary = 0;
    private const int ValidRowEtagParsedValidationBoundary = 0;
    private const int IsValidCleanupCutoffTicksValidationBoundary = 0;

    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const string InvalidRequest = "The membership authority request is invalid.";
    private const string InvalidReply = "The membership authority reply is invalid.";

    internal static void Call(ReplicaMembershipAuthorityCallV1 call, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call.Version != ReplicaMembershipAuthorityProtocol.Version)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, InvalidRequest); }
        if (string.IsNullOrWhiteSpace(call.ClusterId) || !Bounded(value: call.ClusterId, membershipOptions: membershipOptions)
            || call.AuthorityPhysicalShardId == Guid.Empty || call.AuthorityIncarnation == Guid.Empty
            || call.CallerPhysicalShardId == Guid.Empty || call.CallerIncarnation == Guid.Empty || call.RequestId == Guid.Empty
            || !Bounded(value: call.CallerVoterId, membershipOptions: membershipOptions) || !CanonicalAddress(value: call.CallerSiloAddress, membershipOptions: membershipOptions)
            || !Enum.IsDefined((ReplicaMembershipAuthorityOperation)call.Operation))
        { throw Errors.Fail(ErrorCode.Validation, InvalidRequest); }

        var operation = (ReplicaMembershipAuthorityOperation)call.Operation;
        ValidateOperation(operation, call);
        if (call.CandidateEntry is { } entry)
        {
            if (operation == ReplicaMembershipAuthorityOperation.UpdateIAmAlive)
            { ReplicaMembershipAuthorityHeartbeat.Validate(entry, membershipOptions); }
            else
            { Entry(entry: entry, membershipOptions: membershipOptions); }
        }
        if (call.TargetSiloAddress is { } target && !CanonicalAddress(value: target, membershipOptions: membershipOptions))
        { throw Errors.Fail(ErrorCode.Validation, InvalidRequest); }
    }

    internal static void Entry(ReplicaMembershipAuthorityEntryV1 entry, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        const int ProxyPortEmptyCount = 0;

        if (entry is null || !CanonicalAddress(value: entry.Address, membershipOptions: membershipOptions) || !Bounded(value: entry.Host, membershipOptions: membershipOptions) || !Bounded(value: entry.Name, membershipOptions: membershipOptions)
            || !Enum.IsDefined(entry.Status) || entry.ProxyPort is < ProxyPortEmptyCount or > IPEndPoint.MaxPort
            || !ValidRowEtag(entry.RowETag)
            || entry.Suspects.IsDefault || entry.Suspects.Length > membershipOptions.Value.MaximumSuspects)
        { throw Errors.Fail(ErrorCode.Validation, InvalidRequest); }
        foreach (var suspect in entry.Suspects)
        {
            if (suspect is null || !CanonicalAddress(value: suspect.Address, membershipOptions: membershipOptions))
            { throw Errors.Fail(ErrorCode.Validation, InvalidRequest); }
        }
        if (NativeSerialization.Measure(entry) > membershipOptions.Value.MaximumRowBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidRequest); }
    }

    internal static void Reply(ReplicaMembershipAuthorityReplyV1 reply, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ValidateReplyEnvelope(reply: reply, membershipOptions: membershipOptions);
        ValidateReplyRows(reply: reply, membershipOptions: membershipOptions);
        ValidateReplyOutcome(reply);
        if (NativeSerialization.Measure(reply) > membershipOptions.Value.MaximumReplyBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidReply); }
    }

    private static void ValidateReplyEnvelope(ReplicaMembershipAuthorityReplyV1 reply, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        const int ErrorDetailCodeEmptyCount = 0;
        const int ErrorDetailCodeValidationBound = 12;

        if (reply.Version != ReplicaMembershipAuthorityProtocol.Version || reply.AuthorityPhysicalShardId == Guid.Empty
            || reply.AuthorityIncarnation == Guid.Empty || reply.RequestId == Guid.Empty
            || !ValidNonceText(reply.RequestNonce) || !Enum.IsDefined((ReplicaMembershipAuthorityResultKind)reply.ResultKind)
            || reply.ErrorDetailCode is < ErrorDetailCodeEmptyCount or > ErrorDetailCodeValidationBound
            || reply.ErrorCode is { } error && !Enum.IsDefined(error)
            || reply.TableVersionETag is null
            || reply.Rows.IsDefault || reply.Rows.Length > membershipOptions.Value.MaximumRows)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidReply); }
    }

    private static void ValidateReplyRows(ReplicaMembershipAuthorityReplyV1 reply, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        foreach (var row in reply.Rows)
        { Entry(entry: row, membershipOptions: membershipOptions); }
    }

    private static void ValidateReplyOutcome(ReplicaMembershipAuthorityReplyV1 reply)
    {
        const int EmptyErrorDetailCode = 0;
        const int EmptyTableVersion = 0;
        const int EmptyTableVersionETagLength = 0;

        var failed = (ReplicaMembershipAuthorityResultKind)reply.ResultKind == ReplicaMembershipAuthorityResultKind.Failed;
        if (failed && (reply.ErrorCode is null || reply.ErrorDetailCode == EmptyErrorDetailCode || reply.Applied || !reply.Rows.IsEmpty
                || reply.TableVersion != EmptyTableVersion || reply.TableVersionETag.Length != EmptyTableVersionETagLength)
            || !failed && (reply.ErrorCode is not null || reply.ErrorDetailCode != EmptyErrorDetailCode))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidReply); }
    }

    private static void ValidateOperation(ReplicaMembershipAuthorityOperation operation, ReplicaMembershipAuthorityCallV1 call)
    {
        const int ExpectedTableVersionValidationBoundary = 0;
        const int CleanupBeforeUtcTicksValidationBoundary = 0;

        var defaults = call.ExpectedTableVersion >= ExpectedTableVersionValidationBoundary && call.CleanupBeforeUtcTicks >= CleanupBeforeUtcTicksValidationBoundary;
        var valid = defaults && ValidateOperationShape(operation, call);
        if (!valid)
        { throw Errors.Fail(ErrorCode.Validation, InvalidRequest); }
        if ((operation is ReplicaMembershipAuthorityOperation.InsertRow or ReplicaMembershipAuthorityOperation.UpdateIAmAlive)
            && call.CandidateEntry!.RowETag != ReplicaMembershipAuthorityProtocol.InitialRowETag)
        { throw Errors.Fail(ErrorCode.Validation, InvalidRequest); }
    }

    private static bool ValidateOperationShape(ReplicaMembershipAuthorityOperation operation,
        ReplicaMembershipAuthorityCallV1 call)
        => operation switch
        {
            ReplicaMembershipAuthorityOperation.ReadAll => IsValidReadAll(call),
            ReplicaMembershipAuthorityOperation.ReadRow => IsValidReadRow(call),
            ReplicaMembershipAuthorityOperation.InsertRow => IsValidInsertRow(call),
            ReplicaMembershipAuthorityOperation.UpdateRow => IsValidUpdateRow(call),
            ReplicaMembershipAuthorityOperation.UpdateIAmAlive => IsValidUpdateIAmAlive(call),
            ReplicaMembershipAuthorityOperation.CleanupDefunct => IsValidCleanupDefunct(call),
            _ => false
        };

    private static bool IsValidReadAll(ReplicaMembershipAuthorityCallV1 call)
        => call.TargetSiloAddress is null && call.CandidateEntry is null
            && call.ExpectedTableVersion == IsValidReadAllEmptyExpectedTableVersion && call.ExpectedTableVersionETag is null && call.ExpectedRowETag is null
            && call.CleanupBeforeUtcTicks == IsValidReadAllEmptyCleanupBeforeUtcTicks;

    private static bool IsValidReadRow(ReplicaMembershipAuthorityCallV1 call)
        => call.TargetSiloAddress is not null && call.CandidateEntry is null
            && call.ExpectedTableVersion == IsValidReadRowEmptyExpectedTableVersion && call.ExpectedTableVersionETag is null && call.ExpectedRowETag is null
            && call.CleanupBeforeUtcTicks == IsValidReadRowEmptyCleanupBeforeUtcTicks;

    private static bool IsValidInsertRow(ReplicaMembershipAuthorityCallV1 call)
        => call.TargetSiloAddress is null && call.CandidateEntry is not null
            && ValidTableVersion(call) && call.ExpectedRowETag is null && call.CleanupBeforeUtcTicks == IsValidInsertRowEmptyCleanupBeforeUtcTicks;

    private static bool IsValidUpdateRow(ReplicaMembershipAuthorityCallV1 call)
        => call.TargetSiloAddress is not null && call.CandidateEntry is not null
            && ValidTableVersion(call) && call.ExpectedRowETag is not null
            && call.ExpectedRowETag == call.CandidateEntry.RowETag && call.CleanupBeforeUtcTicks == IsValidUpdateRowEmptyCleanupBeforeUtcTicks;

    private static bool IsValidUpdateIAmAlive(ReplicaMembershipAuthorityCallV1 call)
        => call.TargetSiloAddress is null && call.CandidateEntry is not null
            && call.ExpectedTableVersion == IsValidUpdateIAmAliveEmptyExpectedTableVersion && call.ExpectedTableVersionETag is null && call.ExpectedRowETag is null
            && call.CleanupBeforeUtcTicks == IsValidUpdateIAmAliveEmptyCleanupBeforeUtcTicks;

    private static bool IsValidCleanupDefunct(ReplicaMembershipAuthorityCallV1 call)
        => call.CandidateEntry is null && call.TargetSiloAddress is null
            && call.ExpectedTableVersion == IsValidCleanupDefunctEmptyExpectedTableVersion && call.ExpectedTableVersionETag is null && call.ExpectedRowETag is null
            && IsValidCleanupCutoff(call.CleanupBeforeUtcTicks);

    private static bool ValidTableVersion(ReplicaMembershipAuthorityCallV1 call)
        => call.ExpectedTableVersionETag is { Length: > ValidTableVersionExpectedTableVersionETagEmptyCount } value
            && long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            && parsed >= ValidTableVersionParsedValidationBoundary && parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) == value;

    private static bool ValidRowEtag(string value)
        => long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
            out var parsed) && parsed >= ValidRowEtagParsedValidationBoundary && parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) == value;

    private static bool IsValidCleanupCutoff(long ticks)
        => ticks > IsValidCleanupCutoffTicksValidationBoundary && ticks <= DateTime.MaxValue.Ticks;

    internal static bool Bounded(string? value, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        if (string.IsNullOrWhiteSpace(value))
        { return false; }
        try
        { return Utf8.GetByteCount(value) <= membershipOptions.Value.MaximumAddressBytes; }
        catch (EncoderFallbackException) { return false; }
    }

    internal static bool CanonicalAddress(string? value, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        if (!Bounded(value: value, membershipOptions: membershipOptions))
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
        const char PredicateCharacter = '-';
        const char UnderscoreCharacter = '_';
        const char OldCharCharacter = '-';
        const char NewCharCharacter = '+';
        const char SlashCharacter = '/';
        const string ValidNonceComparisonText = "==";
        const char TrimCharCharacter = '=';
        const char ValidNonceOldCharCharacter = '+';
        const char ValidNonceNewCharCharacter = '-';

        if (value is not { Length: ReplicaMembershipAuthorityProtocol.NonceCharacters }
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not PredicateCharacter and not UnderscoreCharacter))
        { return false; }
        Span<byte> bytes = stackalloc byte[ReplicaMembershipAuthorityProtocol.NonceBytes];
        var encoded = value.Replace(OldCharCharacter, NewCharCharacter).Replace(UnderscoreCharacter, SlashCharacter) + ValidNonceComparisonText;
        return Convert.TryFromBase64String(encoded, bytes, out var count) && count == bytes.Length
            && string.Equals(Convert.ToBase64String(bytes).TrimEnd(TrimCharCharacter).Replace(ValidNonceOldCharCharacter, ValidNonceNewCharCharacter).Replace(SlashCharacter, UnderscoreCharacter),
                value, StringComparison.Ordinal);
    }

    private static bool ValidNonceText(string? value) => ValidNonce(value);
}
