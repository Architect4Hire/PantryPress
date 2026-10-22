// PreToolUse: refuse to write anything that looks like a real secret.
import { readInput, incomingText, filePath, block } from "./lib.mjs";

const input = await readInput();
const path = filePath(input);
const text = incomingText(input);
if (!text) process.exit(0);

const patterns = [
  ["Stripe secret key", /\b(sk|rk)_(live|test)_[A-Za-z0-9]{20,}/],
  ["Stripe webhook secret", /\bwhsec_[A-Za-z0-9]{20,}/],
  ["Azure storage account key", /AccountKey=[A-Za-z0-9+/]{40,}={0,2}/],
  ["Azure SAS signature", /[?&]sig=[A-Za-z0-9%+/]{30,}/],
  ["SQL connection string password", /(Password|Pwd)\s*=\s*(?!\s*[<{$])[^;"'\s]{8,}/i],
  ["Private key", /-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----/],
  ["AWS access key", /\bAKIA[0-9A-Z]{16}\b/],
  ["GitHub token", /\bgh[pousr]_[A-Za-z0-9]{36,}\b/],
  ["Google API key", /\bAIza[0-9A-Za-z_-]{35}\b/],
  ["PantryPress publishing key", /\bpp_live_[A-Za-z0-9]{24,}\b/],
  ["Azure Communication Services key", /endpoint=https:\/\/[^;]+;accesskey=[A-Za-z0-9+/]{40,}/i],
];

const hits = patterns.filter(([, re]) => re.test(text)).map(([name]) => name);
if (hits.length) {
  block(
    `Blocked: the content for ${path || "this file"} looks like it contains a secret (${hits.join(", ")}).\n` +
    "Use dotnet user-secrets, Aspire parameters, or Key Vault references instead. " +
    "If this is a deliberate fake for a test, make it obviously fake (for example sk_test_FAKE)."
  );
}
