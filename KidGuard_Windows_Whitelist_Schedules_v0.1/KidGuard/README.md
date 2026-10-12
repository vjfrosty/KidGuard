# KidGuard — Windows account whitelist and schedules

Source package, version 0.1 (2026-10-12), first release. `CHANGELOG.md` lists everything it contains. Designed for Windows 11 x64 and one **standard (non-administrator) child account**, such as Viktor. The account can be a local account, a Microsoft account, a work or school (Entra ID) account or a domain account; see "Account types" below.

**Validation status:** source syntax and packaging can be checked in the authoring environment; Windows compilation, WFP behavior, browser compatibility, account sign-out and reboot recovery must be tested on Windows. This is an engineering preview, not a certified or unbypassable parental-control product. The installer compiles and runs the included self-tests before changing account/network settings.

## What you asked for

| Item | Location / behavior |
|---|---|
| Whitelist file in an admin folder | `C:\ProgramData\KidGuard\Admin\whitelist.txt` |
| Time schedules | `C:\ProgramData\KidGuard\Admin\schedule.json` |
| Who can edit | Elevated Administrators and SYSTEM; inheritance is removed from the dedicated installation folder |
| Account restriction | One explicitly selected account (local, Microsoft, Entra ID or domain), identified by its Windows SID |
| Web restriction | Persistent WFP rules for the child's outbound IPv4/IPv6 traffic; only the local whitelist proxy is exempted |
| Browser setup | Child-only Windows proxy and Edge, Chrome and Firefox user policies |
| Outside account hours | Disable the account and sign out its sessions; re-enable inside allowed hours |
| Outside internet hours | Close proxy connections and reject new web requests, while allowing offline use if account hours permit |
| Manual lock | `Manage.ps1 -Action LockNow [-Minutes N]` |
| Extra time / internet pause | `Manage.ps1 -Action Bonus -Minutes N [-IncludeInternet]`, `-Action PauseInternet -Minutes N` |
| Advance warning | Message in the child's session 10 and 1 minutes before sign-out, 5 minutes before internet cutoff |
| Blocked sites review | `Manage.ps1 -Action Blocked`, then `-Action Allow -Domain <name>` |
| Removal | Restore the original account-enabled state and saved registry values; delete only KidGuard WFP rules/service/task |

The root is the Windows CommonApplicationData folder, normally `C:\ProgramData`. The installer never modifies the parent's proxy or browser policies. Windows' hosts file is not used.

**Scheduled sign-out closes applications and can discard unsaved work.** The child sees a message 10 minutes and 1 minute before sign-out (and 5 minutes before an internet cutoff), but the sign-out itself is not delayed. It is not just a lock-screen action: a lock screen can be unlocked again, so the implementation signs out and disables the account until access is allowed.

## Install

### Before you start

- Keep your own working administrator account and password.
- Your child needs their own Windows account that is **not an administrator**. Local, Microsoft (e-mail sign-in), work/school (Entra ID) and domain accounts all work. Sign in to it once so its profile exists, then sign it out completely (switching user is not enough).

### Account types

| Type | How KidGuard blocks sign-in | Things to know |
|---|---|---|
| Local | Turns the Windows account off ("disabled") outside allowed time | Original, most tested method. |
| Microsoft account | Same as local: it is a local Windows account linked to an online login | If the child is in a **Microsoft Family** group, turn off Family screen time and web filters for this account, or the two systems will conflict. Resetting the Microsoft password online does not re-enable the account. |
| Work or school (Entra ID) | Adds the Windows "Deny log on locally" and "Deny log on through Remote Desktop" rights for the account | Organisation policies (Intune) that set these rights can override KidGuard; it re-applies them every 5 seconds while blocked. Administrator rights granted through groups or roles are detected only when the child signs in. |
| Domain (Active Directory) | Same as work or school | Domain Group Policy that defines these rights can override KidGuard in the same way. On a PC managed by a school or employer, ask its administrator first. |

For all types, sessions are identified by their security ID (SID), the network filter matches the same SID, and the service warns (Dashboard, log) if the signed-in child has administrator rights.

