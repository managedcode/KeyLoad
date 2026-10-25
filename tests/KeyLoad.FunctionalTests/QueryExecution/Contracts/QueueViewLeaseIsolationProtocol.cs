using System.Collections.Immutable;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueueViewLeaseIsolationProtocol
{
    internal const string Root = "root";
    internal const string Reader = "lease-view-reader";
    internal const string Queue = "model-jobs";
    internal const string Events = "model-events";
    internal const string Stream = "order-history";
    internal const string First = "message-created";
    internal const string Second = "message-paid";
    internal const string Healthy = "zz-healthy";
    internal const string FirstEvent = "event-created";
    internal const string SecondEvent = "event-paid";
    internal const string CreatedType = "OrderCreated";
    internal const string PaidType = "OrderPaid";
    internal const string FirstPayload = "{\"secret\":\"private-queue-canary\",\"value\":\"created\"}";
    internal const string SecondPayload = "{\"secret\":\"private-queue-canary\",\"value\":\"paid\"}";
    internal const string HealthyPayload = "{\"secret\":\"private-queue-canary\",\"value\":\"healthy\"}";
    internal const string FirstHeaders = "{\"privateHeader\":\"private-queue-header-canary\",\"source\":\"orders\"}";
    internal const string SecondHeaders = "{\"privateHeader\":\"private-queue-header-canary\",\"source\":\"billing\"}";
    internal const string HealthyHeaders = "{\"privateHeader\":\"private-queue-header-canary\",\"source\":\"health\"}";
    internal const string PayloadPath = "/secret";
    internal const string HeaderPath = "/privateHeader";
    internal const string ModelPayloadPath = "/payload/secret";
    internal const string ModelHeaderPath = "/headers/privateHeader";
    internal const string Classification = "restricted";
    internal const string ReadGrant = "lease-view.read";
    internal const string UseGrant = "lease-view.use";
    internal const string QueueSql = "SELECT TOP (3) * FROM QUEUE_MESSAGES('model-jobs') ORDER BY id";
    internal const string HiddenSql = "SELECT TOP (3) leaseOwner,leaseVersion,leaseUntil,token,deliveryGeneration FROM QUEUE_MESSAGES('model-jobs') ORDER BY id";
    internal const string HiddenJson = "{\"leaseOwner\":null,\"leaseVersion\":null,\"leaseUntil\":null,\"token\":null,\"deliveryGeneration\":null}";
    internal const string PrivateSql = "SELECT TOP (3) * FROM QUEUE_MESSAGES('model-jobs') WHERE payload.secret='private-queue-canary'";
    internal const string StaleSql = "SELECT TOP (3) * FROM EVENTS('model-events','order-history',2) ORDER BY revision";
    internal const string UpdateSql = "UPDATE [model-jobs] SET state='Acked'";
    internal const string QueuePath = "model-scan:queue-messages";
    internal const string UnsupportedCursor = "lease-view-model-cursor";
    internal const string Identifier = "/@id";
    internal const string Star = "*";
    internal const string EnqueueKind = "enqueue";
    internal const string AppendKind = "appendEvents";
    internal const string Absent = "The original execution lease-view authority is absent.";
    internal const string Equal = "=";
    internal const string PrivateValue = "private-queue-canary";
    internal const string PrivateField = "/payload/secret";
    internal const string RenewKind = "Renew";
    internal const string AckKind = "Ack";
    internal const string ExplainId = "explain";
    internal const string WrongRoot = "The original execution lease-view roots must exist and remain nonempty.";
    internal const string MaskedNotBeforeJsonValue = "1970-01-01T00:00:00+00:00";
    internal const string CreatedValue = "created";
    internal const string PaidValue = "paid";
    internal const string HealthyValue = "healthy";
    internal const string Orders = "orders";
    internal const string Billing = "billing";
    internal const string Health = "health";
    internal const int Empty = 0;
    internal const int One = 1;
    internal const int RowLimit = 3;
    internal const int LeaseSeconds = 120;
    internal const int ColdCuts = 2;
    internal const long Initial = 1L;
    internal const long SecondSequence = 2L;
    internal const long HealthySequence = 3L;
    internal const long Leased = 2L;
    internal const long Renewed = 3L;
    internal const long Acked = 4L;
    internal const long DeniedEpoch = 2L;
    internal const long RepairedEpoch = 3L;
    internal static readonly DateTimeOffset NotBefore = DateTimeOffset.UnixEpoch;
    internal static readonly ImmutableArray<Selection> HiddenSelections =
    [new("/leaseOwner", "leaseOwner"), new("/leaseVersion", "leaseVersion"), new("/leaseUntil", "leaseUntil"),
     new("/token", "token"), new("/deliveryGeneration", "deliveryGeneration")];
}
