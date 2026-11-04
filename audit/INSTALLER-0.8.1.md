# Installer candidate 0.8.1 - preparation record

Date: 2026-11-04. Package version 0.8.1; App/Monitor 0.8.1; Service 0.4.2.

| Check | Evidence | Status |
| --- | --- | --- |
| Core logic and access-policy tests | 103 tests on Linux, .NET SDK 10.0.403 | PASS |
| App cross-build | Release, zero warnings/errors on Linux targeting Windows | PASS |
| Setup helper cross-build | Release, zero warnings/errors | PASS |
| Windows installer compilation | Windows workflow required | NOT RUN |
| Installation, service IPC, power restoration, UI initialization, removal | Windows workflow required | NOT RUN |
| NVIDIA hardware, FPS in actual games, owner laptop | Physical Windows validation required | NOT RUN |

The workflow does not publish a stable release or enable repository write permissions. It uploads an installer candidate only after its installation tests succeed. No signing key, password, owner SID, machine settings or personal file is bundled. Third-party and runtime license notices must be included.

Existing publication history was audited previously; it is preserved. Current changed files must pass a source privacy scan before upload. Heuristic scans cannot prove absence of all identifying data. Historical 0.8.0 audit documents retain their original scope and versions.
