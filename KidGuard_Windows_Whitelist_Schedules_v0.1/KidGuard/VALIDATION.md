# Validation record — version 0.1, 2026-10-12

Authoring PC: Windows 11 Pro 10.0.26200 x64, Chrome 155.0.8059.40, Edge 155.0.4283.45. Tests ran from a normal (non-elevated) account unless stated otherwise.

## Verified for this release

- **Build** (`Build.ps1`, .NET Framework `csc.exe`, `/platform:x64 /langversion:5 /warn:4`): `KidGuard.exe`, `KidGuardPanel.exe` and `KidGuardTray.exe` compile without errors or warnings. `KidGuard.exe --version` reports 0.1. The example configuration, site list and start page (22 linked sites) validate.
- **Self-tests: PASS, 585 assertions**:
  - domain matching and rejected input, schedules (time zones, daylight saving, overnight windows), overrides and next-change calculation;
  - TLS ClientHello parsing, including a captured Chrome 155 hello with the decoy ECH extension;
  - private/public IP classification and certificate-authority download hosts;
  - configuration store (atomic writes, last valid copy, fail-closed), blocked list;
  - start page links and rules, account-type planning (deny-sign-in rights);
  - child requests (validation, merging, limits, expiry, answers), child status;
  - settings, translations (every Bulgarian text keeps its `{0}` placeholders);
  - site-check sorting (site, needed, general, tracking; merged CDN servers; shared hosting kept exact; choice of extra pages);
  - removal of a junction without following it.
- **Real browsers through the filter** (`tests\BrowserTest.ps1`: the real proxy code on a spare port with the default whitelist): Chrome and Edge open www.wikipedia.org, en.wikipedia.org/wiki/Bulgaria and www.khanacademy.org, and example.com is blocked. **8 of 8 checks pass.**
- **Start page sites through the filter**: every link was loaded in Chrome with the rules "Turn on" creates, recording each blocked address. This showed that ucha.se stayed empty without `static.ucha.se`, and found similar gaps on six other sites. The site list now contains what they need. The rest that is still blocked is tracking, advertising and chat widgets.
- **Start page sites in Chrome and Edge** (2026-10-12, after a report that Chrome was very slow with tunnel errors): `--check-site` was run on all 22 start page links and the missing addresses were added to the site list. All 22 then opened in Chrome 155 and Edge 155 through the filter with only the start page rules (1-9 s per page, knigovishte.bg and esa.int up to 21 s in one browser). The slowness on the PC came from a Chrome profile supervised by Google Family Link, whose page check was blocked; it is now always allowed.
- **Check what a site needs**: `KidGuard.exe --check-site` was run on ucha.se, ChessKid, NatGeo Kids, Math Learning Center, Knigovishte and the EU Learning Corner. It found the same addresses as the manual checks, plus video-player servers the manual check missed. The Parent Panel dialog was run for real in English and Bulgarian and its result inspected.
- **Child drop-box permissions**: the folder rules created by the service code were applied with this account standing in for the child. Creating a request file works. Listing the folder, reading the file back, creating subfolders, and writing into or creating folders in the child folder are all denied.
- **Screens**: every Parent Panel tab, the helper's window and dialogs, and setup pages 1–3 were rendered in English and Bulgarian from sample data and inspected at this PC's 1366x720 screen. The defects found (cut-off text, a button hidden on small screens, unclear disabled buttons) were fixed and re-checked.
- **Account list**: `Install.ps1 -ListCandidates` classified this PC's real accounts correctly: own account, built-in Administrator, a Microsoft account with administrator rights, the child's Microsoft account, and a disabled system account.
- Shortcuts: the build creates "Install KidGuard" and "Update KidGuard", which Windows shows with the shield (checked by extracting the icons). The Parent Panel shortcut code (`PanelShortcuts` in `KidGuard.exe`) was run on a locked test folder. The panel program and icon became readable for Users and ALL APPLICATION PACKAGES, a second run added nothing, and the shortcut points to `KidGuardPanel.exe,0` for its icon. On the installed copy the Start menu icon was blank while Explorer could read the file, because the Start menu runs as an app package without that permission. The desktop and Start menu shortcuts of a real install and `Update.cmd` itself are not executed yet (acceptance 9a–9b).
- All PowerShell scripts parse with the Windows PowerShell 5.1 parser. Scripts and service sources are plain ASCII.

## Found on this PC during development and fixed in this release

- Windows PowerShell 5.1 writes UTF-8 with a byte-order mark, which the .NET JSON reader rejected. An install stopped at its first step and rolled back cleanly. Files are now written without the mark and read as text.
- Smart App Control blocked unsigned self-built programs at random. Setup now detects it and stops before changing anything.
- Chrome and Edge always send a decoy Encrypted ClientHello extension, which the filter rejected, so allowed sites did not open. Reproduced with a captured ClientHello; the extension is now accepted.
- krokotak.com sends an incomplete certificate chain. With the cached intermediate certificate removed, it failed until certificate-authority downloads were let through.

## Not executed yet (needs administrator rights or the child's session)

The installation of this build, and everything that runs only as SYSTEM or in the child's session:
- the network filter rules for the child, sign-out and sign-in blocking;
- warnings and the child's helper in the child's session;
- the desktop shortcut;
- the site check from the elevated Parent Panel;
- reboot, crash and uninstall behaviour.

Work through `tests\WINDOWS-ACCEPTANCE.md` (52 items) before relying on KidGuard for a child's everyday account.

Status: **engineering preview**. No code signature, penetration test or production-security claim is supplied.
