# Matching & Template Syntax - Implementation Status

This document compares the documented syntax (in `matching.md`, `ebnf.md`, `dive.md`, `routing_design.md`) against the actual implementation.

## WARNING: Two Condition Systems Exist

There are **two separate condition systems** in the codebase:

1. **`StringCondition` / `StringConditionKind`** (`Matching/StringCondition.cs`, `StringConditionKind.cs`)
   - Used for route template expression matching (`{var:int}`, `{var:alpha}`, etc.)
   - Works with `MatchSegment`, `MatchExpression`, `MultiValueMatcher`
   - **This is the primary/correct implementation**

2. **`IFastCond` / `Conditions`** (`Layers/Conditions.cs`)
   - Used for fast path preconditions in routing layers
   - `FastCondPathEquals`, `FastCondHasPathPrefixes`, `FastCondHasPathSuffixes`, etc.
   - **Separate system, not unified with StringCondition**

There was an attempt at unification that **failed and produces test failures**. The two systems should remain separate until properly unified.

## WARNING: AST-based Parser Was Dropped

There were **two implementations** of matching/templating parsing:

1. **Hand-coded parser** (current, in `Matching/` folder)
   - `MatchExpression.cs`, `MatchSegment.cs`, `MultiValueMatcher.cs`
   - `StringTemplate.cs`, `MultiTemplate.cs`
   - **This is the surviving implementation**

