#!/usr/bin/env python3
"""Create, start, and inspect a local vdsm/virtual-dsm instance for the Download Station E2E test.

Adapted from the DatadogSynology project. Defaults to the Mac-friendly no-KVM path
(QEMU/TCG software emulation), so it runs on hosts without /dev/kvm — at the cost of
a slow first boot.
"""
from __future__ import annotations

import argparse
import os
import shlex
import socket
import subprocess
import sys
import time
from pathlib import Path


DEFAULT_IMAGE = "docker.io/vdsm/virtual-dsm:latest"
DEFAULT_CONTAINER = "rdtclient-e2e-vdsm"
DEFAULT_STORAGE = ".e2e/vdsm"
DEFAULT_DSM_PORT = 55100
DEFAULT_SSH_PORT = 55122


class SetupError(RuntimeError):
    pass


def env(name: str, default: str | None = None) -> str | None:
    value = os.environ.get(name)
    return default if value in (None, "") else value


def env_int(name: str, default: int) -> int:
    value = env(name)
    return default if value is None else int(value)


def env_bool(name: str, default: bool) -> bool:
    value = env(name)
    if value is None:
        return default
    return value.lower() in {"1", "true", "yes", "y", "on"}


def display_cmd(cmd: list[str]) -> str:
    return " ".join(shlex.quote(part) for part in cmd)


def run(cmd: list[str], *, check: bool = True, timeout: int | None = None, quiet: bool = False) -> subprocess.CompletedProcess[str]:
    if not quiet:
        print(f"$ {display_cmd(cmd)}", flush=True)
    result = subprocess.run(
        cmd,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        timeout=timeout,
        check=False,
    )
    if result.stdout and not quiet:
        print(result.stdout, end="" if result.stdout.endswith("\n") else "\n")
    if check and result.returncode != 0:
        raise SetupError(f"Command failed with exit {result.returncode}: {display_cmd(cmd)}")
    return result


def require_docker() -> None:
    result = run(["docker", "version", "--format", "{{.Server.Version}}"], check=False, timeout=15)
    if result.returncode != 0:
        raise SetupError("Docker is not available or the Docker daemon is not reachable.")


def container_state(name: str) -> str | None:
    result = run(["docker", "inspect", "-f", "{{.State.Status}}", name], check=False, timeout=15)
    if result.returncode != 0:
        return None
    return result.stdout.strip()


def container_health(name: str) -> str:
    result = run(
        ["docker", "inspect", "-f", "{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}", name],
        check=False,
        timeout=15,
    )
    if result.returncode != 0:
        return "unknown"
    return result.stdout.strip()


def wait_tcp(host: str, port: int, timeout: int) -> bool:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            with socket.create_connection((host, port), timeout=3):
                return True
        except OSError:
            time.sleep(3)
    return False


def wait_dsm_ready(url: str, timeout: int, stable: int = 2) -> bool:
    """Wait until the *guest* DSM web server is genuinely serving.

    vdsm/virtual-dsm answers a plain HTTP 200 with its own boot/loading page within
    seconds of `docker run`, long before the guest DSM has booted. Probe the DSM WebAPI
    instead: SYNO.API.Info is unauthenticated, available during first-run setup, and only
    answers with the expected JSON once the guest DSM web server is up. Require a few
    consecutive hits so a transient answer during the boot flap doesn't count as ready.
    """
    base = url.rstrip("/")
    probe = (
        f"{base}/webapi/query.cgi?api=SYNO.API.Info&method=query"
        "&version=1&query=SYNO.API.Auth"
    )
    deadline = time.monotonic() + timeout
    start = time.monotonic()
    next_note = start + 30
    hits = 0
    while time.monotonic() < deadline:
        result = run(["curl", "-fsS", "-m", "8", probe], check=False, timeout=12, quiet=True)
        if result.returncode == 0 and "SYNO.API.Auth" in result.stdout:
            hits += 1
            if hits >= stable:
                return True
            time.sleep(2)
            continue
        hits = 0
        now = time.monotonic()
        if now >= next_note:
            print(f"  ... still waiting for the guest DSM ({int(now - start)}s elapsed)", flush=True)
            next_note = now + 30
        time.sleep(5)
    return False


def docker_run_args(args: argparse.Namespace, storage: Path) -> list[str]:
    cmd = [
        "docker",
        "run",
        "-d",
        "--name",
        args.container,
        "-e",
        f"DISK_SIZE={args.disk_size}",
        "-e",
        f"RAM_SIZE={args.ram_size}",
        "-e",
        f"CPU_CORES={args.cpu_cores}",
        # virtual-dsm defaults the disk to cache=none (O_DIRECT) + aio=native, which syncs
        # every write straight to the host disk and makes the DSM install punishingly slow on
        # a virtualized disk. writeback + threads routes guest writes through the host page
        # cache, which is safe for an ephemeral test instance.
        "-e",
        f"DISK_CACHE={args.disk_cache}",
        "-e",
        f"DISK_IO={args.disk_io}",
        "-p",
        f"{args.dsm_port}:5000",
        "-p",
        f"{args.ssh_port}:22",
    ]
    if args.disable_kvm:
        cmd.extend(["-e", "KVM=N"])
    else:
        cmd.append("--device=/dev/kvm")
    cmd.extend(
        [
            "--device=/dev/net/tun",
            "--cap-add",
            "NET_ADMIN",
            "-v",
            f"{storage}:/storage",
            "--stop-timeout",
            "120",
            args.image,
        ]
    )
    return cmd