### With the setup wizard (recommended)

1. Extract the ZIP and double-click **`Setup.cmd`** in the KidGuard folder (after the first build there is also an **Install KidGuard** shortcut with the KidGuard shield next to it). Windows asks for administrator approval. If SmartScreen warns about the file, check that it came from you, then choose More info > Run anyway.
2. Setup builds and tests KidGuard on this computer (about 20 seconds, no system changes yet), then opens the wizard.
3. **Choose your child's account** from the list. Every account on the computer is shown. The ones that cannot be used say why and how to fix it. Fix it in Windows, then press **Refresh list**.
4. Confirm and wait for the installation to finish, then press **Open Parent Panel** to set the schedule and allowed sites.

KidGuard then appears in **Settings > Apps > Installed apps** (uninstall from there), and **KidGuard Parent Panel** (with the shield) is on your desktop and in the Start menu.

If a KidGuard folder from an earlier installation is found (an uninstall keeps it), setup moves it aside as `C:\ProgramData\KidGuard.previous-<date>` and can keep its allowed sites and schedule.

### Smart App Control

Smart App Control is a Windows 11 feature (Settings > Privacy & security > Windows Security > App & browser control > Smart App Control). When it is **On**, it blocks programs that are not signed by a publisher Windows trusts. KidGuard is compiled on your own computer and is unsigned, so Windows blocks it. The blocking is inconsistent: a file can run once and be blocked a minute later, or after a restart. For parental controls that is unsafe. If the service or watchdog is blocked, the internet stays blocked but **the child's account is no longer locked**.

Setup therefore checks first and stops, without changing anything, while Smart App Control is On. The options are:

