#!/usr/bin/env node
// Completes the DSM first-run browser wizard on a fresh vdsm/virtual-dsm instance:
// fills the device/account form, skips optional Synology Account / QuickConnect and
// analytics prompts, waits for the DSM desktop, sets the admin password to the
// configured value, enables SSH (so the Download Station install step can use
// synopkg), and suppresses first-run promo/upsell dialogs. Adapted from the
// DatadogSynology project.
import { createRequire } from "node:module";
import { mkdirSync } from "node:fs";
import { resolve } from "node:path";

const require = createRequire(import.meta.url);

function env(name, fallback = undefined) {
  const value = process.env[name];
  return value === undefined || value === "" ? fallback : value;
}

function envBool(name, fallback) {
  const value = env(name);
  if (value === undefined) return fallback;
  return ["1", "true", "yes", "y", "on"].includes(value.toLowerCase());
}

function envInt(name, fallback) {
  const value = env(name);
  return value === undefined ? fallback : Number.parseInt(value, 10);
}

function loadPlaywright() {
  const candidates = [
    "playwright",
    resolve(".e2e/web-setup-node/node_modules/playwright"),
  ];
  for (const candidate of candidates) {
    try {
      return require(candidate);
    } catch {
      // Try the next location.
    }
  }
  throw new Error(
    "Playwright is not installed. Run: npm --prefix .e2e/web-setup-node install playwright@1.60.0",
  );
}

function requiredEnv(name) {
  const value = env(name);
  if (!value) {
    throw new Error(`${name} is required.`);
  }
  return value;
}

function log(message) {
  console.log(`==> ${message}`);
}

async function launchChromium(chromium, headless) {
  const channel = env("VDSM_WEB_CHANNEL", "chrome");
  if (channel) {
    try {
      return await chromium.launch({ channel, headless });
    } catch (error) {
      console.warn(`Unable to launch ${channel}; falling back to Playwright Chromium.`);
      console.warn(error.message.split("\n")[0]);
    }
  }
  return chromium.launch({ headless });
}

async function pageText(page) {
  return page.locator("body").innerText({ timeout: 5000 }).catch(() => "");
}

async function screenshot(page, name) {
  const dir = env("VDSM_WEB_ARTIFACT_DIR", ".e2e/vdsm-web");
  mkdirSync(dir, { recursive: true });
  const path = resolve(dir, `${String(Date.now())}-${name}.png`);
  await page.screenshot({ path, fullPage: true }).catch(() => {});
  return path;
}

async function clickButton(page, names, options = {}) {
  for (const name of names) {
    const locator = page.getByRole("button", { name, exact: true });
    if ((await locator.count()) === 1 && (await locator.isVisible())) {
      await locator.click(options);
      return name;
    }
  }

  for (const name of names) {
    const locator = page.locator("button").filter({ hasText: name });
    const count = await locator.count();
    if (count > 0) {
      await locator.first().click(options);
      return name;
    }
  }

  return null;
}

async function clickText(page, names) {
  for (const name of names) {
    const locator = page.getByText(name, { exact: true });
    const count = await locator.count();
    if (count > 0 && (await locator.first().isVisible())) {
      await locator.first().click();
      return name;
    }
  }
  return null;
}

async function fillFirstRunAccount(page, config) {
  const device = page.locator('input[name="device_name"]');
  if ((await device.count()) === 0) return false;

  log("Filling DSM first-run account form");
  await device.fill(config.deviceName);
  await page.locator('input[name="nas_account"]').fill(config.username);
  const passwordFields = page.locator('input[type="password"]');
  if ((await passwordFields.count()) < 2) {
    throw new Error("Expected two password fields on the DSM setup form.");
  }
  await passwordFields.nth(0).fill(config.password);
  await passwordFields.nth(1).fill(config.password);

  const webAssistant = page.locator('input[type="checkbox"]').first();
  if ((await webAssistant.count()) === 1) {
    const checked = await webAssistant.isChecked();
    if (checked !== config.webAssistant) {
      await webAssistant.setChecked(config.webAssistant, { force: true });
    }
  }

  const clicked = await clickButton(page, ["Next"]);
  if (!clicked) throw new Error("Could not find Next button on the account form.");
  await page.waitForTimeout(1000);
  await clickButton(page, ["Yes", "OK", "Continue"]);
  return true;
}

