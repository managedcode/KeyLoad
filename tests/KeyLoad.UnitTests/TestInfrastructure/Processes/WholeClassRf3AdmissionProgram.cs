namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class WholeClassRf3AdmissionProgram
{
    internal const string Source = """
        import assert from 'node:assert/strict';
        import { pathToFileURL } from 'node:url';
        const { nativeSelection } = await import(pathToFileURL(process.argv[1]).href);
        const filter = process.argv[2];
        const args = Object.freeze(['--KeyLoadTests:Suite=rf3', '--KeyLoadTests:Filter=' + filter,
          '--KeyLoadTests:LocalRf3Image:Enabled=true']);
        const inherited = Object.freeze({ PATH: process.env.PATH ?? '' });
        const originalArgs = JSON.stringify(args);
        const originalEnvironment = JSON.stringify(inherited);
        const selected = nativeSelection(args, inherited);
        const invalid = 'Invalid native local RF3 image selection.';
        const reject = (input, environment = inherited, message = invalid) =>
        {
          assert.throws(() => nativeSelection(input, environment), { message });
          assert.equal(JSON.stringify(nativeSelection(args, inherited)), baseline);
        };
        const baseline = JSON.stringify(selected);
        const badFilters = [filter + ' ', filter.replace('/*', '/Unknown'),
          filter.replace('/*/*/', '/*/*/Unknown'), '/*/*/UnknownReleaseCase/*',
          filter + '|/*/*/UnknownReleaseCase/*', filter.replace('/*/*/', '/*/*/*'),
          filter.slice(0, -1) + 'UnknownMethod'];
        for (const value of badFilters) reject([args[0], '--KeyLoadTests:Filter=' + value, args[2]]);
        if (filter.includes('('))
        {
          const classes = filter.slice('/*/*/('.length, -')/*'.length).split('|');
          [classes[0], classes[1]] = [classes[1], classes[0]];
          reject([args[0], '--KeyLoadTests:Filter=/*/*/(' + classes.join('|') + ')/*', args[2]]);
        }
        for (const key of ['KEYLOAD_IMAGE_RECEIPT', 'GITHUB_SHA', 'GITHUB_ACTIONS',
          'KEYLOAD_IMAGE_PROVENANCE', 'KEYLOAD_LOCAL_IMAGE_RECEIPT', 'KeyLoad__ContainerImages__Server',
          'KEYLOAD_LOCAL_RF3_IMAGE_CHILD', 'KEYLOAD_TUNIT_LOCAL_RF3_IMAGE_ARGUMENTS',
          'KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS']) reject(args, { ...inherited, [key]: 'foreign' });
        for (const option of ['NativeCoverage:ServerMode=rf3-original-node-v1', 'CoverageSettings=foreign',
          'ScaleProfile=scaled-100k-c16', 'Unknown=true']) reject([...args, '--KeyLoadTests:' + option]);
        reject([...args, '--KeyLoadTests:FilterUid=foreign'], inherited, 'Invalid native focused text UID selection.');
        reject(['--KeyLoadTests:Suite=unit', args[1], args[2]]);
        assert.equal(JSON.stringify(args), originalArgs);
        assert.equal(JSON.stringify(inherited), originalEnvironment);
        assert.equal(JSON.stringify(nativeSelection(args, inherited)), baseline);
        assert.equal(selected.args[selected.args.indexOf('--treenode-filter') + 1], filter);
        assert.ok(!selected.args.includes('--filter-uid'));
        assert.equal(selected.args[selected.args.indexOf('--maximum-parallel-tests') + 1], '50');
        assert.deepEqual(JSON.parse(selected.environment.KEYLOAD_TUNIT_LOCAL_RF3_IMAGE_ARGUMENTS), args);
        for (const key of ['KEYLOAD_IMAGE_PROVENANCE', 'KEYLOAD_LOCAL_IMAGE_RECEIPT',
          'KeyLoad__ContainerImages__Server', 'KEYLOAD_LOCAL_RF3_IMAGE_CHILD', 'GITHUB_SHA', 'GITHUB_ACTIONS'])
          assert.ok(!Object.hasOwn(selected.environment, key));
        process.stdout.write(JSON.stringify({ filter, refused: badFilters.length + (filter.includes('(') ? 1 : 0) + 15,
          inputPreserved: true, exactRepair: true, nativeFilterRetained: true, noUid: true }));
        """;
}
