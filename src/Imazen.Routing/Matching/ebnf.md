# EBNF Grammars for Imazen.Routing Matching & Templating

This document provides Extended Backus-Naur Form (EBNF) grammars for the URL matching and templating syntaxes used in `Imazen.Routing`.

**Note:** These grammars aim for clarity and may simplify some aspects of the implementation's handling of escaping and specific validation rules, which are detailed in the code and the `dive.md` document.

## 1. Matching Expression EBNF

```ebnf
(* --- Top Level --- *)
MatchingExpressionWithFlags = Expression, [ Flags ] ;
Expression                = PathExpression, [ QuerySeparator, QueryExpression ] ;
Flags                     = "[", FlagName, { ",", FlagName }, "]" ;
FlagName                  = lowercase_letter, { lowercase_letter | "-" } ;

(* --- Path Expression --- *)
PathExpression            = { PathSegment } ;
PathSegment               = LiteralSegment | LogicalSegment ;

(* --- Query Expression --- *)
QuerySeparator            = "?" ;
QueryExpression           = QueryPair, { "&", QueryPair } ;
QueryPair                 = QueryKey, "=", QueryValueExpression ;
QueryKey                  = literal_char, { literal_char } ; (* Query keys must be literals *)
QueryValueExpression      = { ValueSegment } ; (* Same structure as PathExpression segments *)
ValueSegment              = LiteralSegment | LogicalSegment ;

(* --- Common Segment Structures --- *)
LiteralSegment            = literal_char, { literal_char } ;
LogicalSegment            = "{", SegmentContent, "}" ;
SegmentContent            = [ VariableName ], { ":", ConditionOrModifier } ;
VariableName              = identifier_start_char, { identifier_char } ; (* Valid C# identifier, not a reserved condition name *)

ConditionOrModifier       = GlobModifier | OptionalModifier | Condition ;
GlobModifier              = "*" | "**" ;
OptionalModifier          = "?" | "optional" ;

Condition                 = ConditionName, [ "(", Arguments, ")" ] ;
ConditionName             = condition_alias, { condition_char } ; (* e.g., "int", "range", "starts", "alpha", "allow"; see matching.md for full list and aliases *)
Arguments                 = Argument, { ArgumentSeparator, Argument } ;
Argument                  = CharacterClass | StringArray | QuotedString | Number | UnquotedString ; (* Parsed based on context/syntax *)
ArgumentSeparator         = "," | "|" ; (* '|' used for specific conditions like 'equals', ',' otherwise *)

CharacterClass            = "[", [ "^" ], { CharClassContent }, "]" ;
CharClassContent          = EscapedChar | Range | SingleChar ;
Range                     = SingleChar, "-", SingleChar ;
StringArray               = Argument, { "|", Argument } ; (* Simplified, | is specific *)

(* --- Basic Lexical Elements (Simplified) --- *)
identifier_start_char     = letter | "_" ;
identifier_char           = letter | digit | "_" ;
condition_alias           = letter, { letter | digit | "-" } ; (* Plus aliases like 'eq', 'len', 'int' etc. *)
condition_char            = letter | digit | "-" ;
literal_char              = any_char - ( "{" | "}" | "?" | "*" ) | EscapedChar ; (* Simplified - depends on context *)
SingleChar                = any_char - ( "]" | "\" | "-" ) ; (* Inside CharClass *)
EscapedChar               = "\", any_char ; (* Specific chars are meaningful escapes *)
letter                    = "a".."z" | "A".."Z" ;
digit                     = "0".."9" ;
lowercase_letter          = "a".."z" ;
Number                    = digit, { digit }, [".", digit, { digit } ] ; (* Simplified *)
QuotedString              = '"', { any_char - '"' | EscapedChar }, '"' |
                            "'", { any_char - "'" | EscapedChar }, "'" ;
UnquotedString            = ? Sequence of characters not matching other Argument types ? ;
```

## 2. Templating Expression EBNF

