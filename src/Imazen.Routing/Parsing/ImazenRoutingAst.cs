using System.Collections.Generic;
using sly.lexer;
using Imazen.Routing.Parsing; // Needed for TokenViewModel

namespace Imazen.Routing.Parsing;

// Base interface for all AST nodes
public interface IAstNode { }

// Represents the entire parsed expression
public record Expression(IAstNode? Path, IReadOnlyList<QueryPair>? Query, FlagList? Flags) : IAstNode;

// Represents the path part of an expression
public record PathExpression(IReadOnlyList<ISegment> Segments) : IAstNode;

// Represents a segment in the path or query value
public interface ISegment : IAstNode { }

// Represents literal text
// Value includes unescaped content
public record LiteralSegment(string Value) : ISegment;

// Represents a variable segment like {name:mod1:mod2}
public record VariableSegment(string? Name, IReadOnlyList<IModifier> Modifiers) : ISegment;

// Interface for modifiers within a variable segment (conditions or transformations)
public interface IModifier : IAstNode { }

// Represents a condition or transformation like name(arg1, arg2)
public record Modifier(string Name, ArgumentList? Arguments) : IModifier;

// Represents simple modifiers without arguments (e.g., ?, *, **)
public record SimpleModifier(string Name) : IModifier;

// Represents a list of arguments for a condition/transformation
// Stores view models wrapping raw tokens or reconstructed char classes.
public record ArgumentList(IReadOnlyList<ImazenRoutingParser.TokenViewModel> Tokens) : IAstNode;

// Represents a query string key-value pair (key is literal, value is sequence of segments)
public record QueryPair(IdentifierNode Key, SegmentListAstNode? ValueSegmentsNode) : IAstNode;

// New AST Node for a list of QueryPairs
public record QueryPairListAstNode(IReadOnlyList<QueryPair> Pairs) : IAstNode;

// Represents the list of flags like [flag1, flag2]
public record FlagList(IReadOnlyList<IdentifierNode> Flags) : IAstNode;

// AST Node for simple identifiers
public record IdentifierNode(string Name) : IAstNode;

// AST Node for wrapping a list of modifiers
public record ModifierListAstNode(IReadOnlyList<IModifier> Modifiers) : IAstNode;

// AST Node for a generic token, useful for literal parts or other token-level nodes
public record TokenNode(Token<ImazenRoutingToken> Token) : IAstNode;

// New AST Node for a list of ISegments
public record SegmentListAstNode(IReadOnlyList<ISegment> Segments) : IAstNode;

// New AST Node for content within a character class
public record CharClassContentNode(IReadOnlyList<TokenNode> Tokens) : IAstNode;

// TODO: Potentially add more specific nodes if needed during parser implementation,
// e.g., separate ConditionNode and TransformationNode inheriting from Modifier if their
// structure or processing logic diverges significantly.
// TODO: Represent CharacterClass `[...]` arguments more specifically than just string?
// For now, keeping arguments as strings simplifies the AST, pushing parsing logic
// (like CharacterClass parsing) to the evaluation/validation stage. 