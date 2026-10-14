# Releasing KidGuard

A release is a git tag (`v0.1`, `v0.2`, …) on GitHub. Its one download for parents is **`KidGuard-v<version>.zip`**: the `KidGuard` package folder (sources, setup scripts and `SHA256SUMS.txt`, with `Setup.cmd` at the top).

The zip contains no `.exe` files on purpose. `Setup.cmd` and `Update.cmd` always compile KidGuard on the parent's PC and overwrite `build\`, so prebuilt programs would be replaced anyway. Building locally also means parents run exactly the published source.

The zip is the only file uploaded to a release. GitHub shows its SHA-256 on the release page. GitHub also adds "Source code (zip / tar.gz)" to every release automatically; these can't be removed, so the release notes tell parents to ignore them.

## Steps

Run these from the repository root, in PowerShell, on a clean `main` that is pushed.

1. **Update the version notes.** Add the new version to `CHANGELOG.md` in the package folder.

2. **Build and test** (no system changes):
   ```powershell
   cd KidGuard_Windows_Whitelist_Schedules_v0.1\KidGuard
   .\Build.ps1
   cd ..\..
   ```
   Work through `tests\WINDOWS-ACCEPTANCE.md` on a test PC for anything that touches installing, accounts or filtering.

3. **Refresh the checksums**, if any package file changed:
   ```powershell
   cd KidGuard_Windows_Whitelist_Schedules_v0.1\KidGuard
   Get-ChildItem -Recurse -File | Where-Object { $_.FullName -notmatch '\\build\\' -and $_.Extension -ne '.lnk' -and $_.Name -ne 'SHA256SUMS.txt' } |
     Sort-Object FullName | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName).Hash.ToLower(), $_.FullName.Substring($PWD.Path.Length + 1).Replace('\','/') } |
     Set-Content SHA256SUMS.txt -Encoding ascii
   cd ..\..
   ```

4. **Commit and push** the changes.

5. **Make the zip.** This checks every file against `SHA256SUMS.txt` first and stops if anything is missing or different:
   ```powershell
   .\tools\New-ReleasePackage.ps1 -Version 0.2
   ```
   It writes `dist\KidGuard-v0.2.zip`. It empties `dist\` first. `dist\` is not committed.

6. **Publish the release.** This tags the current `main` and uploads the zip:
   ```powershell
   gh release create v0.2 dist\KidGuard-v0.2.zip `
     --target main --title "KidGuard 0.2" --notes-file release-notes.md
   ```
   Release notes should start with the parent-facing install summary (download, Unblock, Extract All, `Setup.cmd`, or `Update.cmd` for an existing install), followed by what changed. Say clearly that the zip is the file to download.
   Add `--prerelease` for test builds. Parents following the README always get the newest full release via `/releases/latest`.

7. **Check it** on the release page: download the zip, extract it, and confirm that `Setup.cmd` is at the top of the `KidGuard` folder.

## Fixing a release

- Wrong file uploaded: `gh release upload v0.2 dist\KidGuard-v0.2.zip --clobber`
- Wrong notes: `gh release edit v0.2 --notes-file release-notes.md`
- Don't move or reuse a published tag. Release a new patch version (`v0.2.1`) instead.

## Line endings

`.gitattributes` stores every file byte-for-byte (`* -text`), so `SHA256SUMS.txt` matches on any clone, whatever `core.autocrlf` is set to.
