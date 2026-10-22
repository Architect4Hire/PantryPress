// Shared helpers for PantryPress Claude Code hooks. Node 18+, no dependencies, cross-platform.
export async function readInput() {
  let raw = "";
  for await (const chunk of process.stdin) raw += chunk;
  try { return JSON.parse(raw || "{}"); } catch { return {}; }
}

// The text a Write, Edit, or MultiEdit call is about to put into a file.
export function incomingText(input) {
  const t = input.tool_input || {};
  if (typeof t.content === "string") return t.content;
  if (typeof t.new_string === "string") return t.new_string;
  if (Array.isArray(t.edits)) return t.edits.map((e) => e.new_string || "").join("\n");
  return "";
}

export function filePath(input) {
  return ((input.tool_input || {}).file_path || "").replace(/\\/g, "/");
}

// Exit code 2 blocks the tool call and shows the message to Claude.
export function block(message) {
  process.stderr.write(message + "\n");
  process.exit(2);
}
