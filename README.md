# Cleaner for Windows

[Русский](README.ru.md) · English

**1.1.0-rc.1 — test release.** Cleaner helps inspect temporary files, understand results, quarantine selected data and recover it. It does not guarantee a faster computer. Real Windows compatibility checks and user feedback are required before a stable release.

**Graphite appearance:** choose Dark or Light in Settings. The entire interface updates immediately and remembers your choice. This release candidate still requires the Windows compatibility matrix and novice-user trials before a stable release. Legacy quarantine items whose restore location is on another volume are preserved for manual recovery; automatic cross-volume restore is currently blocked rather than using an unverified path-based copy.

## Download and start

1. Open [Releases](https://github.com/Sking32/Cleaner/releases). Download `Cleaner-1.1.0-rc.1-win-x64.zip` under **Assets**. **Source code** archives are not the runnable app. If RC1 is not published yet, obtain the complete ZIP from its author.
2. Extract the entire ZIP to an ordinary writable folder. Keep `Data` beside `Cleaner.App.exe`; do not run directly from the ZIP.
3. Start `Cleaner.App.exe`. The package includes .NET; no separate runtime installation is required.
4. Optional: download `.zip.sha256` and compare it with `Get-FileHash -Algorithm SHA256 .\Cleaner-1.1.0-rc.1-win-x64.zip`. A matching checksum confirms archive integrity, not software safety.

Targets: **Windows 11 x64** and **Windows 10 22H2 x64**. The real-machine validation matrix is still pending. ARM64, Insider, macOS and Linux are outside this release. The EXE is not signed; do not disable Windows protection or antivirus to run it.

## First launch

Read the safety notice. **Create a restore point** performs a real system action and requests UAC permission if necessary. After restarting with elevated permissions, click it again. System Protection must be enabled; Windows may refuse a point created too soon after the previous one. Cleaner does not change those restrictions or protection settings.

**Understood, continue** lets you explore without creating a point. Reopen the notice under **Settings → Safety and restore point**. Restore points do not replace document backups or recover permanently deleted personal files. [Microsoft System Restore guide](https://support.microsoft.com/en-us/windows/experience/backup-recovery/system-restore).

First launch follows the Windows display language: Russian → Russian, English → English, others → English. You can override it in Settings; restart after changing the language.

## Inspect and clean

1. Start with **normal mode** and the Gentle profile: user and Windows TEMP. Standard adds thumbnail/browser caches; Deep adds application/package-manager caches and older OBS logs. **For gamers** preserves saves, game settings and shader caches.
2. Click **Check computer**. Analysis never moves or deletes data. Review sizes, consequences and skipped files. On the categories tab, clicking a bar opens details.
3. Choose a mode and start cleanup. **Quarantine** is the default. **Permanent deletion** requires separate confirmation and cannot be undone by Cleaner.
4. Review **History**, which distinguishes deleted data from quarantine size.

Manual TEMP cleanup has no age or extension threshold: close applications first, because an unlocked file may still be needed later. Automatic cleanup requires both creation and last modification to be at least 24 hours old. File identity, approved scope and exclusions are checked again before action; new files are not picked up. Locked, changed and unverifiable objects and .NET runtime components are preserved. Cleaner does not force applications to close or change access permissions.

**Quarantine occupies space.** Moving files on the same drive does not reclaim space. Cross-drive quarantine is excluded in this beta: the file remains untouched with an explanation. Deleted file size is not a measured gain in free space, especially for compressed files or while other programs are writing data.

Normal mode exposes 45 cleanup categories, including optional diagnostic/history cleanup. Expert mode adds 11 system-affecting operations and unverified system tweaks with explicit warnings. Unsupported OS versions and critical services remain protected. Availability is not a claim of tested compatibility. Presets exclude Prefetch, completed downloads, cloud data, browser sessions, shaders, diagnostic dumps and irreversible system actions. Downloads cleanup only considers unfinished temporary downloads older than 30 days. Native commands bypass quarantine and may create new logs; service tweaks only change startup configuration, without force-stopping running services. Shell preferences may require sign-out or a user-initiated restart.

## Recovery, disk and settings

- In **Data protection → Quarantine**, select a session. **Session files** supports search by name, original path or category and a filter for interrupted operations. Restore restores the entire selected session without overwriting existing files; collisions receive a separate `.restored` copy.
- Damaged and incomplete journals are preserved and block unsafe mutations. Legacy system backups without enough state to detect outside changes require manual review.
- The **Disk** semicircle shows occupancy; labels show used, free and total capacity. Color describes occupancy, not disk health or speed. Right-click a map cell to open its location; tooltips show full paths.
- The history chart shows the last 10 completed cleanups. Analyses and legacy entries with unknown cleanup methods are excluded. Empty history has no demonstration data.
- **Automatic cleanup** runs only while Cleaner is open, every hour, 6 hours, day or week. Only old user temp files are quarantined. It waits while the foreground window fills the screen, another operation is running or a dialog is open. Missed runs are not replayed; the interval starts again when the app opens.
- **Start with Windows** is separate and off by default, as is automatic cleanup. Re-enable it from the new copy after moving the app or updating from a ZIP.
- The update check runs on request in Settings and queries GitHub. Cleaner never automatically installs an update. **What’s new?** includes the current version and release history.

Preferences, history, quarantine and recovery data live in `%LOCALAPPDATA%\Cleaner`, with logs in `Logs`. A new ZIP uses the same profile. Keep recovery data until you have checked results. Do not run old and new versions together.

**Settings → Cleaner data and disk space** removes older daily logs after preview/confirmation and retains today’s log. Manage quarantine under Recovery; recovery snapshots are removed after successful rollback. Windows servicing creates DISM/CBS logs; separate categories select logs older than 7 days, preserving active CBS.log. Caches, restore points and hibernation files may be recreated and use disk space again. Extracted .NET components are required by the running app and remain protected.

## Feedback

Use [Issues — bug or feature request](https://github.com/Sking32/Cleaner/issues/new/choose) or [Discussions — questions and impressions](https://github.com/Sking32/Cleaner/discussions). A GitHub account is required; Russian and English are welcome.

Include app version, Windows version and build, architecture, language, display scale, reproduction steps, expected and actual results. Attach a diagnostic export for cleanup problems.

Diagnostic exports from the graphical app show a preview first. Personal paths, file names and free-form error text are hidden by default. Review the preview and screenshots before sharing. Include personal details only deliberately; never publish quarantine contents, your full profile or registry backups. The graphical app does not send reports or files automatically. The separately built CLI can write unredacted reports/logs and send results to an explicitly configured webhook; its output does not use this preview.

[Tester checklist](https://github.com/Sking32/Cleaner/blob/master/docs/testing/FRIEND_TESTING.md) · [Compatibility](https://github.com/Sking32/Cleaner/blob/master/docs/testing/COMPATIBILITY.md)

Dependency licenses are included under `Licenses` and `THIRD_PARTY_NOTICES.md`. The project's own license has not been selected.

**Soft**, **Standard** and **Deep** presets provide predefined selections. **All** checks available categories in the current mode; **None** clears them. Selection alone never starts cleanup; expert-mode All also selects system actions. The gaming profile is preserved.
