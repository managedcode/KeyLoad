namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeLifetimeProgram
{
    internal const string Success = "success";
    internal const string NonzeroExit = "nonzero-exit";
    internal const string StandardOutputLimit = "stdout-limit";
    internal const string BothOutputLimits = "both-output-limits";
    internal const string CancellationTree = "cancellation-tree";
    internal const string StandardOutput = "node-out\n";
    internal const string StandardError = "node-err\n";
    internal const string NonzeroOutput = "node-exit-23\n";
    internal const string NonzeroError = "node-stderr-23\n";
    internal const string OutputLimitMessage = "Isolated aggregate child output exceeded its bound.";
    internal const string OriginalDidNotCancel = "The original Node operation did not preserve cancellation.";
    internal const string OriginalDidNotSettle = "The original Node operation did not settle within cleanup.";
    internal const string IdentityReceiptInvalid = "The native child process identity receipt was invalid.";
    internal const string Source = """
        const { spawn } = require('node:child_process');
        const fs = require('node:fs');
        const scenario = process.argv[1];
        if (scenario === 'success') {
          process.stdout.write('node-out\n');
          process.stderr.write('node-err\n');
        } else if (scenario === 'nonzero-exit') {
          process.stdout.write('node-exit-23\n');
          process.stderr.write('node-stderr-23\n');
          process.exitCode = 23;
        } else if (scenario === 'stdout-limit') {
          process.stdout.write('x'.repeat(65537));
        } else if (scenario === 'both-output-limits') {
          const chunk = 'z'.repeat(4096);
          for (let i = 0; i < 20; i++) {
            process.stdout.write(chunk);
            process.stderr.write(chunk);
          }
          setTimeout(() => {}, 1000);
        } else if (scenario === 'cancellation-tree') {
          const child = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], { stdio: 'inherit' });
          const receiptPath = process.argv[2];
          const temporaryPath = receiptPath + '.tmp';
          const receipt = fs.openSync(temporaryPath, 'wx');
          fs.writeFileSync(receipt, JSON.stringify({ Parent: process.pid, Child: child.pid }));
          fs.closeSync(receipt);
          fs.renameSync(temporaryPath, receiptPath);
          setInterval(() => {}, 1000);
        } else {
          throw new Error('Unknown native child scenario.');
        }
        """;
    internal const int NonzeroExitCode = 23;
    internal const int OutputFailureCount = 2;
}
