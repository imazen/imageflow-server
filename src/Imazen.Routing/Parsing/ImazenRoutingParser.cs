using sly.lexer;
using sly.parser;
using sly.parser.generator;
using sly.parser.parser;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text;

namespace Imazen.Routing.Parsing;

// Note: This parser definition is a starting point and will likely need refinement,
// especially around handling literals, escapes, and ambiguity.
public class ImazenRoutingParser
{
    // --- Entry Point --- -> Could be expression root

    [Production("root : expression")]
    public IAstNode Root(Expression expr) => expr;

    // --- Simplified Expression Structure --- 
    [Production("expression : pathPart flagPart?")] // Optional flagPart
    public IAstNode ExpressionWithPathAndFlags(PathExpression path, ValueOption<FlagList> flags)
        => new Expression(path, null, flags.Match(f => f, () => null));

    [Production("expression : flagPart")] 
    public IAstNode ExpressionWithFlagsOnly(FlagList flags)
        => new Expression(null, null, flags);

    [Production("expression : pathPart")] 
    public IAstNode ExpressionWithPathOnly(PathExpression path)
        => new Expression(path, null, null);

    // --- Path Part --- 
    [Production("pathPart : segment")] 
    public PathExpression PathPart(ISegment segment) => new PathExpression(new List<ISegment> { segment }); 

    // --- Segments --- (Literal or Variable)
    [Production("segment : literalSegment")]
    public ISegment SegmentLiteral(LiteralSegment lit) => lit;

    [Production("segment : variableSegment")]
    public ISegment SegmentVariable(VariableSegment varSeg) => varSeg;

    // --- Literal Segment ---
    [Production("literalSegment : literalPart")] 
    public LiteralSegment Literal(TokenNode partNode) 
    {
        var sb = new StringBuilder();
        var token = partNode.Token; 
        if (token.TokenID == ImazenRoutingToken.ESCAPE_SEQUENCE && token.Value.Length > 1) sb.Append(token.Value[1]);
        else sb.Append(token.Value);
        return new LiteralSegment(sb.ToString());
    }

