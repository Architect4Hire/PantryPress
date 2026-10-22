// PreToolUse: block edits that break PantryPress invariants. Conservative on purpose; reviewers catch the rest.
import { readInput, incomingText, filePath, block } from "./lib.mjs";

const input = await readInput();
const path = filePath(input);
const text = incomingText(input);
if (!text || !path) process.exit(0);

const problems = [];

// PP-D12: no AI anywhere in PantryPress.
if (/(\.csproj|package\.json|Directory\.Packages\.props)$/.test(path)) {
  const ai = /(Azure\.AI\.OpenAI|"openai"|Include="OpenAI"|Anthropic|@anthropic-ai|Microsoft\.SemanticKernel|Microsoft\.Extensions\.AI|@google\/generative-ai|langchain)/i;
  if (ai.test(text)) problems.push("PP-D12: AI SDKs are not allowed in PantryPress.");
}

// Tenancy: IgnoreQueryFilters only in platform operator code or tests.
if (/\.cs$/.test(path) && /IgnoreQueryFilters\s*\(/.test(text)
    && !/\/Modules\/Platform\//.test(path) && !/Tests?\//.test(path)) {
  problems.push("Tenancy: IgnoreQueryFilters() is only allowed in Modules/Platform/ operator code (see .claude/rules/tenancy.md).");
}

// No interpolated raw SQL.
if (/\.cs$/.test(path) && /(FromSqlRaw|ExecuteSqlRaw)\s*\(\s*\$"/.test(text)) {
  problems.push("Persistence: interpolated raw SQL is not allowed. Use FromSql with parameters in a repository.");
}

// Public site: SSR safety and no direct data access.
if (/web\/projects\/pantrypress-site\//.test(path) && /\.ts$/.test(path)) {
  if (/\blocalStorage\b|\bsessionStorage\b/.test(text)) {
    problems.push("SSR: the public site must not use localStorage or sessionStorage (rendering.md).");
  }
}

// Public pages never talk to SQL.
if (/src\/PantryPress\.Site\//.test(path) && /\.cs$/.test(path) && /PantryPressDbContext/.test(text)
    && !/(Write|Commands?|Facade)/.test(path)) {
  problems.push("ARCH-013: PantryPress.Site read paths must not use the DbContext; serve page data.");
}

if (problems.length) block("Blocked by PantryPress invariant guard:\n- " + problems.join("\n- "));
