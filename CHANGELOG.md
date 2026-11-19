# Changelog

Each release's section is copied into its GitHub release notes. Newest first.

## Unreleased

- Fix no longer treats a suggestion that only adds a capital letter as a spelling fix, so short Hebrew
  words typed on the English keyboard swap as they should (`vhh` → `היי`, not `Vhh`, #30).
- Windows: works when the English keyboard is installed as English (Israel) or another English variant
  that Windows gives no classic language id (#29). When a keyboard is really missing, the hotkey now shows
  a message saying so the first time, instead of only beeping.

## 1.5.0

- Fix now swaps a whole sentence typed on the wrong keyboard when every word reads right after the swap,
  even if some words also look like short Hebrew words ("how are you" typed on Hebrew, #15).
- Fix adds the missing apostrophe to English contractions: `whats` → `what's`, `cant` → `can't`,
  `im` → `I'm` (#14). Real words such as `its`, `lets` and `wont` are left alone.
- A function key alone (for example `F10`) can now be the hotkey (#16).
- New, off by default: **double tap** Ctrl (or ⌥, Shift, ⌘) to run Fix (#19). The README's privacy section
  says what this option watches.
- Settings says why a shortcut was not accepted, instead of only beeping (#18), and explains Fix and Swap
  with examples (#17).

## 1.4.0

- Public release: MIT license, a README written for users (English and Hebrew), a privacy and security
  section, and step-by-step help for the Mac and Windows install warnings.
- Release notes now come from this changelog and list SHA-256 checksums of both downloads.

## 1.3.0

- Settings shows the app version, a link to the release notes and a **Check Now** button.
- Daily update check: when a newer release exists, **Update to X.Y.Z…** appears in the menu.
- **Report a Problem…** and **Suggest an Idea…** open short GitHub forms (Hebrew and English) with the
  app version and your computer filled in.

## 1.2.0

- Nothing selected? The hotkey fixes the word just before the cursor, including in Hebrew text.
- VS Code: works with nothing selected (VS Code would otherwise copy the whole line).
- Windows: default hotkey is now `Ctrl+Shift+Space` (`Ctrl+Alt+Space` is used by other apps), and the
  app warns at startup when another app already owns the hotkey.

## 1.1.0

- First Windows version (tray app), with the same smart conversion as the Mac.
- Mac app runs on both Apple Silicon and Intel.

## 1.0.0

- First Mac version (menu-bar app): select text, press `⌃⇧Space`. Wrong keyboard: the text is swapped to
  the other layout and the keyboard switches. Right keyboard: misspelled words are fixed.
