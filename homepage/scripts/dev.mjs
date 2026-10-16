import { spawn, spawnSync } from "node:child_process";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const composeFile = join(root, "backend", "docker-compose.yml");

function readEnvFile(path) {
  if (!existsSync(path)) return {};
  return Object.fromEntries(readFileSync(path, "utf8")
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line && !line.startsWith("#") && line.includes("="))
    .map((line) => {
      const separator = line.indexOf("=");
      return [line.slice(0, separator).trim(), line.slice(separator + 1).trim().replace(/^['"]|['"]$/g, "")];
    }));
}

function run(command, args, options = {}) {
  const result = spawnSync(command, args, { cwd: root, stdio: "inherit", ...options });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`${command} exited with code ${result.status}.`);
}

async function waitForHealth(url, timeoutMs = 180000) {
  const startedAt = Date.now();
  while (Date.now() - startedAt < timeoutMs) {
    try {
      const response = await fetch(url, { cache: "no-store" });
      if (response.ok) return;
    } catch {
      // The stack is still starting; retry below.
    }
    await new Promise((resolve) => setTimeout(resolve, 2000));
  }
  throw new Error(`Timed out waiting for ${url}.`);
}

function openBrowser(url) {
  if (process.platform === "win32") {
    spawn("powershell.exe", ["-NoProfile", "-Command", `Start-Process '${url.replaceAll("'", "''")}'`], {
      cwd: root,
      detached: true,
      stdio: "ignore"
    }).unref();
  } else if (process.platform === "darwin") {
    spawn("open", [url], { detached: true, stdio: "ignore" }).unref();
  } else {
    spawn("xdg-open", [url], { detached: true, stdio: "ignore" }).unref();
  }
}

const rootEnv = { ...readEnvFile(join(root, ".env")), ...process.env };
const apiBaseUrl = rootEnv.UMAT_API_BASE_URL || "http://localhost:8080";
const frontendUrl = rootEnv.UMAT_FRONTEND_URL || "http://localhost:8080/homepage.html";
const requiredBackendEnv = [
  "POSTGRES_DB", "POSTGRES_USER", "POSTGRES_PASSWORD", "REDIS_PASSWORD",
  "ANONYMOUS_TRACKING_PEPPER", "FRONTEND_URL"
];
const backendEnvPath = join(root, "backend", ".env");
const backendEnv = readEnvFile(backendEnvPath);
const missing = requiredBackendEnv.filter((name) => !backendEnv[name]);

if (!existsSync(backendEnvPath) || missing.length) {
  throw new Error(`backend/.env is missing required values: ${missing.join(", ") || "file not found"}.`);
}

writeFileSync(join(root, "runtime-config.json"), `${JSON.stringify({ apiBaseUrl }, null, 2)}\n`);

try {
  console.log("Building the backend API...");
  run("dotnet", [
    "publish",
    join(root, "backend", "src", "ComplaintSystem.Api", "ComplaintSystem.Api.csproj"),
    "--configuration", "Release",
    "--output", join(root, "backend", "docker-publish")
  ]);
  console.log("Starting PostgreSQL, Redis, API, frontend, and local database viewer...");
  run("docker", ["compose", "--file", composeFile, "up", "--detach", "--build"]);
  console.log("Waiting for the backend and its dependencies...");
  await waitForHealth(`${apiBaseUrl}/api/health`);
  await waitForHealth(rootEnv.UMAT_DATABASE_HEALTH_URL || `${apiBaseUrl}/api/database/health`);
  await waitForHealth(rootEnv.UMAT_AUTH_HEALTH_URL || `${apiBaseUrl}/api/auth/health`);
  console.log(`UMaT Complaint Management is ready: ${frontendUrl}`);
  openBrowser(frontendUrl);
} catch (error) {
  console.error(`\nStartup failed: ${error.message}`);
  console.error("\nRecent service logs:");
  spawnSync("docker", ["compose", "--file", composeFile, "logs", "--tail", "80", "api", "nginx", "postgres", "redis"], {
    cwd: root,
    stdio: "inherit"
  });
  process.exitCode = 1;
}
