import { execFileSync } from "node:child_process";
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const project = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const temporary = mkdtempSync(resolve(tmpdir(), "ar-openapi-"));
const cli = resolve(
  project,
  "../artifacts/bin/AgentRouter.Cli/debug/AgentRouter.Cli.dll",
);
const env = { ...process.env, AR_CONFIG_DIR: resolve(temporary, "data") };
delete env.AR_SECRET_KEY;
const command = (...args) =>
  execFileSync("dotnet", [cli, ...args], {
    cwd: temporary,
    env,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
  }).trim();
let started = false;
try {
  const output = command("ui", "--no-open", "--management-port", "0");
  started = true;
  const url = output.match(/Management UI: (http:\/\/127\.0\.0\.1:\d+)/)?.[1];
  if (!url) throw new Error("Isolated management did not report its URL.");
  const token = command("config", "token", "--managed");
  const response = await fetch(
    `${url}/management/v1/openapi/management-v1.json`,
    { headers: { Authorization: `Bearer ${token}` } },
  );
  if (!response.ok) throw new Error("Management OpenAPI export failed.");
  const schema = await response.json();
  mkdirSync(resolve(project, "src/generated"), { recursive: true });
  writeFileSync(
    resolve(project, "src/generated/management.openapi.json"),
    JSON.stringify(schema, null, 2) + "\n",
  );
  console.log("Generated management OpenAPI using isolated temporary storage.");
} finally {
  if (started) command("service", "stop");
  rmSync(temporary, { recursive: true, force: true });
}