1. **Turn Smart App Control off** on this PC, then run setup. This lowers protection against unknown programs for every account on the PC. On current Windows 11 versions it may not be possible to turn it back on without resetting Windows; check Microsoft's current documentation before deciding.
2. **Sign KidGuard with a code-signing certificate from a publisher Windows trusts** (a commercial or open-source code-signing certificate, or Microsoft's signing service). Install the certificate, set `KIDGUARD_SIGN_THUMBPRINT` to its thumbprint, and run `Setup.cmd` from that environment. `Build.ps1` and `Setup.ps1` then sign every program, and the check accepts the signed build. A self-signed certificate does **not** satisfy Smart App Control.
3. Use a PC where Smart App Control is off.

In **evaluation mode**, setup continues with a warning: Windows may switch Smart App Control on by itself later and start blocking KidGuard. Check the Parent Panel regularly; it shows "not reporting" when the service is not running.

### Update an existing installation

Double-click **`Update.cmd`** (or the **Update KidGuard** shortcut) in the folder of the newer KidGuard. It keeps your schedule, allowed sites and other settings, replaces the programs, and restarts KidGuard. While KidGuard restarts, your child is signed out, so do it when they are not using the PC. Close the Parent Panel first. If the new version does not start correctly, the previous one is restored automatically; a copy stays in `C:\ProgramData\KidGuard\backup-<date>`. Missing desktop or Start menu shortcuts are created again.

The **Install KidGuard** and **Update KidGuard** shortcuts are made by `Build.ps1` on your PC (a `.cmd` file cannot show its own icon). They contain this folder's full path, so they are rebuilt with every build and are not part of the checksums.

### From PowerShell (advanced)

Open **64-bit Windows PowerShell 5.1 as Administrator** in the extracted KidGuard folder:

```powershell
.\Install.ps1 -ListCandidates            # JSON: every account and why it can or cannot be used
.\Install.ps1 -ChildUser "Viktor"         # exact local user name, not the display name
.\Install.ps1 -ChildUser "Viktor" -ArchivePrevious -ReuseSettings   # after a previous installation
```

If Windows blocks the downloaded scripts, review the source and unblock only this extracted package with `Get-ChildItem . -Recurse -File | Unblock-File`, then `Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned`. No machine-wide execution-policy change is needed. Organization policies may prevent running unsigned scripts; do not bypass those policies. (`Setup.cmd` starts PowerShell with `-ExecutionPolicy Bypass` for that one process; Group Policy settings still take precedence.)

Build uses `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, and references .NET Framework libraries. There are no NuGet, Python or third-party runtime dependencies. No binary is supplied: the installer builds `KidGuard.exe` from the included C# source. ARM64 and 32-bit Windows are not supported in this release.

Installation blocks the child's sign-in while configuring it. It sets up:

- An automatic LocalSystem service named `KidGuard`, dependent on BFE.
- Three persistent WFP filters in a dedicated provider/sublayer; the provider is associated with the automatic service so filters remain active after reboot.
- A SYSTEM watchdog task named `KidGuard-Watchdog`, at startup and every minute.
- Child-only proxy/browser registry values, with exact original values saved for uninstall.
- Service restart-on-failure recovery and administrator-only configuration/log files.
- The Parent Panel, a Start menu shortcut for the installing administrator, and an Apps & features entry that runs `Uninstall.cmd`.

If a setup step fails, the installer attempts rollback and reports any remaining failure. Finish `tests\WINDOWS-ACCEPTANCE.md` before relying on the package for your child's normal account.

## Parent Panel

The easiest way to manage KidGuard. Open **Start menu > KidGuard Parent Panel**, or run `C:\ProgramData\KidGuard\KidGuardPanel.exe`. Windows asks for administrator approval; your child cannot open it without your password. The shortcut is created only for the administrator account that ran the installer.

| Tab | What you can do |
|---|---|
| Dashboard | See whether Windows and internet are allowed right now and when that changes. Lock (now or for 1 hour), Unlock, give extra time (+15/+30/+60 min, optionally with internet), pause or resume internet, and clear all temporary changes. Warnings appear if a settings file is broken or the service is not running. |
| Schedule | Weekly grid in 15-minute steps. Drag to mark allowed time; start a drag on a coloured cell to erase. Switch between Windows hours and internet hours, choose the time zone, then **Save schedule**. |
| Requests | What your child asked for in the helper next to the clock: a blocked site (with their reason) or more time. Allow the whole site or only that address, give the time (with or without internet), or say no. Your child sees the answer within seconds. The tab shows how many requests are waiting. |
| Allowed sites | Paste any web address (the panel extracts the site name). With "Check what the site needs first" (on by default) KidGuard opens the site in a hidden browser and lists every address it loads; see "Checking what a site needs" below. "Whole site" adds the site and all its subdomains. Test whether an address is allowed, remove rules, add sites from the ready-made list, or edit the list as text (checked before saving). |
| Blocked | Addresses your child tried to open, with counts. Allow one, allow its whole site, or dismiss it. |
| Activity log | Recent events, with a filter for parent actions, requests, warnings and errors. Log lines are always in English. |
| Settings | Language (English or Bulgarian), the helper for your child on or off, version. |

Everything the panel changes goes through the same checks as `Manage.ps1`: invalid input is rejected and files are replaced atomically. Changes apply within about 5 seconds. The panel also works when the service is stopped (it then says so and shows the status calculated from the settings).

**Site list:** `Admin\bundles.txt` holds the ready-made sites (Bulgarian learning, languages, coding and maths, science, creative). These are starting points based on each site's main address, not tested complete lists. After your child first uses a site, check the Blocked tab for the extra addresses it needs. You can edit the file; `KidGuard.exe --check-bundles <file>` validates it.

## Kids' start page

A page of links for your child, on their desktop, with all its sites allowed. The package includes the Bulgarian kids' sites page ("Интернетът на Виктор"); you can use any HTML page with links.

- **During setup:** keep "Put the kids' start page on ... desktop" ticked.
- **Later:** Parent Panel > **Start page** > **Turn on**. "Use another page..." imports a different HTML file, "Preview" opens it in your own browser, and the list shows each linked site and whether it is allowed.
- The shortcut (with the KidGuard icon) appears on the child's desktop a few seconds after they sign in. With a Microsoft account this can be the OneDrive Desktop folder. If the child deletes it, it comes back. **Turn off** removes it.
- The page itself is stored in `C:\ProgramData\KidGuard-StartPage`, readable only by the child and administrators. The parent's copy is `Admin\startpage.html`.
- Allowing the page's sites also allows the extra addresses listed for the same site in the site list (for example Khan Academy's image servers). Sites can still need more addresses for videos or logins; check the Blocked tab.

## Checking what a site needs

Most sites load pictures, scripts and videos from other addresses (ucha.se from `static.ucha.se`, Math Learning Center videos from Vimeo). Allowing only the main address leaves such a site empty or broken. KidGuard finds these addresses for you:

- **Allowed sites** > Add (with "Check what the site needs first" ticked), **Requests** > **Check and allow the site...**, and **Start page** > **Check all its sites...** all open the site(s) in a hidden Edge (or Chrome) window. It goes through a temporary test filter that lets everything through and records every address the page uses. Two more pages of the same site are opened too (for example a lesson), because inner pages often need more.
- The result is a list with check boxes:
  - **Part of the site** (ticked): the site and all its subdomains (`ucha.se`, `*.ucha.se`).
  - **Needed by the site** (ticked): picture, video, font and script servers. Several servers of one provider become one rule (`*.vimeocdn.com`), because their names change. On shared hosting (Amazon, CloudFront, Azure, Google APIs…), where anyone can publish, only the exact address is allowed.
  - **Big general sites** (not ticked): YouTube, Google, Facebook, Vimeo and similar. Allowing one opens all of it to your child.
  - **Tracking and advertising** (not ticked): statistics, ads, chat and cookie widgets. Sites work without them.
  - The browser's own background traffic is not shown. Addresses that are already allowed are marked.
- The check runs on your side only. The test filter uses a random port that the child's account cannot reach (KidGuard's network rule lets the child connect only to its own filter), and it stops when the check ends.
- Videos, logins or games deeper inside a site can still need more; check the **Blocked** tab after your child uses the site. From PowerShell: `KidGuard.exe --check-site https://ucha.se/` prints the same suggestions without changing anything.

## Helper for the child

A KidGuard shield next to your child's clock (notification area). Clicking it shows:

- how much computer time is left today and when the internet turns off;
- **Ask for a site...**: the sites blocked in the last 30 minutes are listed, so your child can pick the one they wanted (or type an address) and add a reason;
- **Ask for more time...**: +15, +30 or +60 minutes, with a reason;
- their recent requests and your answers. When you answer, a notification tells them.

Requests appear in the Parent Panel **Requests** tab (and on the Dashboard). Nothing changes until you answer; extra time you give starts when their normal time ends, like the Dashboard buttons.

How it works: the service starts the helper in your child's session and restarts it if it is closed (up to 3 times in 10 minutes). It cannot change anything itself. It reads a status file and drops request files into `C:\ProgramData\KidGuard-Child\inbox`. Your child can create files there, but cannot read, list or change anything else. The service checks every request (valid site name, 15/30/60 minutes, reason up to 200 characters, at most 20 waiting) and deletes the files. Turn the helper off in **Settings**. From PowerShell: `Manage.ps1 -Action Requests`, then `-Action Answer -Id <id> -Reply allow|allow-site|deny`.

## Language

KidGuard speaks English or Bulgarian. Choose it on the first page of setup, or later in Parent Panel > **Settings** (the panel restarts). The choice also applies to everything your child sees: the warnings before sign-out and the helper next to the clock. Without a choice, the Windows display language decides. The Bulgarian texts are in `lang\bg.txt` (pairs of `en:`/`bg:` lines), so wording can be adjusted and rebuilt; `Build.ps1` lists any text without a translation.

## Edit the whitelist

Run from an elevated PowerShell:

```powershell
& 'C:\ProgramData\KidGuard\Manage.ps1' -Action EditWhitelist
# Or change one entry without opening an editor:
& 'C:\ProgramData\KidGuard\Manage.ps1' -Action Allow -Domain 'www.khanacademy.org'
& 'C:\ProgramData\KidGuard\Manage.ps1' -Action Allow -Domain '*.khanacademy.org'
& 'C:\ProgramData\KidGuard\Manage.ps1' -Action Disallow -Domain 'www.khanacademy.org'
```

`EditWhitelist` and `EditSchedule` open a **copy** in Notepad. After you save and press Enter, the copy is validated and only then replaces the live file. An invalid edit is rejected and the live file stays unchanged.

Example file:

```text
# Exact hostname only
wikipedia.org
# All subdomains, but not the root domain itself
*.wikipedia.org
upload.wikimedia.org
www.khanacademy.org
```

One ASCII/punycode hostname per line. Blank lines and lines beginning with `#` are ignored. Do not enter a URL, path, port, IP address or a bare `*`. `example.org` does not grant `www.example.org`; include both or an intentional wildcard. Wildcards enforce a dot boundary, so `*.example.org` does not admit `evil-example.org` or `example.org.evil.com`.

The examples are a starting list, not a tested complete dependency list for each site. Logins, video, fonts and images may need additional exact domains. Denied hostnames are collected in `Admin\blocked.json` (up to 500, most recent first, with counts). Review them with:

```powershell
& 'C:\ProgramData\KidGuard\Manage.ps1' -Action Blocked
& 'C:\ProgramData\KidGuard\Manage.ps1' -Action Dismiss -Domain 'ads.example.net'
```

A domain disappears from the list once the whitelist allows it. Verify a domain's purpose before allowing it. Do not broadly allow cloud hosting, URL proxies, VPN services or shared execution environments.

The service reloads rules every approximately five seconds and closes existing tunnels whenever the whitelist or schedule changes. Temporary overrides (locks, extra time, pauses) do not close tunnels by themselves.

**Broken configuration:** after each successful load the service saves a copy in `Admin\lastgood\`. If someone later edits a live file by hand and breaks it, the service keeps enforcing the last valid policy, logs the error and shows it in `-Action Status`. It no longer signs the child out because of a typo. If there is no valid copy at all (for example the first start after installing a broken file), the service still fails closed: web access is denied and the child account is disabled and signed out.

## Set allowed hours

```powershell
& 'C:\ProgramData\KidGuard\Manage.ps1' -Action EditSchedule
```

The example is:

| Days | Windows account allowed | Internet allowed |
|---|---|---|
| Monday–Friday | 16:00–20:00 | 16:00–19:30 |
| Saturday–Sunday | 09:00–20:00 | 09:00–19:30 |

`FLE Standard Time` is the Windows time-zone ID used for Sofia and includes daylight-saving transitions. Times are wall-clock times in that configured zone, regardless of the computer's displayed zone. Changing the computer's actual date/time can affect enforcement, so the child must not have administrator privileges.

```json
{
  "timeZone": "FLE Standard Time",
  "forceLock": false,
  "accountHours": [
    { "days": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"], "start": "16:00", "end": "20:00" },
    { "days": ["Saturday", "Sunday"], "start": "09:00", "end": "20:00" }
  ],
  "internetHours": [
    { "days": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"], "start": "16:00", "end": "19:30" },
    { "days": ["Saturday", "Sunday"], "start": "09:00", "end": "19:30" }
  ]
}
```

- All hours **outside** these windows are blocked. Start is inclusive, end is exclusive.
- Internet access requires both an account-hours match and an internet-hours match.
- Multiple windows per day are allowed. Empty arrays allow no time.
- Use full English day names and `HH:mm`; `24:00` is allowed only as an end.
- `00:00`–`24:00` means all day. Equal start/end values are rejected.
- `Friday 22:00`–`02:00` continues into Saturday until 02:00.
- During the repeated autumn daylight-saving hour, the same wall-clock rule applies twice. Missing spring times do not occur.
- This is a recurring timetable, not a daily usage quota. It does not count minutes used.

## Temporary overrides

Temporary decisions are stored in `Admin\overrides.json`, separate from the timetable, and expire on their own:

```powershell
$m='C:\ProgramData\KidGuard\Manage.ps1'
& $m -Action LockNow                       # lock until ResumeSchedule
& $m -Action LockNow -Minutes 60           # lock for one hour
& $m -Action ResumeSchedule                # remove the lock
& $m -Action Bonus -Minutes 30             # 30 extra minutes, offline only
& $m -Action Bonus -Minutes 30 -IncludeInternet
& $m -Action PauseInternet -Minutes 45     # internet off, account stays usable
& $m -Action ResumeInternet
& $m -Action ClearOverrides                # remove every temporary override
```

Rules:

- A lock beats everything, including extra time. `Bonus` is refused while a lock is active.
- Extra time starts when access would otherwise end. `+30` at 19:50 with a 20:00 cutoff runs until 20:30. Giving more extra time during a bonus extends it.
- An internet pause beats internet hours and extra internet time.
- `ResumeSchedule` removes the lock (also `"forceLock": true` in `schedule.json`); it does not override a blocked time.
- All parent actions are logged in `Admin\kidguard.log` with a `Parent:` prefix. Account enforcement normally runs every five seconds. The watchdog normally disables/signs out the child within approximately one minute after it detects a stopped/stale service. Startup, resume, hung OS APIs and Task Scheduler delays can lengthen this; there is no pre-logon credential-provider integration or zero-gap boot guarantee.

## How filtering works, and its limits

WFP matches the **owner of the connecting process**, using a SID security-descriptor condition. It does not decide based on whichever desktop happens to be active. The child's direct outbound traffic is denied at ALE_AUTH_CONNECT for IPv4 and IPv6, including TCP, UDP/QUIC and direct-IP access. A higher-weight exception within the same sublayer permits TCP to `127.0.0.1:8877`. The LocalSystem proxy makes approved upstream connections under its own identity.

The proxy permits HTTPS CONNECT only to allowed domains on port 443. It requires the initial visible TLS SNI to match that domain, refuses known ECH public names as whitelist entries (the decoy ECH extension that Chrome and Edge always send is accepted), rejects private/reserved destination addresses, and pins the validated DNS address for the connection. TLS is then passed through unchanged: it does **not** install a root certificate, decrypt HTTPS, or inspect passwords. Plain HTTP GET/HEAD requests for allowed domains are redirected to HTTPS; HTTP-only websites and HTTP POST are unsupported.

Edge/Chrome/Firefox settings select the proxy and disable DoH, QUIC and ECH for the child. Firefox ECH GREASE is also disabled because an inert ECH extension would otherwise be rejected. Other browsers must use the Windows proxy or be configured manually for HTTP/HTTPS proxy `127.0.0.1`, port `8877`, no bypass. Ignoring the proxy is intended to result in blocked connectivity. A browser update or conflicting machine policy may break allowed-site access; test the installed browser versions.

This is **not an unbypassable all-content filter**:

- A domain allowlist allows content and functionality hosted by that domain. It cannot determine whether an allowed site is appropriate for a child, nor inspect URL paths/search terms inside TLS.
- TLS SNI checks cannot prevent every form of domain fronting, encrypted HTTP/2 connection coalescing, remote-browser use or tunneling through an allowed service. Do not allow broadly shared/proxy-capable domains.
- WFP user rules do not reliably attribute work delegated to non-impersonating SYSTEM services (for example brokered transfers), privileged VPNs, virtual machines or another account. Such software requires separate application/service controls.
- These are outbound user-token filters, not a complete per-user inbound firewall or application allowlisting product. Existing higher-priority hard-permit filters/third-party security software require integration testing.
- An administrator, someone with the parent's password, or someone able to change the offline OS can remove the protection. Do not give the child those rights.
- Persistent filters survive service crashes/restarts while BFE and the provider remain enabled. They are not boot-time filters. BFE failure, disabled services, Safe Mode and startup gaps are outside the guarantee.
- Proxy service outage means no approved web connection through this proxy. Graceful stop disables the account; a hard crash requires the watchdog. Offline cached content is not erased.
- Child games, chat apps, printers/LAN access and other software that needs direct networking may stop working. This package intentionally provides no broad direct-network exceptions.

A managed firewall/application-control product with tested browser integration is needed for stronger tamper resistance and broker coverage. These limitations correct the original blueprint's claim that a simple WFP/SNI service is automatically flawless.

## Diagnose and recover

```powershell
& 'C:\ProgramData\KidGuard\Manage.ps1' -Action Status
& 'C:\ProgramData\KidGuard\Manage.ps1' -Action Validate
```

`Status` reads `Admin\status.json`, which the service rewrites every cycle. It shows whether the account and internet are allowed now, when each next changes, active overrides, the number of child sessions, and any configuration error or fail-closed reason. Times in `status.json` and `overrides.json` are UTC.

Inspect `Admin\kidguard.log`. It contains denied domains, parent actions, warnings sent and operational errors, not page content, passwords or full browsing URLs. Logs rotate at approximately 2 MiB with one previous file.

To check the filter with real browsers without touching the installation, run `.\Build.ps1` and then `.\tests\BrowserTest.ps1` from the package folder. If an allowed site fails, check its dependencies and browser policies at `edge://policy`, `chrome://policy`, or `about:policies`; restart the browser after policy changes. ECH errors mean the browser still sends an ECH extension. Never fix this by allowing all domains or disabling WFP.

To remove protection, from **your administrator account**: Settings > Apps > Installed apps > **KidGuard parental controls** > Uninstall (this runs `C:\ProgramData\KidGuard\Uninstall.cmd`). Or from an elevated PowerShell:

```powershell
& 'C:\ProgramData\KidGuard\Uninstall.ps1'
```

Do not delete the folder or just remove the service: the persistent WFP rules and disabled child account can remain. The uninstaller first stops the watchdog/service, removes only this package's WFP filters, restores saved child registry values and the original enabled/disabled state, and removes the service. It retains the protected folder/configuration for inspection; archive/delete it only after uninstall succeeds.

If the uninstaller itself cannot finish, run these recovery steps in elevated PowerShell and keep the folder:

```powershell
Disable-ScheduledTask -TaskName 'KidGuard-Watchdog' -ErrorAction SilentlyContinue
Stop-ScheduledTask -TaskName 'KidGuard-Watchdog' -ErrorAction SilentlyContinue
Stop-Service KidGuard -ErrorAction SilentlyContinue
& 'C:\ProgramData\KidGuard\KidGuard.exe' --remove-filter
# If filter removal succeeds, rerun Uninstall.ps1 to restore the saved proxy settings.
& 'C:\ProgramData\KidGuard\Uninstall.ps1'
```

Do not run a global firewall reset. If you manually enable the child without restoring proxy/filter state, web access may remain blocked. A renamed/deleted/recreated target account requires identity review; do not edit the SID to guess a replacement.

## Package contents

- `src/*.cs`: Windows service, WFP interop, account control, whitelist/schedules/overrides, configuration store, proxy and self-tests.
- `Setup.cmd`, `Setup.ps1`, `Update.cmd`, `Update.ps1`, `Shortcuts.ps1`, `setup/SetupWizard.cs`, `setup/app.manifest`: double-click setup and its wizard (front end only; `Install.ps1` does all checks and changes). `Uninstall.cmd`: elevated uninstall used by Apps & features. `Signing.ps1`: optional code signing and clear reporting of application-control blocks.
- `panel/*.cs`, `panel/app.manifest`: the Parent Panel (WinForms). It references `KidGuard.exe` for all policy logic.
- `tray/TrayApp.cs`, `tray/app.manifest`: the helper for the child (runs as the child; self-contained, compiled with `src/ChildData.cs` and `src/Text.cs`).
- `lang/bg.txt`: Bulgarian texts. `Translations.ps1`: build-time check for texts without a translation.
- `config/bundles.txt`: ready-made site list for the panel. `config/startpage.html`: default kids' start page.
- `assets/`: KidGuard icon and logo. `Assets.ps1` builds `kidguard.ico` from the icon during the build.
- `CHANGELOG.md`: what this release contains.
- `Build.ps1`: compile and run self-tests without installing.
- `Install.ps1`, `Uninstall.ps1`, `Manage.ps1`, `Registry.ps1`: setup, administration and restoration.
- `config/`: whitelist and timetable examples.
- `tests/WINDOWS-ACCEPTANCE.md`: required native integration checks.
- `VALIDATION.md`: exact checks performed here and outstanding checks.
- `SOURCES.md`: authoritative technical references.
