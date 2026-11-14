# Shift Happens

Typed a sentence on the wrong keyboard? Select it, press one hotkey. Nothing selected? The hotkey fixes the
word just before the cursor.

- **Wrong keyboard** (`akuo` instead of `שלום`, `יקךךם` instead of `hello`): the text is swapped key for key
  to the other layout, and the keyboard switches to the right language.
- **Right keyboard, typo** (`teh wrold`): misspelled words are fixed with the system spell checker.
- Mixed text (`I love איןמעד`) swaps only the words that need it.
- **Nothing selected:** the word just before the cursor is selected and fixed the same way (`akuo|` →
  `שלום|`). It is selected one character at a time with Shift+Left, so words with punctuation keys in them
  (`ba,g` → `נשמע`) are taken whole. On by default; turn it off in Settings.

Hebrew ↔ English, on macOS (menu-bar app) and Windows (tray app).

## Use (macOS)

| Action | Default hotkey |
|---|---|
| Fix selection (smart: swap or spell-fix) | `⌃⇧Space` |
| Swap selection (always swap) | not set |

Both are set in **Settings…** from the menu-bar keyboard icon. `⌃⌥Space` is not the default because macOS
uses it for "next input source". Undo with `⌘Z` in the app you typed in.

## Install from a release (macOS 13+, Apple Silicon and Intel)

```bash
D="$(mktemp -d)" && gh release download --repo Elaad-Adar/shift-happens --pattern '*-mac.zip' --output "$D/shift-happens-mac.zip" && pkill ShiftHappens; ditto -x -k "$D/shift-happens-mac.zip" /Applications && xattr -dr com.apple.quarantine "/Applications/Shift Happens.app" && open "/Applications/Shift Happens.app"
```

Approve it under System Settings → Privacy & Security → **Accessibility** (once per new version; if an old
entry is there, run `tccutil reset Accessibility io.github.elaad-adar.shift-happens` first). The app is not
notarized by Apple, which is why the `xattr` step is needed.

## Windows

| Action | Default hotkey |
|---|---|
| Fix selection (smart: swap or spell-fix) | `Ctrl+Shift+Space` |
| Swap selection (always swap) | not set |

Both are set in **Settings…** from the tray icon (right-click; double-click opens Settings), together with
"Start with Windows". Undo with `Ctrl+Z` in the app you typed in.

### Install from a release (Windows 10/11, x64)

PowerShell, one command at a time:

```powershell
gh release download --repo Elaad-Adar/shift-happens --pattern '*-windows.zip' --output "$env:TEMP\shift-happens-windows.zip" --clobber
```

```powershell
Stop-Process -Name ShiftHappens -ErrorAction SilentlyContinue
```

```powershell
Expand-Archive -Path "$env:TEMP\shift-happens-windows.zip" -DestinationPath "$env:LOCALAPPDATA\Programs\ShiftHappens" -Force
```

```powershell
Unblock-File "$env:LOCALAPPDATA\Programs\ShiftHappens\ShiftHappens.exe"
```

```powershell
Start-Process "$env:LOCALAPPDATA\Programs\ShiftHappens\ShiftHappens.exe"
```

Without `gh`: download the `-windows.zip` from the Releases page and unzip it anywhere. The exe is not
code-signed, so SmartScreen may say "Windows protected your PC": **More info → Run anyway**. It is
self-contained (no .NET install needed), hence ~70 MB.

Needs an English and a Hebrew keyboard in Settings → Time & language → Language & region. Hebrew typos
are fixed only if Windows has the Hebrew spell checker (part of the Hebrew language pack); without it the
app still swaps layouts. Check with:

```powershell
& "$env:LOCALAPPDATA\Programs\ShiftHappens\ShiftHappens.exe" --spell שלום he
```

Command-line checks (run from PowerShell; it does not wait for the exe, so output can appear after the
next prompt):

```powershell
& "$env:LOCALAPPDATA\Programs\ShiftHappens\ShiftHappens.exe" --convert "akuo" --verbose
& "$env:LOCALAPPDATA\Programs\ShiftHappens\ShiftHappens.exe" --dump-layout "$env:TEMP\windows-us-hebrew.json"
```

