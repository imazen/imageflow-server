# Plan: Simplify Matching and Templating Syntax

Analyze the existing matching tests, and add a test for failures so we know how good the error messages are, and for all error messages you see in the Imazen.Routing.Matching source code. Then, duplicate those tests for the new parsing code.

## 1. Goals

*   Analyze the current matching and templating expression syntaxes in `src/Imazen.Routing/Matching` and the new `src/Imazen.Routing/Parsing` code.
*   Identify opportunities to make the syntax more consistent and potentially more human-friendly, especially for the templating part (which is currently a rough draft).
*   Refactor the parsing logic to reduce code duplication (DRY) between matching and templating components.
*   Ensure error messages remain helpful or are improved, providing better context for debugging.
*   Prioritize the stability and backward compatibility of the existing matching syntax, while allowing more flexibility for templating syntax changes.

## 2. Analysis Summary

### Key Differences

*   **Purpose:** Matching validates input/extracts variables; Templating generates output using variables.
*   **Core Logic:** Matching uses `StringCondition` for validation; Templating uses `ITransformation` for value modification.
*   **Argument Parsing:**
    *   Matching: Complex type detection (`GetArgType`), `CharacterClass` parsing, diverse argument types (`[...]`, `a|b|c`).
    *   Templating: Simpler string arguments, specific escaping (`\(\)\,`), comma delimiter default, pipe delimiter (`|`) for `equals`.
*   **Optionality:** Different syntax and slightly different semantics (`:?` in matching vs. `:?`/`:optional`/`:default`/`:or_var` in templating).
*   **Query Keys:** Literals only in matching; can contain variables in templating.
*   **Segment Boundaries:** Core concept in matching (`SegmentBoundary` influencing capture start/end); irrelevant in templating.

### Key Similarities

