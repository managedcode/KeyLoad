namespace KeyLoad.Clustering;

internal static class NativeCommandMetadataProtocol
{
    internal const int CurrentVersion = 1;
    internal const int VersionField = 0;
    internal const int MaximumReadBytesField = 1;
    internal const int MaximumExaminedRecordsField = 2;
    internal const int MaximumResultBytesField = 3;
    internal const int ReadBytesField = 0;
    internal const int ExaminedRecordsField = 1;
    internal const int ResultBytesField = 2;
    internal const int ReplyMetadataField = 4;
    internal const string AdmissionAlias = "keyload.native-command-metadata-admission.v1";
    internal const string StatisticsAlias = "keyload.native-command-metadata-statistics.v1";
}