def ensure_container(args: argparse.Namespace) -> None:
    require_docker()
    storage = Path(args.storage).expanduser().resolve()
    state = container_state(args.container)

    if state is None:
        storage.mkdir(parents=True, exist_ok=True)
        run(docker_run_args(args, storage), timeout=args.create_timeout)
    elif state != "running":
        run(["docker", "start", args.container], timeout=60)
    else:
        print(f"Container {args.container} is already running.")

    print_status(args)

    if args.wait:
        url = f"http://127.0.0.1:{args.dsm_port}/"
        print(f"Waiting for the guest DSM web API at {url} ...")
        if not wait_dsm_ready(url, args.http_timeout):
            raise SetupError(f"Guest DSM web API did not become ready within {args.http_timeout}s.")
        print(f"Guest DSM web API is responding: {url}")

    print_next_steps(args)


def print_status(args: argparse.Namespace) -> None:
    run(
        [
            "docker",
            "ps",
            "-a",
            "--filter",
            f"name={args.container}",
            "--format",
            "{{.Names}} {{.Image}} {{.Status}} {{.Ports}}",
        ],
        check=False,
        timeout=15,
    )
    print(f"Health: {container_health(args.container)}")


def print_next_steps(args: argparse.Namespace) -> None:
    print()
    print("Local vDSM endpoints:")
    print(f"  DSM UI:  http://127.0.0.1:{args.dsm_port}/")
    print(f"  SSH:     127.0.0.1:{args.ssh_port}")
    print()
    print("Next: complete DSM first-run setup and install Download Station with:")
    print("  make e2e-vdsm-setup     # wizard (Playwright) + Download Station install")
    print("Then run the E2E test with:")
    print("  make e2e-vdsm")


def stop_container(args: argparse.Namespace) -> None:
    require_docker()
    if container_state(args.container) is None:
        print(f"Container {args.container} does not exist.")
        return
    run(["docker", "stop", args.container], timeout=180)


def remove_container(args: argparse.Namespace) -> None:
    require_docker()
    if container_state(args.container) is None:
        print(f"Container {args.container} does not exist.")
        return
    run(["docker", "rm", "-f", args.container], timeout=180)


def logs_container(args: argparse.Namespace) -> None:
    require_docker()
    run(["docker", "logs", "--tail", str(args.tail), args.container], check=False)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Create, start, and inspect a local vdsm/virtual-dsm instance.")
    parser.add_argument("action", nargs="?", choices=("start", "status", "stop", "rm", "logs"), default="start")
    parser.add_argument("--container", default=env("VDSM_CONTAINER", DEFAULT_CONTAINER))
    parser.add_argument("--image", default=env("VDSM_IMAGE", DEFAULT_IMAGE))
    parser.add_argument("--storage", default=env("VDSM_STORAGE", DEFAULT_STORAGE))
    parser.add_argument("--dsm-port", type=int, default=env_int("VDSM_DSM_PORT", DEFAULT_DSM_PORT))
    parser.add_argument("--ssh-port", type=int, default=env_int("VDSM_SSH_PORT", DEFAULT_SSH_PORT))
    parser.add_argument("--disk-size", default=env("VDSM_DISK_SIZE", "16G"))
    parser.add_argument("--ram-size", default=env("VDSM_RAM_SIZE", "4G"))
    parser.add_argument("--cpu-cores", default=env("VDSM_CPU_CORES", "4"))
    parser.add_argument("--disk-cache", default=env("VDSM_DISK_CACHE", "writeback"))
    parser.add_argument("--disk-io", default=env("VDSM_DISK_IO", "threads"))
    parser.add_argument("--disable-kvm", action="store_true", default=env_bool("VDSM_DISABLE_KVM", True))
    parser.add_argument("--enable-kvm", action="store_false", dest="disable_kvm")
    parser.add_argument("--no-wait", action="store_false", dest="wait")
    parser.add_argument("--http-timeout", type=int, default=env_int("VDSM_HTTP_TIMEOUT", 1800))
    parser.add_argument("--create-timeout", type=int, default=env_int("VDSM_CREATE_TIMEOUT", 900))
    parser.add_argument("--tail", type=int, default=env_int("VDSM_LOG_TAIL", 160))
    parser.set_defaults(wait=True)
    return parser


def main() -> int:
    args = build_parser().parse_args()
    try:
        if args.action == "start":
            ensure_container(args)
        elif args.action == "status":
            require_docker()
            print_status(args)
            print_next_steps(args)
        elif args.action == "stop":
            stop_container(args)
        elif args.action == "rm":
            remove_container(args)
        elif args.action == "logs":
            logs_container(args)
        return 0
    except SetupError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
