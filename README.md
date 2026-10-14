<p align="center">
  <img src="Media/KidGuard%20guardian%20shield%20logo.png" alt="KidGuard logo" width="320">
</p>

# KidGuard

**Free parental controls for your child's Windows 11 account.** You choose which websites your child can open and when they can use the computer. Everything else is blocked. Your own account is not affected.

<p align="center">
  <b>Current version: 0.1</b> · <a href="https://github.com/vjfrosty/KidGuard/releases/latest">Download</a> · <a href="https://github.com/vjfrosty/KidGuard/releases">All versions and what's new</a>
</p>

- 🌐 **Only the sites you allow.** Your child can open the websites on your list, in Edge, Chrome or Firefox. Everything else is blocked.
- 🕒 **Time limits.** Set the hours for using the computer and, separately, for using the internet. Your child gets a warning 10 minutes and 1 minute before being signed out.
- 🙋 **Your child can ask.** A small icon next to the clock shows how much time is left. Your child can ask you for a new website or for more time, and you answer from your Parent Panel.
- ⏸️ **Quick changes.** Lock the computer now, give extra time, or pause the internet. These changes end on their own.
- 🏠 **Kids' start page.** A page of links to good sites for children, all already allowed.
- 🇧🇬 **English and Bulgarian.**
- ↩️ **Easy to remove.** Uninstalling puts your child's account back the way it was.

> ⚠️ **Please read:** KidGuard is a new, free project and is still an early version. It helps a lot, but no parental control is perfect. A determined child with your password, or another administrator account, can get around it. KidGuard is a tool to support you, not a replacement for talking with your child.

---

## What you need

- A PC with **Windows 11** (64-bit, the usual kind).
- **Your own account** on the PC that is an **administrator** (you probably have this already).
- **A separate account for your child** that is **not** an administrator. It can be a local account or a Microsoft account (signed in with an e-mail).
  If you don't have one yet: **Settings > Accounts > Other users > Add account**.
