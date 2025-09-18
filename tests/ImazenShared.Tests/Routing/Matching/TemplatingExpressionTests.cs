using System;
using System.Collections.Generic;
using Imazen.Routing.Matching.Templating;
using Xunit;
using System.Linq;
using Imazen.Routing.Matching; // Added for ExpressionFlags

namespace Imazen.Tests.Routing.Matching;

public class TemplatingExpressionTests
{
    private readonly ITestOutputHelper output;
    public TemplatingExpressionTests(ITestOutputHelper output)
    {
        this.output = output;
    }
    // =================== Parsing Tests ===================

    [Theory]
    // Basic Path
    [InlineData("/users/{id}", true, null)]
    [InlineData("/images/{filename}.jpg", true, null)]
    [InlineData("/literals/only", true, null)]
    // Basic Query
    [InlineData("?key={value}", true, null)]
    [InlineData("/path?key={value}", true, null)]
    [InlineData("/path?k1={v1}&k2={v2}", true, null)]
    [InlineData("?key=literal", true, null)]
    [InlineData("?{keyName}={value}", true, null)] // Templated key
    // Transformations
    [InlineData("/path/{var:lower}", true, null)]
    [InlineData("/path/{var:upper:encode}", true, null)]
    [InlineData("/path/{var:map(a,b):map(c,d):default(x)}", true, null)]
    [InlineData("/path/{var:or(fallback):lower}", true, null)]
    [InlineData("/path?var={val:optional}", true, null)]
    [InlineData("/path?var={val:?}", true, null)]
    // Escaping (Using Verbatim Strings @"")
    [InlineData(@"\{literal\}", true, null)] // Escaped braces
    [InlineData(@"/path/{var:default(a\,b)}", true, null)] // Escaped comma in default
    [InlineData(@"/path/{var:map(a\(1\),b)}", true, null)] // Escaped parens in map
    [InlineData(@"/path/{var:map(a\\b,c)}", true, null)] // Escaped backslash in map
    // New Parsing Tests for allow/map-default
    [InlineData("/path/{var:allow(a,b,c)}", true, null)] // Valid allow
    [InlineData("/path/{var:allow()}", false, "requires at least one argument")] // Invalid allow (no args)
    [InlineData("/path/{var:allow}", false, "requires arguments")] // Invalid allow (no parens)
    [InlineData("/path/{var:map(x,y):map-default(z)}", true, null)] // Valid map-default
    [InlineData("/path/{var:map-default()}", true, null)] // map-default allows going to an empty string
    [InlineData("/path/{var:map-default(a,b)}", false, "exactly one argument")] // Invalid map-default (too many args)
    [InlineData("/path/{var:map-default}", false, "requires arguments")] // Invalid map-default (no parens)
    // New Parsing Tests for Flags
    [InlineData("/path[flag1]", true, null)]
    [InlineData("/path?key=val[flag1,flag2]", true, null)]
    [InlineData("/path[flag-with-hyphen]", true, null)]
    [InlineData("/path[flag-with-hyphen][another-flag]", true, null)]
    [InlineData("/path", true, null)] // No flags
    [InlineData("/path[ ]", false, "Invalid flag")] // Invalid flag char (space)
    [InlineData("/path[flag1", true, null)] // Could be valid use of [] in a path... how can we know?
    [InlineData("/path]", false, "only found closing ]")] // Missing opening [
    [InlineData("/path[flag1,[flag2]]", false, "Invalid flag")] // Invalid nested brackets, comma treated as part of flag name
    // Invalid Syntax
    [InlineData("/path/{var", false, "Unmatched '{'")] // Unmatched {
    [InlineData("/path/var}", false, "Unexpected '}'")] // Unmatched }
    [InlineData("/path/{var:badtransform}", false, "Unknown transformation")] // Unknown transform
    [InlineData("/path/{var:lower()}", false, "does not accept arguments")] // Transform with unexpected args
    [InlineData("/path/{var:map(a)}", false, "even number of arguments")] // Map with odd args
    [InlineData("/path/{var:or()}", false, "exactly one argument")] // Or with wrong arg count
    [InlineData("/path/{var:default}", false, "requires arguments")] // Default requires args
    [InlineData("/path/{:lower}", false, "Variable name cannot be empty")] // Empty variable name
    [InlineData("/path/{guid}", false, "reserved")] // Reserved variable name TODO: Add more
    public void ParseTemplate(string template, bool shouldSucceed, string? expectedErrorSubstring)
    {
        bool success = MultiTemplate.TryParse(template.AsMemory(), out var multiTemplate, out var error);

        if (shouldSucceed)
        {
            Assert.True(success, $"Parsing should succeed for '{template}', but failed with: {error}");
            Assert.NotNull(multiTemplate);
            Assert.Null(error);

        }
        else
        {
            Assert.False(success, $"Parsing should fail for '{template}', but succeeded.");
            Assert.Null(multiTemplate);
            Assert.NotNull(error);
            if (expectedErrorSubstring != null)
            {
                output.WriteLine($"Template: {template}");
                output.WriteLine($"Error: {error}");
                output.WriteLine($"Expected error substring: {expectedErrorSubstring}");
                Assert.Contains(expectedErrorSubstring, error, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // =================== Evaluation Tests ===================

    private static Dictionary<string, string> Vars(params string[] pairs)
    {
        var dict = new Dictionary<string, string>();
        for (int i = 0; i < pairs.Length; i += 2)
        {
            dict.Add(pairs[i], pairs[i + 1]);
        }
        return dict;
    }

    [Theory]
    // Basic Path
    [InlineData("/users/{id}", "/users/123", "id", "123")]
    [InlineData("/files/{name}.{ext}", "/files/report.pdf", "name", "report", "ext", "pdf")]
    [InlineData("literal/path", "literal/path")]
    // Basic Query
    [InlineData("/search?q={term}", "/search?q=testing", "term", "testing")]
    [InlineData("/api?id={id}&ver={v}", "/api?id=abc&ver=2", "id", "abc", "v", "2")]
    [InlineData("/api?{key}={val}", "/api?user=def", "key", "user", "val", "def")]
    // Case Transforms
    [InlineData("/users/{name:lower}", "/users/alice", "name", "Alice")]
    [InlineData("/products/{code:upper}", "/products/XYZ", "code", "Xyz")]
    // Escaping in Template (Using Verbatim Strings @"")
    [InlineData(@"/file\{name\}.txt", "/file{name}.txt")] // Should output literal braces
    // Default Transform
    [InlineData("/items?page={p:default(1)}", "/items?page=1")] // Missing p
    [InlineData("/items?page={p:default(1)}", "/items?page=5", "p", "5")] // Present p
    [InlineData("/items?page={p:default(1)}", "/items?page=1", "p", "")] // Empty p
    // Or Transform
    [InlineData("/data?id={id:or-var(g)}", "/data?id=abc", "id", "abc")] // id present
    [InlineData("/data?id={id:or-var(g)}", "/data?id=fallback123", "g", "fallback123")] // id missing, guid present
    [InlineData("/data?id={id:or-var(g)}", "/data?id=", "id", "")] // id empty, use empty
    [InlineData("/data?id={id:or-var(g)}", "/data?id=")] // Both missing
    // Map Transform
    [InlineData("/status?code={val:map(1,ok):map(2,err)}", "/status?code=ok", "val", "1")]
    [InlineData("/status?code={val:map(1,ok):map(2,err)}", "/status?code=err", "val", "2")]
    [InlineData("/status?code={val:map(1,ok):map(2,err)}", "/status?code=3", "val", "3")] // No match
    [InlineData("/status?code={val:map(1,ok):map(2,err):allow(1,2):default(unk)}", "/status?code=unk", "val", "3")] // No match with default
    [InlineData("/status?code={val:only(1|2):default(unk)}", "/status?code=unk", "val", "3")] // No match with default
    // TODO: broken
    //[InlineData("/status?code={val:only(1|2):default(unk)}", "/status?code=2", "val", "2")] // No match with default
    [InlineData("/status?code={val:map(1,ok):map(2,err):map-default(unk)}", "/status?code=unk", "val", "3")] // No match with default
     // Encode Transform
    [InlineData("/search?q={term:encode}", "/search?q=hello%20world", "term", "hello world")]
    [InlineData("/path/{term:encode}/details", "/path/a%2Fb%2Fc/details", "term", "a/b/c")]
    // Optional Query Params (?)
    [InlineData("/api?user={u:?}", "/api", "u", "")] // Empty optional -> omit pair
    [InlineData("/api?user={u:?}", "/api", "x", "y")] // Missing optional -> omit pair
    [InlineData("/api?user={u:?}", "/api?user=test", "u", "test")] // Present optional -> include pair
    [InlineData("/api?user={u:lower:?}", "/api?user=test", "u", "TEST")] // Transform before optional check
    [InlineData("/api?user={u:lower:?}", "/api", "u", "")] // Empty after transform -> omit
    [InlineData("/api?{k:?}=val", "/api", "k", "")] // Optional key -> omit pair
    [InlineData("/api?key={v:?}", "/api", "v", "")] 
    [InlineData("/api?{k:?}=val&a=b", "/api?a=b", "k", "")] // Omit first optional
    [InlineData("/api?a=b&k={v:?}", "/api?a=b", "v", "")] // Omit second optional
    [InlineData("/api?a=b&k={v:?}", "/api?a=b&k=1", "v", "1")] // Include second optional
    // Optional Query Params (:optional)
    [InlineData("/api?user={u:optional}", "/api", "u", "")] // Empty optional -> omit pair
    // Chaining
    [InlineData("/data/{id:or-var(g):upper}", "/data/FALLBACK123", "g", "fallback123")] // or -> upper
    [InlineData("/path/{val:map(a,b):default(X):upper}", "/path/B", "val", "a")] // map -> default(no) -> upper
    [InlineData("/path/{val:map(a,b):default(X):upper}", "/path/C", "val", "c")] // map(no) -> default -> upper
    // Empty Variable Handling (Not Optional)
    [InlineData("/api?user={u}", "/api?user=", "u", "")] // Empty non-optional var -> key=
    [InlineData("/api?{k}=val", "/api?=val", "k", "")] // Empty non-optional key -> =val
    // New Allow Transform Tests
    [InlineData("/filter?format={f:allow(jpg,png,gif)}", "/filter?format=jpg", "f", "jpg")] // Allow: Value allowed
    [InlineData("/filter?format={f:allow(jpg,png,gif)}", "/filter?format=png", "f", "png")] // Allow: Value allowed
    [InlineData("/filter?format={f:allow(jpg,png,gif)}", "/filter?format=", "f", "webp")] // Allow: Value not allowed -> empty string
    [InlineData("/filter?format={f:allow(jpg,png,gif)}", "/filter?format=", "f", "")] // Allow: Empty input -> empty string
    [InlineData("/filter?format={f:allow(jpg,png,gif)}", "/filter?format=")] // Allow: Missing input -> empty string
    [InlineData("/filter?format={f:allow(jpg,png,gif):upper}", "/filter?format=JPG", "f", "jpg")] // Allow -> Upper
    [InlineData("/filter?format={f:allow(jpg,png,gif):upper}", "/filter?format=", "f", "webp")] // Allow fails -> Upper not applied
    [InlineData("/filter?format={f:upper:allow(JPG,PNG,GIF)}", "/filter?format=JPG", "f", "jpg")] // Upper -> Allow
    // New Allow Transform Tests with Optional
    [InlineData("/filter?format={f:allow(jpg,png,gif):?}", "/filter?format=jpg", "f", "jpg")] // Allow: Value allowed (optional) -> include pair
    [InlineData("/filter?format={f:allow(jpg,png,gif):?}", "/filter", "f", "webp")] // Allow: Value not allowed (optional) -> omit pair
    [InlineData("/filter?format={f:allow(jpg,png,gif):?}", "/filter", "f", "")] // Allow: Empty input (optional) -> omit pair
    [InlineData("/filter?format={f:allow(jpg,png,gif):?}", "/filter")] // Allow: Missing input (optional) -> omit pair
    // New MapDefault Transform Tests
    [InlineData("/status?code={val:map(1,ok):map(2,err):map-default(unk)}", "/status?code=ok", "val", "1")] // Map matches, map-default ignored
    [InlineData("/status?code={val:map(1,ok):map(2,err):map-default(unk)}", "/status?code=err", "val", "2")] // Map matches, map-default ignored
    // dupe [InlineData("/status?code={val:map(1,ok):map(2,err):map-default(unk)}", "/status?code=unk", "val", "3")] // Map no match, map-default applied
    [InlineData("/status?code={val:map(1,ok):map(2,err):map-default(unk)}", "/status?code=unk", "val", "")] // Map no match (empty input), map-default applied
    [InlineData("/status?code={val:map(1,ok):map(2,err):map-default(unk)}", "/status?code=unk")] // Map no match (missing input), map-default applied
    [InlineData("/status?code={val:map-default(unk)}", "/status?code=unk", "val", "any")] // No preceding map, map-default applied
    [InlineData("/status?code={val:map-default(unk)}", "/status?code=unk")] // No preceding map, missing input, map-default applied
    [InlineData("/status?code={val:map(1,ok):map-default(unk):upper}", "/status?code=OK", "val", "1")] // Map matches -> upper
    [InlineData("/status?code={val:map(1,ok):map-default(unk):upper}", "/status?code=UNK", "val", "3")] // Map no match -> map-default -> upper
    // Interaction: Allow and MapDefault
    [InlineData("/process?action={a:map(create,NEW):allow(NEW,OLD):map-default(NONE)}", "/process?action=NEW", "a", "create")] // Map -> Allow (pass) -> map-default(ignored)
    [InlineData("/process?action={a:map(delete,OLD):allow(NEW,OLD):map-default(NONE)}", "/process?action=OLD", "a", "delete")] // Map -> Allow (pass) -> map-default(ignored)
    // This case depends on Allow(fail) returning null, which then stops further processing in the current Evaluate loop. map-default is not reached.
    [InlineData("/process?action={a:map(update,MOD):allow(NEW,OLD):map-default(NONE)}", "/process?action=", "a", "update")] // Map -> Allow (fail) -> empty
    // This case depends on Allow(fail) returning null. map-default is not reached.
    [InlineData("/process?action={a:allow(create,delete):map(create,NEW):map-default(NONE)}", "/process?action=NEW", "a", "create")] // Allow(pass) -> Map -> map-default(ignored)
    // This case depends on Allow(fail) returning null. Map is not reached, map-default is not reached.
    [InlineData("/process?action={a:allow(create,delete):map(create,NEW):map-default(NONE)}", "/process?action=NONE", "a", "update")] // Allow(fail) -> empty
    public void EvalTemplate(string template, string expectedOutput, params string[] variables)
    {
        var validationContext = TemplateValidationContext.VarsAndMatcherFlags(null, null);
        var parsedTemplate = MultiTemplate.Parse(template.AsMemory(), validationContext);
        var inputVars = Vars(variables);
        var prettyPrintInputVars = string.Join(", ", inputVars.Select(kvp => $"{kvp.Key}: {kvp.Value}"));

        if (!parsedTemplate.TryEvaluateToCombinedString(inputVars, out var result, out var error))
        {
            throw new Exception($"Parsing failed: {error}, input vars: {prettyPrintInputVars}, template: {template}");
        }

        output.WriteLine($"Template: {template}");
        output.WriteLine($"Input Vars: {prettyPrintInputVars}");
        output.WriteLine($"Expected: {expectedOutput}");
        output.WriteLine($"Result: {result}");

        Assert.Equal(expectedOutput, result);
    }

    // =================== Validation Parsing Tests ===================

    // Update Helper to return Dictionary for context creation
    private static Dictionary<string, MatcherVariableInfo>? ParseMatcherVarsToDict(string? varInfoString)
    {
        if (string.IsNullOrWhiteSpace(varInfoString)) return new Dictionary<string, MatcherVariableInfo>(StringComparer.OrdinalIgnoreCase); // Return empty dict for null/empty/whitespace

        try
        {
            var dict = new Dictionary<string, MatcherVariableInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in varInfoString.Split(','))
            {
                var pair = part.Trim().Split(':');
                if (pair.Length != 2) throw new FormatException($"Invalid var info format in segment: '{part.Trim()}'");
                var name = pair[0].Trim();
                 if (string.IsNullOrEmpty(name)) throw new FormatException($"Empty variable name in segment: '{part.Trim()}'.");
                var optional = pair[1].Trim().Equals("opt", StringComparison.OrdinalIgnoreCase);
                 if (!optional && !pair[1].Trim().Equals("req", StringComparison.OrdinalIgnoreCase))
                 {
                      throw new FormatException($"Invalid optionality type '{pair[1].Trim()}' in segment: '{part.Trim()}'. Use 'req' or 'opt'.");
                 }

                 if (!dict.TryAdd(name, new MatcherVariableInfo(name, optional))) // Use TryAdd for duplicate check
                 {
                     throw new ArgumentException($"Duplicate matcher variable name provided: {name}");
                 }
            }
            return dict;
        }
        catch(Exception ex)
        {
             throw new ArgumentException($"Failed to parse MatcherVariableInfo string '{varInfoString}': {ex.Message}", ex);
        }
    }

    [Theory]
    // --- Success Cases ---
    [InlineData("/path/{reqVar}", "reqVar:req", true, null)] // Required var used
    [InlineData("/path/literal", "reqVar:req", true, null)] // No vars used
    [InlineData("/path/{optVar:?}", "optVar:opt", true, null)] // Optional handled by :?
    [InlineData("/path/{optVar:optional}", "optVar:opt", true, null)] // Optional handled by :optional
    [InlineData("/path/{optVar:default(x)}", "optVar:opt", true, null)] // Optional handled by :default
    [InlineData("/path/{optVar:or-var(fallback)}", "optVar:opt,fallback:req", true, null)] // Optional handled by :or (fallback defined)
    [InlineData("/path/{optVar:or-var(fallback)}", "optVar:opt,fallback:opt", true, null)] // Optional handled by :or (optional fallback defined)
    [InlineData("/path/{reqVar:or-var(fallback)}", "reqVar:req,fallback:opt", true, null)] // Required handled by :or (optional fallback defined)
    [InlineData("/path/{reqVar:allow(a)}", "reqVar:req", true, null)] // Required var used with other transform
    [InlineData("?k={optVar:?}&v=1", "optVar:opt", true, null)] // Optional in query
    [InlineData("?k={reqVar}", "reqVar:req", true, null)] // Required in query
    [InlineData("/{optVar:?}/{reqVar}", "optVar:opt,reqVar:req", true, null)] // Mix in path

    // --- Failure Cases ---
    // Rule 1: Undefined Variable
    [InlineData("/path/{undefined}", "reqVar:req", false, "uses variable 'undefined' which is not defined")]
    [InlineData("/path/{reqVar:or-var(undef)}", "reqVar:req", false, "uses fallback variable 'undef' which is not defined")]
    [InlineData("/path/{var1}", "", false, "uses variable 'var1' which is not defined")] // Empty var info string
    [InlineData("/path/{var1}", " ", false, "uses variable 'var1' which is not defined")] // Whitespace var info string

    // Rule 2: Unhandled Optional Variable
    [InlineData("/path/{optVar}", "optVar:opt", false, "uses optional variable 'optVar' without providing a fallback")]
    [InlineData("/path/{optVar:lower}", "optVar:opt", false, "uses optional variable 'optVar' without providing a fallback")]
    [InlineData("/path/{optVar:allow(a)}", "optVar:opt", false, "uses optional variable 'optVar' without providing a fallback")]
    [InlineData("?k={optVar}", "optVar:opt", false, "uses optional variable 'optVar' without providing a fallback")]

    // Rule 3: Undefined 'or' Fallback (Rule 1 covers this implicitly now, but test specific case)
    // dupe [InlineData("/path/{reqVar:or-var(undef)}", "reqVar:req", false, "uses fallback variable 'undef' which is not defined")]

    // --- Null Info = No Validation ---
    [InlineData("/path/{anything}", null, false, "uses variable 'anything' which is not defined")] // Should fail when vars is null
    [InlineData("/path/{opt:lower}", null, false, "uses variable 'opt' which is not defined")] // Should fail when vars is null

    public void TestValidatedParsing(string template, string? varInfoString, bool shouldSucceed, string? expectedErrorSubstring)
    {
        var matcherVarsDict = ParseMatcherVarsToDict(varInfoString); // Use updated helper

        // Create the validation context (only populating variables for now)
        TemplateValidationContext? validationContext = null;
        if (matcherVarsDict != null)
        {
            // Note: We parse template flags inside MultiTemplate.TryParse now,
            // so we only need to provide the MatcherVariables here.
            // MatcherFlags would come from parsing the corresponding MatchExpression if needed.
            validationContext = new TemplateValidationContext(
                MatcherVariables: matcherVarsDict,
                Flags: DualExpressionFlags.Empty,
                TemplateFlagRegex: null,
                RequirePath: false,
                RequireSchemeForPaths: false,
                AllowedSchemes: null
                
            );
        }

        // Call the overload that accepts the validation context
        bool success = MultiTemplate.TryParse(template.AsMemory(), true, validationContext, out var multiTemplate, out var error); // Pass context

        if (shouldSucceed)
        {
            Assert.True(success, $"Validated parsing should succeed for '{template}' with vars '{varInfoString}', but failed with: {error}");
            Assert.NotNull(multiTemplate);
            Assert.Null(error);
        }
        else
        {
            Assert.False(success, $"Validated parsing should fail for '{template}' with vars '{varInfoString}', but succeeded.");
            Assert.Null(multiTemplate);
            Assert.NotNull(error);
            if (expectedErrorSubstring != null)
            {
                Assert.Contains(expectedErrorSubstring, error, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // TODO: Add tests for evaluation errors (e.g., missing required variable)
    // TODO: Add tests for validation mode once implemented
    // TODO: Add tests using QueryStringParser once available/implemented
    // Adding tests to tests/ImazenShared.Tests/Routing/Matching/TemplatingExpressionTests.cs

    // =================== End-to-End Tests ===================

    /// <summary>
    ///  MatchAndTemplate is worthless generated garbagae, it doesn't use multivalue matcher among other things
    /// </summary>
    /// <param name="match"></param>
    /// <param name="t"></param>
    /// <param name="input"></param>
    /// <param name="expected"></param>

    [Theory]
    // Basic Path Match -> Path Template
    [InlineData("/users/{id:int}",                           // Matcher
                "/u/{id}",                                   // Template
                "/users/123",                                // Input URL
                "/u/123")]                                   // Expected Output
    // Path Match with transform -> Path Template
    [InlineData("/products/{code:upper}",                    // Matcher
                "/items/{code:lower}",                       // Template
                "/products/ABC",                             // Input URL
                "/items/abc")]                               // Expected Output
    // Path Match -> Query Template
    [InlineData("/items/{itemId}",                           // Matcher
                "/api/query?identifier={itemId}",            // Template
                "/items/xyz789",                             // Input URL
                "/api/query?identifier=xyz789")]             // Expected Output
    // Query Match -> Path Template
    // [InlineData("/path?action={act}",                        // Matcher
    //             "/do/{act}",                                 // Template
    //             "/path?action=create&user=1",                // Input URL (extra query ignored by matcher)
    //             "/do/create")]                               // Expected Output
    // Query Match -> Query Template
    // [InlineData("/data?filter={f:equals(a,b)}",               // Matcher
    //             "/results?f={f:upper}&source=original",      // Template
    //             "/data?filter=a",                            // Input URL
    //             "/results?f=A&source=original")]             // Expected Output
    // Optional Matcher Variable -> Optional Template Handling
    [InlineData("/search/{term:?}",                          // Matcher (optional term)
                "/find?q={term:?:default(all)}",             // Template (:? not strictly needed due to default)
                "/search/",                                  // Input URL (term not present)
                "/find?q=all")]                              // Expected Output (default applied)
    [InlineData("/search/{term:?}",                          // Matcher (optional term)
                "/find?q={term:?}&go=1",                     // Template (:? causes omission)
                "/search/",                                  // Input URL (term not present)
                "/find?go=1")]                               // Expected Output (q param omitted)
    [InlineData("/search/{term:?}",                          // Matcher (optional term)
                "/find?q={term:?}",                            // Template (No optional handling!)
                "/search/",                                  // Input URL (term not present)
                "/find")]                                 // Expected Output (evaluates to empty)
    [InlineData("/search/{term:?}",                          // Matcher (optional term)
        "/find?q={term:default()}",                            // Template (No optional handling!)
        "/search/",                                  // Input URL (term not present)
        "/find?q=")]                                 // Expected Output (evaluates to empty)
    // Matcher with multiple captures -> Template using subset
    // [InlineData("/img/{w:int}x{h:int}/{name}.{ext:eq(jpg)}", // Matcher
    //             "/thumb/{name}?width={w}",                    // Template (ignores h, ext)
    //             "/img/100x50/flower.jpg",                     // Input URL
    //             "/thumb/flower?width=100")]                  // Expected Output
     // Matcher uses :or, Template uses both
    // [InlineData("/file/{id}",                       // Matcher
    //             "/f/{id:upper:or-var(path)}",              // Template
    //             "/file/abc",                                 // Input URL (matches id)
    //             "/f/ABC")]                                   // Expected Output (uses id)
    // [InlineData("/file/{id}",                       // Matcher
    //             "/f/{id:upper:or-var(path:lower)}",              // Template
    //             "/file/",                                    // Input URL (matches path implicitly? No, need actual path)
    //             "/f/TEST/PATH")]                             // Expected Output (uses path) - INPUT NEEDS FIX
    // [InlineData("/file/{id:int:?}/{path:alpha}",             // Matcher (optional id)
    //             "/{path}?id={id:?:default(none)}",           // Template
    //             "/file/testpath",                            // Input URL (no id)
    //             "/testpath?id=none")]                        // Expected Output
    // [InlineData("/file/{id:int:?}/{path:alpha}",             // Matcher (optional id)
    //             "/{path}?id={id:?:default(none)}",           // Template
    //             "/file/123/testpath",                        // Input URL (with id)
    //             "/testpath?id=123")]                         // Expected Output
    public void MatchAndTemplate(
        string match,
        string t,
        string input,
        string expected)
    {
        var matcherExpression = match;
        var templateExpression = t;
        var inputUrl = input;
        var expectedOutputUrl = expected;
        // 1. Parse Matcher (using default options for simplicity)
        // For real usage, options might come from config
        var matcherOptions = ExpressionParsingOptions.Default; // Or path/query specific options? Assume default ok for tests.
        var pathOnlyOptions = PathParsingOptions.DefaultCaseInsensitive.ToExpressionParsingOptions(); // Mimic ASP.NET default?
        var matcher = MatchExpression.Parse(pathOnlyOptions, matcherExpression); // Assuming path matcher for now

        // 2. Get Matcher Info for Validation
        var matcherVars = matcher.GetMatcherVariableInfo();
        // TODO: Get matcher flags if needed for flag validation later
        var validationContext = TemplateValidationContext.VarsAndMatcherFlags(matcherVars, null);

        // 3. Parse Template with Validation
        bool templateParsed = MultiTemplate.TryParse(templateExpression.AsMemory(), validationContext, out var template, out var templateError);
        Assert.True(templateParsed, $"Template parsing failed: {templateError}\nMatcher: {matcherExpression}\nTemplate: {templateExpression}");
        Assert.NotNull(template);

        // 4. Match Input URL (using default context)
        // NOTE: MatchExpression.CaptureDictOrThrow only handles path matching.
        // Need a way to match path + query if matcherExpression includes '?'
        // For now, assuming tests use path-only matchers or simple query matchers where CaptureDictOrThrow is sufficient.
        // A more robust test would use MultiValueMatcher if query matching is involved.
        Dictionary<string, string> capturedVars;
        try
        {
             // Using path-only matcher, we only match the path part of inputUrl
             var pathInput = inputUrl.Split('?')[0];
             capturedVars = matcher.CaptureDictOrThrow(MatchingContext.Default, pathInput);
             // TODO: Handle query part matching if necessary using MultiValueMatcher
        }
        catch(ArgumentException ex)
        {
             Assert.Fail($"Input URL '{inputUrl}' did not match expression '{matcherExpression}': {ex.Message}");
             return; // Satisfy compiler
        }


        // 5. Evaluate Template
        string actualOutput = template.Evaluate(capturedVars);

        // 6. Assert
        Assert.Equal(expectedOutputUrl, actualOutput);
    }
} 
