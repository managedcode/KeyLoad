namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferFields
{
    internal const int Purpose = 0;
    internal const int Incarnation = 1;
    internal const int Source = 2;
    internal const int TransferId = 3;
    internal const int Destination = 4;
    internal const int PrincipalId = 5;
    internal const int Message = 6;
    internal const int Fingerprint = 7;
    internal const int TargetCommit = 8;
    internal const int State = 9;
    internal const int IntentToken = 10;
    internal const int ReceiptToken = 11;
    internal const int ReceiptReservationBytes = 12;
    internal const int StoredBytes = 13;
    internal const int StoredRecords = 14;
    internal const int AcceptAttempts = 15;
    internal const int Repairs = 16;
    internal const int RemoteTarget = 17;
    internal const int RemoteOrigin = 17;
}
