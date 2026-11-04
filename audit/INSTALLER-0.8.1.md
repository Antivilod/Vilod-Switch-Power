# Installer 0.8.1 - verification record

Date: 2026-11-04. Package version 0.8.1; App/Monitor 0.8.1; Service 0.4.2.

| Check | Evidence | Status |
| --- | --- | --- |
| Core logic and access-policy tests | 103 tests on Linux, .NET SDK 10.0.403 | PASS |
| App cross-build | Release, zero warnings/errors on Linux targeting Windows | PASS |
| Setup helper cross-build | Release, zero warnings/errors | PASS |
| Windows installer compilation | Successful Windows Server 2025 CI run 41817199582 | PASS |
| Installation, service IPC, power restoration, crash restoration, UI initialization, removal | CI run 41817199582, WINDOWS-VERIFICATION.json | PASS |
| NVIDIA hardware, FPS in actual games, owner laptop | Physical Windows validation required | NOT RUN |

The build workflow has read-only repository access and uploads an artifact only after its installation tests succeed. A separate scoped publication workflow downloads that exact successful run, checks source commit, SHA-256 and all Windows result flags, then publishes a pre-release using a temporary GitHub Actions token. No external credentials are stored.

Build commit: 48cf73acc374497dd4407870bcfa460df6bd3ddf. Evidence: https://github.com/Antivilod/Vilod-Switch-Power/actions/runs/41817199582 . Later documentation and publication workflow changes do not change the tested application source. No signing key, password, owner SID, machine settings or personal file is bundled. Third-party and runtime license notices must be included.

Existing publication history was audited previously; it is preserved. Current changed files must pass a source privacy scan before upload. Heuristic scans cannot prove absence of all identifying data. Historical 0.8.0 audit documents retain their original scope and versions.
