namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedGitHubTransportReceipts
{
    internal const string Source = """
        const recorded = {
          "partial": {
            "endpoint": "repos/managedcode/KeyLoad/actions/runs/38811914189/attempts/1/jobs?per_page=100&page=3",
            "status": 200,
            "bytes": 219259,
            "sha256": "60a9c1f9a789f806b83dfaad221d865480b017d423a38b8014d0791a984030c1",
            "runtimeProcess": {
              "exitCode": 1,
              "signal": null,
              "timedOut": false,
              "exceeded": false,
              "spawnFailed": false,
              "cancelled": false,
              "stderrBytes": 54,
              "stderrSha256": "e07007b39afb8a93beedec3ecc655222f0615a73ed8316a2a1d0c63699cf7441"
            }
          },
          "failure": {
            "endpoint": "repos/managedcode/KeyLoad/actions/runs/38643737058/attempts/1/jobs?per_page=100&page=7",
            "repeat": 0,
            "status": 200,
            "body": "metadata/requests/request-00034.body",
            "headers": "metadata/requests/request-00034.headers",
            "bytes": 0,
            "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            "runtimeProcess": {
              "exitCode": 1,
              "signal": null,
              "timedOut": false,
              "exceeded": false,
              "spawnFailed": false,
              "cancelled": false,
              "stderrBytes": 54,
              "stderrSha256": "e07007b39afb8a93beedec3ecc655222f0615a73ed8316a2a1d0c63699cf7441"
            },
            "runtimeFailure": "metadata/requests/request-00034.body.execution-process.json",
            "waitMs": 0,
            "plannedAccumulatedWaitMs": 5491
          },
          "recovered": {
            "endpoint": "repos/managedcode/KeyLoad/actions/runs/38643737058/attempts/1/jobs?per_page=100&page=6",
            "repeat": 1,
            "status": 200,
            "body": "metadata/attempts/38643737058/1/jobs-page-06.json",
            "headers": "metadata/requests/request-00033.headers",
            "bytes": 326051,
            "sha256": "e926a51610da2c74507fe80e187bacafeed02eadd9fc1553c6f353837c84e556",
            "runtimeProcess": {
              "exitCode": 0,
              "signal": null,
              "timedOut": false,
              "exceeded": false,
              "spawnFailed": false,
              "cancelled": false,
              "stderrBytes": 0,
              "stderrSha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
            },
            "runtimeFailure": null,
            "waitMs": 0,
            "plannedAccumulatedWaitMs": 5491
          },
          "transient": {
            "endpoint": "repos/managedcode/KeyLoad/actions/runs/38643737058/attempts/1/jobs?per_page=100&page=6",
            "repeat": 0,
            "status": 502,
            "body": "metadata/requests/request-00032.body",
            "headers": "metadata/requests/request-00032.headers",
            "bytes": 32,
            "sha256": "66f94f66a4663eb2a43d552b17609a828166862b7c5dba75b425301de772f858",
            "runtimeProcess": {
              "exitCode": 1,
              "signal": null,
              "timedOut": false,
              "exceeded": false,
              "spawnFailed": false,
              "cancelled": false,
              "stderrBytes": 28,
              "stderrSha256": "a23c9f20796b2fda2921413c8762d2cdea7b84b24e72cec1bf59ad8b3ee27c7d"
            },
            "runtimeFailure": "metadata/requests/request-00032.body.execution-process.json",
            "waitMs": 5484,
            "plannedAccumulatedWaitMs": 5484
          }
        };
        """;
}
