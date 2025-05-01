# Parsing Deep Dive: Imazen.Routing Matching & Templating

This document explains the parsing process for both the URL matching expressions and the URL templating expressions used within the `Imazen.Routing` library.

## 1. Matching Expression Parsing

The goal of the matching expression parser is to take a string like `/images/{category:alpha}/{id:int}?w={width:int:range(10,1000):?}[flag]` and convert it into a structure that can efficiently match incoming URLs and extract variable values.

**Entry Point:** `MultiValueMatcher.TryParse(ReadOnlyMemory<char> expressionWithFlags, ...)`

**Steps:**

1.  **Flag Parsing (`ExpressionFlags.TryParseFromEnd`)**:
    *   The parser first looks for flags enclosed in square brackets (`[...]`) at the *end* of the expression string.
    *   It extracts the flag names (comma-separated, e.g., `[flag1,flag2]`) and validates them (must be `a-z` and `-`).
    *   The remaining part of the expression (without the flags) is passed to the next stage.
    *   The extracted flags influence the overall parsing and matching behavior via `ParsingOptions`.

2.  **Path/Query Separation & Initial Segmentation (`MatchExpression.TryParseWithSmartQuery`)**:
    *   The expression (without flags) is scanned for the first unescaped question mark (`?`) to separate the path part from the query part.
    *   The core segmentation logic resides in `SplitExpressionSections`. This function iterates through the input string, identifying literal segments and logical segments enclosed in curly braces (`{...}`). It correctly handles escaped braces (`\{`, `\}`).
    *   For query strings, `SplitQuerystringChars` further breaks down literal segments by query string delimiters (`?`, `&`, `=`), treating logical segments (`{...}`) as atomic units. This prepares the segments for pair parsing.

3.  **Path Matcher Parsing (`MatchExpression.TryParseInternal`)**:
    *   The segments belonging to the path part are processed.
    *   `MatchSegment.TryParseSegmentExpression` is called for each segment.
        *   If a segment doesn't start with `{`, it's treated as a literal. A `MatchSegment` is created with `SegmentBoundary.Literal` as the start and `SegmentBoundary.LiteralEnd` as the end. Case sensitivity depends on `ParsingOptions`.
        *   If a segment starts with `{` and ends with `}`, `TryParseLogicalSegment` is called.

4.  **Logical Segment Parsing (`MatchSegment.TryParseLogicalSegment`)**:
    *   The content inside the braces (`{...}`) is processed.
    *   It's split by the colon character (`:`) while respecting escaping (`\:`, `\\:`).
    *   The **first part** is tentatively considered the variable `name`. It's validated using `ExpressionParsingHelpers.ValidateSegmentName` (must be a valid identifier, not a reserved condition name). If the first part is a glob pattern (`*`, `**`, `?`) or explicitly a condition, the `name` remains null.
    *   **Subsequent parts** are parsed as conditions or segment boundary modifiers using `TryParseConditionOrSegment`.

5.  **Condition/Boundary Parsing (`MatchSegment.TryParseConditionOrSegment`, `SegmentBoundary.TryCreate`, `StringCondition.TryParse`)**:
    *   Each part after the optional name is analyzed.
    *   Special markers (`*`, `**`, `?`, `optional`) modify the segment's optionality or globbing behavior (though globbing currently doesn't add constraints). `**` allows slashes in the capture.
    *   Other parts are treated as conditions. `ExpressionParsingHelpers.TryParseCondition` parses the `conditionName(arg1, arg2)` syntax, handling escaped commas and parentheses within arguments.
    *   `SegmentBoundary.TryCreate` attempts to interpret the condition as a *segment boundary* modifier (like `starts(prefix)`, `ends(suffix)`, `eq(literal)`, `len(5)`). These directly influence how the start and end points of the segment's capture are determined during matching.
    *   If not a boundary modifier, `StringCondition.TryParse` is used.
        *   It resolves the `conditionName` (handling aliases like `int` -> `int32`, `len` -> `length`) and checks if an ignore-case version exists (`starts` -> `starts-i`).
        *   It determines the expected argument types (`ExpectedArgs`) for the `StringConditionKind`.
        *   It parses the arguments (`TryParseArg`) based on their apparent type (number, string, character class `[...]`, array `a|b|c`). `CharacterClass.TryParseInterned` is used for `[...]`.
        *   A `StringCondition` instance is created, holding the parsed condition data.
    *   The parsed `SegmentBoundary` (start/end) and `StringCondition` list are stored in the `MatchSegment`. Default boundaries (`DefaultStart`, `DefaultEnd`) are used if no specific boundary conditions are found.

