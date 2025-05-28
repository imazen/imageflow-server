using sly.lexer;
using sly.parser;
using sly.parser.generator;
using sly.parser.syntax;
using System.Collections.Generic;
using System.Linq;
using System; // For ValueTuple
using System.Text; // For StringBuilder

namespace Imazen.Routing.Parsing;

// Note: This parser definition is a starting point and will likely need refinement,
// especially around handling literals, escapes, and ambiguity.
public class ImazenRoutingParser
{
    // --- Entry Point --- -> Could be expression root

    [Production("root : expression EOS")]
    public IAstNode Root(Expression expr, Token<ImazenRoutingToken> eos) => expr;

    // --- Main Expression Structure --- (Path ? Query ? Flags)

    [Production("expression : pathPart queryPart? flagPart?")]
    public IAstNode ExpressionWithPathQueryFlags(IAstNode path, IReadOnlyList<QueryPair>? query, FlagList? flags)
        => new Expression(path, query, flags);

    [Production("expression : queryPart flagPart?")]
    public IAstNode ExpressionWithQueryFlags(IReadOnlyList<QueryPair> query, FlagList? flags)
        => new Expression(null, query, flags);

    [Production("expression : pathPart flagPart?")]
    public IAstNode ExpressionWithPathFlags(IAstNode path, FlagList? flags)
        => new Expression(path, null, flags);

    [Production("expression : flagPart")]
    public IAstNode ExpressionWithFlags(FlagList flags)
        => new Expression(null, null, flags);

    // --- Path Part --- (Sequence of Segments)
    // One-or-more (+) maps to List<T>
    [Production("pathPart : segment+ ")]
    public IAstNode PathPart(List<ISegment> segments) => new PathExpression(segments);

    // --- Query Part --- (? key=value & key=value)

    [Production("queryPart : QUESTION queryParamList")]
    public IReadOnlyList<QueryPair> QueryPart(Token<ImazenRoutingToken> qMark, List<QueryPair> pairs) => pairs;

    // Zero-or-more (*) maps to List<T>. Using ValueTuple for the group.
    [Production("queryParamList : queryPair (AMPERSAND queryPair)*")]
    public List<QueryPair> QueryParamList(QueryPair first, List<ValueTuple<Token<ImazenRoutingToken>, QueryPair>> rest)
        => new List<QueryPair> { first }.Concat(rest.Select(g => g.Item2)).ToList();

    // Query Pair (key must be identifier or dashed identifier treated as literal)
    // Optional value part (?) maps to nullable List<ISegment>?
    [Production("queryPair : queryKey EQUALS queryValueSegments?")]
    public QueryPair QueryParam(string key, Token<ImazenRoutingToken> eq, List<ISegment>? valueSegments)
        => new QueryPair(key, valueSegments ?? new List<ISegment>()); // Provide empty list if null

    [Production("queryKey : IDENTIFIER")]
    public string QueryKeyIdentifier(Token<ImazenRoutingToken> id) => id.Value;

    [Production("queryKey : DASHED_IDENTIFIER")]
    public string QueryKeyDashed(Token<ImazenRoutingToken> id) => id.Value;

    // One-or-more (+) maps to List<T>
    [Production("queryValueSegments : segment+")]
    public List<ISegment> QueryValueSegments(List<ISegment> segments) => segments;

    // --- Flag Part --- ([ flag1 , flag2 ])

    [Production("flagPart : LSQUARE flagList RSQUARE")]
    public FlagList FlagPart(Token<ImazenRoutingToken> lsq, FlagList list, Token<ImazenRoutingToken> rsq) => list;

    // Zero-or-more (*) maps to List<T>. Using ValueTuple for the group.
    [Production("flagList : flagName (COMMA flagName)*")]
    public FlagList FlagList(string first, List<ValueTuple<Token<ImazenRoutingToken>, string>> rest)
        => new FlagList(new List<string> { first }.Concat(rest.Select(g => g.Item2)).ToList());

    // Flag names can have dashes
    [Production("flagName : IDENTIFIER")]
    public string FlagNameIdentifier(Token<ImazenRoutingToken> id) => id.Value;
    [Production("flagName : DASHED_IDENTIFIER")]
    public string FlagNameDashed(Token<ImazenRoutingToken> id) => id.Value;

