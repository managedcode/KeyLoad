using System.Net;

namespace KeyLoad.ClusterTests.OrleansExecution;

internal sealed record RequestIdReceipt(string Path, HttpStatusCode StatusCode, string[] HeaderValues, Version Version);
