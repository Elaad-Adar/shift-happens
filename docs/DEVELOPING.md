# Developing Shift Happens

For users, see the [README](../README.md). This page is for building, testing and releasing.

## Layout

- `Sources/ShiftCore`: mapping and decision logic, no AppKit (tested with a fake dictionary).
- `Sources/ShiftMac`: layout reading (`UCKeyTranslate`) and the macOS spell checker.
- `Sources/ShiftHappensApp`: menu-bar app: hotkeys, clipboard, Accessibility word selection, settings,
  update check.
- `windows/`: the Windows app. `src/ShiftCore` is a line-for-line port of the Swift `ShiftCore` (keep the
  two in step), `src/ShiftHappens` the tray app, `tests/` xUnit.
- `fixtures/`: the Mac layout table and `smart-cases.json` (run against the real spell checker).
- `.github/workflows`: `ci.yml` (every PR and `main`), `release.yml` (on `v*` tags).

## Build and test: Mac (macOS 13+)

```bash
scripts/test.sh && scripts/build-app.sh && cp -R "build/Shift Happens.app" /Applications/ && open "/Applications/Shift Happens.app"
```

`scripts/test.sh` works with only the Command Line Tools installed (no Xcode). Local builds are ad-hoc
signed, so macOS forgets the Accessibility permission after each rebuild:

```bash
tccutil reset Accessibility io.github.elaad-adar.shift-happens; open "/Applications/Shift Happens.app"
```

Approve it in the pop-up, then `pkill ShiftHappens; open "/Applications/Shift Happens.app"`.

## Build and test: Windows (.NET 8 SDK)

```powershell
cd D:\dev\shift-happens
```

```powershell
git pull
```

```powershell
cd windows
```

```powershell
dotnet test
```

```powershell
dotnet publish src\ShiftHappens -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ..\build\windows
```

The Windows app also builds on a Mac or Linux (`EnableWindowsTargeting`), but runs only on Windows.

## Command line (debugging)

Mac:

```bash
.build/debug/shift-happens --convert "akuo' nv ba,g?" --verbose
```

Windows (PowerShell does not wait for the exe, so output can appear after the next prompt):

```powershell
& "$env:LOCALAPPDATA\Programs\ShiftHappens\ShiftHappens.exe" --convert "akuo" --verbose
```

```powershell
& "$env:LOCALAPPDATA\Programs\ShiftHappens\ShiftHappens.exe" --spell שלום he
```

Also `--force` (always swap) and `--dump-layout [file]` on both.

## How it decides

1. Words are grouped by script (Hebrew letters / Latin letters); a group was typed on one keyboard.
2. Each word is checked as typed and after a key-for-key swap.
3. Score: known word as typed = 1, typo with a close spelling (3+ letters) = 0.75; known after swap = 1,
   or 0.5 for swapped words of 1-3 letters (dictionaries accept many short letter strings).
4. Higher swap score: swap the group. Otherwise fix misspelled words. Nothing known at all: swap.
5. A 4+ letter word that clearly reads right the other way goes against its group (`hello akuo` →
   `hello שלום`).

With nothing selected, the word before the cursor is found in logical order through Accessibility (Mac)
or UI Automation (Windows); apps without those fall back to Shift+Left steps. VS Code's whole-line copy
of an empty selection is recognised by its clipboard marker.

## Layout table

Never typed by hand: read from the installed layouts. `fixtures/mac-abc-hebrew.json` is the table for
Apple ABC + Hebrew; refresh it with `.build/debug/shift-happens --dump-layout > fixtures/mac-abc-hebrew.json`.

## Icon

`packaging/logo-gemini.jpeg` is the source artwork. `scripts/make-icon.swift` cuts out the rounded square
(its transparent background is painted in) and `iconutil` builds `packaging/AppIcon.icns`;
`scripts/make-ico.sh` builds the Windows `packaging/app.ico`. The menu-bar glyph is drawn in code.

## Releasing

1. Add the version's section at the top of `CHANGELOG.md` (`## X.Y.Z`) and set the version in
   `packaging/Info.plist` and `windows/src/ShiftHappens/ShiftHappens.csproj`.
2. Merge to `main` through a PR (CI and a Codex review must pass).
3. Tag it:

   ```bash
   git checkout main && git pull && git tag -a vX.Y.Z -m "Shift Happens X.Y.Z" && git push origin vX.Y.Z
   ```

4. The `release` workflow tests, builds both apps, and publishes the release with the changelog section
   and SHA-256 checksums.
5. Upload both zips at [virustotal.com](https://www.virustotal.com) and add the two report links to the
   release notes.
