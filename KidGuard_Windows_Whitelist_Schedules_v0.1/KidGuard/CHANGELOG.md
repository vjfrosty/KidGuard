# Changelog

## 0.1 (2026-10-12): first release

Parental controls for one child's Windows 11 account, managed from an administrator-only Parent Panel.

### Protection
- **Website whitelist** (`Admin\whitelist.txt`): exact names or `*.example.org` wildcards. The child's outbound IPv4/IPv6 traffic is blocked with persistent Windows Filtering Platform rules matched to the child's SID. The only exception is KidGuard's local proxy on `127.0.0.1:8877`, which allows HTTPS only to whitelisted names. The TLS ClientHello name (SNI) must match the requested name; connections are pinned to the checked public IP; known Encrypted ClientHello front names cannot be whitelisted. Chromium's decoy (GREASE) ECH extension is accepted, so Chrome and Edge work. Certificate downloads from the main certificate authorities are let through, so sites with an incomplete certificate chain open. HTTPS is not decrypted and no root certificate is installed.
- **Schedules** (`Admin\schedule.json`): separate weekly hours for Windows use and internet use, with time zone and overnight windows. Outside account hours the child is signed out and sign-in is blocked; outside internet hours connections are closed. Warnings 10 and 1 minutes before sign-out and 5 minutes before the internet turns off.
- **Temporary changes**: lock (for a time or until unlocked), extra time (starting at the normal end time, with or without internet), pause internet. They expire on their own.
- **All account types**: local and Microsoft accounts are disabled outside allowed time; work/school (Entra ID) and domain accounts get the Windows deny-sign-in rights. Rights the account already had are never removed. A child session with administrator rights is reported.
- **Fail-safe**: configuration writes are validated and atomic; a broken file keeps the last valid policy; without any valid policy KidGuard fails closed. A LocalSystem service with restart-on-failure, plus a SYSTEM watchdog task every minute.
- Folders the service writes for the child in `C:\ProgramData` are checked: a link, or a folder not owned by Administrators/SYSTEM, is replaced; removal never follows links.

### For the parent
- **Setup wizard** (`Setup.cmd`): builds and self-tests on this PC before changing anything, lists every account with the reason it can or cannot be used, language choice, start-page option, rollback on failure. Installs a Start menu shortcut and an Apps & features entry. Refuses clearly while Smart App Control is on (optional code signing: `KIDGUARD_SIGN_THUMBPRINT`).
- **Parent Panel** with these tabs:
  - Dashboard, Schedule (drag in a weekly grid), Allowed sites, Start page;
  - Requests: from the child;
  - Blocked: addresses the child tried, with one-click allow;
  - Activity log, Settings.
- **Check what a site needs**: allowing a site opens it, and two more of its pages, in a hidden Edge/Chrome window. This goes through a temporary test filter that the child's account cannot reach, and lists every address used. The site itself and the servers it needs are ticked; big general sites and tracking/advertising are not; the browser's own traffic is hidden. Servers of one provider become one wildcard rule, except on shared hosting. Also on Requests and Start page, and as `KidGuard.exe --check-site <address>`.
- **Site list** (`config\bundles.txt`): Bulgarian learning, languages, coding and maths, science and creative sites. Every site was opened in Chrome and Edge through the filter and the addresses its pages need were added, so the start page sites work as soon as KidGuard is installed.
- **Always allowed** (built in, no pages to browse; Google Search stays blocked): the Google services Chrome needs for Family Link (`kidsmanagement-pa.googleapis.com`, `accounts.google.com`, `oauth2.googleapis.com`, `www.googleapis.com`), plus `www.gstatic.com` and Google Fonts. Without them, a Chrome profile supervised by Family Link waited on every page and loaded sites very slowly, with tunnel errors.
- **Kids' start page** (`config\startpage.html`, the Bulgarian kids' sites page): kept on the child's real desktop (follows OneDrive), with its sites allowed.
- **English and Bulgarian** for everything the parent and the child see (`lang\bg.txt`). Log lines stay in English.
- `Manage.ps1` and `KidGuard.exe` commands for everything the panel does.
- **Update** (`Update.cmd`): replaces the programs of an installed KidGuard and keeps all settings. It builds and tests first, signs the child out briefly, waits for a healthy service, and restores the previous programs on failure.
- **Shortcuts with the KidGuard shield**: Parent Panel on the desktop and in the Start menu, and "Install KidGuard" / "Update KidGuard" next to `Setup.cmd` and `Update.cmd` (made by the build). The Parent Panel shortcuts are made by `KidGuard.exe --panel-shortcuts` during setup and take the shield from the panel program itself. Only the panel program and its icon can be read by users and by the Windows 11 Start menu (which runs as an app package), so the shortcuts work. The program folder and settings stay administrators-only. The service sets this read access every time it starts, and the panel repairs a shortcut of its own that shows no icon.

### For the child
- **Helper next to the clock**: time left, when the internet turns off, **Ask for a site** (offers the sites blocked in the last 30 minutes) and **Ask for more time** (+15/30/60), with a reason. It shows the parent's answers. Requests go through a drop-box folder in which the child can only create files; the service validates every request.

### Removal
- `Uninstall.cmd` / Apps & features: removes the service, task, network rules, the child's desktop shortcut, the Parent Panel shortcuts and the child helper; restores the child's browser settings and the account's original state. The settings folder is kept.