```ebnf
(* --- Top Level --- *)
TemplateExpressionWithFlags = TemplateExpression, [ Flags ] ;
TemplateExpression          = TemplatePath, [ QuerySeparator, TemplateQuery ] ;
Flags                       = "[", FlagName, { ",", FlagName }, "]" ;
FlagName                    = lowercase_letter, { lowercase_letter | "-" } ;

(* --- Template Path --- *)
TemplatePath                = { TemplateSegment } ;
TemplateSegment             = TemplateLiteral | TemplateVariable ;

(* --- Template Query --- *)
QuerySeparator              = "?" ;
TemplateQuery               = TemplateQueryPair, { "&", TemplateQueryPair } ;
TemplateQueryPair           = TemplateQueryKey, "=", TemplateQueryValue ;
TemplateQueryKey            = { TemplateSegment } ; (* Keys can contain variables *)
TemplateQueryValue          = { TemplateSegment } ; (* Values can contain variables *)

(* --- Common Template Structures --- *)
TemplateLiteral             = literal_char, { literal_char } ;
TemplateVariable            = "{", VariableName, { ":", Transformation }, "}" ;
VariableName                = identifier_start_char, { identifier_char } ; (* Must exist in the corresponding Matcher *)

Transformation              = TransformName, [ "(", Arguments, ")" ] ;
TransformName               = transform_alias, { transform_char } ; (* e.g., "lower", "upper", "map", "default", "or-var", "other", "optional", "encode", "equals" *)
Arguments                   = Argument, { ArgumentSeparator, Argument } ;
Argument                    = (* Similar to Matching, but simpler types needed *) EscapedStringValue ;
ArgumentSeparator           = "," | "|" ; (* '|' used for 'equals', ',' otherwise *)
EscapedStringValue          = { any_char - (")" | "," | "|") | EscapedArgChar } ; (* Simplified *)


(* --- Basic Lexical Elements (Simplified) --- *)
identifier_start_char       = letter | "_" ;
identifier_char             = letter | digit | "_" ;
transform_alias             = letter, { letter | digit | "_" } ; (* Case-insensitive *)
transform_char              = letter | digit | "_" ;
literal_char                = any_char - "{" | EscapedChar ; (* Simplified *)
EscapedChar                 = "\", ( "{" | "}" | "\" ) ;
EscapedArgChar              = "\", ( "(" | ")" | "," | "|" | "\" ) ;
letter                      = "a".."z" | "A".."Z" ;
digit                       = "0".."9" ;
lowercase_letter            = "a".."z" ;
```

## 3. Analysis and Potential Improvements

### Uniformity Challenges

1.  **Argument Syntax:**
    *   **Matching:** Arguments can be diverse (numbers, character classes `[...]`, strings `a|b|c`, unquoted literals). Parsing relies heavily on context and `GetArgType`.
    *   **Templating:** Arguments are generally simpler strings, parsed with escaping rules for `()\,`. The `equals` transform uses `|` as a delimiter, while others use `,`.
    *   *Suggestion:* Could templating adopt a more restricted, uniform argument syntax? Perhaps always comma-delimited, with explicit string quoting if needed? Or could matching simplify its argument parsing if character classes were the only non-simple type?

2.  **Condition vs. Transformation Naming:**
    *   Some concepts overlap but have different names (e.g., `chars([...])` or `only(...)` in matching vs. `equals(a|b|c)` in templating).
    *   Matching uses `len`, `length`, `range`, while templating doesn't have direct equivalents.
    *   Templating has `map`, `or`, `default`, `encode` which have no direct matching counterparts.
    *   *Suggestion:* Standardize names where functionality overlaps. Could `equals` replace `allow`/`only` in matching? Could a length check be added to templating if useful?

3.  **Optional Handling:**
    *   **Matching:** Optionality is marked by `?` *after* a condition or name (e.g., `{name:int:?}`) or as a condition (`optional`).
    *   **Templating:** Optionality (for omitting query pairs) is marked by `:?` or `:optional` as a transformation, or implied by `:default` or `:or`.
    *   *Suggestion:* Using `:?` or `:optional` consistently in both could be clearer. However, the *meaning* of optional differs (match capture vs. template output).