async function handleWizardStep(page, config) {
  const text = await pageText(page);

  // Fresh data disk: DSM shows an install/deployment wizard first.
  if (/Install|Set up your NAS|Synology DiskStation Manager|Install now|Quick Install/i.test(text) && /Install/i.test(text)) {
    const clicked = await clickButton(page, ["Quick Install", "Install", "Start"]);
    if (clicked) {
      log(`DSM disk install step — clicked "${clicked}" (waiting up to 5 min for install)`);
      await page.waitForTimeout(60000);
      return true;
    }
  }

  if (/Welcome to DSM 7|Next-generation data management begins here/.test(text)) {
    log("Starting DSM welcome wizard");
    await clickButton(page, ["Start"]);
    await page.waitForTimeout(1000);
    return true;
  }

  if (/Get started with your VirtualDSM|Administrator account|Device name/.test(text)) {
    return fillFirstRunAccount(page, config);
  }

  if (/DSM Update|update settings|important updates|Install important updates|Notify me/.test(text)) {
    log("Accepting default DSM update settings");
    await clickButton(page, ["Next", "Apply"]);
    await page.waitForTimeout(1000);
    return true;
  }

  if (/Synology Account|QuickConnect|sign in to or register/.test(text)) {
    log("Skipping Synology Account / QuickConnect setup");
    const skipped = await clickButton(page, ["Skip", "Skip this step", "Later"]) || await clickText(page, ["Skip this step"]);
    if (!skipped) {
      await clickButton(page, ["Next"]);
    }
    await page.waitForTimeout(1000);
    await clickButton(page, ["Yes", "OK", "Continue"]);
    return true;
  }

  if (/privacy|Device Analytics|Share my location|data collection|service data/i.test(text)) {
    log("Leaving optional analytics/data-sharing disabled");
    await clickButton(page, ["Next", "Apply", "Skip"]);
    await page.waitForTimeout(1000);
    return true;
  }

  if (/All set|You're all set|Done|Finish|Start managing/.test(text)) {
    log("Finishing DSM wizard");
    await clickButton(page, ["Done", "Finish", "Start", "Go"]);
    await page.waitForTimeout(1500);
    return true;
  }

  const generic = await clickButton(page, ["Next", "Apply", "Done", "Finish", "OK"]);
  if (generic) {
    log(`Clicked ${generic}`);
    await page.waitForTimeout(1000);
    return true;
  }

  return false;
}

async function loginIfNeeded(page, config) {
  const text = await pageText(page);
  if (/Get started with your VirtualDSM|Administrator account|Device name/.test(text)) {
    return false;
  }

  const userInputs = page.locator('input:visible[name="username"], input:visible[name="account"], input:visible[placeholder*="Account"], input:visible[placeholder*="Username"]');
  if (!/\b(Sign in|Log in|Login)\b/i.test(text) || (await userInputs.count()) === 0) {
    return false;
  }

  log("Logging in to DSM");
  await userInputs.first().fill(config.username);
  const loginBtn = page.locator(".login-btn").first();
  if ((await loginBtn.count()) > 0) {
    await loginBtn.click();
  } else {
    await userInputs.first().press("Enter");
  }
  await page.locator("input[type='password']").first().waitFor({ state: "visible", timeout: 8000 }).catch(() => {});
  const passwordInputs = page.locator('input[type="password"]');
  if ((await passwordInputs.count()) > 0) {
    await passwordInputs.first().fill(config.password);
    await passwordInputs.first().press("Enter");
  }
  await page.waitForTimeout(3000);
  return true;
}

