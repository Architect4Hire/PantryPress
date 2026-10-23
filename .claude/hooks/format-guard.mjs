// PostToolUse: format the file that was just written. Fast, best effort, never fails the edit.
import { execFileSync } from "node:child_process";
import { existsSync } from "node:fs";
import { join } from "node:path";
import { readInput, filePath } from "./lib.mjs";

const input = await readInput();
const path = filePath(input);
if (!path || !existsSync(path)) process.exit(0);

const root = (process.env.CLAUDE_PROJECT_DIR || process.cwd()).replace(/\\/g, "/");
const run = (cmd, args, cwd) => {
  try { execFileSync(cmd, args, { cwd, stdio: "ignore", timeout: 45000, shell: process.platform === "win32" }); }
  catch { /* formatting is best effort */ }
};

if (/\/web\/.*\.(ts|html|scss|css|json|md)$/.test(path)) {
  const web = join(root, "src", "web");
  const prettier = join(web, "node_modules", ".bin", process.platform === "win32" ? "prettier.cmd" : "prettier");
  if (existsSync(prettier)) run(prettier, ["--write", path], web);
} else if (/\.cs$/.test(path) && existsSync(join(root, "PantryPress.slnx"))) {
  run("dotnet", ["format", "whitespace", join(root, "PantryPress.slnx"), "--include", path], root);
}
process.exit(0);
