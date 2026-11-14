import AppKit
import ShiftCore
import ShiftMac

// Command-line modes, used for fixtures and quick checks:
//   shift-happens --dump-layout            print the layout table as JSON
//   shift-happens --convert "text" [--force] [--verbose]
//   shift-happens --spell word en|he      one spell check with suggestions
let args = CommandLine.arguments

func fail(_ message: String) -> Never {
    FileHandle.standardError.write((message + "\n").data(using: .utf8)!)
    exit(1)
}

if args.contains("--dump-layout") {
    do {
        let table = MacLayouts.table(try MacLayouts.findPair())
        print(String(data: try table.jsonData(), encoding: .utf8)!)
        exit(0)
    } catch {
        fail("\(error)")
    }
}

if let i = args.firstIndex(of: "--convert") {
    guard i + 1 < args.count else { fail("usage: --convert \"text\" [--force] [--verbose]") }
    do {
        let spell = MacSpellChecker()
        spell.warmUp()
        let converter = Converter(table: MacLayouts.table(try MacLayouts.findPair()), spell: spell)
        let result = converter.convert(args[i + 1], mode: args.contains("--force") ? .forceSwap : .smart)
        print(result.text)
        if args.contains("--verbose") {
            result.notes.forEach { print("  " + $0) }
            print("  switch to: \(result.switchTo?.rawValue ?? "-")")
        }
        exit(0)
    } catch {
        fail("\(error)")
    }
}

if let i = args.firstIndex(of: "--spell"), i + 2 < args.count {
    let sc = MacSpellChecker()
    sc.warmUp()
    let lang: Lang = args[i + 2] == "he" ? .he : .en
    print(args[i + 1], sc.isCorrect(args[i + 1], lang: lang) ? "OK" : "MISS", sc.guesses(for: args[i + 1], lang: lang).prefix(5))
    exit(0)
}

MainActor.assumeIsolated { runApp() }
