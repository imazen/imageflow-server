using sly.lexer;

namespace Imazen.Routing.Parsing;

// Using GenericLexer attributes where possible for performance.
// Need modes to handle context-sensitive lexing (e.g., inside braces vs outside).
public enum ImazenRoutingToken
{
    // --- Structural Characters ---
    // Define sugars explicitly using Lexeme
    [Lexeme("{")]
    LBRACE = 1,

    [Lexeme("}")]
    RBRACE = 2,

    [Lexeme(@"\(")]
    LPAREN = 3,

    [Lexeme(@"\)")]
    RPAREN = 4,

    [Lexeme(@"\[")]
    LSQUARE = 5,

    [Lexeme(@"\]")]
    RSQUARE = 6,

    [Lexeme(":")]
    COLON = 7,

    [Lexeme(",")]
    COMMA = 8,

    [Lexeme("\\|")]
    PIPE = 9,

    [Lexeme("\\?")]
    QUESTION = 10,

    [Lexeme("=")]
    EQUALS = 11,

    [Lexeme("&")]
    AMPERSAND = 12,

    [Lexeme("\\*")]
    STAR = 13,

    [Lexeme("\\^")]
    CARET = 14,

    [Lexeme("-")]
    DASH = 15,

    [Lexeme("\\+")]
    PLUS = 16,

    [Lexeme("/")]
    DIVIDE = 17,

    // --- Literals and Identifiers ---
    [Lexeme(@"[a-zA-Z_][a-zA-Z0-9_]*")]
    IDENTIFIER = 20,

    [Lexeme(@"[a-zA-Z][a-zA-Z0-9_-]+")] // Separate token for identifiers allowing dashes
    DASHED_IDENTIFIER = 21,

    // Integer Numbers
    [Lexeme(@"-?\d+")] // Replaced GenericToken.Int with regex
    INT = 25,

    // Literal Chars - Any character not part of other tokens/delimiters
    // Needs careful exclusion list. Excludes: \{}()[]:?,=*&|^- \r\n 
    // and characters starting IDENTIFIER ([a-zA-Z_]) or INT (\d)
    // This regex might be overly complex or incorrect; requires testing.
    [Lexeme(@"[^\\\{\}\(\)\[\]:,?=*&|\^\-\s\da-zA-Z_]")]
    LITERAL_CHAR = 28,

    // --- Escapes ---
    [Lexeme(@"\\.")] // Matches backslash followed by *any* char
    ESCAPE_SEQUENCE = 30,

    // --- Whitespace (Skippable) ---
    [Lexeme(@"[ \t\r\n]+", isSkippable: true)] // Explicit whitespace definition
    WS = 100,

    // --- Content Chars (Catch-all for literals) ---
    // Deferring literal handling complexity to the parser logic for now.
    // The parser will need rules to consume sequences of IDENTIFIER, DASHED_IDENTIFIER, INT etc.
    // and potentially other characters as part of literal segments.

    // --- End of Input ---
    // EOS is handled implicitly by csly, no explicit token needed
    // [Lexeme(GenericToken.EOS)] // Generic End Of Stream
    // EOS = 200
} 