*   **Structure:** Both use `{...}` for variable segments.
*   **Delimiter:** Both use `:` to separate name from conditions/transforms and between items in the list.
*   **Flags:** Both use `[...]` suffix for expression-level flags.
*   **Escaping:** Both use `\` for escaping special characters - allow all generally expected escapes (plus the needed ones) regardless of context, error on unknown escapes. 
*   **Variable Names:** Both require valid identifiers.

## 3. Proposed Refactoring Areas

### A. Unified Argument Parser

*   **Proposal:** Create a shared `ArgumentParser` utility.
*   **Functionality:** Takes an argument span, delimiter (`:` for top-level, `,` or `|` for args), and escape rules. Returns a `List<string>` of unescaped arguments.
*   **Impact:** Replaces parts of `ExpressionParsingHelpers.TryParseCondition` and `StringTemplate.TryParseArguments`/`UnescapeArgument`.
*   **Challenges:** Matching still needs its type detection (`GetArgType`, `CharacterClass`) logic *after* getting the string list. Templating's `equals` transform using `|` needs specific handling (pass delimiter to parser).

### B. Unified Segment Splitting

*   **Proposal:** Refactor `ExpressionParsingHelpers.SplitExpressionSections` into a general utility or ensure `StringTemplate.TryParse` reuses it.
*   **Functionality:** Takes an expression, delimiters (`{`, `}`), and escape character (`\`), yielding literal and delimited segments.
*   **Impact:** `MatchExpression.TryParse` and `StringTemplate.TryParse` would share the core brace/escape handling.

### C. Consistent Optionality Marker

*   **Proposal:** Standardize on `:?` (and `:optional` as an alias) in both syntaxes where the concept applies.
*   **Matching:** Maintain existing `:?` behavior.
*   **Templating:** Consolidate `:?` and `:optional` to one form. Note that `:default` and `:or_var` still imply optional *handling* (suppressing output on null/empty) which is distinct from the marker itself.
*   **Impact:** Minor syntax cleanup and improved consistency.

### D. Naming Alignment (Conditions vs. Transforms)

*   **Proposal:** Align names where functionality clearly overlaps, potentially using aliases.
    *   Examine `allow`/`only` (matching) vs. `equals` (templating). Direct replacement is hard due to `CharacterClass` vs. string array arguments. Consider adding an `equals(a|b|c)` condition to matching for parity?
    *   Keep distinct names for clearly different concepts (`or_var`, `map`, `default`).
    *   Ensure `map_default` (formerly `other`) is consistently named.
*   **Impact:** Improved clarity, primarily in documentation and code readability. Requires careful analysis of any subtle behavioral differences.

### E. Enhanced Error Reporting

*   **Proposal:** Propagate parsing context (original string/span, current index) through parsing functions. Use custom `ParseException` types or error structs containing this context. Format errors to include position and a snippet of the code.
*   **Impact:** More complex function signatures/return types but significantly better debugging.

## 4. Implementation Strategy

1.  **Foundation:** Start with adding comprehensive unit tests covering matching and templating syntax, including edge cases and escaping.
2.  **Low Hanging Fruit:** Implement Unified Segment Splitting (B) and Consistent Optionality Marker (C). Address Naming Alignment (D) through aliases or documentation updates first.
3.  **Core Refactor:** Tackle the Unified Argument Parser (A), carefully preserving matching's specific argument type detection logic.
4.  **Error Handling:** Implement Enhanced Error Reporting (E) alongside the other refactors.
5.  **Validation:** Continuously run tests and manually verify behaviour, especially for the stable matching component.

## 5. Open Questions & Considerations

*   What level of change to the stable matching syntax is acceptable for DRY benefits? (Likely minimal).
*   Is a fully unified argument *type* parser feasible or desirable, given the different needs? (Probably better to unify the string splitting/unescaping part only).
*   Should templating syntax enforce stricter rules (e.g., quoted string arguments) for easier parsing, potentially at the cost of human-friendliness? 

## 6. Current Status & Next Steps (As of 2024-07-18)

**Goal:** Replace the hand-rolled recursive descent parsers for matching and templating expressions with a unified parser built using the `sly` parser generator library (v3.7.3).

**Progress:**

*   **Setup:**
    *   `sly` (v3.7.3) NuGet package added to `Imazen.Routing`.
    *   Required `System.IO.Pipelines` dependency added.
    *   New directory `src/Imazen.Routing/Parsing/` created.
    *   New files created:
        *   `ImazenRoutingLexer.cs`: Defines tokens using `[Lexeme]` attributes (explicitly defined sugars, generic `INT`, regex for `IDENTIFIER`/`DASHED_IDENTIFIER`/`ESCAPE_SEQUENCE`, explicit `WS`).
        *   `ImazenRoutingAst.cs`: Defines record-based AST nodes (`Expression`, `PathExpression`, `QueryPair`, `LiteralSegment`, `VariableSegment`, `Modifier`, `SimpleModifier`, `ArgumentList`, `FlagList`).
        *   `ImazenRoutingParser.cs`: Defines parser class with `[Production]` rules for basic expression structure (path/query/flags), segments, variables, modifiers. Handles optional (`?`) and list (`*`/`+`) operators using nullable types and `List<T>`. Introduced `TokenViewModel` to handle arguments, including basic Character Class (`[...]`) reconstruction.
        *   `UnifiedExpressionParser.cs`: Facade class to build and invoke the `csly` parser.
        *   `MatcherAstEvaluator.cs`: Class structure created. Implements the core `MatchPathInternal` loop. Includes `BoundaryInfo` helper class and initial logic for deriving/applying boundaries based on AST modifiers (`ApplyModifierToBoundaries`, `CloseOpenSegment`). Basic condition evaluation (`EvaluateStringCondition`) implemented, including calls to `StringConditionMatchingHelpers` and `CharacterClass.TryParseInterned`.
        *   `TemplateAstEvaluator.cs`: Class structure created. Implements AST traversal (`EvaluatePath`, `EvaluateQuery`, `EvaluateSegment`). Maps AST `Modifier` nodes back to original `ITransformation` record instances (`ModifierToTransformation`) for evaluation reuse.
        *   `UnifiedExpressionParserTests.cs`: Basic tests added for parser initialization and placeholder tests for evaluator functionality.
*   **Build Status:** The project currently builds successfully, although the parser grammar and evaluator logic are incomplete.

**Known Issues & TODOs:**

*   **Parser (`ImazenRoutingParser.cs`):**
    *   Literal segment parsing (`literalSegment` rule using `literalPart+`) needs review for robustness, especially concerning complex sequences and escape handling.
    *   Character Class argument parsing (`argumentToken : characterClass`) returns a placeholder; the evaluator relies on `ReconstructArgumentFromTokens`.
    *   Argument handling (`ArgumentList` rule collecting `TokenViewModel`s) needs refinement. The evaluators currently perform context-dependent splitting (`|` vs `,`) and type parsing.
*   **Matcher Evaluator (`MatcherAstEvaluator.cs`):**
    *   **CRITICAL:** The boundary logic implementation (`BoundaryInfo.TryMatch/TryScan`, `ApplyModifierToBoundaries`, `CloseOpenSegment`) requires thorough validation and completion to exactly replicate the original `SegmentBoundary` semantics and edge cases.
    *   `EvaluateStringCondition` needs robust argument parsing (esp. for Character Classes, potentially using `ReconstructArgumentFromTokens` more effectively or directly processing tokens) and handling of `-i` variants for ignore-case conditions.
    *   `ExtractArguments` helper needs refinement for context and complex types.
    *   Query parameter matching logic is missing.
    *   Handling of `ParsingOptions` (e.g., `RawQueryAndPath`, `IgnorePath`) is missing.
*   **Template Evaluator (`TemplateAstEvaluator.cs`):**
    *   Argument extraction/unescaping within `ModifierToTransformation` needs review, especially if the parser doesn't handle it completely.
*   **Linter Errors:** Persistent, potentially spurious errors CS0019 (Operator `??`) and CS1061 (`EndsLiteral` not found) in `MatcherAstEvaluator.cs` are currently being ignored after multiple failed fix attempts.

**Immediate Next Steps:**

1.  **Implement `MatcherAstEvaluator` Boundary Logic:** Focus on completing and verifying the logic within `BoundaryInfo.TryMatch`, `BoundaryInfo.TryScan`, `ApplyModifierToBoundaries`, and `CloseOpenSegment` to ensure semantic equivalence with the original matcher.
2.  **Refine Argument Handling (Matcher):** Implement robust parsing within `EvaluateStringCondition`, likely by refining `ExtractArguments` or `ReconstructArgumentFromTokens` to correctly handle character classes and context-dependent delimiters.
3.  **Write Targeted Tests:** Add unit tests specifically for `BoundaryInfo` logic and `EvaluateStringCondition` argument parsing scenarios. Begin writing comparison tests between `MatcherAstEvaluator` and the original `MatchExpression` for simple cases. 