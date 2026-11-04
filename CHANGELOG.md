# Changelog

## 0.8.1 - installer candidate, 2026-11-04

- Windows x64 installer configuration with bundled .NET, Start menu shortcut and removal.
- Service setup binds controls to the local interactive account instead of shipping an owner SID.
- Administrator removal first locks new profile changes and restores owned settings; conflicts stop removal.
- Protected service recovery directory and separate local public-edition preferences.
- First-run automatic switching, memory cleanup and resume remain off until enabled by the user.
- App and monitor 0.8.1; service component 0.4.2; setup helper 0.8.1.
- 103 Linux core tests pass; App and Setup cross-build checks pass. Windows installation and hardware outcomes must be reported separately.

## 0.8.0 - 2026-11-02

Initial cleaned source release. No ready installer was included.
