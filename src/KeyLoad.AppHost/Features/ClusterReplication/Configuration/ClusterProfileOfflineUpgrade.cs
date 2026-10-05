using System.Text.Json;
using KeyLoad.AppHost.Features.ClusterReplication;

internal static class ClusterProfileOfflineUpgrade
{
    private const int LegacyRequiredFields = 4;
    private static readonly string[] LegacyFields =
    [nameof(LegacyLocalProfile.Incarnation), nameof(LegacyLocalProfile.SigningKey),
        nameof(LegacyLocalProfile.PeerSecret), nameof(LegacyLocalProfile.AdminKey)];

    internal static LocalProfile Run(string dataRoot)
    {
        const int VersionValue = 2;

        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var root = Path.GetFullPath(dataRoot);
        ClusterProfileStore.RejectLinks(root);
        var path = Path.Combine(root, ClusterProfileStore.ProfileName);
        var backupPath = Path.Combine(root, ClusterProfileStore.LegacyBackupName);
        ClusterProfileStore.RejectLinks(path);
        ClusterProfileStore.RejectLinks(backupPath);
        if (!File.Exists(path) || File.Exists(backupPath))
        { throw new InvalidOperationException(ClusterProfileStore.InvalidProfile); }

        ClusterProfilePermissions.RequirePrivate(path);
        var fileMode = ClusterProfilePermissions.ReadMode(path);
        var originalBytes = ClusterProfileStore.ReadBoundedBytes(path);
        var legacy = DeserializeLegacy(originalBytes);
        var profile = new LocalProfile(VersionValue, Guid.NewGuid(), legacy.Incarnation,
            legacy.SigningKey, legacy.PeerSecret, legacy.AdminKey);
        ClusterProfileStore.Validate(profile);
        return Publish(path, backupPath, originalBytes, profile, fileMode);
    }

    private static LocalProfile Publish(string path, string backupPath, byte[] originalBytes,
        LocalProfile profile, UnixFileMode? fileMode)
    {
        var backupStage = ClusterProfileStore.StagingPath(backupPath);
        var profileStage = ClusterProfileStore.StagingPath(path);
        try
        {
            StageAndVerifyBackup(backupStage, originalBytes, fileMode);
            StageAndVerifyProfile(profileStage, profile, fileMode);
            File.Move(backupStage, backupPath);
            ClusterProfileStore.VerifyCopy(originalBytes, ClusterProfileStore.ReadBoundedBytes(backupPath));
            ClusterProfilePermissions.EnsureMode(backupPath, fileMode);
            File.Move(profileStage, path, overwrite: true);
            var published = ClusterProfileStore.DeserializeCurrent(ClusterProfileStore.ReadBoundedBytes(path));
            if (published != profile)
            { throw new InvalidOperationException(ClusterProfileStore.InvalidProfile); }
            ClusterProfilePermissions.EnsureMode(path, fileMode);
            return published;
        }
        finally
        {
            ClusterProfileStore.DeleteStage(backupStage);
            ClusterProfileStore.DeleteStage(profileStage);
        }
    }

    private static void StageAndVerifyBackup(string stage, byte[] originalBytes, UnixFileMode? fileMode)
    {
        ClusterProfileStore.WriteStage(stage, originalBytes, fileMode);
        ClusterProfileStore.VerifyCopy(originalBytes, ClusterProfileStore.ReadBoundedBytes(stage));
        ClusterProfilePermissions.EnsureMode(stage, fileMode);
    }

    private static void StageAndVerifyProfile(string stage, LocalProfile profile, UnixFileMode? fileMode)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(profile, ClusterProfileStore.Json);
        ClusterProfileStore.WriteStage(stage, bytes, fileMode);
        if (ClusterProfileStore.DeserializeCurrent(ClusterProfileStore.ReadBoundedBytes(stage)) != profile)
        { throw new InvalidOperationException(ClusterProfileStore.InvalidProfile); }
        ClusterProfilePermissions.EnsureMode(stage, fileMode);
    }

    private static LegacyLocalProfile DeserializeLegacy(byte[] bytes)
    {
        try
        {
            ClusterProfileStore.RequireFields(bytes, LegacyFields, LegacyRequiredFields);
            var profile = JsonSerializer.Deserialize<LegacyLocalProfile>(bytes, ClusterProfileStore.Json)
                ?? throw new InvalidOperationException(ClusterProfileStore.InvalidProfile);
            ClusterProfileStore.ValidateCredentials(profile.Incarnation, profile.SigningKey,
                profile.PeerSecret, profile.AdminKey);
            return profile;
        }
        catch (JsonException) { throw new InvalidOperationException(ClusterProfileStore.InvalidProfile); }
    }

    private sealed record LegacyLocalProfile(Guid Incarnation, string SigningKey, string PeerSecret, string AdminKey);
}
