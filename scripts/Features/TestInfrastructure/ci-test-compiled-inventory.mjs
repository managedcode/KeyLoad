// Compare actual TUnit discovery with source methods, including every class selected by explicit CI lanes.
export function checkCompiledInventory(entry, census) {
  if (census.schemaVersion !== 1 || !Array.isArray(census.tests) || census.tests.length === 0) {
    throw new Error('Compiled TUnit discovery must contain the original nonempty test list.');
  }
  const expected = new Set(entry.classes.flatMap(type => type.methods.map(method =>
    `${type.namespace}.${type.name}.${method.name}`)));
  const actual = new Set();
  const identities = new Set();
  const errors = [];
  for (const test of census.tests) {
    const type = test.type;
    if (!type || !type.assemblyFullName?.startsWith(`${entry.suite.assembly},`) ||
        !type.namespace || !type.typeName || !type.methodName || !test.uid) {
      throw new Error('Compiled discovery must contain only complete identities from the selected assembly.');
    }
    if (identities.has(test.uid)) errors.push(`Compiled test identity is repeated: ${test.uid}.`);
    identities.add(test.uid);
    // TUnit includes constructor data-source types in its displayed type name.
    const name = type.typeName.split('(', 1)[0];
    actual.add(`${type.namespace}.${name}.${type.methodName}`);
  }
  for (const method of actual) {
    if (!expected.has(method)) errors.push(`Compiled test method is missing from the source inventory: ${method}.`);
  }
  for (const method of expected) {
    if (!actual.has(method)) errors.push(`Source test method is absent from compiled discovery: ${method}.`);
  }
  return { errors, cases: identities.size, methods: actual.size };
}
