import Testing
@testable import ShiftCore

@Suite struct RichTextTests {
    @Test func listKeepsItsTags() {
        let html = "<meta charset='utf-8'><ol><li>akuo</li>\n<li>akuo akuo</li></ol>"
        #expect(RichText.convertHTML(html, original: "akuo\nakuo akuo", converted: "שלום\nשלום שלום")
            == "<meta charset='utf-8'><ol><li>שלום</li>\n<li>שלום שלום</li></ol>")
    }

    @Test func listNumbersOnlyInPlainText() {
        // Electron editors put "1." in the plain text, while the HTML list draws its numbers.
        let html = "<ol><li><p>akuo</p></li><li><p>akuo</p></li></ol>"
        #expect(RichText.convertHTML(html, original: "1. akuo\n2. akuo", converted: "1. שלום\n2. שלום")
            == "<ol><li><p>שלום</p></li><li><p>שלום</p></li></ol>")
        #expect(RichText.convertHTML("<ul><li>akuo</li></ul>", original: "• akuo", converted: "• שלום")
            == "<ul><li>שלום</li></ul>")
        // A marker the conversion changed, or a missing word with letters, still falls back.
        #expect(RichText.convertHTML(html, original: "1. akuo\n2. akuo", converted: "1ץ שלום\n2ץ שלום") == nil)
        #expect(RichText.convertHTML("<p>akuo</p>", original: "akuo akuo", converted: "שלום שלום") == nil)
    }

    @Test func unchangedTextStaysByteForByte() {
        let html = "<p>x &amp; y</p><p><b>akuo</b></p>"
        #expect(RichText.convertHTML(html, original: "x & y\nakuo", converted: "x & y\nשלום")
            == "<p>x &amp; y</p><p><b>שלום</b></p>")
    }

    @Test func changedTextIsEncoded() {
        #expect(RichText.convertHTML("<p>a&nbsp;&lt;b</p>", original: "a <b", converted: "c <d")
            == "<p>c&nbsp;&lt;d</p>")
    }

    @Test func numericEntities() {
        #expect(RichText.convertHTML("<p>&#1513;x &#x5e9;y</p>", original: "שx שy", converted: "a b")
            == "<p>a b</p>")
    }

    @Test func styleScriptAndCommentsAreSkipped() {
        let html = "<style>p { color: red }</style><!--StartFragment--><p>akuo</p><!--EndFragment-->"
        #expect(RichText.convertHTML(html, original: "akuo", converted: "שלום")
            == "<style>p { color: red }</style><!--StartFragment--><p>שלום</p><!--EndFragment-->")
    }

    @Test func quotedGreaterThanInsideATag() {
        #expect(RichText.convertHTML("<a title=\"a>b\" href='x>y'>akuo</a>", original: "akuo", converted: "שלום")
            == "<a title=\"a>b\" href='x>y'>שלום</a>")
    }

    @Test func wordAndOutlookEntities() {
        #expect(RichText.convertHTML("<p>don&rsquo;t&hellip; &rlm;akuo</p>", original: "don\u{2019}t\u{2026} \u{200F}akuo",
                                     converted: "don\u{2019}t\u{2026} \u{200F}שלום")
            == "<p>don\u{2019}t\u{2026} \u{200F}שלום</p>")
    }

    @Test func wordSplitByATagFallsBack() {
        #expect(RichText.convertHTML("<p>ak<b>uo</b></p>", original: "akuo", converted: "שלום") == nil)
    }

    @Test func textThatDoesNotMatchFallsBack() {
        #expect(RichText.convertHTML("<p>akuo extra</p>", original: "akuo", converted: "שלום") == nil)
        #expect(RichText.convertHTML("<p>akuo</p>", original: "akuo akuo", converted: "שלום שלום") == nil)
        #expect(RichText.convertHTML("<p>akuo</p>", original: "akuo", converted: "שלום שלום") == nil)
    }
}
