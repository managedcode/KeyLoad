namespace KeyLoad.CrashHost;

internal static class NativeChecksumProfileProtocol
{
    internal const string SignalSeparator = ":";
    internal const string VectorCapability = "vector=";
    internal const string Sse42Capability = "sse42=";
    internal const string AdvSimdCapability = "advsimd=";
    internal const string Mode = "native-checksum-profile";
    internal const string CapabilitySignalPrefix = "native-checksum-profile:";
    internal const string CompletionSignalPrefix = "native-checksum-profile-complete:";
    internal const string Seed = "seed";
    internal const string Extend = "extend";
    internal const string Cold = "cold";
    internal const string Enabled = "1";
    internal const string Disabled = "0";
    internal const string DotnetIntrinsics = "DOTNET_EnableHWIntrinsic";
    internal const string ComPlusIntrinsics = "COMPlus_EnableHWIntrinsic";
    internal const string CutFile = "native-checksum-profile-cut.bin";
    internal const string FollowUpCommandFile = "native-checksum-profile-follow-up-command.bin";
    internal const string FollowUpReceiptFile = "native-checksum-profile-follow-up.bin";
    internal const string CutAlias = "keyload.test.native.checksum-profile-cut.v1";
    internal const string RowAlias = "keyload.test.native.checksum-profile-row.v1";
    internal const string Invalid = "The current native checksum profile operation did not preserve its complete cut.";
    internal const int ArgumentCount = 4;
    internal const int ModeIndex = 0;
    internal const int RootIndex = 1;
    internal const int PhaseIndex = 2;
    internal const int ProfileIndex = 3;
    internal const int OriginalEffects = 3;
    internal const int HealthyEffects = 4;
    internal const int EmptyRevision = 0;
    internal const int InitialRevision = 1;
    internal const int Success = 0;
}