4.  **Query Keys:**
    *   **Matching:** Query keys *must* be literals.
    *   **Templating:** Query keys can be literals or contain `{variables}`.
    *   *Suggestion:* This difference is likely necessary due to their different purposes, but it's a point of divergence.

### Code Reuse & Error Messages

1.  **Segment Splitting:** The logic in `ExpressionParsingHelpers.SplitExpressionSections` (splitting by `{...}`) could potentially be reused by `StringTemplate.TryParse`. Currently, `StringTemplate.TryParse` has its own loop for finding `{` and `}`. Refactoring might share the core brace-finding and escaping logic.

2.  **Argument Parsing:**
    *   `ExpressionParsingHelpers.TryParseCondition` parses the `name(args)` structure for matching. `StringTemplate.TryParseSingleTransformation` does something similar for templating.
    *   `StringCondition.TryParseArg` (matching) and `StringTemplate.TryParseArguments`/`UnescapeArgument` (templating) handle argument parsing/unescaping.
    *   *Suggestion:* Create a shared, robust argument list parser that handles delimiters and escaping consistently. This parser could be configured with the expected delimiter (`,` or `|`). Both matching and templating could use it. The specific type validation (number, string, char class) would still happen separately.

3.  **Error Message Context:**
    *   Parsing failures deep within nested calls (e.g., `TryParseArg` called by `TryParseCondition` called by `TryParseLogicalSegment`) can lose context.
    *   *Suggestion:* Pass down the original expression string or memory slice and the current parsing position. When an error occurs, format it with the position and a snippet of the original string (e.g., "...failed near ':[bad_condition]' at index 25..."). Using custom `ParseException` types might help propagate this context.

4.  **Validation Context:** The `TemplateValidationContext` is a good step towards linking matcher definitions to template parsing. Expanding this to provide more detailed information during parsing could enable richer validation and potentially better error messages (e.g., "Template uses variable 'foo' which is captured as optional by matcher 'bar' but has no default/or/optional marker").

5.  **CharacterClass Parsing:** `CharacterClass.TryParseInterned` is well-contained and reused.

By addressing the argument parsing and segment splitting inconsistencies, and by improving error context propagation, the codebase could become more maintainable and developer-friendly. Standardizing naming where possible would also aid understanding. 


By addressing the argument parsing and segment splitting inconsistencies, and by improving error context propagation, the codebase could become more maintainable and developer-friendly. Standardizing naming where possible would also aid understanding.

## 4. Escaping Complexities in Embedded Contexts