6.  **Query Matcher Parsing (`MatchExpression.TryParseWithSmartQuery`)**:
    *   The segments belonging to the query part are processed after splitting by `&`.
    *   Each `key=value` pair is parsed.
        *   The `key` *must* be a literal.
        *   The `value` part (which can contain multiple segments, including literals and `{...}`) is parsed into a separate `MatchExpression` using `MatchExpression.TryParseInternal`, similar to the path parsing.
    *   The results are stored in a dictionary mapping query keys to their corresponding `MatchExpression` value matchers.

7.  **Final Validation (`MultiValueMatcher` constructor, `GetValidationErrors`)**:
    *   Checks for duplicate variable names across the entire path and all query matchers.
    *   Checks for inconsistencies based on flags (e.g., requiring a path expression unless `[ignore-path]` is present).

The final result is a `MultiValueMatcher` containing an optional `MatchExpression` for the path, an optional dictionary of `MatchExpression`s for query values, and the effective `ParsingOptions`.

## 2. Templating Expression Parsing

The goal of the templating expression parser is to take a string like `/users/{id:upper}?src={source:lower:default(web)}[out-flag]` and convert it into a structure that can be efficiently evaluated using a dictionary of variables captured during matching.

**Entry Point:** `MultiTemplate.TryParse(ReadOnlyMemory<char> expressionWithFlags, ...)`

**Steps:**

1.  **Flag Parsing (`ExpressionFlags.TryParseFromEnd`)**:
    *   Identical to the matching parser, this extracts and validates flags (`[...]`) from the end of the template string.
    *   The flags are stored in the resulting `MultiTemplate` but don't currently influence parsing itself (they might be used by the calling code).

2.  **Path/Query Separation (`MultiTemplate.TryParse`)**:
    *   The template (without flags) is split into a path part and a query part based on the first unescaped `?`.

3.  **Path Template Parsing (`StringTemplate.TryParse`)**:
    *   The path part is parsed into a `StringTemplate`.
    *   `StringTemplate.TryParse` iterates through the input span:
        *   It looks for the next unescaped `{`. Everything before it is added as a `LiteralSegment` (handling unescaping of `\\`, `\{`, `\}` etc.).
        *   Once `{` is found, it looks for the matching unescaped `}`.
        *   The content between the braces is passed to `TryParseVariableContent`.

4.  **Variable Segment Parsing (`StringTemplate.TryParseVariableContent`)**:
    *   The content inside the braces (`{...}`) is processed.
    *   It's split by the first unescaped colon (`:`).
    *   The part *before* the colon is the `variableName`. It's validated (must be a valid identifier).
    *   If a `TemplateValidationContext` is provided (linking to a corresponding `MatchExpression`), it checks:
        *   If the `variableName` exists in the matcher's captured variables.
        *   If the variable is marked as optional in the matcher but isn't handled by a suitable transformation (`:or`, `:default`, `:optional`, `:?`) in the template.
    *   The part *after* the colon (if any) contains the transformations.

5.  **Transformations Parsing (`StringTemplate.TryParseTransformations`, `TryParseSingleTransformation`)**:
    *   The transformations string is split by unescaped colons (`:`).
    *   `TryParseSingleTransformation` handles each part:
        *   It separates the `transformName` from its arguments (if any) enclosed in parentheses `(...)`. It correctly handles escaped parentheses and commas within arguments (`\(` `\)` `\,`).
        *   It determines the argument delimiter (`,` by default, but `|` for the `equals` transformation).
        *   Arguments are parsed using `TryParseArguments`, respecting escaping (`\\`, `\(`, `\)`, `\,`).
        *   Based on the `transformName` (case-insensitive) and the presence/absence/count of arguments, it creates the corresponding `ITransformation` object (e.g., `ToLowerTransform`, `MapTransform`, `DefaultTransform`, `OrTransform`, `OptionalMarkerTransform`, `EqualsTransform`).
        *   Validation checks are performed (e.g., `map` needs an even number of args, `or`/`default` need exactly one, `lower`/`upper` accept none).
        *   If a validation context is present, the `or` transformation checks if its fallback variable name exists in the matcher.
    *   The parsed transformations are collected into a list for the `VariableSegment`.

6.  **Query Template Parsing (`MultiTemplate.TryParse`)**:
    *   The query part is split by unescaped ampersands (`&`).
    *   Each resulting pair is split by the first unescaped equals sign (`=`).
    *   Both the key part and the value part are parsed independently into `StringTemplate` instances using the same logic described in step 3.
    *   The resulting `(StringTemplate Key, StringTemplate Value)` pairs are stored in a list.

The final result is a `MultiTemplate` containing an optional `StringTemplate` for the path, an optional list of key/value `StringTemplate` pairs for the query, and any parsed `Flags`. 