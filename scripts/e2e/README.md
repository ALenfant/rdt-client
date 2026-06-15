# Local Download Station E2E (self-contained vDSM)

`DownloadStationE2ETest` is a live test that drives a **real** Synology DSM:
it creates the destination folder, creates a Download Station task, downloads a
file, and verifies the result. These scripts boot a throwaway DSM in Docker
(`vdsm/virtual-dsm`), install Download Station into it, and run that test — so
you don't need a real NAS. The approach is ported from the DatadogSynology
project.

It defaults to the **no-KVM** path (QEMU/TCG software emulation) so it runs on
macOS without `/dev/kvm`. The first boot is slow (several minutes under
emulation); the VM disk is persisted under `.e2e/vdsm`, so later runs reuse it.

## Requirements

- Docker
- Python 3.9+
- Node 18+ and `npm` (for the Playwright wizard automation)
- `sshpass` (`brew install sshpass`) — used to drive `synopkg` over SSH
- Google Chrome (the wizard script prefers it; otherwise it falls back to
  Playwright's bundled Chromium)

## Quick start

```sh
make e2e-vdsm-all
```

That runs the whole chain: boot the vDSM, complete DSM first-run setup, install
Download Station, create a `Downloads` share, and run the E2E test.

## Step by step

```sh
make e2e-vdsm-up       # boot (or start) the vDSM, wait for the DSM web API
make e2e-vdsm-setup    # one-time: DSM wizard (Playwright) + Download Station + share
make e2e-vdsm          # run the .NET DownloadStationE2ETest against it
make e2e-vdsm-down     # stop the vDSM   (e2e-vdsm-rm to delete it)
make e2e-vdsm-logs     # tail container logs
make e2e-vdsm-check    # syntax-check the helper scripts
```

`e2e-vdsm-setup` is only needed once per VM (its result persists in `.e2e/vdsm`).
After that, iterate with just `make e2e-vdsm`.

## What each script does

| Script | Role |
|---|---|
| `setup_local_vdsm.py` | create/start the `vdsm/virtual-dsm` container (no-KVM by default), wait for the guest DSM web API. `start`/`status`/`stop`/`rm`/`logs`. |
| `setup_vdsm_web.mjs` | Playwright: complete the DSM first-run wizard (or use the auto-logged-in `admin` desktop), set the admin password, enable SSH, suppress promos. |
| `setup_downloadstation.py` | over SSH (`sshpass`+`synopkg`): install + start Download Station and create the download share — all in one SSH session so DSM Auto Block isn't tripped. |

## Configuration

Defaults live in the top-level `Makefile`; override on the command line:

| Var | Default | Meaning |
|---|---|---|
| `VDSM_DSM_PORT` | `55100` | host port mapped to DSM `:5000` |
| `VDSM_SSH_PORT` | `55122` | host port mapped to DSM `:22` |
| `VDSM_SETUP_USER` | `admin` | DSM account (stock vDSM boots into `admin`) |
| `VDSM_SETUP_PASSWORD` | `RdtE2ePass1!` | password set on that account |
| `RDT_E2E_SHARE` | `Downloads` | shared folder created for downloads |
| `RDT_E2E_URL` | a 1 MB test file | file the DS task downloads |
| `VDSM_DISABLE_KVM` | `1` | TCG emulation; set `0` on a KVM-capable Linux host for a much faster VM |
| `VDSM_WEB_HEADLESS` | `1` | set `0` to watch the wizard in a real browser window |

## Notes

- **Internet:** installing Download Station and downloading the test file both
  require outbound internet from the vDSM container.
- **Auto Block:** DSM blocks an IP after rapid logins. The scripts minimise SSH
  connections and retry transient `Permission denied` failures.
- **CI:** the no-KVM path can be too slow/flaky for CI. On a KVM-capable Linux
  runner, set `VDSM_DISABLE_KVM=0` for a usable VM.