2. **Csly (sly) parser** (DROPPED in commit `6f1d23f`)
   - Used `sly` package v3.7.3 (C# lexer/parser generator)
   - Was in `src/Imazen.Routing/Parsing/` folder
   - Files removed: `ImazenRoutingAst.cs`, `ImazenRoutingLexer.cs`, `ImazenRoutingParser.cs`, `MatcherAstEvaluator.cs` (898 lines!), `TemplateAstEvaluator.cs`, `UnifiedExpressionParser.cs`
   - **1,989 lines deleted** - this was the failed unification attempt
   - Commit message: "Drop AST-based parsing stub"

The `ebnf.md` document describes the grammar but the AST-based parser that would use it was dropped. The hand-coded parser is what's actually used.

## Status Legend
- **IMPLEMENTED** - Working in code
- **ASPIRATIONAL** - Documented but not implemented
- **OUTDATED** - Was planned/documented but approach changed
- **PARTIAL** - Some functionality works

---

## Matching Conditions (StringConditionKind.cs)

### IMPLEMENTED - After-matching Conditions
| Condition | Aliases | Status |
|-----------|---------|--------|
| `alpha` | `alpha()` | IMPLEMENTED |
| `alpha-lower` | | IMPLEMENTED |
| `alpha-upper` | | IMPLEMENTED |
| `alphanumeric` | | IMPLEMENTED |
| `hex` | `hexadecimal` | IMPLEMENTED |
| `int32` | `int`, `i32`, `integer` | IMPLEMENTED |
| `int64` | `long`, `i64` | IMPLEMENTED |
| `uint32` | `uint`, `u32` | IMPLEMENTED |
| `uint64` | `u64` | IMPLEMENTED |
| `range` | `integer-range` | IMPLEMENTED |
| `chars` | `allow`, `only` | IMPLEMENTED (CharClass) |
| `starts-with-chars` | `starts-with-only`, `starts-chars` | IMPLEMENTED |
| `length` | `len` | IMPLEMENTED |
| `guid` | | IMPLEMENTED |
| `equals` | `eq` | IMPLEMENTED (single & array) |
| `equals-i` | `eq-i` | IMPLEMENTED (single & array) |
| `starts-with` | `starts` | IMPLEMENTED (single & array) |
| `starts-with-i` | `starts-i` | IMPLEMENTED |
| `ends-with` | `ends` | IMPLEMENTED (single & array) |
| `ends-with-i` | `ends-i` | IMPLEMENTED |
| `contains` | `includes` | IMPLEMENTED (single & array) |
| `contains-i` | `includes-i` | IMPLEMENTED |

### OUTDATED - Discontinued
| Condition | Status |
|-----------|--------|
| `image-ext-supported` | DISCONTINUED - "too magic" per code comment |

---

## Segment Boundaries (SegmentBoundary.cs)

### IMPLEMENTED
| Boundary | Aliases | Status |
|----------|---------|--------|
| `equals` | `eq`, `` (empty) | IMPLEMENTED |
| `starts-with` | `starts_with`, `starts` | IMPLEMENTED |
| `ends-with` | `ends_with`, `ends` | IMPLEMENTED |
| `prefix` | | IMPLEMENTED (excludes from capture) |
| `suffix` | | IMPLEMENTED (excludes from capture) |
| `len` | `length` | IMPLEMENTED (fixed length) |

### Ignore-case variants
All boundary functions support `-i` suffix for case-insensitive matching.

---

## Template Transformations (ITransformation.cs)

### IMPLEMENTED
| Transform | Status | Notes |
|-----------|--------|-------|
| `lower` | IMPLEMENTED | `ToLowerInvariant()` |
| `upper` | IMPLEMENTED | `ToUpperInvariant()` |
| `encode` | IMPLEMENTED | `Uri.EscapeDataString()` |
| `map(old,new,...)` | IMPLEMENTED | First match wins |
| `or(fallback_var)` | IMPLEMENTED | Use other variable if empty |
| `default(value)` | IMPLEMENTED | Static default if empty |
| `optional` / `?` | IMPLEMENTED | Marker for optional output |
| `equals(a|b|c)` | IMPLEMENTED | Pass-through validation |
| `map-default(value)` | IMPLEMENTED | Default only if no map matched |

### PLACEHOLDER (parsed but not fully implemented)
| Transform | Status | Notes |
|-----------|--------|-------|
| `allow(a\|b\|c)` | PLACEHOLDER | `AllowTransform` - says "Placeholder: Actual logic needed" |
| `only(a\|b\|c)` | PLACEHOLDER | `OnlyTransform` - says "Placeholder: Actual logic needed" |

### ASPIRATIONAL (in matching.md but not implemented)
| Transform | Status | Notes |
|-----------|--------|-------|
| `clamp(min,max)` | ASPIRATIONAL | Numeric clamping |
| `prepend(prefix)` | ASPIRATIONAL | |
| `append(suffix)` | ASPIRATIONAL | |
| `replace(old,new)` | ASPIRATIONAL | |

---

## Expression Flags

### IMPLEMENTED (ParsingOptions.cs, RoutingExpressionParser.cs)
| Flag | Status | Notes |
|------|--------|-------|
| `ignore-case` / `i` | IMPLEMENTED | Case-insensitive path matching |
| `case-sensitive` | IMPLEMENTED | Explicit case-sensitive |
| `raw` | IMPLEMENTED | Match raw path+query together |
| `sort-raw` | IMPLEMENTED | Sort query before raw matching |
| `ignore-path` | IMPLEMENTED | Apply query matcher to all paths |
| `provider=NAME` | IMPLEMENTED | Route to named provider |
| `v1` | IMPLEMENTED | Version flag |

### ASPIRATIONAL (in matching.md but not implemented)
| Flag | Status | Notes |
|------|--------|-------|
| `import-accept-header` | ASPIRATIONAL | Parse Accept header to query params |
| `require-accept-webp` | ASPIRATIONAL | Match only if Accept includes webp |
| `require-accept-avif` | ASPIRATIONAL | Match only if Accept includes avif |
| `require-accept-jxl` | ASPIRATIONAL | Match only if Accept includes jxl |
| `stop-here` | ASPIRATIONAL | Prevent further rewrite rules |
| `keep-query` | ASPIRATIONAL | |
| `copy-path` | ASPIRATIONAL | |

---

## Special Segment Syntax

### IMPLEMENTED
| Syntax | Status | Notes |
|--------|--------|-------|
| `{name}` | IMPLEMENTED | Named capture |
| `{name:condition}` | IMPLEMENTED | With validation |
| `{name:?}` | IMPLEMENTED | Optional segment |
| `{name*}` | IMPLEMENTED | Glob (capture until end) |
| `{name**}` | IMPLEMENTED | Glob including slashes |
| `[a-zA-Z0-9]` | IMPLEMENTED | Character classes |
| `\{`, `\}`, etc. | IMPLEMENTED | Escape sequences |

### IMPLEMENTED but PARTIAL
| Syntax | Status | Notes |
|--------|--------|-------|
| `{?}` | PARTIAL | Anonymous optional - needs verification |

---

## Query String Matching

### IMPLEMENTED
- Structural query parsing with `?key={value}` syntax
- Query keys must be literals
- Query values can contain variables with conditions

---

## Document Status Summary

### matching.md
- **70% IMPLEMENTED** - Core conditions and boundaries work
- **ASPIRATIONAL** - Accept header flags, some transforms
- **OUTDATED** - `image-ext-supported` discontinued

### ebnf.md
- **ACCURATE** - Describes implemented grammar well
- **REFERENCE** - Good formal specification

### dive.md
- **ACCURATE** - Implementation deep-dive matches code
- **REFERENCE** - Good for understanding parser flow

### routing_design.md
- **MOSTLY ASPIRATIONAL** - High-level architecture goals
- **PARTIAL** - Some concepts implemented (named routes, conditions)
- **OUTDATED** - TypeScript/Rust generation not implemented
- **OUTDATED** - POST/PUT/multipart support not implemented

---

## Recommendations

1. **Archive or clearly mark** `routing_design.md` as aspirational/future
2. **Update** `matching.md` to mark Accept header flags as TODO
3. **Remove** discontinued `image-ext-supported` from docs
4. **Keep** `ebnf.md` and `dive.md` as they accurately describe implementation
5. **Add** `TOML_PROVIDER_DESIGN.md` covers the new provider config (already done)
