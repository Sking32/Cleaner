# Cleaner for Windows

[Русский](README.ru.md) · English

**Current beta: 1.1.0-beta.3.** A desktop app for inspecting cleanup candidates, reviewing Windows tweaks, managing startup entries and services, and recovering quarantined files. This is a test release: system changes still need validation on real Windows installations. Cleaner does not promise to speed up every computer.

## Download and start

1. Open [Releases](https://github.com/Sking32/Cleaner/releases) and choose the beta you want to test. Download `Cleaner-1.1.0-beta.3-win-x64.zip` under **Assets**, rather than GitHub’s automatic “Source code” archives.
2. Compare the ZIP’s SHA-256 against the accompanying `.zip.sha256` file. In PowerShell: `Get-FileHash -Algorithm SHA256 .\Cleaner-1.1.0-beta.3-win-x64.zip`. A matching hash checks integrity; it does not prove the software is safe.
3. Extract the entire archive into an ordinary writable folder. Keep `Data` beside `Cleaner.App.exe`; do not run directly from the ZIP.
4. Start `Cleaner.App.exe`. The portable x64 package includes .NET. It targets Windows with the required .NET 10/WPF support; compatibility with individual system actions is not yet verified across all Windows builds. ARM64 needs a separate build and testing; macOS and Linux are not supported.

If this beta has not been published yet, request the complete ZIP from its author. Release assets may be absent until the first publication. There is no installer in this package.

The beta is not code-signed. Windows may warn about an unfamiliar app. Check its source and hash; do not disable antivirus, SmartScreen or Windows protection to run it. If you are unsure, stop and contact the author.

## First launch and recovery

The first launch shows a safety notice. Simply opening it does not run cleanup, apply tweaks or create a restore point.

- **Create a restore point** requests administrator permission if needed. Cleaner restarts after UAC approval; click the same button in the new window to create the point. Canceling UAC leaves the current app open without creating a point.
- Windows needs System Protection enabled and may refuse a new point if one was created within the last 24 hours. Cleaner does not change those policies, schedules or protection settings. If creation fails, the notice explains how to open System Protection manually.
- A restore point covers Windows system files and settings; it does not recover deleted personal files. Back up important documents separately. See [Microsoft’s System Restore guide](https://support.microsoft.com/en-us/windows/experience/backup-recovery/system-restore).
- **Understood, continue** lets you explore the app without creating a point. It does not bypass the separate safeguards for dangerous actions. The notice is remembered for this Windows user profile and can be reopened in **Settings → Safety and restore point**. Existing beta users will also see this new notice once.

First launch follows the Windows **display language**: Russian → Russian, English → English, other languages → English. Keyboard layout and regional number/date settings do not select the interface language. Settings offers **Match Windows**, **Русский**, and **English**; a manual choice overrides detection. Restart after changing it.

## What to test first

Start with navigation, window sizes, search, settings, language, the version in the header, **History**, and **What’s new?** in the lower sidebar. `Ctrl+K` opens command search.

In **Cleanup**, choose **Analyze without cleaning** and review sizes, sample paths and skipped items. Analysis reads data without moving or deleting files. Real cleanup is a separate action. Regular files normally move to quarantine: a move on the same drive does not free disk space. Some system cleanup actions delete permanently. Deleting quarantine permanently removes its contents.

Use a disposable Windows VM with a snapshot for system tweaks, service changes and other unfamiliar actions. Cancellation stops further work; it does not undo changes already made. Do not assume a “low risk” label guarantees safety for every computer.

Preferences, history, recovery data and quarantine live under `%LOCALAPPDATA%\Cleaner`. App logs are in its `Logs` directory. Keep recovery data until you have checked the result. This is not a fully isolated portable data profile: a new ZIP still uses this account’s existing data. Close Cleaner before running its CLI; they share recovery state.

## Report a bug

For a reproducible problem, open a [bug report](https://github.com/Sking32/Cleaner/issues/new/choose). For an idea or general experience, choose **Feedback / Предложение** in the issue form. A GitHub account is needed to submit a report. English and Russian reports are welcome. Include:

- App version; Windows edition/build, architecture and display scale.
- Interface language, page and steps to reproduce; whether administrator permission was used.
- Expected result and actual result, plus a screenshot if useful.
- For restore points: whether UAC was canceled, System Protection was enabled, and another point was created recently.

Check screenshots and exported reports for usernames, personal paths and file names. Do not upload your quarantine, registry backups or full settings profile. The app does not automatically send your bug report. Use a private channel for sensitive reports.

## Build, publish and previous changes

[Developer guide](DEVELOPMENT.md) · [Build a portable package](RELEASE_GUIDE.md) · [Publish a beta / Публикация beta](PUBLISHING.md) · [Russian testing checklist](FRIEND_TESTING.md)

Version history is also available inside **What’s new?**. Each update adds a new entry and retains earlier ones. Packages must be built from the final committed source. Native Windows actions still need verification in a disposable VM. Dependency notices are included in the ZIP; the project’s own license has not yet been selected.
