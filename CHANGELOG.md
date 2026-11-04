# Changelog

## 0.8.2 - hybrid CPU policy fix, 2026-11-04

- Apply EPP and minimum processor state to additional efficiency classes 1/2 only when their Windows settings exist; verify optional writes by reading them back.
- Explicit Turbo mode: Work 0, normal gaming 1, EPP 0 / Game Boost 2. Gaming no longer inherits a disabled Turbo setting from the base scheme.
- Public default preferences, logo and GPU controls are unchanged.
- App/Monitor/Setup 0.8.2; service 0.4.3; installer/UI version expectations aligned.
- Windows integration regression covers EPP 90/70/0, AC/DC, supported additional classes, minimum 3/5, Turbo 0/1/2, source preservation, activation and restoration.
- Release is gated by Windows installer/service/restoration/UI/uninstall CI, CPU-policy results and SHA-256. Real games and thermal stability are not tested.

## 0.8.1 - Windows installer pre-release, 2026-11-04

- Windows x64 installer configuration with bundled .NET, Start menu shortcut and removal.
- Service setup binds controls to the local interactive account instead of shipping an owner SID.
- Administrator removal first locks new profile changes and restores owned settings; conflicts stop removal.
- Protected service recovery directory and separate local public-edition preferences.
- First-run automatic switching, memory cleanup and resume remain off until enabled by the user.
- App and monitor 0.8.1; service component 0.4.2; setup helper 0.8.1.
- 103 Linux core tests pass; App and Setup cross-build checks pass. Windows CI passed installation, service IPC, power restoration, crash recovery, UI initialization and removal. Physical NVIDIA hardware remains untested.

## 0.8.0 - 2026-11-02

Initial cleaned source release. No ready installer was included.