// When DSM boots in factory-default state (admin / empty password), the browser
// auto-authenticates for local access. Set admin's password so SSH password auth works.
async function configureAdminPassword(page, config) {
  const result = await page.evaluate(async ({ password }) => {
    const params = new URLSearchParams({ api: "SYNO.Core.User", version: "1", method: "set", name: "admin", password });
    const resp = await fetch("/webapi/entry.cgi", { method: "POST", credentials: "same-origin", body: params });
    return resp.json();
  }, { password: config.password });

  if (result?.success) {
    log("Admin password set to configured value ✓");
  } else {
    log(`Admin password configuration skipped or failed: ${JSON.stringify(result?.error ?? result)}`);
  }
}

async function enableSsh(page, config) {
  if (!config.enableSsh) return;
  log("Enabling SSH through DSM Web API");
  const result = await page.evaluate(async ({ sshPort }) => {
    const params = new URLSearchParams({
      api: "SYNO.Core.Terminal",
      version: "3",
      method: "set",
      enable_ssh: "true",
      ssh_port: String(sshPort),
      enable_telnet: "false",
    });
    const response = await fetch(`/webapi/entry.cgi?${params.toString()}`, {
      credentials: "same-origin",
      method: "GET",
    });
    return response.json();
  }, { sshPort: config.sshPort });

  if (!result?.success) {
    // Code 119 = insufficient privilege for this session type. If SSH is already reachable,
    // treat this as non-fatal.
    if (result?.error?.code === 119) {
      log("SSH enable returned insufficient-privilege — checking if SSH is already active...");
      const sshAlreadyUp = await waitForTcp("127.0.0.1", config.mappedSshPort, 5000);
      if (sshAlreadyUp) {
        log("SSH is already reachable — skipping enable");
        return;
      }
    }
    throw new Error(`Failed to enable SSH: ${JSON.stringify(result)}`);
  }
}

async function waitForTcp(host, port, timeoutMs) {
  const net = await import("node:net");
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const ok = await new Promise((resolveOk) => {
      const socket = net.createConnection({ host, port, timeout: 2000 }, () => {
        socket.destroy();
        resolveOk(true);
      });
      socket.on("timeout", () => {
        socket.destroy();
        resolveOk(false);
      });
      socket.on("error", () => resolveOk(false));
    });
    if (ok) return true;
    await new Promise((resolveWait) => setTimeout(resolveWait, 2000));
  }
  return false;
}

// Suppress DSM's first-run promo/upsell dialogs at the source so they cannot interrupt
// later steps. The PromotionApp gates the whole flow on `last_promote_patch`: once it
// equals the current DSM build, every promo page is skipped.
async function suppressPromotions(page) {
  log("Suppressing DSM promo/upsell dialogs");

  const result = await page.evaluate(async () => {
    const out = {};
    const call = async (api, version, method, extra = {}) => {
      const params = new URLSearchParams({ api, version: String(version), method, ...extra });
      const resp = await fetch("/webapi/entry.cgi", { method: "POST", credentials: "same-origin", body: params });
      return resp.json();
    };
    try {
      const sys = await call("SYNO.Core.System", 1, "info");
      const fw = (sys && sys.data && sys.data.firmware_ver) || "";
      const m = fw.match(/(\d+)\.(\d+)\.(\d+)\D+(\d+)/);
      const patch = m ? { major: m[1], minor: m[2], micro: m[3], nano: "0", buildnumber: m[4] } : null;

      if (window.SYNO?.SDS?.UserSettings?.setProperty) {
        if (patch) SYNO.SDS.UserSettings.setProperty("SYNO.SDS.App.PromotionApp", "last_promote_patch", patch);
        for (const k of ["not_done_ss", "not_done_ai", "not_done_udc", "not_done_ew", "not_done_ewevent"]) {
          SYNO.SDS.UserSettings.setProperty("SYNO.SDS.App.PromotionApp", k, false);
        }
        out.userSettings = patch ? `set (build ${patch.buildnumber})` : "set (build not parsed)";
      } else {
        out.userSettings = "no-usersettings-api";
      }
      const ns = await call("SYNO.Core.Promotion.PreInstall", 1, "set_never_show");
      out.preInstallNeverShow = !!(ns && ns.success);
    } catch (e) {
      out.err = e.message;
    }
    return out;
  }).catch((e) => ({ evaluateFailed: String(e) }));
  log(`Promo suppression: ${JSON.stringify(result)}`);

  await page.waitForTimeout(2500);
}

