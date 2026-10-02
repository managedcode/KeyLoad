namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MetadataTestContract
{
    internal const int ManifestLimitBytes = 16_384;
    internal const int IdentityLimitBytes = 4_096;
    internal const int OversizedMetadataBytes = 8_388_608;
    internal const int OneByte = 1;
    internal const int NestedDepth = 65;
    internal const int UnsupportedManifestVersion = 2;
    internal const int WhitespaceChunkBytes = 4_096;
    internal const byte JsonWhitespace = (byte)' ';

    internal const string DirectoryPrefix = "keyload-metadata-";
    internal const string GuidFormat = "N";
    internal const string SourceDirectoryName = "source";
    internal const string BackupDirectoryName = "backup";
    internal const string RestoredDirectoryName = "restored";
    internal const string StoredKey = "metadata/key";
    internal const string StoredValue = "metadata-value";
    internal const string IdentityFileName = "identity.json";
    internal const string JournalFileName = "commands.wal";
    internal const string ManifestFileName = "backup.json";
    internal const string OwnerLockFileName = "owner.lock";

    internal const string ManifestOverLimitPath = "manifest-over-limit";
    internal const string IdentityOverLimitPath = "identity-over-limit";
    internal const string IdentityLengthRestorePath = "identity-length-restore";
    internal const string IdentityChecksumRestorePath = "identity-checksum-restore";
    internal const string IdentityInnerChecksumRestorePath = "identity-inner-checksum-restore";
    internal const string VerificationOrderRestorePath = "verification-order-restore";
    internal const string LinkRestorePath = "link-restore";
    internal const string ExistingDestinationPath = "existing-destination";
    internal const string PreservedFileName = "preserved.txt";
    internal const string MissingFileRestorePath = "missing-file-restore";
    internal const string NullManifestRestorePath = "null-manifest-restore";
    internal const string MalformedManifestRestorePath = "malformed-manifest-restore";
    internal const string UnsupportedVersionRestorePath = "unsupported-version-restore";
    internal const string IdentityJsonRestorePath = "identity-json-restore";
    internal const string OversizedManifestSuffix = "-oversized-manifest";
    internal const string OversizedIdentitySuffix = "-oversized-identity";

    internal const string FilesJsonKey = "files";
    internal const string NameJsonKey = "name";
    internal const string LengthJsonKey = "length";
    internal const string ChecksumJsonKey = "checksum";
    internal const string VersionJsonKey = "version";

    internal const string ManifestUnsupportedDetail = "The backup manifest is unsupported.";
    internal const string IdentityFormatUnsupportedDetail = "This database requires a different storage format.";
    internal const string IdentityChecksumInvalidDetail = "The database identity checksum is invalid.";
    internal const string BackupFileLinkDetail = "Backup files cannot be links.";
    internal const string BackupFileVerificationFailedDetail = "A backup file failed verification.";
    internal const string RestoreDestinationNotEmptyDetail = "Restore requires an empty destination.";
    internal const string PreservedFileContents = "keep";
    internal const string NullManifestJson = "null";
    internal const string MalformedJson = "{not-json";
    internal const string IdentityNullJson = "null";
    internal const string JsonMemberSeparator = ",";
    internal const string JsonObjectClose = "}";
    internal const string JsonBooleanTrue = "true";
    internal const string UnknownJsonMemberPrefix = "\"unknown\":";
    internal const string UnknownJsonBooleanMember = UnknownJsonMemberPrefix + JsonBooleanTrue;
    internal const string NestedJsonScalar = "0";
    internal const string FixtureSizeError = "The metadata file exceeds the requested fixture size.";

    internal static string OversizedManifestRestorePath(string path) => path + OversizedManifestSuffix;
    internal static string OversizedIdentityRestorePath(string path) => path + OversizedIdentitySuffix;
}
