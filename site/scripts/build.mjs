import { buildSite } from '../Features/BenchmarkComparisons/build-site.mjs';

try {
  const result = await buildSite(process.argv.slice(2));
  console.log(JSON.stringify(result));
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