If the hotkey is taken by another app (the Claude desktop app uses `Ctrl+Alt+Space`), Shift Happens says so
at startup and opens Settings to pick another.

Windows does not let a normal app send keys into an app running as administrator; run Shift Happens as
administrator too if you need that.

### Build from source (Windows, .NET 8 SDK)

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

`windows/src/ShiftCore` is a line-for-line port of the Swift `ShiftCore`; its tests run the same cases on
the Mac layout table, so a change to the decision logic goes into both.

## Release a new version

Merge the change to `main`, then tag it. The `release` workflow tests, builds, and publishes a Mac zip
and a Windows zip:

```bash
git tag -a v1.1.0 -m "Shift Happens 1.1.0" && git push origin v1.1.0
```

## Build and install from source (macOS 13+)

```bash
scripts/test.sh
scripts/build-app.sh
cp -R "build/Shift Happens.app" /Applications/
open "/Applications/Shift Happens.app"
```

Then allow it in System Settings → Privacy & Security → **Accessibility** (needed to send ⌘C / ⌘V).

The build is ad-hoc signed unless `SIGN_IDENTITY` is set, and macOS forgets the Accessibility permission
after each rebuild of an ad-hoc signed app. After a rebuild:

```bash
tccutil reset Accessibility io.github.elaad-adar.shift-happens
open "/Applications/Shift Happens.app"
```

Approve it in the pop-up, then restart it: `pkill ShiftHappens; open "/Applications/Shift Happens.app"`.

## How it decides

1. Words are grouped by script (Hebrew letters / Latin letters); a group was typed on one keyboard.
2. Each word is checked as typed and after a key-for-key swap.
3. Score: known word as typed = 1, typo with a close spelling (3+ letters) = 0.75; known after swap = 1,
   or 0.5 for swapped words of 1–3 letters (dictionaries accept many short letter strings).
4. Higher swap score: swap the group. Otherwise fix misspelled words. Nothing known at all: swap.
5. A 4+ letter word that clearly reads right the other way goes against its group (`hello akuo` →
   `hello שלום`).

Known limits: code editors that copy the whole line when nothing is selected (VS Code, JetBrains) are seen
as "something selected", so select the word yourself there; apps where Shift+Left does not select (terminals)
just beep; 1–3 letter words in mixed-language text follow their group; apps that block synthetic
copy/paste (some password fields, remote desktops) are not supported; clipboard managers see the
temporary clipboard content.

## Layout table

Never typed by hand: read from the installed layouts with `UCKeyTranslate`.
`fixtures/mac-abc-hebrew.json` is this table for Apple ABC + Hebrew; refresh it with:

```bash
.build/debug/shift-happens --dump-layout > fixtures/mac-abc-hebrew.json
```

## Icon

`packaging/logo-gemini.jpeg` is the source artwork (made with Gemini). Its transparent background is painted
in, so `scripts/make-icon.swift` cuts the rounded square out and builds `packaging/AppIcon.icns`:

```bash
swift scripts/make-icon.swift packaging/logo-gemini.jpeg build/AppIcon.iconset && iconutil -c icns build/AppIcon.iconset -o packaging/AppIcon.icns
```

The menu-bar glyph is drawn in code (`Sources/ShiftHappensApp/MenuBarIcon.swift`). The Windows icon
`packaging/app.ico` comes from the same artwork: `scripts/make-ico.sh`.

## Command line (debugging)

```bash
.build/debug/shift-happens --convert "akuo' nv ba,g?" --verbose
.build/debug/shift-happens --convert "hello" --force
.build/debug/shift-happens --spell שלומ he
```

## Layout

- `Sources/ShiftCore`: mapping and decision logic, no AppKit (`swift test` covers it with a fake dictionary).
- `Sources/ShiftMac`: layout reading and the macOS spell checker.
- `Sources/ShiftHappensApp`: menu-bar app, hotkeys, clipboard, settings.
- `fixtures/`: layout table and `smart-cases.json`, run against the real spell checker.
- `windows/`: the Windows app (`src/ShiftCore` logic, `src/ShiftHappens` tray app, `tests/`).