function parseArgs() {
  const url = env("VDSM_URL", `http://127.0.0.1:${env("VDSM_DSM_PORT", "55100")}/`);
  return {
    url,
    username: requiredEnv("VDSM_SETUP_USER"),
    password: requiredEnv("VDSM_SETUP_PASSWORD"),
    deviceName: env("VDSM_SETUP_DEVICE", "VirtualDSM"),
    webAssistant: envBool("VDSM_SETUP_WEB_ASSISTANT", false),
    enableSsh: envBool("VDSM_SETUP_ENABLE_SSH", true),
    sshPort: envInt("VDSM_SETUP_SSH_PORT", 22),
    mappedSshPort: envInt("VDSM_SSH_PORT", 55122),
    headless: envBool("VDSM_WEB_HEADLESS", true),
    timeoutMs: envInt("VDSM_WEB_TIMEOUT", 300000),
  };
}

// vdsm/virtual-dsm serves a boot/loading page on the DSM port within seconds of container
// start, well before the guest DSM is actually serving, so the first navigation can hit a
// transient reset/refusal while DSM finishes booting. Retry on transient network errors.
const TRANSIENT_NAV = /net::|ERR_|ECONN|ETIMEDOUT|EAI_AGAIN|socket hang up|Timeout \d+ms exceeded|NS_ERROR_/i;

async function gotoWithRetry(page, url, timeoutMs, navTimeout = 30000) {
  const deadline = Date.now() + timeoutMs;
  for (let attempt = 1; ; attempt++) {
    try {
      await page.goto(url, { waitUntil: "domcontentloaded", timeout: navTimeout });
      return;
    } catch (err) {
      const msg = String((err && err.message) || err);
      if (!TRANSIENT_NAV.test(msg) || Date.now() >= deadline) throw err;
      log(`  DSM not serving yet (attempt ${attempt}): ${msg.split("\n")[0]} — retrying in 3s`);
      await page.waitForTimeout(3000);
    }
  }
}

async function main() {
  const config = parseArgs();
  const { chromium } = loadPlaywright();
  const browser = await launchChromium(chromium, config.headless);
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });

  try {
    log(`Opening ${config.url}`);
    await gotoWithRetry(page, config.url, config.timeoutMs);

    const deadline = Date.now() + config.timeoutMs;
    let stableDesktopSeen = false;
    while (Date.now() < deadline) {
      const text = await pageText(page);
      if (/Package Center|Control Panel|File Station/.test(text) && !/Welcome to DSM 7|Get started with your VirtualDSM/.test(text)) {
        stableDesktopSeen = true;
        break;
      }
      const handled = await handleWizardStep(page, config);
      if (!handled) {
        await loginIfNeeded(page, config);
      }
      if (!handled) {
        await page.waitForTimeout(1500);
      }
    }

    if (!stableDesktopSeen) {
      const path = await screenshot(page, "setup-timeout");
      throw new Error(`DSM setup did not reach the desktop before timeout. Screenshot: ${path}`);
    }

    await configureAdminPassword(page, config);

    const langResult = await page.evaluate(async () => {
      const params = new URLSearchParams({ api: "SYNO.Core.PersonalSettings", version: "1", method: "set", language: "enu" });
      const resp = await fetch("/webapi/entry.cgi", { method: "POST", credentials: "same-origin", body: params });
      return resp.json();
    }).catch(() => null);
    if (langResult?.success) log("DSM language set to English ✓");

    await enableSsh(page, config);

    if (config.enableSsh) {
      const sshReady = await waitForTcp("127.0.0.1", config.mappedSshPort, 60000);
      if (!sshReady) {
        throw new Error(`SSH did not become reachable on 127.0.0.1:${config.mappedSshPort}`);
      }
      log("SSH is reachable ✓");
    }

    await suppressPromotions(page);

    log("DSM browser setup completed");
  } catch (error) {
    const path = await screenshot(page, "setup-error");
    console.error(`ERROR: ${error.message}`);
    console.error(`Screenshot: ${path}`);
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
}

main();
