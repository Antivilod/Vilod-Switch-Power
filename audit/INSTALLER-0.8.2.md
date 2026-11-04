# VSP 0.8.2 - scope and release verification

Date: 2026-11-04. Base: 85e86b41f133c1dbd3a789aae7e7efd5115e81a6. Package/App/Monitor/Setup 0.8.2; Service 0.4.3.

The owner supplied a source-only patch for additional processor efficiency class policies and explicit gaming Turbo. No personal profile, local log or physical-device inventory is included. The owner reports 103 local core tests, app/service builds and Windows policy validation; those are reported evidence, not a substitute for this repository's CI.

## Publication gate

The release workflow applies the reviewed, SHA-256-pinned source patch on the stated base in a separate branch, regenerates the complete file inventory, audits current files and reachable/unreachable Git objects, and commits using the neutral GitHub Actions identity. It never imports local user files. Build and integration jobs have read-only access and checkout credentials are not persisted. Only the prepare/publish jobs receive scoped temporary repository-write tokens; no personal token or signing key is used.

Windows CI must build the installer, run the 103 core tests and the native CPU-policy integration regression, then verify installation, service IPC, power restoration, service-crash recovery, UI initialization and uninstallation. Publication checks all required boolean results, report versions, the tested source SHA and installer SHA-256. Only then is an unsigned pre-release created with the EXE, SHA256SUMS.txt, WINDOWS-VERIFICATION.json and CPU-POLICY-VERIFICATION.json. A failed check must stop publication.

CPU tests use disposable cloned power schemes. They check EPP 90/70/0 and Turbo 0/1/2 for AC/DC, explicitly disabled source Turbo, minimum 3/5, additional classes where settings exist, source preservation and restoration of the original active scheme. Missing class settings are skipped; counts in the CPU report distinguish coverage from absence. A Windows registry setting is not evidence of physical hybrid CPU hardware.

## Limits and privacy

Real games, sustained load, thermal stability, NVIDIA hardware and the owner's laptop are not tested by hosted CI. No performance or temperature improvement is promised. Public default preferences are unchanged. Original power schemes are not edited by the application patch. Existing installation/preflight protections remain. MULTIKIT is outside scope.

Current source files and Git history must pass the privacy scanner before release; heuristic scanning cannot prove the absence of every identifying datum. Original third-party license notices and prior audit records are preserved. Own code remains MIT. Runtime notices are included by the installer build. No test logs, local profiles, signing materials or private archives are release assets.

Microsoft references:
- https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfenergypreference
- https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfboostmode
