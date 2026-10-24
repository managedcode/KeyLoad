namespace KeyLoad.UnitTests.TestInfrastructure;

internal static class TestSelectionProgram
{
    internal const string Source = """
            import { pathToFileURL } from 'node:url';
            const { runtimeSelection, executionPipeDirectory } = await import(pathToFileURL(process.argv[1]));
            const suite = process.argv[2];
            if (process.platform !== 'win32') {
              const directory = executionPipeDirectory('/home/runner/work/KeyLoad/KeyLoad/w');
              if (Buffer.byteLength(directory + '/MONITORTOHOST_' + '0'.repeat(32)) > 103) {
                throw new Error('The website monitor socket exceeds its Unix path bound.');
              }
              for (const root of ['/home/runner/work/KeyLoad/KeyLoad/website', '/' + 'é'.repeat(30)]) {
                let rejected = false; try { executionPipeDirectory(root); } catch { rejected = true; }
                if (!rejected) throw new Error('An oversized Unix monitor socket path was accepted.');
              }
            }
            for (const bad of [[], ['--KeyLoadTests:Suite=unknown'], ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Suite=cluster'],
              ['--KeyLoadTests:Suite=unit-scalar'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Execution:MaximumParallelTests=51'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Execution:MaximumParallelTests=0'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Execution:MaximumParallelTests=1.5'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Execution:MaximumParallelTests=invalid'],
              ['--KeyLoadTests:Suite=cluster','--KeyLoadTests:Filter=/*/*/HeavyDocumentLoadClusterTests/*'],
              ['--KeyLoadTests:Suite=unit','--KeyLoadTests:Unknown=true']]) {
              let rejected = false; try { runtimeSelection(bad, {}); } catch { rejected = true; }
              if (!rejected) throw new Error('Invalid execution test selection was accepted.');
            }
            const heavyArgs = ['--KeyLoadTests:Suite=cluster', '--KeyLoadTests:Filter=/*/*/HeavyDocumentLoadClusterTests/*', '--KeyLoadTests:HeavyLoad:Enabled=true'];
            const heavy = runtimeSelection(heavyArgs, {});
            if (heavy.args[heavy.args.indexOf('--maximum-parallel-tests') + 1] !== '1'
              || !runtimeSelection(['--KeyLoadTests:Suite=cluster'], {}).args.includes('/*/*/*/*[Category!=HeavyLoad]')) {
              throw new Error('Exclusive heavy load selection was not preserved.');
            }
            for (const extra of [['--KeyLoadTests:Execution:MaximumParallelTests=20'], ['--KeyLoadTests:CoverageSettings=settings.xml']]) {
              let rejected = false; try { runtimeSelection([...heavyArgs, ...extra], {}); } catch { rejected = true; }
              if (!rejected) throw new Error('Heavy load was accepted as an ordinary or coverage run.');
            }
            """ + SimdSelectionProgram.Source + """
            const selectedArgs = Object.freeze(['--KeyLoadTests:Suite='+suite, '--KeyLoadTests:Filter=/*/*/ActualCase/*',
              '--KeyLoadTests:ReportTrx=true', '--KeyLoadTests:CoverageSettings=settings.xml',
              '--KeyLoadTests:CoverageOutput=coverage.xml']);
            const inherited = Object.freeze({ GITHUB_SHA: 'original-revision',
              TMPDIR: 'inherited-scratch', TESTINGPLATFORM_PIPE_DIRECTORY: 'inherited-pipes' });
            const originalArgs = JSON.stringify(selectedArgs);
            const selected = runtimeSelection(selectedArgs, inherited);
            if (JSON.stringify(selectedArgs) !== originalArgs
              || JSON.stringify(runtimeSelection(selectedArgs, inherited)) !== JSON.stringify(selected)) {
              throw new Error('Execution selection changed its arguments or healthy result.');
            }
            const explicit20 = runtimeSelection(['--KeyLoadTests:Suite='+suite,
              '--KeyLoadTests:Execution:MaximumParallelTests=20'], {});
            const tuned50 = runtimeSelection(['--KeyLoadTests:Suite='+suite,
              '--KeyLoadTests:Execution:MaximumParallelTests=50'], {});
            process.stdout.write(JSON.stringify({ selected, explicit20, tuned50 }));
            """;
}
