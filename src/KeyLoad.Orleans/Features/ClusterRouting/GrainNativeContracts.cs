namespace KeyLoad.Orleans;

internal static class GrainNativeContracts
{
    internal const string RequestPurpose = "keyload-grain-request-data-epoch6-rpc2";
    internal const string EnvelopeAlias = "keyload.request.envelope.v2";
    internal const string ValueAlias = "keyload.request.value.v1";
    internal const string SignedTokenPrefix = "KLT2.";
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
    internal const int ValueField = 0;
}
