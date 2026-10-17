using System.Net;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestIdReceipt(string Path, HttpStatusCode StatusCode, string[] HeaderValues);
