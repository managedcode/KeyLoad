namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeTestSelectionProgram
{
    internal const string Source = """
            import { pathToFileURL } from 'node:url';
            const { nativeSelection } = await import(pathToFileURL(process.argv[1]));
            const suite = process.argv[2];
            for (const bad of [[], ['--KeyLoadTests:Suite=unknown'], ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Suite=cluster'],
              ['--KeyLoadTests:Suite=unit-scalar'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Execution:MaximumParallelTests=51'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Execution:MaximumParallelTests=0'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Execution:MaximumParallelTests=1.5'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Execution:MaximumParallelTests=invalid'],
              ['--KeyLoadTests:Suite=cluster','--KeyLoadTests:Filter=/*/*/HeavyDocumentLoadClusterTests/*'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:NativeCoverage:ServerMode=wrong']]) {
              let rejected = false; try { nativeSelection(bad, {}); } catch { rejected = true; }
              if (!rejected) throw new Error('Invalid native test selection was accepted.');
            }
            const heavyArgs = ['--KeyLoadTests:Suite=cluster', '--KeyLoadTests:Filter=/*/*/HeavyDocumentLoadClusterTests/*', '--KeyLoadTests:HeavyLoad:Enabled=true'];
            const heavy = nativeSelection(heavyArgs, {});
            if (heavy.args[heavy.args.indexOf('--maximum-parallel-tests') + 1] !== '1'
              || !nativeSelection(['--KeyLoadTests:Suite=cluster'], {}).args.includes('/*/*/*/*[Category!=HeavyLoad]')) {
              throw new Error('Exclusive heavy load selection was not preserved.');
            }
            for (const extra of [['--KeyLoadTests:Execution:MaximumParallelTests=20'], ['--KeyLoadTests:CoverageSettings=settings.xml'], ['--KeyLoadTests:NativeCoverage:ServerMode=all']]) {
              let rejected = false; try { nativeSelection([...heavyArgs, ...extra], {}); } catch { rejected = true; }
              if (!rejected) throw new Error('Heavy load was accepted as an ordinary or coverage run.');
            }
            const ordinaryCovered = nativeSelection(['--KeyLoadTests:Suite=cluster', '--KeyLoadTests:NativeCoverage:ServerMode=all'], {});
            if (!JSON.parse(ordinaryCovered.environment.KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS)
              .includes('--KeyLoadTests:Filter=/*/*/*/*[Category!=HeavyLoad]')) {
              throw new Error('Native preparation lost the ordinary heavy-load exclusion.');
            }
            """ + NativeSimdSelectionProgram.Source + """
            const selected = nativeSelection(['--KeyLoadTests:Suite='+suite, '--KeyLoadTests:Filter=/*/*/ActualCase/*',
              '--KeyLoadTests:ReportTrx=true', '--KeyLoadTests:CoverageSettings=settings.xml',
              '--KeyLoadTests:CoverageOutput=coverage.xml'], { GITHUB_SHA: 'original-revision' });
            const explicit20 = nativeSelection(['--KeyLoadTests:Suite='+suite,
              '--KeyLoadTests:Execution:MaximumParallelTests=20'], {});
            const tuned50 = nativeSelection(['--KeyLoadTests:Suite='+suite,
              '--KeyLoadTests:Execution:MaximumParallelTests=50'], {});
            const localArgs = ['--KeyLoadTests:Suite=cluster',
              '--KeyLoadTests:Filter=/*/*/RelationalSqlClusterJoinRejectionTests/*',
              '--KeyLoadTests:LocalClusterImage:Enabled=true'];
            const standardArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/(PartitionQueryMcpSchemaTests|RelationalSqlClusterJoinTests|RelationalSqlClusterJoinAuthorizationTests|RelationalSqlClusterJoinBudgetTests|RelationalSqlClusterJoinCancellationTests|RelationalSqlClusterJoinReadCutTests)/*", localArgs[2]];
            const standard = nativeSelection(standardArgs, {});
            const rejectionArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/RelationalSqlClusterJoinRejectionTests/*", localArgs[2]];
            const rejection = nativeSelection(rejectionArgs, {});
            """ + NativeRetainedSnapshotSelectionProgram.Source + """
            const nativeTextArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/(NativeTextAsyncClusterTests)|(NativeTextClusterLeaderLossTests)|(NativeTextClusterTests)|(NativeTextWaitClusterTests)/*", localArgs[2]];
            const nativeText = nativeSelection(nativeTextArgs, {});
            """ + NativeGraphIncomingSelectionProgram.Source + NativeResourcePolicySelectionProgram.Source + NativeMcpAuthoritySelectionProgram.Source + NativeR33PublicSelectionProgram.Source + NativeR34PublicSelectionProgram.Source + NativeR34PublicSelectionProgram.SuccessorSource + NativePublicCohortSelectionProgram.Source + NativePublicCohortSelectionProgram.SuccessorSource + NativePublicCohortSelectionProgram.FinalSource + NativeCurrentModelSelectionProgram.Source + """
            for (const selectedLocalArgs of [standardArgs, rejectionArgs, retainedSnapshotArgs, nativeTextArgs, graphIncomingArgs, resourcePolicyArgs, mcpAuthorityArgs, mcpCredentialArgs, mcpInitialArgs, r33PublicArgs, r34PublicArgs, r35PublicArgs, publicCohortArgs, nextPublicCohortArgs, finalPublicCohortArgs, ...currentModelArgs]) {
            for (const [bad, env] of [
              [selectedLocalArgs.filter(value => !value.includes('Filter=')), {}],
              [selectedLocalArgs.map(value => value.includes('Filter=') ? '--KeyLoadTests:Filter=/*/*/OtherCase/*' : value), {}],
              [selectedLocalArgs, { GITHUB_SHA: 'source-revision' }],
              [selectedLocalArgs, { KEYLOAD_IMAGE_RECEIPT: 'github-receipt' }],
              [selectedLocalArgs, { GITHUB_ACTIONS: 'true' }],
              [selectedLocalArgs, { KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS: 'coverage' }],
              [selectedLocalArgs, { KEYLOAD_TUNIT_LOCAL_CLUSTER_IMAGE_ARGUMENTS: 'inherited' }],
              [selectedLocalArgs.map(value => value.replace('Enabled=true', 'Enabled=false')), {}],
              [selectedLocalArgs.concat('--KeyLoadTests:CoverageSettings=settings.xml'), {}],
              [selectedLocalArgs.concat('--Benchmarks:Scenario=read'), {}],
              [selectedLocalArgs, { KEYLOAD_IMAGE_PROVENANCE: 'local-development' }],
              [selectedLocalArgs, { KEYLOAD_LOCAL_IMAGE_RECEIPT: 'local-receipt' }],
              [selectedLocalArgs, { KeyLoad__ContainerImages__Server: 'keyload/local-server:local-test' }],
              [selectedLocalArgs, { KEYLOAD_LOCAL_CLUSTER_IMAGE_CHILD: 'true' }],
              [selectedLocalArgs.concat('--KeyLoadTests:NativeCoverage:ServerMode=enabled'), {}],
              [selectedLocalArgs.map(value => value.replace('Suite=cluster', 'Suite=unit')), {}]]) {
              let rejected = false; try { nativeSelection(bad, env); } catch { rejected = true; }
              if (!rejected) throw new Error('Invalid local CLUSTER image selection was accepted.');
            }
            }
            let rejectionUnsupportedCount = 0;
            for (const badFilter of ['/*/*/RelationalSqlClusterJoinRejectionTests/RejectedJoinOnFourPublicPathsPreservesRowsAndCompleteHealthyPage',
              '/*/*/RelationalSqlClusterJoinRejection*/*',
              '/*/*/(RelationalSqlClusterJoinRejectionTests|RelationalSqlClusterJoinTests)/*',
              '/*/*/RelationalSqlClusterJoinRejectionTests/* ']) {
              try {
                nativeSelection([rejectionArgs[0], '--KeyLoadTests:Filter='+badFilter, rejectionArgs[2]], {});
              } catch (failure) {
                if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
                rejectionUnsupportedCount++; continue;
              }
              throw new Error('Unsupported public12 filter was accepted.');
            }
            let nativeTextUnsupportedCount = 0;
            for (const badFilter of ['/*/*/NativeTextAsyncClusterTests/*', '/*/*/NativeText*/*',
              '/*/*/(NativeTextAsyncClusterTests)|(NativeTextClusterLeaderLossTests)|(NativeTextClusterTests)|(NativeTextWaitClusterTests)/* ', '/*/*/(NativeTextAsyncClusterTests)|(NativeTextClusterLeaderLossTests)|(NativeTextClusterTests)|(NativeTextWaitClusterTests)/*|/*/*/OtherCase/*']) {
              try { nativeSelection([nativeTextArgs[0], '--KeyLoadTests:Filter='+badFilter, nativeTextArgs[2]], {}); }
              catch (failure) {
                if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
                nativeTextUnsupportedCount++; continue;
              }
              throw new Error('Unsupported native text CLUSTER filter was accepted.');
            }
            console.log(JSON.stringify({ currentModels, selected, explicit20, tuned50, standard, rejection,
              r33Public, r33PublicUnsupportedCount, r34Public, r34PublicUnsupportedCount, r35Public, r35PublicUnsupportedCount, publicCohort, publicCohortUnsupportedCount, publicCohortNoEffects, nextPublicCohort, nextPublicCohortUnsupportedCount, nextPublicCohortNoEffects, finalPublicCohort, finalPublicCohortUnsupportedCount, finalPublicCohortNoEffects, rejectionUnsupportedCount, nativeText, nativeTextUnsupportedCount, graphIncoming, graphIncomingUnsupportedCount, retainedSnapshot, retainedSnapshotUnsupportedCount, resourcePolicy, resourcePolicyUnsupportedCount, mcpAuthority, mcpCredential, mcpAuthorityUnsupportedCount, mcpCredentialUnsupportedCount, mcpInitial, mcpInitialUnsupportedCount }));
            """;
}