    // --- Segments --- (Literal or Variable)

    // How to handle literals? They can be IDENTIFIERs, DASHED_IDENTIFIERs, INTs, or sequences of them.
    // csly might treat unrecognized sequences as errors unless defined.
    // Option 1: Define a broad "LITERAL_CHUNK" token (difficult regex).
    // Option 2: Define segment as sequence of allowed tokens, then post-process AST to merge literals.

    // Let's try defining segment content loosely and relying on post-processing or specific literal rules.
    [Production("segment : literalSegment")]
    public ISegment SegmentLiteral(LiteralSegment lit) => lit;

    [Production("segment : variableSegment")]
    public ISegment SegmentVariable(VariableSegment varSeg) => varSeg;

    // Literal Segment - consumes one or more literal-contributing tokens
    [Production("literalSegment : literalPart+")]
    public LiteralSegment Literal(List<Token<ImazenRoutingToken>> parts)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.TokenID == ImazenRoutingToken.ESCAPE_SEQUENCE)
            {
                // Unescape known sequences or just append the escaped char
                if (part.Value.Length > 1)
                {
                    char escapedChar = part.Value[1];
                    // Add more specific unescape logic if needed (e.g., \t, \n)
                    // For now, just append the character after backslash
                    sb.Append(escapedChar);
                }
                // else: dangling backslash? Ignore or handle as error?
            }
            else
            {
                sb.Append(part.Value);
            }
        }
        return new LiteralSegment(sb.ToString());
    }

    // Defines what can be part of a literal sequence - return the Token itself
    [Production("literalPart : IDENTIFIER")]
    public Token<ImazenRoutingToken> LiteralPartIdentifier(Token<ImazenRoutingToken> token) => token;

    [Production("literalPart : DASHED_IDENTIFIER")]
    public Token<ImazenRoutingToken> LiteralPartDashed(Token<ImazenRoutingToken> token) => token;

    [Production("literalPart : INT")]
    public Token<ImazenRoutingToken> LiteralPartInt(Token<ImazenRoutingToken> token) => token; // Pass token, not stringified int

    [Production("literalPart : LITERAL_CHAR")]
    public Token<ImazenRoutingToken> LiteralPartChar(Token<ImazenRoutingToken> token) => token;

    // Include other single-char tokens that can be part of literals
    [Production("literalPart : PLUS")]
    public Token<ImazenRoutingToken> LiteralPartPlus(Token<ImazenRoutingToken> token) => token;
    [Production("literalPart : MINUS")]
    public Token<ImazenRoutingToken> LiteralPartMinus(Token<ImazenRoutingToken> token) => token;
    [Production("literalPart : TIMES")]
    public Token<ImazenRoutingToken> LiteralPartTimes(Token<ImazenRoutingToken> token) => token;
    [Production("literalPart : DIVIDE")]
    public Token<ImazenRoutingToken> LiteralPartDivide(Token<ImazenRoutingToken> token) => token;
    [Production("literalPart : ASSIGN")]
    public Token<ImazenRoutingToken> LiteralPartAssign(Token<ImazenRoutingToken> token) => token;
    [Production("literalPart : CARET")]
    public Token<ImazenRoutingToken> LiteralPartCaret(Token<ImazenRoutingToken> token) => token;
    [Production("literalPart : DASH")]
    public Token<ImazenRoutingToken> LiteralPartDash(Token<ImazenRoutingToken> token) => token;
    // Add COMMA, PIPE etc. if they should be treated as literals outside specific contexts
    [Production("literalPart : COMMA")]
    public Token<ImazenRoutingToken> LiteralPartComma(Token<ImazenRoutingToken> token) => token;
    [Production("literalPart : PIPE")]
    public Token<ImazenRoutingToken> LiteralPartPipe(Token<ImazenRoutingToken> token) => token;
    [Production("literalPart : AMPERSAND")]
    public Token<ImazenRoutingToken> LiteralPartAmpersand(Token<ImazenRoutingToken> token) => token;

    // Explicitly handle escape sequences as part of a literal
    [Production("literalPart : ESCAPE_SEQUENCE")]
    public Token<ImazenRoutingToken> LiteralPartEscape(Token<ImazenRoutingToken> token) => token;

    // --- Variable Segment --- ({ name? : modifier* })

    [Production("variableSegment : LBRACE variableContent RBRACE")]
    public VariableSegment VariableSeg(Token<ImazenRoutingToken> lb, VariableSegment content, Token<ImazenRoutingToken> rb) => content;

    // Content can be just modifiers, name + modifiers, or just name
    [Production("variableContent : IDENTIFIER COLON modifierList")]
    public VariableSegment VarContentNameAndMods(Token<ImazenRoutingToken> name, Token<ImazenRoutingToken> colon, List<IModifier> modifiers)
        => new VariableSegment(name.Value, modifiers);

    [Production("variableContent : IDENTIFIER")]
    public VariableSegment VarContentNameOnly(Token<ImazenRoutingToken> name)
        => new VariableSegment(name.Value, new List<IModifier>());

    [Production("variableContent : modifierList")] // e.g. {*:?} or {?:int}
    public VariableSegment VarContentModsOnly(List<IModifier> modifiers)
        => new VariableSegment(null, modifiers);

    // Zero-or-more (*) maps to List<T>. Using ValueTuple for the group.
    [Production("modifierList : modifier (COLON modifier)*")]
    public List<IModifier> ModifierList(IModifier first, List<ValueTuple<Token<ImazenRoutingToken>, IModifier>> rest)
        => new List<IModifier> { first }.Concat(rest.Select(g => g.Item2)).ToList();

    // --- Modifiers --- (Simple or With Arguments)

    // Optional argumentList? maps to ArgumentList? (nullable)
    [Production("modifier : modifierName LPAREN argumentList? RPAREN")] // With args: name(arg1,arg2)
    public IModifier ModifierWithArgs(string name, Token<ImazenRoutingToken> lp, ArgumentList? args, Token<ImazenRoutingToken> rp)
    {
        // TODO: Add logic here or in evaluator to validate args based on modifier name (e.g., check delimiter used)
        return new Modifier(name, args);
    }

    [Production("modifier : modifierName")] // Simple modifier: name or ? or *
    public IModifier ModifierSimple(string name)
    {
        // Distinguish simple name from simple modifier symbols
        if (name == "?" || name == "*" || name == "**") // Check if ** needs separate token
            return new SimpleModifier(name);
        else
            return new Modifier(name, null); // Treat as modifier without args
    }

    // Handling STAR and QUESTION tokens as simple modifiers
    [Production("modifier : STAR")]
    public IModifier ModifierStar(Token<ImazenRoutingToken> star) => new SimpleModifier("*");

    [Production("modifier : QUESTION")]
    public IModifier ModifierQuestion(Token<ImazenRoutingToken> q) => new SimpleModifier("?");

    // Modifier Name (Identifier or Dashed Identifier)
    [Production("modifierName : IDENTIFIER")]
    public string ModifierNameId(Token<ImazenRoutingToken> id) => id.Value;

    [Production("modifierName : DASHED_IDENTIFIER")]
    public string ModifierNameDashed(Token<ImazenRoutingToken> id) => id.Value;

    // --- Argument List --- 
    // Collects view models (wrapping tokens or char classes)
    [Production("argumentList : argumentToken (argumentSeparator argumentToken)*")]
    public ArgumentList ArgumentList(TokenViewModel first, List<ValueTuple<Token<ImazenRoutingToken>, TokenViewModel>> rest)
    {
        var allViewModels = new List<ImazenRoutingParser.TokenViewModel> { first }; // Start with the first view model
        foreach (var group in rest)
        {
            // group.Item1 is the separator token (ignored here)
            // group.Item2 is the TokenViewModel for the argument part
            allViewModels.Add(group.Item2);
        }
        // Pass the List<TokenViewModel> to the ArgumentList constructor
        return new ArgumentList(allViewModels);
    }

    // Define argument separator (handling both COMMA and PIPE might need parser logic or separate rules)
    [Production("argumentSeparator : COMMA")]
    public Token<ImazenRoutingToken> ArgSepComma(Token<ImazenRoutingToken> comma) => comma;

    [Production("argumentSeparator : PIPE")]
    public Token<ImazenRoutingToken> ArgSepPipe(Token<ImazenRoutingToken> pipe) => pipe;

    // ArgumentToken: returns the Token itself or reconstructs CharacterClass string
    [Production("argumentToken : IDENTIFIER")]
    public TokenViewModel ArgumentTokenIdentifier(Token<ImazenRoutingToken> id) => new TokenViewModel(id);

    [Production("argumentToken : DASHED_IDENTIFIER")]
    public TokenViewModel ArgumentTokenDashed(Token<ImazenRoutingToken> id) => new TokenViewModel(id);

    [Production("argumentToken : INT")]
    public TokenViewModel ArgumentTokenInt(Token<ImazenRoutingToken> num) => new TokenViewModel(num);

    [Production("argumentToken : ESCAPE_SEQUENCE")]
    public TokenViewModel ArgumentTokenEscape(Token<ImazenRoutingToken> esc) => new TokenViewModel(esc);
    
    // Rule for Character Class Argument -> returns a special view model indicating it's a char class
    [Production("argumentToken : characterClass")]
    public TokenViewModel ArgumentTokenCharClass(CharacterClassViewModel charClass) => new TokenViewModel(charClass);

    // Character Class Structure
    [Production("characterClass : LSQUARE charClassContent RSQUARE")]
    public CharacterClassViewModel CharacterClassProduction(Token<ImazenRoutingToken> lsq, List<Token<ImazenRoutingToken>> content, Token<ImazenRoutingToken> rsq)
    {
        // Reconstruct the raw string representation, including brackets
        var sb = new StringBuilder();
        sb.Append(lsq.Value); // Append '['
        foreach(var token in content)
        {
            sb.Append(token.Value); // Append raw content tokens
        }
        sb.Append(rsq.Value); // Append ']'
        return new CharacterClassViewModel(sb.ToString());
    }

    // Define charClassContent - sequence of non-RSQUARE tokens (simplified)
    [Production("charClassContent : charClassToken*")] // Use * for zero or more content tokens
    public List<Token<ImazenRoutingToken>> CharClassContent(List<Token<ImazenRoutingToken>> tokens) => tokens;
    
    // Tokens allowed inside a character class (excluding RSQUARE, handled by parser)
    // Added COLON based on original parser needing escape
    [Production("charClassToken : IDENTIFIER | DASHED_IDENTIFIER | INT | LITERAL_CHAR | ESCAPE_SEQUENCE | DASH | CARET | COMMA | PIPE | LPAREN | RPAREN | LBRACE | RBRACE | PLUS | TIMES | DIVIDE | ASSIGN | QUESTION | STAR | EQUALS | AMPERSAND | COLON")]
    public Token<ImazenRoutingToken> CharClassToken(Token<ImazenRoutingToken> token) => token;

    // --- Helper ViewModels for ArgumentList ---
    // Need a way to pass either a raw Token or a reconstructed CharacterClass string
    // Use a simple wrapper/discriminator for now.
    public record TokenViewModel 
    {
        public Token<ImazenRoutingToken>? RawToken { get; } = null;
        public CharacterClassViewModel? CharClass { get; } = null;
        public bool IsCharacterClass => CharClass != null;
        
        public TokenViewModel(Token<ImazenRoutingToken> token) { RawToken = token; }
        public TokenViewModel(CharacterClassViewModel charClass) { CharClass = charClass; }

        // Helper to get value, abstracting reconstruction for char class
        public string GetValue()
        {
            if (IsCharacterClass) return CharClass!.RawValue;
            if (RawToken?.TokenID == ImazenRoutingToken.INT) return RawToken.IntValue.ToString();
            if (RawToken?.TokenID == ImazenRoutingToken.ESCAPE_SEQUENCE && RawToken.Value.Length > 1) return RawToken.Value.Substring(1, 1); // Basic unescape
            return RawToken?.Value ?? "";
        }
    }
    public record CharacterClassViewModel(string RawValue); 

    // Update ArgumentList in AST to use TokenViewModel
    // public record ArgumentList(IReadOnlyList<Token<ImazenRoutingToken>> Tokens) : IAstNode; --> Needs change in AST file
} 