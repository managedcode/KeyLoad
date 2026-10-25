using System.Net;

namespace KeyLoad.IntegrationTests.OrleansExecution;

internal sealed record RequestIdReceipt(string Path, HttpStatusCode StatusCode, string[] HeaderValues, Version Version);