When these matching or templating expressions are embedded within other file formats (like JSON, YAML, TOML), configuration files, environment variables, or even within programming language strings (like C#), the escaping rules can become significantly more complex and potentially confusing.

**Key Challenges:**

1.  **Double Escaping:** The primary issue is the need for multiple layers of escaping.
    *   The expression syntax itself uses the backslash (`\\`) to escape special characters like `{}():,|[]?*` and the backslash itself.
    *   The embedding format often *also* uses the backslash for its own escaping rules (e.g., within JSON strings or C# string literals).
    *   **Example:** To represent a literal backslash (`\\`) within a character class in the matching syntax, you write `[\\\\]`. If this expression is then placed inside a JSON string, the backslash needed for the JSON string escaping must *also* be escaped, leading to `"[\\]"`. In a C# verbatim string (`@"..."`), it might be slightly simpler (`@"[\\\\]"`), but in a regular C# string, it becomes `"[\\]"`.
    *   **Example:** To match a literal parenthesis in a condition like `starts(a\\(b\\))`, the backslash escapes the parenthesis for the expression parser. In JSON, this becomes `"starts(a\\\\(b\\\\))"`. In C#, it's `"starts(a\\\\(b\\\\))"` or `@"starts(a\(b\))"`.

2.  **Conflicting Special Characters:** Characters that are special in the expression syntax (`{}[](),:|?*`) might also conflict with the syntax of the embedding format.
    *   **JSON/C# Strings:** Double quotes (`"`) need escaping within the host format's string.
    *   **YAML/TOML:** Characters like `#` (comments), `:` (mapping), `[` and `{` (structures) can cause conflicts if the expression isn't properly quoted or placed within a block scalar literal (in YAML). YAML's complex quoting and escaping rules can interact unpredictably.
    *   **Environment Variables:** Complex expressions with spaces, quotes, or shell-special characters might be misinterpreted by the shell when reading the variable. Careful quoting is often needed when *setting* the variable.
    *   **String Interpolation:** If expressions are constructed within interpolated strings (e.g., C# `$"..."`), the `{}` characters used for interpolation clash directly with the `{}` used for logical segments/template variables. Double braces (`{{`, `}}`) are often needed to escape them for the interpolation layer.

**Specific Format Examples:**

*   **JSON:** Requires escaping `"` and `\\`. An expression like `/path/{file:chars([a-z\\.])}` becomes `"\\/path\\/{file:chars([a-z\\\\\\\\.])}"`. Note the quadruple `\\\\` to represent a single literal backslash in the character class.
*   **C# (Regular String):** Requires escaping `"` and `\\`. The same example is `"\\/path\\/{file:chars([a-z\\\\\\\\.])}"`.
*   **C# (Verbatim String):** Requires escaping `"` by doubling (`""`). The example is `@"\/path\/{file:chars([a-z\\\.])}"`. Much simpler for backslashes.
*   **YAML:** Highly context-dependent. Using block scalars (`|` or `>`) often avoids most escaping issues. Plain scalars might require quoting (`'` or `"`) and escaping similar to JSON or C# strings, depending on the content. Example (Block Scalar):
    ```yaml
    matcher: |
      /path/{file:chars([a-z\.])} 
    ```
*   **TOML:** Standard strings require escaping `"` and `\\`. Literal strings (`'...'`) only escape `'`. Multiline literal strings (`'''...'''`) allow most characters literally. Example (Multiline Literal String):
    ```toml
    matcher = '''
    /path/{file:chars([a-z\.])}
    '''
    ```
*   **Environment Variable (Bash/Zsh):** Needs careful quoting when setting.
    ```bash
    export MATCHER='/path/{file:chars([a-z\.])}' 
    # Or potentially:
    export MATCHER="/path/{file:chars([a-z\\\\.])}" # Depending on shell interpretation
    ```

**Mitigation/Suggestions:**

*   **Clear Documentation:** Provide explicit examples of how to embed expressions in common formats, highlighting the escaping required.
*   **Configuration Providers:** When reading from config files (JSON, YAML, etc.), ensure the configuration provider correctly handles the unescaping for the format before passing the string to the expression parser.
*   **Verbatim Strings:** Encourage the use of verbatim strings (like C# `@""`) or literal strings (TOML `'''...'''`, YAML `|` or `>`) where available, as they significantly reduce the complexity of backslash escaping.
*   **Alternative Syntax (Future Consideration):** If embedding proves extremely common and problematic, a future version could potentially offer an alternative, less escape-heavy syntax specifically designed for embedding, though this adds complexity to the library itself.
 

 ## 4. Escaping Complexities in Embedded Contexts

When these matching or templating expressions are embedded within other file formats (like JSON, YAML, TOML), configuration files, environment variables, or even within programming language strings (like C#), the escaping rules can become significantly more complex and potentially confusing.

**Key Challenges:**

1.  **Double Escaping:** The primary issue is the need for multiple layers of escaping.
    *   The expression syntax itself uses the backslash (`\\`) to escape special characters like `{}():,|[]?*` and the backslash itself.
    *   The embedding format often *also* uses the backslash for its own escaping rules (e.g., within JSON strings or C# string literals).
    *   **Example:** To represent a literal backslash (`\\`) within a character class in the matching syntax, you write `[\\\\]`. If this expression is then placed inside a JSON string, the backslash needed for the JSON string escaping must *also* be escaped, leading to `"[\\]"`. In a C# verbatim string (`@"..."`), it might be slightly simpler (`@"[\\\\]"`), but in a regular C# string, it becomes `"[\\]"`.
    *   **Example:** To match a literal parenthesis in a condition like `starts(a\\(b\\))`, the backslash escapes the parenthesis for the expression parser. In JSON, this becomes `"starts(a\\\\(b\\\\))"`. In C#, it's `"starts(a\\\\(b\\\\))"` or `@"starts(a\(b\))"`.

2.  **Conflicting Special Characters:** Characters that are special in the expression syntax (`{}[](),:|?*`) might also conflict with the syntax of the embedding format.
    *   **JSON/C# Strings:** Double quotes (`"`) need escaping within the host format's string.
    *   **YAML/TOML:** Characters like `#` (comments), `:` (mapping), `[` and `{` (structures) can cause conflicts if the expression isn't properly quoted or placed within a block scalar literal (in YAML). YAML's complex quoting and escaping rules can interact unpredictably.
    *   **Environment Variables:** Complex expressions with spaces, quotes, or shell-special characters might be misinterpreted by the shell when reading the variable. Careful quoting is often needed when *setting* the variable.
    *   **String Interpolation:** If expressions are constructed within interpolated strings (e.g., C# `$"..."`), the `{}` characters used for interpolation clash directly with the `{}` used for logical segments/template variables. Double braces (`{{`, `}}`) are often needed to escape them for the interpolation layer.

**Specific Format Examples:**

*   **JSON:** Requires escaping `"` and `\\`. An expression like `/path/{file:chars([a-z\\.])}` becomes `"\\/path\\/{file:chars([a-z\\\\\\\\.])}"`. Note the quadruple `\\\\` to represent a single literal backslash in the character class.
*   **C# (Regular String):** Requires escaping `"` and `\\`. The same example is `"\\/path\\/{file:chars([a-z\\\\\\\\.])}"`.
*   **C# (Verbatim String):** Requires escaping `"` by doubling (`""`). The example is `@"\/path\/{file:chars([a-z\\\.])}"`. Much simpler for backslashes.
*   **YAML:** Highly context-dependent. Using block scalars (`|` or `>`) often avoids most escaping issues. Plain scalars might require quoting (`'` or `"`) and escaping similar to JSON or C# strings, depending on the content. Example (Block Scalar):
    ```yaml
    matcher: |
      /path/{file:chars([a-z\.])} 
    ```
*   **TOML:** Standard strings require escaping `"` and `\\`. Literal strings (`'...'`) only escape `'`. Multiline literal strings (`'''...'''`) allow most characters literally. Example (Multiline Literal String):
    ```toml
    matcher = '''
    /path/{file:chars([a-z\.])}
    '''
    ```
*   **Environment Variable (Bash/Zsh):** Needs careful quoting when setting.
    ```bash
    export MATCHER='/path/{file:chars([a-z\.])}' 
    # Or potentially:
    export MATCHER="/path/{file:chars([a-z\\\\.])}" # Depending on shell interpretation
    ```

**Mitigation/Suggestions:**

*   **Clear Documentation:** Provide explicit examples of how to embed expressions in common formats, highlighting the escaping required.
*   **Configuration Providers:** When reading from config files (JSON, YAML, etc.), ensure the configuration provider correctly handles the unescaping for the format before passing the string to the expression parser.
*   **Verbatim Strings:** Encourage the use of verbatim strings (like C# `@""`) or literal strings (TOML `'''...'''`, YAML `|` or `>`) where available, as they significantly reduce the complexity of backslash escaping.
*   **Alternative Syntax (Future Consideration):** If embedding proves extremely common and problematic, a future version could potentially offer an alternative, less escape-heavy syntax specifically designed for embedding, though this adds complexity to the library itself.
 