using System.Collections.Immutable;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueueViewLeaseIsolationProtocol
{
    internal const int Empty = 0;
    internal const int One = 1;
    internal const int Routes = 6;
    internal const int SdkSql = 0;
    internal const int SdkAst = 1;
    internal const int McpSql = 2;
    internal const int McpAst = 3;
    internal const int SdkQ1 = 4;
    internal const int McpQ1 = 5;
    internal const int ColdCuts = 2;
    internal const int SourceLimit = 3;
    internal const int LeaseSeconds = 120;
    internal const long InitialVersion = 1L;
    internal const long LeasedVersion = 2L;
    internal const long RenewedVersion = 3L;
    internal const long AckedVersion = 4L;
    internal const long DeniedEpoch = 2L;
    internal const long RepairedEpoch = 3L;
    internal const string Administrator = "root";
    internal const string Healthy = "zz-healthy";
    internal const string HealthyPayload = "{\"secret\":\"private-queue-canary\",\"value\":\"healthy\"}";
    internal const string HealthyHeaders = "{\"privateHeader\":\"private-queue-header-canary\",\"source\":\"health\"}";
    internal const string MaskedNotBeforeJsonValue = "2020-10-01T10:00:00+00:00";
    internal const string Created = "created";
    internal const string Paid = "paid";
    internal const string HealthyValue = "healthy";
    internal const string Orders = "orders";
    internal const string Billing = "billing";
    internal const string Health = "health";
    internal const string Star = "*";
    internal const string ExplainId = "explain";
    internal const string UnknownRoute = "The public queue view route is invalid.";
    internal const string IdentifierOrder = "/@id";
    internal const string QueuePath = "model-scan:queue-messages";
    internal const string PayloadPath = "/payload/secret";
    internal const string HeaderPath = "/headers/privateHeader";
    internal const string UnsupportedCursor = "queue-view-original-unsupported-cursor";
    internal const string UpdateSql = "UPDATE [model-jobs] SET state = 'Acked'";
    internal const string HiddenSql = "SELECT TOP (3) leaseOwner, leaseVersion, leaseUntil, token, deliveryGeneration FROM QUEUE_MESSAGES('model-jobs') ORDER BY id";
    internal const string HiddenJson = "{\"leaseOwner\":null,\"leaseVersion\":null,\"leaseUntil\":null,\"token\":null,\"deliveryGeneration\":null}";
    internal const string PrivateUseSql = "SELECT TOP (3) * FROM QUEUE_MESSAGES('model-jobs') WHERE payload.secret = 'private-queue-canary' ORDER BY id";
    internal const string EqualityOperator = "=";
    internal const string FirstPayload = "{\"secret\":\"private-queue-canary\",\"value\":\"created\"}";
    internal const string FirstHeaders = "{\"privateHeader\":\"private-queue-header-canary\",\"source\":\"orders\"}";
    internal const string SecondPayload = "{\"secret\":\"private-queue-canary\",\"value\":\"paid\"}";
    internal const string SecondHeaders = "{\"privateHeader\":\"private-queue-header-canary\",\"source\":\"billing\"}";
    internal static readonly ImmutableArray<Selection> HiddenSelections =
    [new("/leaseOwner", "leaseOwner"), new("/leaseVersion", "leaseVersion"),
     new("/leaseUntil", "leaseUntil"), new("/token", "token"), new("/deliveryGeneration", "deliveryGeneration")];
    internal const string EnqueueKind = "enqueue";
    internal const string AppendKind = "appendEvents";
    internal const string Absent = "The original public queue lease or canonical receipt is absent.";
}
