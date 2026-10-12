# Windows acceptance checks

Run these on Windows before relying on KidGuard for a child's everyday account. Use a test child account first (standard, not administrator), with your own administrator account available. Record the Windows build, browser versions, date and PASS/FAIL for each item. A successful build or install is not evidence that these cases pass.

## Build and install

1. Run `Build.ps1`: compilation succeeds, all self-tests pass, configuration validation succeeds. No account or network changes happen in this step.
2. Read the example configuration before installing. Use a current allowed account/internet window for the access tests.
3. Double-click `Setup.cmd` from a non-elevated Explorer window (also from a path with spaces): UAC prompt, build output, then the wizard. Choose the language on the first page.
4. The account list matches the accounts on the PC. Your own account, administrators, never-signed-in, signed-in and disabled accounts show the reason and cannot be chosen. Fixing one (e.g. signing the child out) and pressing Refresh makes it selectable.
5. A full install through the wizard succeeds. The finish page opens the Parent Panel. "KidGuard parental controls" appears in Apps & features. `Manage.ps1 -Action Status` shows the service, watchdog, enabled filters and a fresh heartbeat.
6. `C:\ProgramData\KidGuard` allows only SYSTEM and Administrators. As the child, editing, replacing, renaming or deleting Admin files fails.
7. Setup with KidGuard installed is refused with a clear message. After an uninstall, setup moves the old folder aside as `KidGuard.previous-<date>` and, with "Keep allowed sites and schedule", uses the previous files.
8. With Smart App Control on (unsigned build), `Setup.cmd` explains and stops and nothing changes. With a validly signed build (`KIDGUARD_SIGN_THUMBPRINT`), setup proceeds and the service still starts after a restart.
9a. After installing (no update needed), **KidGuard Parent Panel** with the shield is on your desktop and in the Start menu (also after signing out and in again), both open the panel after the UAC prompt, and "Install KidGuard" / "Update KidGuard" next to `Setup.cmd` show the shield. Uninstall removes both Parent Panel shortcuts.
9b. Run `Update.cmd` (or "Update KidGuard") with the Parent Panel closed: it rebuilds, signs the child out briefly, restarts KidGuard, and the schedule, allowed sites, requests and language are unchanged. With the Parent Panel open it refuses and changes nothing. Delete the desktop shortcut first: the update creates it again.
9. Make the install fail on purpose (e.g. occupy port 8877 after the account check): the wizard shows "Setup did not finish" with the log, and the rollback leaves no service, task or filters.

## Account scope and browsers

10. As the parent, browse an unlisted site: access is unchanged. With parent and child both signed in (fast user switching), repeat: filtering follows the connecting user, not the visible desktop.
11. In the child's Edge, Chrome and Firefox (fresh tabs), `https://en.wikipedia.org` opens. HTTP to an allowed site redirects to HTTPS. Private/incognito windows behave the same.
12. An unlisted site such as `https://example.com` fails in every browser.
13. In the child account:

```powershell
# Direct IPv4 without DNS/proxy must fail.
curl.exe --noproxy '*' --connect-timeout 5 https://1.1.1.1/
# Allowed site through the proxy must succeed during internet hours.
curl.exe --proxy http://127.0.0.1:8877 --connect-timeout 10 https://en.wikipedia.org/
# Unlisted site through the proxy must fail.
curl.exe --proxy http://127.0.0.1:8877 --connect-timeout 10 https://example.com/
# Direct IPv6 must fail (only meaningful if native IPv6 otherwise works).
curl.exe --noproxy '*' --connect-timeout 5 'https://[2606:4700:4700::1111]/'
```

