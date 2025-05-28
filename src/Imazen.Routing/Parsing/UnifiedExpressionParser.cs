using sly.lexer;
using sly.parser;
using sly.parser.generator;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Imazen.Routing.Parsing;

/// <summary>
/// Defines the parsing mode to adjust validation and error messages.
/// </summary>
public enum ParseMode
{
    /// <summary>
    /// Parsing a matching expression.
    /// </summary>
    Matching,
    /// <summary>
    /// Parsing a templating expression.
    /// </summary>
    Templating
}

/// <summary>
/// Result of parsing an expression.
/// </summary>
public class ParseResult
{
    public bool IsError => Error != null || SyntaxErrors?.Count > 0;
    public Expression? AstRoot { get; private set; }
    public string? Error { get; private set; } // General or validation errors
    public List<ParseError>? SyntaxErrors { get; private set; } // Errors from csly

    internal static ParseResult Ok(Expression root)
    {
        return new ParseResult { AstRoot = root };
    }

    internal static ParseResult Fail(string error)
    {
        return new ParseResult { Error = error };
    }

    internal static ParseResult Fail(List<ParseError> errors)
    {
        return new ParseResult { SyntaxErrors = errors };
    }
}

/// <summary>
/// Facade for parsing Imazen Routing expressions (both matching and templating)
/// using the csly parser generator.
/// </summary>
public static class UnifiedExpressionParser
{
    private static readonly Lazy<Parser<ImazenRoutingToken, IAstNode>> ParserInstance =
        new Lazy<Parser<ImazenRoutingToken, IAstNode>>(() =>
        {
            var parser = new ImazenRoutingParser();
            var builder = new ParserBuilder<ImazenRoutingToken, IAstNode>();
            var buildResult = builder.BuildParser(parser, ParserType.LL_RECURSIVE_DESCENT, "root");

            if (buildResult.IsError)
            {
                var errors = string.Join("\n", buildResult.Errors.Select(e => e.Message));
                throw new InvalidOperationException($"Failed to build ImazenRoutingParser: \n{errors}");
            }

            return buildResult.Result;
        });

    public static ParseResult TryParse(string expression, ParseMode mode)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return ParseResult.Fail("Expression cannot be empty or whitespace.");
        }

        Parser<ImazenRoutingToken, IAstNode> parser;
        try
        {
            parser = ParserInstance.Value;
        }
        catch (Exception ex)
        {
            // Catch errors during parser build (Lazy initialization)
            return ParseResult.Fail($"Failed to initialize parser: {ex.Message}");
        }

        var result = parser.Parse(expression);

        if (result.IsError || result.Result == null)
        {
            // Collect csly syntax errors
            return ParseResult.Fail(result.Errors ?? new List<ParseError>());
        }

        if (result.Result is not Expression astRoot)
        {
            return ParseResult.Fail($"Parsing succeeded but resulted in unexpected AST root type: {result.Result.GetType().Name}");
        }

        // TODO: Add mode-specific validation steps here
        // Example:
        // if (mode == ParseMode.Matching)
        // {
        //     var validationError = ValidateMatcherAst(astRoot);
        //     if (validationError != null) return ParseResult.Fail(validationError);
        // }
        // else // ParseMode.Templating
        // {
        //     var validationError = ValidateTemplateAst(astRoot);
        //     if (validationError != null) return ParseResult.Fail(validationError);
        // }

        return ParseResult.Ok(astRoot);
    }

    // Placeholder for matching-specific AST validation
    private static string? ValidateMatcherAst(Expression astRoot)
    {
        // Check query key literals, variable definitions, etc.
        return null;
    }

    // Placeholder for templating-specific AST validation
    private static string? ValidateTemplateAst(Expression astRoot)
    {
        // Check if variables used have fallbacks if corresponding matcher var is optional, etc.
        return null;
    }
} 