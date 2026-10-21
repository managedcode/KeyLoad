const NON_CODE = /\/\/[^\r\n]*|\/\*[\s\S]*?\*\/|\$*("{3,})[\s\S]*?\1|(?:\$@|@\$|@)"(?:[^"]|"")*"|\$?"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'/g;
const TEMPORARY_API = /\b(?:Path\s*\.\s*(?:GetTempPath|GetTempFileName)|Directory\s*\.\s*CreateTempSubdirectory)\s*\(/g;
const SYSTEM_ROOT = /^\/(?:tmp|var|private\/(?:tmp|var))(?:\/|$)/;
const PATH_ARGUMENT = /\b(?:(?:Path\s*\.\s*(?:Combine|Join)|Directory\s*\.\s*CreateDirectory|File\s*\.\s*(?:Create|CreateText|Open|OpenWrite|WriteAll(?:Text|Bytes|Lines)(?:Async)?))|new\s+(?:FileStream|DirectoryInfo|FileInfo))\s*\(\s*$/;

function lineAt(source, index) {
  return source.slice(0, index).split('\n').length;
}

export function testOutputPathDiagnostics(repositoryPath, source) {
  if (!/^(?:tests|benchmarks)\//.test(repositoryPath)) return [];
  const literals = [];
  const code = source.replace(NON_CODE, (text, delimiter, index) => {
    if (text.startsWith('"') || text.startsWith('@"') || text.startsWith('$')) {
      literals.push({ index, value: text.replace(/^[$@]*"+|"+$/g, '') });
    }
    return text.replace(/[^\r\n]/g, ' ');
  });
  const violations = [...code.matchAll(TEMPORARY_API)].map(match => match.index);
  for (const literal of literals) {
    if (SYSTEM_ROOT.test(literal.value) && PATH_ARGUMENT.test(code.slice(0, literal.index))) {
      violations.push(literal.index);
    }
  }
  return violations.sort((left, right) => left - right).map(index =>
    `Test-owned files must use the executing assembly output directory: ${repositoryPath}:${lineAt(source, index)}`);
}
