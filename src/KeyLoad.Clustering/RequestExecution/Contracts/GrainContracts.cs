namespace KeyLoad.Clustering;

internal static class GrainContracts
{
    internal const string RequestPurpose = "keyload-grain-request-data-epoch8-rpc3";
    internal const string RuntimeJournalPurpose = "keyload-runtime-journal-data-epoch8";
    internal const string EnvelopeAlias = "keyload.request.envelope";
    internal const string ValueAlias = "keyload.request.value";
    internal const string CommandObservationAdmissionAlias = "keyload.execution-command-observation-admission";
    internal const string CommandWorkProofAlias = "keyload.execution-command-work-proof";
    internal const string AdministratorAuthenticationRequestAlias = "keyload.execution-administrator-auth-request";
    internal const string AdministratorAuthenticationResultAlias = "keyload.execution-administrator-auth-result";
    internal const string CommandObservationHandleAlias = "keyload.execution-command-observation-handle";
    internal const string CommandObservationResultAlias = "keyload.execution-command-observation-result";
    internal const int NoDtoMarker = 0;
    internal const int PurposeField = 0;
    internal const int RequestIdField = 1;
    internal const int IncarnationField = 2;
    internal const int PrincipalIdField = 3;
    internal const int ReadKindField = 4;
    internal const int CommandKindField = 5;
    internal const int CommandIdField = 6;
    // Field 7 was EncodedPayload. It is retired and must never be reused.
    internal const int ExpiresAtField = 8;
    internal const int PayloadField = 9;
    internal const int ObservationField = 10;
    internal const int MetadataField = 11;
    internal const int ValueField = 0;
}
