# Local Synology Download Station E2E against a self-contained vDSM (virtual-dsm).
# See scripts/e2e/README.md. Boots its own DSM in Docker so the live E2E test needs
# no manually-prepared NAS. Defaults to the no-KVM path so it runs on macOS.

SHELL := /bin/bash

VDSM_DSM_PORT       ?= 55100
VDSM_SSH_PORT       ?= 55122
# The stock vdsm/virtual-dsm image boots straight into the built-in `admin` desktop
# (no first-run wizard), so the setup uses and configures that account.
VDSM_SETUP_USER     ?= admin
VDSM_SETUP_PASSWORD ?= RdtE2ePass1!
RDT_E2E_SHARE       ?= Downloads
RDT_E2E_URL         ?= http://speedtest.tele2.net/1MB.zip

export VDSM_DSM_PORT VDSM_SSH_PORT VDSM_SETUP_USER VDSM_SETUP_PASSWORD RDT_E2E_SHARE

.PHONY: e2e-vdsm-up e2e-vdsm-deps e2e-vdsm-web e2e-vdsm-ds e2e-vdsm-setup \
        e2e-vdsm e2e-vdsm-all e2e-vdsm-down e2e-vdsm-rm e2e-vdsm-logs e2e-vdsm-check

## Boot (or start) the local vDSM and wait for the guest DSM web API.
e2e-vdsm-up:
	python3 scripts/e2e/setup_local_vdsm.py start

## Install Playwright (used for the DSM first-run wizard automation).
e2e-vdsm-deps:
	mkdir -p .e2e/web-setup-node
	npm --prefix .e2e/web-setup-node install playwright@1.60.0

## Complete DSM first-run setup (wizard), set the admin password, enable SSH.
e2e-vdsm-web: e2e-vdsm-deps
	node scripts/e2e/setup_vdsm_web.mjs

## Install Download Station and create the download share (over SSH).
e2e-vdsm-ds:
	python3 scripts/e2e/setup_downloadstation.py

## One-time provisioning of a fresh vDSM: wizard + Download Station + share.
e2e-vdsm-setup: e2e-vdsm-web e2e-vdsm-ds

## Run the .NET Download Station E2E test against the local vDSM.
e2e-vdsm:
	RDT_E2E_DSM_URL=http://127.0.0.1:$(VDSM_DSM_PORT) \
	RDT_E2E_DSM_USER=$(VDSM_SETUP_USER) \
	RDT_E2E_DSM_PASS=$(VDSM_SETUP_PASSWORD) \
	RDT_E2E_ROOT=$(RDT_E2E_SHARE)/rdt-e2e \
	RDT_E2E_URL=$(RDT_E2E_URL) \
	dotnet test server/RdtClient.Service.Test/RdtClient.Service.Test.csproj \
	  --filter FullyQualifiedName~DownloadStationE2ETest

## Full chain on a fresh machine: boot, provision, test.
e2e-vdsm-all: e2e-vdsm-up e2e-vdsm-setup e2e-vdsm

## Stop / remove / inspect the local vDSM.
e2e-vdsm-down:
	python3 scripts/e2e/setup_local_vdsm.py stop
e2e-vdsm-rm:
	python3 scripts/e2e/setup_local_vdsm.py rm
e2e-vdsm-logs:
	python3 scripts/e2e/setup_local_vdsm.py logs

## Syntax-check the e2e helper scripts.
e2e-vdsm-check:
	python3 -m py_compile scripts/e2e/setup_local_vdsm.py scripts/e2e/setup_downloadstation.py
	node --check scripts/e2e/setup_vdsm_web.mjs
	@echo "e2e scripts OK"
