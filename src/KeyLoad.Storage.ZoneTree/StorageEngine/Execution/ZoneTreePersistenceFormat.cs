namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreePersistenceFormat
{
    internal const int SigningKeyBytes = 32;
    internal const string OwnerLockFileName = "owner.lock";
    internal const string IdentityFileName = "identity.json";
    internal const string TreeDirectoryName = "tree";
    internal const string PlainTreeDirectoryName = "plain-tree";
    internal const string TemporaryFileSuffix = ".tmp";
    internal const string GuidFormat = "N";
    internal const string SystemNamespace = "system";
    internal const string IdentityChecksumInvalid = "The database identity checksum is invalid.";
    internal const string IdentityFormatUnsupported = "The database identity format is unsupported.";
    internal const string IdentityScopeInvalid = "The configured cluster identity does not match this database.";
    internal const string RecoveryRequired = "The database must recover before accepting another operation.";
    internal const string UnknownWriteOutcome = "The transaction outcome is unknown. Recover and retry the same command ID.";
    internal const string InvalidRangeLimitMessage = "The scan limit is outside its supported bounds.";
    internal const string ExhaustedRangeRecordsMessage = "The scan examined-record work budget is exceeded.";
    internal const string ExhaustedRangeBytesMessage = "The scan byte budget is exceeded.";
}
