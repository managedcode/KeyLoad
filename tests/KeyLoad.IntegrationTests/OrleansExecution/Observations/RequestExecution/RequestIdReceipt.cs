using System.Net;

namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal sealed record RequestIdReceipt(string Path, HttpStatusCode StatusCode, string[] HeaderValues);
