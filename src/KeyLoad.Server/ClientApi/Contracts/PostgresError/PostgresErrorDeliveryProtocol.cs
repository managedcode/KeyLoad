namespace KeyLoad.Server;

internal static class PostgresErrorDeliveryProtocol
{
    internal const string WitnessAlias = "KeyLoad.PostgresErrorDeliveryWitness";
    internal const string ArmAlias = "KeyLoad.PostgresErrorDeliveryArm";
    internal const string OwnerAlias = "KeyLoad.PostgresErrorDeliveryOwner";
    internal const string Root = "/native-sql-error-probe";
    internal const string Configuration = "KeyLoadTests:PostgresErrorDelivery";
    internal const string SessionSetting = "Session";
    internal const string Owner = "owner.bin";
    internal const string Arm = "arm.bin";
    internal const string WitnessPrefix = "witness-";
    internal const string Suffix = ".bin";
    internal const string PendingSuffix = ".pending";
    internal const string IdentityFormat = "N";
    internal const string Invalid = "The private native SQL error-delivery ownership is invalid.";
    internal const int None = 0;
    internal const int One = 1;
    internal const int PrimaryIndex = 0;
    internal const int FirstFlushFailure = 1;
}