    // --- Literal Part Tokens ---
    [Production("literalPart : IDENTIFIER")]
    public IAstNode LiteralPartIdentifier(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : DASHED_IDENTIFIER")]
    public IAstNode LiteralPartDashed(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : INT")]
    public IAstNode LiteralPartInt(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : LITERAL_CHAR")]
    public IAstNode LiteralPartChar(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : ESCAPE_SEQUENCE")]
    public IAstNode LiteralPartEscape(Token<ImazenRoutingToken> token) => new TokenNode(token);
    
    [Production("literalPart : DASH")] 
    public IAstNode LiteralPartMinus(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : STAR")] 
    public IAstNode LiteralPartTimes(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : DIVIDE")]
    public IAstNode LiteralPartDivide(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : EQUALS")] 
    public IAstNode LiteralPartAssign(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : CARET")]
    public IAstNode LiteralPartCaret(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : COLON")] 
    public IAstNode LiteralPartColon(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : COMMA")]
    public IAstNode LiteralPartComma(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : PIPE")]
    public IAstNode LiteralPartPipe(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : AMPERSAND")]
    public IAstNode LiteralPartAmpersand(Token<ImazenRoutingToken> token) => new TokenNode(token);
        
    [Production("literalPart : QUESTION")] 
    public IAstNode LiteralPartQuestion(Token<ImazenRoutingToken> token) => new TokenNode(token);
    
    [Production("literalPart : LPAREN")]
    public IAstNode LiteralPartLParen(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : RPAREN")]
    public IAstNode LiteralPartRParen(Token<ImazenRoutingToken> token) => new TokenNode(token);
    
    [Production("literalPart : LSQUARE")]
    public IAstNode LiteralPartLSquare(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : RSQUARE")]
    public IAstNode LiteralPartRSquare(Token<ImazenRoutingToken> token) => new TokenNode(token);
    
    [Production("literalPart : LBRACE")] // Allow literal LBRACE if escaped
    public IAstNode LiteralPartLBrace(Token<ImazenRoutingToken> token) => new TokenNode(token);

    [Production("literalPart : RBRACE")] // Allow literal RBRACE if escaped
    public IAstNode LiteralPartRBrace(Token<ImazenRoutingToken> token) => new TokenNode(token);


    // --- Variable Segment (Simplified) ---
    [Production("variableSegment : LBRACE IDENTIFIER (COLON modifierList)? RBRACE")] // Simplified: name, optional colon + modifiers
    public VariableSegment VariableSeg(Token<ImazenRoutingToken> lb, Token<ImazenRoutingToken> nameToken, ValueOption<Group<Token<ImazenRoutingToken>, IAstNode>> colonAndModifiers, Token<ImazenRoutingToken> rb)
    {
        var modifiers = new List<IModifier>();
        if (colonAndModifiers.IsSome) {
             // colonAndModifiers.Value is Group<COLON, modifierList_node>
             // modifierList_node is IAstNode (ModifierListAstNode)
            var modifierListNode = colonAndModifiers.Value.Value as ModifierListAstNode; 
            if (modifierListNode != null) modifiers.AddRange(modifierListNode.Modifiers);
        }
        return new VariableSegment(nameToken.Value, modifiers);
    }

    // --- Flag Part (Simplified, no commas) ---
    [Production("flagPart : LSQUARE flagList? RSQUARE")]
    public FlagList FlagPartProduction(Token<ImazenRoutingToken> lsq, ValueOption<FlagList> list, Token<ImazenRoutingToken> rsq)
        => list.Match(f => f, () => new FlagList(new List<IdentifierNode>()));

    [Production("flagList : flagName (COMMA flagName)*")] 
    public FlagList FlagList(IdentifierNode first, List<ValueTuple<Token<ImazenRoutingToken>, IdentifierNode>> rest) 
    {
        var allIdentifierNodes = new List<IdentifierNode> { first };
        if (rest != null)
        {
            allIdentifierNodes.AddRange(rest.Select(g => g.Item2)); 
        }
        return new FlagList(allIdentifierNodes);
    }

    [Production("flagName : IDENTIFIER")] 
    public IdentifierNode FlagNameIdentifier(Token<ImazenRoutingToken> id) => new IdentifierNode(id.Value);
    [Production("flagName : DASHED_IDENTIFIER")]
    public IdentifierNode FlagNameDashed(Token<ImazenRoutingToken> id) => new IdentifierNode(id.Value);

    // --- Modifier List (Simplified, but using ValueTuple pattern) ---
    [Production("modifierList : modifier (COLON modifier)*")]
    public IAstNode ModifierList(IModifier first, List<ValueTuple<Token<ImazenRoutingToken>, IModifier>> rest) 
    {
        var allModifiers = new List<IModifier> { first };
        if (rest != null)
        {
            allModifiers.AddRange(rest.Select(g => g.Item2));
        }
        var allModifiers = modifierAstNodes.Cast<IModifier>().ToList();
        return new ModifierListAstNode(allModifiers);
    }
    
    // --- Modifier (Simplified) ---
    [Production("modifier : IDENTIFIER")] 
    public IModifier ModifierSimpleByName(Token<ImazenRoutingToken> nameToken) => new SimpleModifier(nameToken.Value);
    
    [Production("modifier : STAR")]
    public IModifier ModifierStar(Token<ImazenRoutingToken> starToken) => new SimpleModifier(starToken.Value);

    [Production("modifier : QUESTION")]
    public IModifier ModifierQuestion(Token<ImazenRoutingToken> qToken) => new SimpleModifier(qToken.Value);

    // --- Argument List (Simplified, using ValueTuple pattern) ---
    [Production("argumentList : argumentToken+")]
    public ArgumentList ArgumentListProduction(List<IAstNode> argumentAstNodes) 
    {
        var allViewModels = argumentAstNodes.Cast<TokenViewModel>().ToList(); 
        return new ArgumentList(allViewModels);
    }

    [Production("argumentToken : IDENTIFIER")] 
    public TokenViewModel ArgumentTokenIdentifier(Token<ImazenRoutingToken> id) => new TokenViewModel(id);
    
    // --- Character Class (Simplified, not really used by above) ---
    [Production("characterClass : LSQUARE IDENTIFIER RSQUARE")] // e.g. [abc]
    public CharacterClassViewModel CharacterClassProduction(Token<ImazenRoutingToken> lsq, Token<ImazenRoutingToken> content, Token<ImazenRoutingToken> rsq) 
        => new CharacterClassViewModel(lsq.Value + content.Value + rsq.Value);

    // charClassToken productions - one for each allowed token type
    [Production("charClassToken : IDENTIFIER")]
    [Production("charClassToken : DASHED_IDENTIFIER")]
    [Production("charClassToken : INT")]
    [Production("charClassToken : LITERAL_CHAR")]
    [Production("charClassToken : ESCAPE_SEQUENCE")]
    [Production("charClassToken : DASH")]
    [Production("charClassToken : CARET")]
    [Production("charClassToken : COMMA")]
    [Production("charClassToken : PIPE")]
    [Production("charClassToken : LPAREN")]
    [Production("charClassToken : RPAREN")]
    [Production("charClassToken : LBRACE")]
    [Production("charClassToken : RBRACE")]
    [Production("charClassToken : PLUS")]
    [Production("charClassToken : STAR")]
    [Production("charClassToken : DIVIDE")]
    [Production("charClassToken : EQUALS")]
    [Production("charClassToken : QUESTION")]
    [Production("charClassToken : AMPERSAND")]
    [Production("charClassToken : COLON")]
    public IAstNode CharClassToken(Token<ImazenRoutingToken> token) => new TokenNode(token);

    // TokenViewModel and CharacterClassViewModel are public as they are used in public AST records
    public record TokenViewModel (Token<ImazenRoutingToken>? RawToken = null, CharacterClassViewModel? CharClass = null) : IAstNode
    {
        public bool IsCharacterClass => CharClass != null;
        public TokenViewModel(Token<ImazenRoutingToken> token) : this(RawToken: token, CharClass: null) {}
        public TokenViewModel(CharacterClassViewModel charClass) : this(RawToken: null, CharClass: charClass) {}

        public string GetValue()
        {
            if (IsCharacterClass) return CharClass!.RawValue;
            if (RawToken?.TokenID == ImazenRoutingToken.INT) return RawToken.IntValue.ToString();
            if (RawToken?.TokenID == ImazenRoutingToken.ESCAPE_SEQUENCE && RawToken.Value.Length > 1) return RawToken.Value.Substring(1, 1); 
            return RawToken?.Value ?? "";
        }
    }
    public record CharacterClassViewModel(string RawValue) : IAstNode;
} 