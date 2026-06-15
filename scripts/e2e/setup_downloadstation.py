#!/usr/bin/env python3
"""Install and start Download Station on a freshly set-up vDSM, and create a shared
folder for the E2E test to download into.

Runs over SSH (sshpass + ssh, like the DatadogSynology harness). Assumes the DSM
first-run wizard is done and SSH is enabled (see setup_vdsm_web.mjs). Everything is
done in a single SSH session so DSM's Auto Block isn't tripped by rapid logins.
"""
from __future__ import annotations

import os
import shlex
import subprocess
import sys
import time


def env(name: str, default: str | None = None) -> str | None:
    value = os.environ.get(name)
    return default if value in (None, "") else value


class SetupError(RuntimeError):
    pass


HOST = "127.0.0.1"
SSH_PORT = int(env("VDSM_SSH_PORT", "55122"))
DSM_PORT = env("VDSM_DSM_PORT", "55100")
USER = env("VDSM_SETUP_USER") or env("RDT_E2E_DSM_USER") or "admin"
PASSWORD = env("VDSM_SETUP_PASSWORD") or env("RDT_E2E_DSM_PASS")
SHARE = env("RDT_E2E_SHARE", "Downloads")

# DSM tools aren't on a non-interactive SSH session's PATH; pin their real locations.
SYNO_PATH = "/usr/syno/bin:/usr/syno/sbin:/usr/bin:/bin:/usr/sbin:/sbin"


def require_program(name: str) -> None:
    if subprocess.run(["which", name], capture_output=True, text=True).returncode != 0:
        raise SetupError(f"Required program '{name}' is not installed (brew install {name}).")


def ssh_argv(remote_cmd: str) -> list[str]:
    return [
        "sshpass", "-p", PASSWORD,
        "ssh",
        "-p", str(SSH_PORT),
        "-o", "StrictHostKeyChecking=no",
        "-o", "UserKnownHostsFile=/dev/null",
        "-o", "LogLevel=ERROR",
        "-o", "ConnectTimeout=10",
        f"{USER}@{HOST}",
        remote_cmd,
    ]


def ssh(remote_cmd: str, *, sudo: bool = False, timeout: int = 600, retries: int = 4) -> subprocess.CompletedProcess[str]:
    """Run a remote command, retrying on the transient auth failures DSM's Auto Block causes."""
    if sudo:
        remote_cmd = f"echo {shlex.quote(PASSWORD)} | sudo -S -p '' sh -c {shlex.quote(remote_cmd)}"
    last = None
    for attempt in range(1, retries + 1):
        result = subprocess.run(ssh_argv(remote_cmd), text=True, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, timeout=timeout, check=False)
        out = result.stdout or ""
        if result.returncode == 0:
            return result
        last = result
        if "Permission denied" in out or "Connection refused" in out or "Connection reset" in out:
            wait = attempt * 5
            print(f"  SSH attempt {attempt}/{retries} failed (likely DSM Auto Block) — retrying in {wait}s", flush=True)
            time.sleep(wait)
            continue
        break
    raise SetupError(f"Remote command failed (exit {last.returncode if last else '?'}):\n{last.stdout if last else ''}")


def wait_for_ssh(timeout: int = 180) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        result = subprocess.run(ssh_argv("true"), capture_output=True, text=True, timeout=20, check=False)
        if result.returncode == 0:
            return
        time.sleep(4)
    raise SetupError(f"SSH did not become usable on {HOST}:{SSH_PORT} within {timeout}s.")


# One remote script does everything, so DSM sees a single SSH login. No single quotes
# inside (it is wrapped in single quotes for sudo), so it stays shell-safe.
REMOTE_PROVISION = f"""
PATH={SYNO_PATH}
SHARE={SHARE}
ACCT={USER}
if synopkg list 2>/dev/null | grep -qi "^DownloadStation"; then
  echo "DownloadStation already installed"
else
  echo "Installing DownloadStation from Synology package server..."
  synopkg install_from_server DownloadStation
fi
synopkg start DownloadStation >/dev/null 2>&1 || true
echo "DS state: $(synopkg is_onoff DownloadStation 2>&1)"
if synoshare --enum ALL 2>/dev/null | grep -qx "$SHARE"; then
  echo "Share $SHARE already exists"
else
  echo "Creating shared folder $SHARE ..."
  synoshare --add "$SHARE" "rdt-client e2e" "/volume1/$SHARE" "" "$ACCT" "" 1 0
fi
if synoshare --enum ALL 2>/dev/null | grep -qx "$SHARE"; then echo "SHARE_OK"; else echo "SHARE_FAIL"; fi
"""


def main() -> int:
    if not PASSWORD:
        print("ERROR: set VDSM_SETUP_PASSWORD (or RDT_E2E_DSM_PASS).", file=sys.stderr)
        return 2
    try:
        require_program("sshpass")
        require_program("ssh")
        print(f"Waiting for SSH on {HOST}:{SSH_PORT} as {USER} ...")
        wait_for_ssh()
        print("Provisioning Download Station + shared folder (single SSH session)...")
        result = ssh(REMOTE_PROVISION, sudo=True)
        print(result.stdout, end="" if result.stdout.endswith("\n") else "\n")
        if "SHARE_OK" not in result.stdout:
            raise SetupError(
                f"Could not create the '{SHARE}' shared folder automatically. Create it once in DSM "
                f"(Control Panel -> Shared Folder) and re-run."
            )
        if "turned on" not in result.stdout and "start" not in result.stdout.lower():
            print("WARNING: Download Station may not be running; check 'synopkg is_onoff DownloadStation'.")
        print()
        print("Download Station is ready. Run the E2E test with:")
        print(f"  make e2e-vdsm")
        print("or directly:")
        print(f"  RDT_E2E_DSM_URL=http://127.0.0.1:{DSM_PORT} \\")
        print(f"  RDT_E2E_DSM_USER={USER} RDT_E2E_DSM_PASS='<password>' \\")
        print(f"  RDT_E2E_ROOT={SHARE}/rdt-e2e \\")
        print("  dotnet test --filter FullyQualifiedName~DownloadStationE2ETest")
        return 0
    except SetupError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
