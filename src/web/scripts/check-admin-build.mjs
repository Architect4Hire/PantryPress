// The admin app's build, checked as the gateway will check it before it serves it (PPL.3): a document for every public
// path and the browser-rendered one, and no inline script or inline event handler in any of them, since the gateway's
// policy is script-src 'self' and it refuses to start on a build that breaks it. Run after `ng build pantrypress-admin`
// so the failure is the build's, not a deployment's. The rule mirrors AdminApp.InlineScriptIn in the gateway.
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const web = join(import.meta.dirname, '..');
const root = join(web, 'dist', 'pantrypress-admin', 'browser');
const source = readFileSync(
  join(web, 'projects', 'pantrypress-admin', 'src', 'app', 'public-paths.ts'),
  'utf8',
);

const literal = /export const PUBLIC_PATHS = \[(?<items>[^\]]*)\]/s.exec(source);
if (!literal) {
  fail('PUBLIC_PATHS is not a plain array literal in public-paths.ts.');
}
const paths = [...literal.groups.items.matchAll(/'(?<path>[^']*)'/g)].map((m) => m.groups.path);

const documents = [
  ...paths.map((path) => (path === '' ? 'index.html' : `${path}/index.html`)),
  'index.csr.html',
];
const dataTypes = new Set(['application/json', 'application/ld+json']);
const handler =
  /<[a-z][a-z0-9-]*(?:[\s/]+[^\s=>"'/]+(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s"'>]+))?)*?[\s/]+on[a-z]+\s*=/i;

const problems = [];
for (const document of documents) {
  const file = join(root, document);
  if (!existsSync(file)) {
    problems.push(`${document} is missing.`);
    continue;
  }
  const html = readFileSync(file, 'utf8');
  for (const script of html.matchAll(/<script\b(?<attributes>[^>]*)>/gi)) {
    const attributes = script.groups.attributes;
    const type = /\stype\s*=\s*["']?(?<type>[^"'\s>]+)/i.exec(attributes)?.groups.type;
    if (!/\ssrc\s*=/i.test(attributes) && !dataTypes.has(type?.toLowerCase() ?? '')) {
      problems.push(`${document} carries inline script: ${script[0]}`);
    }
  }
  const inline = handler.exec(html);
  if (inline) {
    problems.push(`${document} carries an inline event handler: ${inline[0]}`);
  }
}

if (problems.length > 0) {
  fail(
    [
      "The admin build would not be served: the gateway's policy is script-src 'self' (PPL.3).",
      ...problems,
    ].join('\n  '),
  );
}
console.log(`Admin build checked: ${documents.length} documents, no inline script.`);

function fail(message) {
  console.error(message);
  process.exit(1);
}
