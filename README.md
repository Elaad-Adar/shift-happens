# Shift Happens

Typed a sentence on the wrong keyboard? Select it, press one hotkey.

- **Wrong keyboard** (`akuo` instead of `שלום`, `יקךךם` instead of `hello`): the text is swapped key for key
  to the other layout, and the keyboard switches to the right language.
- **Right keyboard, typo** (`teh wrold`): misspelled words are fixed with the system spell checker.
- Mixed text (`I love איןמעד`) swaps only the words that need it.

Hebrew ↔ English. macOS now; Windows next (see `Claude outputs/plan-layout-fix-and-switcher-2026-11-14.md`
in the Elaad-Adar workspace).

## Use (macOS)

| Action | Default hotkey |
|---|---|
| Fix selection (smart: swap or spell-fix) | `⌃⇧Space` |
| Swap selection (always swap) | not set |

Both are set in **Settings…** from the menu-bar keyboard icon. `⌃⌥Space` is not the default because macOS
uses it for "next input source". Undo with `⌘Z` in the app you typed in.

## Install from a release (macOS 13+, Apple Silicon and Intel)

```bash
gh release download --repo Elaad-Adar/shift-happens --pattern '*-mac.zip' --dir /tmp/shift-happens --clobber && ditto -x -k /tmp/shift-happens/*-mac.zip /Applications && xattr -dr com.apple.quarantine "/Applications/Shift Happens.app" && open "/Applications/Shift Happens.app"
```

Approve it under System Settings → Privacy & Security → **Accessibility** (once per new version; if an old
entry is there, run `tccutil reset Accessibility io.github.elaad-adar.shift-happens` first). The app is not
notarized by Apple, which is why the `xattr` step is needed.

## Release a new version

Merge the change to `main`, then tag it. The `release` workflow tests, builds, and publishes the zip:

```bash
git tag -a v1.0.1 -m "Shift Happens 1.0.1" && git push origin v1.0.1
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

Known limits: 1–3 letter words in mixed-language text follow their group; apps that block synthetic
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

The menu-bar glyph is drawn in code (`Sources/ShiftHappensApp/MenuBarIcon.swift`).

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