- **Smart App Control must be off** (see [below](#if-setup-says-smart-app-control-is-on)). On most PCs it already is.

You don't need to install anything else. KidGuard uses tools that are already part of Windows.

---

## How to install

### 1. Prepare your child's account

Sign in to your **child's** account once, so Windows can set it up. Then **sign out** of it completely (Start > your child's name > **Sign out**). Switching to another user is not enough.

### 2. Download KidGuard

1. Go to the **[latest release](https://github.com/vjfrosty/KidGuard/releases/latest)**.
2. Under **Assets**, click **`KidGuard-v0.1.zip`** (or the newest version shown) to download it. That's the only file you need. The release page explains the other files.

### 3. Unpack the zip

1. Open your **Downloads** folder.
2. Right-click the zip, choose **Properties**, tick **Unblock** at the bottom (if you see it), and click **OK**. This stops Windows from warning you about every file.
3. Right-click the zip again and choose **Extract All… > Extract**.
   Put it somewhere you'll find it again, like **Documents**. You'll need this folder for updates.

### 4. Run Setup

1. Sign in to **your own** (administrator) account.
2. Open the extracted **KidGuard** folder and double-click **`Setup.cmd`**.
3. Windows asks *"Do you want to allow this app to make changes?"*. Click **Yes**.
   If a blue **"Windows protected your PC"** window appears, click **More info > Run anyway**.
4. Setup prepares KidGuard on your computer. This takes about 20 seconds and doesn't change anything yet.
5. The setup window opens. **Choose your child's account** from the list. If an account can't be used, the list tells you why and how to fix it. Fix it in Windows, then press **Refresh list**.
6. Click through to the end and wait until it says it's finished.

### 5. Set it up for your child

Press **Open Parent Panel** at the end of setup. Later you can open it from the **KidGuard Parent Panel** icon on your desktop or in the Start menu.

- **Schedule**: drag over the weekly grid to choose when your child can use the computer and the internet.
- **Allowed sites**: add the websites your child may visit. KidGuard opens each site once to find everything it needs to work properly.
- **Requests**: see what your child asked for, and allow or decline with one click.

That's it. Your child can now sign in as usual.

### What gets installed

Setup builds three programs on your PC and copies them to `C:\ProgramData\KidGuard`, a folder only administrators can change:

| Program | What it does |
|---|---|
| `KidGuard.exe` | Runs in the background as a Windows service. It applies the website list and the schedule. |
| `KidGuardPanel.exe` | The **Parent Panel** that you use. It asks for administrator approval to open. |
| `KidGuardTray.exe` | The small helper next to the clock in your child's account. |

They also appear under **Settings > Apps > Installed apps** as **KidGuard parental controls**.

---

## Everyday use

| I want to… | Do this |
|---|---|
| Allow a new website | Parent Panel > **Allowed sites**, or accept a request under **Requests** |
| See which sites were blocked | Parent Panel > **Blocked**. Allow one with one click |
| Give extra time today | Parent Panel > **Dashboard** > extra time |
| Lock the computer right now | Parent Panel > **Dashboard** > lock |
| Change the hours | Parent Panel > **Schedule** |

The Parent Panel always asks for administrator approval, so your child can't open it.

## Updating to a new version

1. Download and extract the new zip as in steps 2 and 3.
2. Close the Parent Panel.
3. In the **new** folder, double-click **`Update.cmd`** and click **Yes**.

Your schedule and allowed sites are kept. Your child is signed out for a moment while KidGuard restarts, so update when they aren't using the computer. If something goes wrong, the old version is put back automatically.

## Removing KidGuard

Go to **Settings > Apps > Installed apps**, find **KidGuard parental controls**, and choose **Uninstall**.

> Please **don't just delete the folder.** The blocks on your child's account would stay in place.

---

## Help

### If Setup says "Smart App Control is ON"

Smart App Control is a Windows security feature that blocks programs from unknown publishers. KidGuard is built on your own PC, so Windows doesn't recognise it, and it might block KidGuard at random later. If that happened, your child's account would stop being locked. To keep your child safe, setup stops and changes nothing.

You can check it under **Settings > Privacy & security > Windows Security > App & browser control > Smart App Control**. Your options are explained in the [full guide](KidGuard_Windows_Whitelist_Schedules_v0.1/KidGuard/README.md#smart-app-control). Please read it before turning anything off: on some PCs, Smart App Control can't be switched back on without resetting Windows.

### Other things to know

- **Microsoft Family:** if your child's Microsoft account is in a Microsoft Family group, turn off Family's screen time and web filters for that child. Otherwise the two will get in each other's way.
- **Games and chat apps** that need the internet may stop working for your child. Only websites on your list get through.
- **Some sites show unsuitable things** even when they're allowed (for example, comments or ads). KidGuard decides by website, not by page content.
- **School or work PCs:** if the PC is managed by a school or company, ask their IT team first.

Something not working? [Open an issue](https://github.com/vjfrosty/KidGuard/issues) and describe what happened.

---

## For developers and the curious

KidGuard is a Windows service written in C#. It blocks all of the child account's outgoing network traffic with Windows Filtering Platform rules matched to the account's SID. The only exception is a local proxy that allows HTTPS to whitelisted host names. HTTPS is not decrypted and no root certificate is installed. Outside the allowed hours, the child is signed out and sign-in is blocked.

- **[Full technical guide](KidGuard_Windows_Whitelist_Schedules_v0.1/KidGuard/README.md):** PowerShell install, whitelist and schedule formats, overrides, how filtering works and its limits, diagnostics and recovery.
- **[Changelog](KidGuard_Windows_Whitelist_Schedules_v0.1/KidGuard/CHANGELOG.md)**, **[validation record](KidGuard_Windows_Whitelist_Schedules_v0.1/KidGuard/VALIDATION.md)**, **[Windows acceptance checklist](KidGuard_Windows_Whitelist_Schedules_v0.1/KidGuard/tests/WINDOWS-ACCEPTANCE.md)**.
- **[How releases are made](RELEASING.md).**

The release zip contains source code, not ready-made programs. `Setup.cmd` compiles KidGuard on your own PC with the C# compiler built into Windows (.NET Framework 4.x, no third-party dependencies), so you run exactly the code you can read here. Every file in the zip is listed in `SHA256SUMS.txt`. Ready-built programs for each version are on the [releases page](https://github.com/vjfrosty/KidGuard/releases).

```powershell
cd KidGuard_Windows_Whitelist_Schedules_v0.1\KidGuard
.\Build.ps1                   # compile all programs and run the self-tests (no system changes)
.\tests\BrowserTest.ps1       # check real Chrome and Edge through the proxy (no install needed)
```

| Path | Contents |
|---|---|
| [`KidGuard_Windows_Whitelist_Schedules_v0.1/KidGuard/`](KidGuard_Windows_Whitelist_Schedules_v0.1/KidGuard/) | The app: `src/` service, `panel/` Parent Panel, `tray/` child's helper, `setup/` wizard, `config/` defaults, `lang/` translations, `tests/`, and the setup scripts |
| [`tools/`](tools/) | Release packaging |
| `Media/` | Logo and icon |