14. A portable browser that ignores the system proxy cannot reach even an allowed site. Set to `127.0.0.1:8877` with no bypass, allowed sites work.
15. UDP/QUIC gives no alternative route (needs a UDP-capable test client or packet capture, not just curl).
16. Direct IP, non-443 CONNECT, mismatched CONNECT/SNI, malformed ClientHello and real ECH requests fail.
17. A site with an incomplete certificate chain (e.g. https://krokotak.com) opens for the child on a fresh profile.

## Whitelist and schedule

18. Add a test site, then remove it during an active connection: the change applies and the connection closes within about 5 seconds. `*.example.org` and `example.org` are separate; lookalike names are refused.
19. Set the internet end a few minutes ahead (account hours longer) and start a download or video: at the cutoff, transfers stop and new requests fail; offline apps keep working.
20. Set the account end a few minutes ahead (save all work): the child is warned at about 10 and 1 minutes, then signed out; sign-in is refused; the parent's session is untouched. The internet cutoff warning appears at about 5 minutes. Warnings never appear in the parent's session.
21. Sign-in works again at the next allowed window. Test an overnight window and a day without hours. Do not change the real clock for this; the self-tests cover time zones and daylight saving.
22. Lock now / for 1 hour / Unlock (Dashboard and `Manage.ps1`): Unlock outside allowed hours stays blocked; a timed lock lifts by itself within about 5 seconds of expiry.
23. Extra time: outside account hours, +15 allows sign-in without internet; with "Include internet" also allowed sites. At 19:55 with a 20:00 end, +10 runs to 20:10. Extra time is refused while locked.
24. Pause internet 30 min: open connections close, offline use continues, browsing resumes afterwards.
25. Break the live `schedule.json` by hand: the child stays signed in under the previous policy, the Dashboard and `Manage.ps1 -Action Status` show the error, the log records it once and "Configuration error resolved" after fixing it.
26. Stop the service, delete `Admin\lastgood`, break the live schedule, start the service: KidGuard fails closed (connections closed, child signed out and blocked, `failClosedReason` in `status.json`).
27. `Manage.ps1 -Action EditWhitelist`/`EditSchedule`: an invalid copy is rejected and the live file is unchanged; a valid copy is applied.

## Account types

Repeat items 20–22 and 47 for each account type you use.

28. **Microsoft account:** outside allowed time, password, PIN and Windows Hello sign-in are all refused. Resetting the Microsoft password online does not unblock it. If Microsoft Family is used, its limits are off for this account.
29. **Entra ID / domain account:** outside allowed time, `Manage.ps1 -Action Status` shows both deny rights; console sign-in, unlocking a locked session and Remote Desktop are refused. Inside allowed time the rights are gone (unless recorded in `deniedBefore`). After `gpupdate /force` or an Intune sync while blocked, the rights are back within about 5 seconds.
30. **Administrator detection:** make a test child account an administrator and sign in: within about 5 seconds the Dashboard shows the red warning and the log a WARNING line. Revert afterwards.

## Parent Panel

31. The Start menu shortcut exists for the installing administrator only and shows a UAC prompt. From the child account the panel cannot be opened without administrator credentials.
32. Dashboard values match `Manage.ps1 -Action Status`. Stopping the service shows "not reporting" within about 20 seconds; starting it shows "active".
33. Schedule tab: paint a window ending a few minutes from now and save; the cutoff and warnings happen at that time. Off-grid times (e.g. 16:10) show the rounding notice. Closing with unsaved changes asks first.
34. Allowed sites: add `ucha.se` with "Check what the site needs first". Within about a minute the list ticks `ucha.se` and `*.ucha.se`, leaves tracking and big general sites unticked, and marks already allowed rules. After "Allow ticked addresses", ucha.se shows pictures and lessons for the child, not an empty page.
35. During a site check, from the child account try to connect to the test filter's port (find it with `netstat -ano` as the parent): the connection fails. After the check the port is closed.
36. Without "Check what the site needs first": a pasted full URL with "Whole site" adds the two rules. The test box matches real browsing. "Edit as text" rejects an invalid line and leaves the file unchanged.
37. Blocked tab: an unlisted site the child opened appears within about 5 seconds; "Allow whole site" adds the rules and removes it from the list; Dismiss removes an entry.
38. Settings > Български > Apply: the panel restarts in Bulgarian; the child's helper and the next sign-out warning are in Bulgarian.
39. Display scaling 150% and 200%: text is readable and nothing is cut off.

## Start page

40. Install with the start-page option, or Start page > Turn on. Sign in as the child: within about 10 seconds the shortcut with the KidGuard icon is on the desktop the child actually sees (with OneDrive backup, the OneDrive Desktop). It opens the page, and each link opens.
41. Delete the shortcut as the child: it comes back within seconds. Turn the page off: the shortcut and `C:\ProgramData\KidGuard-StartPage` disappear. Another standard account cannot read that folder.
42. Start page > Check all its sites: every link is checked, and after allowing the ticked addresses the sites load fully for the child.

## Helper for the child

43. Sign in as the child: within about 10 seconds the KidGuard shield appears next to the clock (log: "Child helper started in session N"). Its times match the Dashboard.
44. As the child, open a blocked site, then **Ask for a site...**: it is offered under "Blocked in the last 30 minutes". Send it with a reason: within about 5 seconds it appears in Requests with the reason. **Check and allow the site...**: the child gets a notification and the site opens after a refresh.
45. **Ask for more time** (+15), then **Give the time**: the Dashboard shows the extra time and the helper the later sign-out time. Ask again and **Say no**: the child is told.
46. As the child: listing `C:\ProgramData\KidGuard-Child\inbox` fails; creating a folder in `C:\ProgramData\KidGuard-Child` fails; `KidGuardTray.exe` there cannot be replaced. Ending the helper in Task Manager: it returns within seconds, at most 3 times in 10 minutes. Settings > helper off: it disappears within seconds; on: it returns.

## Failure, reboot and removal

47. Uninstall from Apps & features: only KidGuard's rules, service, task, desktop shortcut, child helper and child folders are removed; the child's browser settings and the account's original state return. The settings folder `C:\ProgramData\KidGuard` is kept.
48. Stop the service as administrator: proxy access fails and the child is signed out/blocked. Start it again: the schedule decides.
49. On a disposable test setup, crash the service process: the network rules still block direct connections, the service restarts, and the watchdog blocks the child if the heartbeat does not resume.
50. Restart inside and outside account hours; sleep and resume across a cutoff. The filters are active, the service starts automatically, the watchdog runs on battery. Record any gap at startup rather than assuming there is none.
51. As the child, stopping the service, changing the SYSTEM task, removing filters or changing protected files all fail.
52. Check for privileged VPNs, background transfer services, remote browsers or hosting sites on the whitelist. These are known limits, not things the package can certify.

Release gate: any failure in account scope, network blocking, schedule enforcement, recovery or restoration blocks use as parental protection until it is investigated. Keep the signed-off results with the installed version.
