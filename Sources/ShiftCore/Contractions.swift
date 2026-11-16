import Foundation

/// English contractions typed without the apostrophe. Spell checkers often accept these ("whats",
/// "cant"), so Fix would leave them alone. Only words that are not real words on their own are listed:
/// "its", "lets", "wont", "ill", "well", "were", "shell", "hell", "id", "wed", "shed", "hes" stay out.
public enum Contractions {
    static let table: [String: String] = [
        "whats": "what's", "thats": "that's", "dont": "don't", "doesnt": "doesn't", "didnt": "didn't",
        "isnt": "isn't", "arent": "aren't", "wasnt": "wasn't", "werent": "weren't", "cant": "can't",
        "couldnt": "couldn't", "shouldnt": "shouldn't", "wouldnt": "wouldn't", "hasnt": "hasn't",
        "havent": "haven't", "hadnt": "hadn't", "im": "I'm", "ive": "I've", "youre": "you're",
        "theyre": "they're", "theres": "there's", "wheres": "where's", "whos": "who's", "youve": "you've",
        "weve": "we've", "theyve": "they've", "youll": "you'll", "theyll": "they'll", "itll": "it'll",
        "couldve": "could've", "shouldve": "should've", "wouldve": "would've",
    ]

    /// The contraction for `word` with its casing, or nil. "im" always becomes "I'm".
    public static func fix(_ word: String) -> String? {
        guard let c = table[word.lowercased()] else { return nil }
        if c.hasPrefix("I'") {
            return word.count > 1 && word == word.uppercased() ? c.uppercased() : c
        }
        return TextTools.matchCase(c, like: word)
    }
}
