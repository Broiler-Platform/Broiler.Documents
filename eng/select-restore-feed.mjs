import { appendFileSync, copyFileSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { readVersions } from './resolve-preview-version.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const nugetSource = 'https://api.nuget.org/v3/index.json';
export const feeds = ['nuget'];

// All Broiler.* packages come from NuGet.org.
export function readRequiredPackages(packagesProps) {
  const packages = [];
  for (const [element] of packagesProps.matchAll(/<PackageVersion\b[^>]*>/g)) {
    const id = /\bInclude="([^"]+)"/.exec(element)?.[1];
    const version = /\bVersion="([^"]+)"/.exec(element)?.[1];
    if (!id || !version) throw new Error(`Cannot read package reference: ${element}`);
    if (/^Broiler\./i.test(id)) packages.push({ id, version });
  }
  return packages;
}

export function missingPackages(packages, hosted) {
  return packages.filter(({ id, version }) =>
    !(hosted.get(id.toLowerCase()) ?? []).some(value => value.toLowerCase() === version.toLowerCase()));
}

export function chooseFeed(requested, packages, hostedByFeed) {
  if (requested !== 'auto' && requested !== 'nuget') {
    throw new Error(`Unknown feed '${requested}'; use auto or nuget.`);
  }
  const hosted = hostedByFeed['nuget'];
  if (hosted) {
    const missing = missingPackages(packages, hosted);
    if (missing.length) {
      throw new Error(`NuGet.org does not host every required package: ${missing.map(({ id, version }) => `${id} ${version}`).join(', ')}.`);
    }
  }
  return 'nuget';
}

async function readHosted(source, packages, headers, fetchImpl) {
  const hosted = new Map();
  await Promise.all(packages.map(async ({ id }) =>
    hosted.set(id.toLowerCase(), await readVersions(source, [id], headers, fetchImpl))));
  return hosted;
}

export async function selectFeed(requested, packages, env = {}, fetchImpl = fetch) {
  if (requested !== 'auto' && requested !== 'nuget') return chooseFeed(requested, packages, {});
  const hosted = await readHosted(nugetSource, packages, {}, fetchImpl);
  return chooseFeed(requested, packages, { nuget: hosted });
}

async function main() {
  const requested = process.env.FEED || 'auto';
  const packages = readRequiredPackages(readFileSync(resolve(root, 'Directory.Packages.props'), 'utf8'));
  const feed = packages.length ? await selectFeed(requested, packages, process.env)
    : (requested === 'auto' || requested === 'nuget') ? 'nuget' : chooseFeed(requested, packages, {});
  copyFileSync(resolve(root, 'eng/NuGet.nuget-org.config'), resolve(root, 'NuGet.config'));

  const listed = packages.map(({ id, version }) => `${id} ${version}`).join(', ') || 'none';
  console.log(`Restore feed: ${feed} (requested: ${requested}; Broiler packages: ${listed})`);
  if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `feed=${feed}\n`);
  if (process.env.GITHUB_STEP_SUMMARY) {
    appendFileSync(process.env.GITHUB_STEP_SUMMARY,
      `Restore feed: **${feed}** (requested: ${requested})\n\n${packages.map(({ id, version }) => `- ${id} ${version}`).join('\n')}\n`);
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
