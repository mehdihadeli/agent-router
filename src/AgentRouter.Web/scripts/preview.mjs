import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const project = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const workspace = resolve(project, "../..");
const temporary = resolve(workspace, ".testagent/ui-preview");
const cli = resolve(
  project,
  "../artifacts/bin/AgentRouter.Cli/debug/AgentRouter.Cli.dll",
);
if (!existsSync(resolve(dirname(cli), "ui/index.html"))) {
  throw new Error(
    "Build the Angular UI, then build AgentRouter.Cli before starting preview.",
  );
}
mkdirSync(temporary, { recursive: true });
const env = {
  ...process.env,
  AR_CONFIG_DIR: resolve(temporary, "data"),
  TEMP: temporary,
  TMP: temporary,
};
delete env.AR_SECRET_KEY;
const command = (...args) =>
  execFileSync("dotnet", [cli, ...args], {
    cwd: temporary,
    env,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
  }).trim();
console.log(command("ui", "--no-open", "--management-port", "0"));
console.log("Isolated preview. Press Ctrl+C to stop its management service.");
process.stdin.resume();
for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => {
    try {
      command("service", "stop");
    } finally {
      process.exit();
    }
  });
}
