using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeTestSelectionProcess
{
    private const string SelectionProgram = """
            import { pathToFileURL } from 'node:url';
            const { nativeSelection } = await import(pathToFileURL(process.argv[1]));
            const suite = process.argv[2];
            for (const bad of [[], ['--KeyLoadTests:Suite=unknown'], ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Suite=rf3'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Execution:MaximumParallelTests=51'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Execution:MaximumParallelTests=0'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Execution:MaximumParallelTests=1.5'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Execution:MaximumParallelTests=invalid'],
              ['--KeyLoadTests:Suite=rf3','--KeyLoadTests:Filter=/*/*/HeavyDocumentLoadRf3Tests/*'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:NativeCoverage:ServerMode=wrong']]) {
              let rejected = false; try { nativeSelection(bad, {}); } catch { rejected = true; }
              if (!rejected) throw new Error('Invalid native test selection was accepted.');
            }
            const heavyArgs = ['--KeyLoadTests:Suite=rf3', '--KeyLoadTests:Filter=/*/*/HeavyDocumentLoadRf3Tests/*', '--KeyLoadTests:HeavyLoad:Enabled=true'];
            const heavy = nativeSelection(heavyArgs, {});
            if (heavy.args[heavy.args.indexOf('--maximum-parallel-tests') + 1] !== '1'
              || !nativeSelection(['--KeyLoadTests:Suite=rf3'], {}).args.includes('/*/*/*/*[Category!=HeavyLoad]')) {
              throw new Error('Exclusive heavy load selection was not preserved.');
            }
            for (const extra of [['--KeyLoadTests:Execution:MaximumParallelTests=20'], ['--KeyLoadTests:CoverageSettings=settings.xml'], ['--KeyLoadTests:NativeCoverage:ServerMode=all']]) {
              let rejected = false; try { nativeSelection([...heavyArgs, ...extra], {}); } catch { rejected = true; }
              if (!rejected) throw new Error('Heavy load was accepted as an ordinary or coverage run.');
            }
            const ordinaryCovered = nativeSelection(['--KeyLoadTests:Suite=rf3', '--KeyLoadTests:NativeCoverage:ServerMode=all'], {});
            if (!JSON.parse(ordinaryCovered.environment.KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS)
              .includes('--KeyLoadTests:Filter=/*/*/*/*[Category!=HeavyLoad]')) {
              throw new Error('Native preparation lost the ordinary heavy-load exclusion.');
            }
            const selected = nativeSelection(['--KeyLoadTests:Suite='+suite, '--KeyLoadTests:Filter=/*/*/ActualCase/*',
              '--KeyLoadTests:ReportTrx=true', '--KeyLoadTests:CoverageSettings=settings.xml',
              '--KeyLoadTests:CoverageOutput=coverage.xml'], { GITHUB_SHA: 'original-revision' });
            const explicit20 = nativeSelection(['--KeyLoadTests:Suite='+suite,
              '--KeyLoadTests:Execution:MaximumParallelTests=20'], {});
            const tuned50 = nativeSelection(['--KeyLoadTests:Suite='+suite,
              '--KeyLoadTests:Execution:MaximumParallelTests=50'], {});
            const localArgs = ['--KeyLoadTests:Suite=rf3',
              '--KeyLoadTests:Filter=/*/*/TwoRf3MembershipProfileTests/*',
              '--KeyLoadTests:LocalRf3Image:Enabled=true'];
            const local = nativeSelection(localArgs, {});
            const standardArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/(PartitionQueryMcpSchemaTests|RelationalSqlRf3JoinTests|RelationalSqlRf3JoinAuthorizationTests|RelationalSqlRf3JoinBudgetTests|RelationalSqlRf3JoinCancellationTests|RelationalSqlRf3JoinReadCutTests)/*", localArgs[2]];
            const standard = nativeSelection(standardArgs, {});
            const rejectionArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/RelationalSqlRf3JoinRejectionTests/*", localArgs[2]];
            const rejection = nativeSelection(rejectionArgs, {});
            const connectionArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/(ConnectionRf3SequentialTests|ConnectionRf3OverlapTests|ConnectionRf3AuthorizationTests)/*", localArgs[2]];
            const connection = nativeSelection(connectionArgs, {});
            const nativeTextArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/(NativeTextAsyncRf3Tests)|(NativeTextMaintenanceRf3Tests)|(NativeTextRf3LeaderLossTests)|(NativeTextRf3Tests)|(NativeTextWaitRf3Tests)/*", localArgs[2]];
            const nativeText = nativeSelection(nativeTextArgs, {});
            const nativeTextSdkUid = "KeyLoad.IntegrationTests.Features.Search.NativeTextMaintenanceRf3Tests(KeyLoad.IntegrationTests.ClusterFixture).1.1.ProtectedNativeTextMaintenanceReplaysBilingualUpdateDeleteAcrossAllPublicPaths(KeyLoad.IntegrationTests.Features.Search.NativeTextMaintenancePath).1.1.0";
            const nativeTextSdk = nativeSelection([...nativeTextArgs, "--KeyLoadTests:FilterUid="+nativeTextSdkUid], {});
            let nativeTextSdkRejectedCount = 0;
            for (const bad of [
              [nativeTextArgs[0], "--KeyLoadTests:FilterUid="+nativeTextSdkUid, nativeTextArgs[2]],
              [...nativeTextArgs, "--KeyLoadTests:FilterUid=unknown"],
              [...nativeTextArgs, "--KeyLoadTests:FilterUid="+nativeTextSdkUid+" "],
              [...localArgs, "--KeyLoadTests:FilterUid="+nativeTextSdkUid],
              [nativeTextArgs[0], nativeTextArgs[1], "--KeyLoadTests:FilterUid="+nativeTextSdkUid],
              [...nativeTextArgs, "--KeyLoadTests:FilterUid="+nativeTextSdkUid, "--KeyLoadTests:CoverageSettings=settings.xml"],
              [...nativeTextArgs.map(value => value.replace("Suite=rf3", "Suite=unit")), "--KeyLoadTests:FilterUid="+nativeTextSdkUid],
              [...nativeTextArgs, "--KeyLoadTests:FilterUid="+nativeTextSdkUid, "--Benchmarks:Scenario=read"]]) {
              try { nativeSelection(bad, {}); } catch { nativeTextSdkRejectedCount++; continue; }
              throw new Error("Unsupported focused native text UID was accepted.");
            }
            const remoteTransferArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/RemoteTransferDistinctOwnerTests/ActualDistinctOwnersRetainAcceptReceiptAcrossTwoColdRestartsAndBDoesNotResurrectAcknowledgedMessage", localArgs[2]];
            const remoteTransfer = nativeSelection(remoteTransferArgs, {});
            for (const selectedLocalArgs of [localArgs, standardArgs, rejectionArgs, connectionArgs, nativeTextArgs, [...nativeTextArgs, "--KeyLoadTests:FilterUid="+nativeTextSdkUid], remoteTransferArgs]) {
            for (const [bad, env] of [
              [selectedLocalArgs.filter(value => !value.includes('Filter=')), {}],
              [selectedLocalArgs.map(value => value.includes('Filter=') ? '--KeyLoadTests:Filter=/*/*/OtherCase/*' : value), {}],
              [selectedLocalArgs, { GITHUB_SHA: 'source-revision' }],
              [selectedLocalArgs, { KEYLOAD_IMAGE_RECEIPT: 'github-receipt' }],
              [selectedLocalArgs, { GITHUB_ACTIONS: 'true' }],
              [selectedLocalArgs, { KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS: 'coverage' }],
              [selectedLocalArgs, { KEYLOAD_TUNIT_LOCAL_RF3_IMAGE_ARGUMENTS: 'inherited' }],
              [selectedLocalArgs.map(value => value.replace('Enabled=true', 'Enabled=false')), {}],
              [selectedLocalArgs.concat('--KeyLoadTests:CoverageSettings=settings.xml'), {}],
              [selectedLocalArgs.concat('--Benchmarks:Scenario=read'), {}],
              [selectedLocalArgs, { KEYLOAD_IMAGE_PROVENANCE: 'local-development' }],
              [selectedLocalArgs, { KEYLOAD_LOCAL_IMAGE_RECEIPT: 'local-receipt' }],
              [selectedLocalArgs, { KeyLoad__ContainerImages__Server: 'keyload/local-server:local-test' }],
              [selectedLocalArgs, { KEYLOAD_LOCAL_RF3_IMAGE_CHILD: 'true' }],
              [selectedLocalArgs.concat('--KeyLoadTests:NativeCoverage:ServerMode=enabled'), {}],
              [selectedLocalArgs.map(value => value.replace('Suite=rf3', 'Suite=unit')), {}]]) {
              let rejected = false; try { nativeSelection(bad, env); } catch { rejected = true; }
              if (!rejected) throw new Error('Invalid local RF3 image selection was accepted.');
            }
            }
            let rejectionUnsupportedCount = 0;
            for (const badFilter of ['/*/*/RelationalSqlRf3JoinRejectionTests/RejectedJoinOnFourPublicPathsPreservesRowsAndCompleteHealthyPage',
              '/*/*/RelationalSqlRf3JoinRejection*/*',
              '/*/*/(RelationalSqlRf3JoinRejectionTests|RelationalSqlRf3JoinTests)/*',
              '/*/*/RelationalSqlRf3JoinRejectionTests/* ']) {
              try {
                nativeSelection([rejectionArgs[0], '--KeyLoadTests:Filter='+badFilter, rejectionArgs[2]], {});
              } catch (failure) {
                if (failure.message !== 'Invalid native local RF3 image selection.') throw failure;
                rejectionUnsupportedCount++; continue;
              }
              throw new Error('Unsupported public12 filter was accepted.');
            }
            let connectionUnsupportedCount = 0;
            for (const badFilter of ['/*/*/ConnectionRf3SequentialTests/*',
              '/*/*/ConnectionRf3*/*',
              '/*/*/(ConnectionRf3SequentialTests|ConnectionRf3OverlapTests|ConnectionRf3AuthorizationTests|OtherCase)/*',
              '/*/*/(ConnectionRf3SequentialTests|ConnectionRf3OverlapTests|ConnectionRf3AuthorizationTests)/* ']) {
              try {
                nativeSelection([connectionArgs[0], '--KeyLoadTests:Filter='+badFilter, connectionArgs[2]], {});
              } catch (failure) {
                if (failure.message !== 'Invalid native local RF3 image selection.') throw failure;
                connectionUnsupportedCount++; continue;
              }
              throw new Error('Unsupported connection RF3 filter was accepted.');
            }
            let nativeTextUnsupportedCount = 0;
            for (const badFilter of ['/*/*/NativeTextMaintenanceRf3Tests/*', '/*/*/NativeText*/*',
              '/*/*/(NativeTextAsyncRf3Tests)|(NativeTextMaintenanceRf3Tests)|(NativeTextRf3LeaderLossTests)|(NativeTextRf3Tests)|(NativeTextWaitRf3Tests)/* ', '/*/*/(NativeTextAsyncRf3Tests)|(NativeTextMaintenanceRf3Tests)|(NativeTextRf3LeaderLossTests)|(NativeTextRf3Tests)|(NativeTextWaitRf3Tests)/*|/*/*/OtherCase/*']) {
              try { nativeSelection([nativeTextArgs[0], '--KeyLoadTests:Filter='+badFilter, nativeTextArgs[2]], {}); }
              catch (failure) {
                if (failure.message !== 'Invalid native local RF3 image selection.') throw failure;
                nativeTextUnsupportedCount++; continue;
              }
              throw new Error('Unsupported native text RF3 filter was accepted.');
            }
            let remoteTransferUnsupportedCount = 0;
            for (const badFilter of ['/*/*/RemoteTransferDistinctOwnerTests/*',
              '/*/*/RemoteTransferDistinctOwnerTests/ActualDistinctOwners*',
              '/*/*/RemoteTransferDistinctOwnerTests/ActualDistinctOwnersRetainAcceptReceiptAcrossTwoColdRestartsAndBDoesNotResurrectAcknowledgedMessage|/*/*/OtherCase/*', '/*/*/RemoteTransferDistinctOwnerTests/ActualDistinctOwnersRetainAcceptReceiptAcrossTwoColdRestartsAndBDoesNotResurrectAcknowledgedMessage ']) {
              try { nativeSelection([remoteTransferArgs[0], '--KeyLoadTests:Filter='+badFilter, remoteTransferArgs[2]], {}); }
              catch (failure) {
                if (failure.message !== 'Invalid native local RF3 image selection.') throw failure;
                remoteTransferUnsupportedCount++; continue;
              }
              throw new Error('Unsupported distinct-owner RF3 filter was accepted.');
            }
            console.log(JSON.stringify({ selected, explicit20, tuned50, local, standard, rejection,
              rejectionUnsupportedCount, connection, connectionUnsupportedCount, nativeText, nativeTextUnsupportedCount, nativeTextSdk, nativeTextSdkRejectedCount, remoteTransfer, remoteTransferUnsupportedCount }));
            """;

    internal static async Task<string> ReadAsync(string suite)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KeyLoad.slnx")))
        { directory = directory.Parent; }
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Native selection regression requires the checkout.");
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "--input-type=module", "--eval", SelectionProgram,
            Path.Combine(root, "scripts", "Features", "TestInfrastructure", "run-tests.mjs"), suite })
        { start.ArgumentList.Add(argument); }
        using var process = new Process { StartInfo = start };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15), TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current!.Execution.CancellationToken);
        if (!process.Start())
        { throw new InvalidOperationException("Native selection Node process did not start."); }
        var output = process.StandardOutput.ReadToEndAsync(lifetime.Token);
        var error = process.StandardError.ReadToEndAsync(lifetime.Token);
        try
        {
            await process.WaitForExitAsync(lifetime.Token).ConfigureAwait(false);
            var results = await Task.WhenAll(output, error).ConfigureAwait(false);
            await Assert.That(process.ExitCode).IsEqualTo(0);
            await Assert.That(results[1]).IsEmpty();
            return results[0];
        }
        finally
        {
            if (!process.HasExited)
            { process.Kill(entireProcessTree: true); }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3), TimeProvider.System);
            await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            await ((Task)Task.WhenAll(output, error)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
