using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Imazen.Routing.Matching;
using System.Linq;
using System.Diagnostics; // For Debug.Assert
using System.Globalization; // For parsing numbers
using System.Text;
using sly.lexer; // Added for Token<>

namespace Imazen.Routing.Parsing;

// Represents the type of boundary condition
internal enum BoundaryKind
{
    Default, // Start: Match immediately; End: Use next segment
    Literal, // starts-with, ends-with, equals
    Prefix,  // starts-with, non-inclusive
    Suffix,  // ends-with, non-inclusive
    FixedLength // len
}

// Helper class to represent the semantic boundary info derived from AST modifiers
internal class BoundaryInfo
{
    public bool IsOptional { get; set; } = false;
    public BoundaryKind Kind { get; set; } = BoundaryKind.Default;
    public string? LiteralValue { get; set; } = null;
    public bool IgnoreCase { get; set; } = false;
    public int FixedLength { get; set; } = -1; // Only used if Kind is FixedLength
    // Flags derived from Kind and other properties
    public bool IsEndingBoundary { get; set; } = false; // Set explicitly for end boundaries
    public bool IncludeInVariable { get; set; } = true;  // Default true, false for Prefix/Suffix
    public bool MatchesEntireSegment { get; set; } = false;
    public bool UseNextSegmentAsEnd { get; set; } = false;
    public bool IsStartingBoundary => !IsEndingBoundary;

    // Can this boundary type be found by scanning forward?
    public bool SupportsScanning => 
        (Kind == BoundaryKind.Literal || Kind == BoundaryKind.Suffix) && IsEndingBoundary;
    
    // Can this boundary type be matched at the current position?
    public bool SupportsMatching => Kind != BoundaryKind.Default || IsStartingBoundary; // Default end relies on next segment

    // Tries to match the boundary condition at the *start* of the provided text.
    public bool TryMatch(ReadOnlySpan<char> text, out int start, out int end)
    {
        start = 0; 
        end = 0;
        var comparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        switch (Kind)
        {
            case BoundaryKind.Literal:
                 // If MatchesEntireSegment is true, this behaves like 'equals'.
                 // Otherwise, it behaves like 'starts-with'.
                 if (LiteralValue != null) {
                     if (MatchesEntireSegment) { // equals
                         if (text.Equals(LiteralValue.AsSpan(), comparison)) {
                             end = LiteralValue.Length;
                             return true;
                         }
                     } else { // starts-with
                         if (text.StartsWith(LiteralValue.AsSpan(), comparison)) {
                             end = LiteralValue.Length;
                             return true;
                         }
                     }
                 }
                 return false;
            case BoundaryKind.Prefix:
                if (LiteralValue != null && text.StartsWith(LiteralValue.AsSpan(), comparison))
                {
                    end = LiteralValue.Length;
                    return true;
                }
                return false;
            case BoundaryKind.FixedLength:
                 // Cannot match fixed length as a starting condition.
                 return false; 
            case BoundaryKind.Default:
                 // Default start boundary always matches immediately, consumes nothing.
                 return IsStartingBoundary; 
            case BoundaryKind.Suffix:
                 // EndsWith/Suffix cannot be matched as a *starting* boundary condition.
                 return false;
            default:
                return false;
        }
    }

    // Tries to find the *first occurrence* of the boundary condition within the text.
    public bool TryScan(ReadOnlySpan<char> text, out int start, out int end)
    {
         start = -1;
         end = -1;
         // Only support scanning for ending literal/suffix boundaries.
         if (!IsEndingBoundary || !(Kind == BoundaryKind.Literal || Kind == BoundaryKind.Suffix))
         {
             return false;
         }
         // if (!SupportsScanning) return false; // Redundant check

         var comparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
         
         if (LiteralValue != null)
         {
             int index = text.IndexOf(LiteralValue.AsSpan(), comparison);
             if (index != -1)
             {
                 start = index;
                 end = index + LiteralValue.Length;
                 return true;
             }
         }
        return false;
    }
}

/// <summary>
/// Evaluates a parsed AST from a Matching expression against input.
/// This aims to replicate the exact semantics of the original MatchExpression.TryMatch.
/// </summary>
public class MatcherAstEvaluator
{
    private readonly Expression _astRoot;
    private readonly MatchingContext _context;
    private readonly ParsingOptions _parsingOptions; // Store parsing options

    // Internal state for matching process
    private ReadOnlyMemory<char> _input;
    private int _charactersConsumed;
    private int _currentSegmentIndex;
    private int _openSegmentIndex = -1;
    private int _openSegmentAbsoluteStart = -1;
    private int _openSegmentAbsoluteEnd = -1;
    private List<MatchExpressionCapture>? _captures = null;

    // Extracted path segments for easier processing
    private readonly IReadOnlyList<ISegment> _pathSegments;

    // Cache for derived boundary info to avoid re-calculating
    private readonly Dictionary<int, (BoundaryInfo Start, BoundaryInfo End)> _boundaryCache = new();

    public MatcherAstEvaluator(Expression astRoot, MatchingContext context, ParsingOptions parsingOptions)
    {
        _astRoot = astRoot;
        _context = context;
        _parsingOptions = parsingOptions;
        _pathSegments = (astRoot.Path as PathExpression)?.Segments ?? new List<ISegment>();
        // TODO: Handle query and flags based on context.RawQueryAndPath etc.
    }

    public MultiMatchResult Match(string input)
    {
        return Match(input.AsMemory());
    }

    public MultiMatchResult Match(ReadOnlyMemory<char> input)
    {
        // TODO: Handle RawQueryAndPath mode - requires adjusting input based on query/sorting
        // TODO: Handle IgnorePath mode
        // TODO: Handle QueryValueMatchers
        // TODO: Handle ExcessQueryKeys check

        _input = input;
        _charactersConsumed = 0;
        _currentSegmentIndex = 0;
        _openSegmentIndex = -1;
        _openSegmentAbsoluteStart = -1;
        _openSegmentAbsoluteEnd = -1;
        _captures = null;
        _boundaryCache.Clear(); // Clear cache for new match

        if (!MatchPathInternal())
        {
            // TODO: Provide better error based on failing segment index
            return new MultiMatchResult { Success = false, Error = "Path did not match" };
        }

        // If execution reaches here and path matched fully
        return new MultiMatchResult
        {
            Success = true,
            Captures = _captures?.ToDictionary(c => c.Name, c => c.Value),
            // TODO: Populate ExcessQueryKeys and OriginalQuery correctly
            ExcessQueryKeys = null,
            OriginalQuery = null
        };
    }

    private bool MatchPathInternal()
    {
        var inputSpan = _input.Span;
        while (true)
        {
            var remainingInput = inputSpan.Slice(_charactersConsumed);

            // --- End Condition Check ---
            if (_currentSegmentIndex >= _pathSegments.Count)
            {
                // No more segments to match
                if (_openSegmentIndex != -1)
                {   // An open segment needs closing at the end of the input
                    if (!CloseOpenSegment(inputSpan.Length)) return false;
                    // Fall through after closing segment
                }

                if (remainingInput.Length == 0)
                {
                    return true; // All segments matched and all input consumed
                }
                else
                {
                    // Error: Input remaining after all segments matched
                    return false;
                }
            }

            // --- Get Segment and Boundary Info ---
            var currentSegmentAst = _pathSegments[_currentSegmentIndex];
            var (currentStartBoundary, currentEndBoundary) = GetBoundaryInfo(_currentSegmentIndex);

            bool isSearchingStart = _openSegmentIndex != _currentSegmentIndex;
            bool isClosingBoundary = !isSearchingStart;
            BoundaryInfo boundaryToSearch = isSearchingStart ? currentStartBoundary : currentEndBoundary;
            bool isStartingFresh = (_openSegmentIndex == -1);
            int boundaryStarts = -1;
            int boundaryFinishes = -1;
            bool foundBoundaryOrEnd = false;

            // --- Handle Implicit Boundaries (Equals, UseNext) ---
            if (isClosingBoundary) // Trying to find the end of the open segment
            {
                if (boundaryToSearch.MatchesEntireSegment || boundaryToSearch.FixedLength > 0)
                {   // End determined by start (equals) or by fixed length
                    boundaryStarts = _charactersConsumed;
                    boundaryFinishes = (boundaryToSearch.FixedLength > 0) ? _charactersConsumed + boundaryToSearch.FixedLength : _charactersConsumed;
                    // Ensure we don't go past input length for fixed length
                    if (boundaryFinishes > inputSpan.Length) { return false; }
                    foundBoundaryOrEnd = true;
                }
                else if (boundaryToSearch.UseNextSegmentAsEnd) // End is start of next segment
                {
                    // Move to find the start of the *next* segment
                    _currentSegmentIndex++;
                    continue; // Loop again to find boundary for next segment
                }
            }

            // --- Check Searchability ---
            if (!foundBoundaryOrEnd)
            {
                if (!isStartingFresh && !boundaryToSearch.SupportsScanning)
                {
                     // Error: Cannot scan for this boundary mid-input
                    return false;
                }
                if (isStartingFresh && !boundaryToSearch.SupportsMatching)
                {
                    // Error: Cannot match this boundary at current position
                    return false;
                }
            }

            // --- Find Boundary ---
            if (!foundBoundaryOrEnd)
            {
                bool searchResult = (isStartingFresh
                    ? boundaryToSearch.TryMatch(remainingInput, out var s, out var f)
                    : boundaryToSearch.TryScan(remainingInput, out s, out f));
                
                if(searchResult)
                {
                     boundaryStarts = _charactersConsumed + s;
                     boundaryFinishes = _charactersConsumed + f;
                     foundBoundaryOrEnd = true;
                }
            }

             // --- Handle Found/Not Found ---
             if (!foundBoundaryOrEnd)
             {
                  // Boundary not found
                  if (currentSegmentAst is LiteralSegment || !currentStartBoundary.IsOptional) // Literal or Mandatory Variable
                  {
                      return false; // Mandatory segment start not found
                  }
                  else // Optional variable segment
                  {
                       // Skip this optional segment
                       _currentSegmentIndex++;
                       continue;
                  }
             }
             else // Boundary was found
             {
                 Debug.Assert(boundaryStarts != -1 && boundaryFinishes != -1);

                 // --- Close Open Segment (if any) ---
                 if (_openSegmentIndex != -1)
                 {
                     if (!CloseOpenSegment(boundaryStarts)) return false; 
                 }

                 // --- Open New Segment / Advance ---
                 if (isSearchingStart) // We just found the start of currentSegmentAst
                 {
                     if(currentSegmentAst is LiteralSegment litSeg) // Found start of a literal
                     {
                         // Consume the literal
                         _charactersConsumed = boundaryFinishes;
                         _currentSegmentIndex++;
                         // No open segment after literal
                         _openSegmentIndex = -1;
                         _openSegmentAbsoluteStart = -1;
                         _openSegmentAbsoluteEnd = -1;
                         continue; 
                     }
                     else // Found start of a variable segment
                     {
                         _openSegmentIndex = _currentSegmentIndex;
                         _openSegmentAbsoluteStart = boundaryStarts;
                         _openSegmentAbsoluteEnd = boundaryFinishes;
                         // Don't advance _currentSegmentIndex yet, need to find its end
                         continue;
                     }
                 }
                 else // We found the end of the currently open variable segment
                 {
                    // CloseOpenSegment was already called above
                     _currentSegmentIndex++; // Move to next segment
                     continue;
                 }
             }
        }
    }

    // Gets (or creates and caches) BoundaryInfo for a segment index
    private (BoundaryInfo Start, BoundaryInfo End) GetBoundaryInfo(int segmentIndex)
    {
        if (_boundaryCache.TryGetValue(segmentIndex, out var cachedInfo))
        {
            return cachedInfo;
        }

        var segmentAst = _pathSegments[segmentIndex];
        BoundaryInfo startInfo = new BoundaryInfo { Kind = BoundaryKind.Default };
        BoundaryInfo endInfo = new BoundaryInfo { Kind = BoundaryKind.Default, IsEndingBoundary = true, UseNextSegmentAsEnd = true }; // Default end

        if (segmentAst is LiteralSegment literal)
        {
            startInfo.Kind = BoundaryKind.Literal;
            startInfo.LiteralValue = literal.Value;
            startInfo.MatchesEntireSegment = true; // Literals match entirely
            startInfo.IgnoreCase = _parsingOptions.PathParsingOptions.OrdinalIgnoreCase; // Use stored options
            startInfo.IncludeInVariable = false;

            endInfo.Kind = BoundaryKind.Literal;
            endInfo.MatchesEntireSegment = true; // End is implicitly defined by start
        }
        else if (segmentAst is VariableSegment variable)
        {
            foreach (var modifier in variable.Modifiers)
            {
                ApplyModifierToBoundaries(modifier, ref startInfo, ref endInfo);
            }
        }

        var info = (startInfo, endInfo);
        _boundaryCache[segmentIndex] = info;
        return info;
    }

    // Applies AST modifier semantics to boundary info
    private void ApplyModifierToBoundaries(IModifier modifier, ref BoundaryInfo start, ref BoundaryInfo end)
    {
        if (modifier is SimpleModifier sm) { 
            if (sm.Name == "?") { 
                start.IsOptional = true; 
                // Also make the default end boundary optional if the start is optional
                // This mirrors original MatchSegment.IsOptional behavior based on StartsOn.IsOptional
                end.IsOptional = true; 
            } 
        }
        else if (modifier is Modifier mwa)
        {
            var modNameOriginal = mwa.Name;
            var normalizedModName = NormalizeModifierName(modNameOriginal);
            bool ignoreCase = GetEffectiveIgnoreCase(modNameOriginal, normalizedModName);
            string? arg = ExtractSingleArgumentString(mwa.Arguments);

            if (arg != null)
            {
                 switch(normalizedModName)
                 {
                     case "equals": // eq, ""
                        start.Kind = BoundaryKind.Literal; 
                        start.LiteralValue = arg; 
                        start.IgnoreCase = ignoreCase; 
                        start.IncludeInVariable = true;
                        start.MatchesEntireSegment = true; 
                        
                        end.Kind = BoundaryKind.Literal; // End is defined by start for equals
                        end.LiteralValue = arg; // Not strictly needed for end if MatchesEntireSegment is true
                        end.IgnoreCase = ignoreCase;
                        end.IncludeInVariable = true; 
                        end.MatchesEntireSegment = true; 
                        end.UseNextSegmentAsEnd = false;
                        break;
                     case "starts-with": // starts
                        start.Kind = BoundaryKind.Literal; 
                        start.LiteralValue = arg; 
                        start.IgnoreCase = ignoreCase; 
                        start.IncludeInVariable = true;
                        start.MatchesEntireSegment = false; 
                        
                        // Default end: relies on next segment
                        end.Kind = BoundaryKind.Default;
                        end.UseNextSegmentAsEnd = true; 
                        end.MatchesEntireSegment = false;
                        end.LiteralValue = null;
                        end.FixedLength = -1;
                        break;
                     case "ends-with": // ends
                        end.Kind = BoundaryKind.Literal; 
                        end.LiteralValue = arg; 
                        end.IgnoreCase = ignoreCase; 
                        end.IncludeInVariable = true;
                        end.UseNextSegmentAsEnd = false;
                        end.MatchesEntireSegment = false;

                        // Default start: matches immediately
                        start.Kind = BoundaryKind.Default; 
                        start.MatchesEntireSegment = false;
                        start.LiteralValue = null;
                        break;
                     case "prefix": 
                        start.Kind = BoundaryKind.Prefix; 
                        start.LiteralValue = arg; 
                        start.IgnoreCase = ignoreCase; 
                        start.IncludeInVariable = false;
                        start.MatchesEntireSegment = false;

                        end.Kind = BoundaryKind.Default;
                        end.UseNextSegmentAsEnd = true;
                        end.MatchesEntireSegment = false;
                        end.LiteralValue = null;
                        end.FixedLength = -1;
                        break;
                     case "suffix": 
                         end.Kind = BoundaryKind.Suffix; 
                         end.LiteralValue = arg; 
                         end.IgnoreCase = ignoreCase; 
                         end.IncludeInVariable = false;
                         end.UseNextSegmentAsEnd = false;
                         end.MatchesEntireSegment = false;

                         start.Kind = BoundaryKind.Default;
                         start.MatchesEntireSegment = false;
                         start.LiteralValue = null;
                         break;
                     case "len": // length (boundary context)
                         if (int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out int lenVal) && lenVal > 0)
                         {
                             end.Kind = BoundaryKind.FixedLength; 
                             end.FixedLength = lenVal;
                             end.IncludeInVariable = true; 
                             end.UseNextSegmentAsEnd = false;
                             end.MatchesEntireSegment = false; // Length is about the end, not the whole segment "equaling" a length.

                             start.Kind = BoundaryKind.Default; // Start is default for length-based end
                             start.MatchesEntireSegment = false;
                             start.LiteralValue = null;
                         }
                         // Else: Invalid arg for len, boundary info remains unchanged (or could error)
                         break;
                 }
            }
        }
    }

    private string NormalizeModifierName(string name)
    {
        name = name.ToLowerInvariant();
        return name switch
        {
            "eq" => "equals",
            "starts" => "starts-with",
            "ends" => "ends-with",
            "length" => "len", // Canonical for boundary, "length" is for condition
            // Keep other names as is (prefix, suffix, len)
            _ => name
        };
    }

    private bool GetEffectiveIgnoreCase(string originalName, string normalizedName)
    {
        // Check if original name implies ignore case (e.g., ends with "-i")
        // Or if global parsing options dictate ignore case for this type of modifier
        // For now, just use global path parsing options as a base
        bool globalIgnoreCase = _parsingOptions.PathParsingOptions.OrdinalIgnoreCase;
        
        // Specific ignore-case variants like "equals-i" are not yet handled by AST,
        // but if they were, this is where that logic would combine with global settings.
        // For now, if originalName was "equals-i" it becomes "equals" by NormalizeModifierName,
        // and we rely on the global flag.
        // A more robust solution would be to have the parser produce an IgnoreCase flag on the Modifier AST node.
        if (originalName.EndsWith("-i")) return true; // Modifier explicitly requests ignore case

        return globalIgnoreCase; // Fallback to global
    }
    
    private string? ExtractSingleArgumentString(ArgumentList? argList)
    {
        if (argList?.Tokens != null && argList.Tokens.Count == 1)
        {
            // For simplicity, assuming the single token can be directly converted to its string value.
            // This might need refinement if TokenViewModel.GetValue() isn't sufficient or if
            // specific unescaping or type checks are needed here.
            var tokenVm = argList.Tokens[0];
            if (tokenVm.IsCharacterClass)
            {
                // Boundary conditions typically don't use character classes directly as their literal value.
                // This might indicate an error or a need for a different kind of handling.
                // For now, return null if it's a char class used where a simple string is expected for boundary.
                return null; 
            }
            return tokenVm.GetValue();
        }
        return null; // No arguments or more than one argument
    }

    // Closes the currently open segment, validates its content, and adds captures.
    private bool CloseOpenSegment(int closingBoundaryStart)
    {
        if (_openSegmentIndex == -1) return true; // No segment open

        var segmentAst = _pathSegments[_openSegmentIndex];
        var (startBoundary, endBoundary) = GetBoundaryInfo(_openSegmentIndex);

        if (segmentAst is VariableSegment variable)
        {
            // Determine actual variable span based on boundary info
            int variableStart = startBoundary.IncludeInVariable ? _openSegmentAbsoluteStart : _openSegmentAbsoluteEnd;
            int variableEnd = closingBoundaryStart; // Default end point

            // Adjust variableEnd based on the *actual* end boundary found
            if (endBoundary.FixedLength > 0) {
                variableEnd = variableStart + endBoundary.FixedLength;
            } else if (!endBoundary.IncludeInVariable && endBoundary.IsEndingBoundary && endBoundary.Kind == BoundaryKind.Suffix) {
                // If suffix/ends-with boundary excluded its literal, variableEnd remains closingBoundaryStart
                 variableEnd = closingBoundaryStart;
            } else if (endBoundary.MatchesEntireSegment) {
                 // This case shouldn't happen if start was equals, handled earlier?
                 variableEnd = closingBoundaryStart; // Or _openSegmentAbsoluteEnd?
            } // Else: Default case where end is start of next segment, variableEnd = closingBoundaryStart
            
            if (variableEnd < variableStart) variableEnd = variableStart; // Handle zero-length
            if (variableEnd > _input.Length) variableEnd = _input.Length; // Clamp to input length

            var capturedValue = _input.Slice(variableStart, variableEnd - variableStart);

            bool conditionsMet = ApplyConditions(variable.Modifiers, capturedValue.Span);
            if (!conditionsMet)
            {
                 if (startBoundary.IsOptional)
                 {
                     // Optional segment failed condition - effectively skip it.
                     // Don't consume the closing boundary that was found for the *next* segment.
                     _charactersConsumed = _openSegmentAbsoluteStart; // Backtrack consumption
                 }
                 else
                 {
                     return false; // Mandatory segment failed conditions
                 }
            }
            else
            { // Conditions met
                 // Add capture if name exists
                if (!string.IsNullOrEmpty(variable.Name))
                {
                    _captures ??= new List<MatchExpressionCapture>();
                    _captures.Add(new MatchExpressionCapture(variable.Name, capturedValue));
                }
                // Update consumed length *only if conditions met*
                _charactersConsumed = variableEnd; // We consumed up to the boundary that closed this segment
            }
        }
        else
        {
            // Should not happen if literals are handled before closing
            throw new InvalidOperationException("Attempted to close a non-variable segment.");
        }

        // Reset open segment state regardless of condition success (unless optional failed)
        _openSegmentIndex = -1;
        _openSegmentAbsoluteStart = -1;
        _openSegmentAbsoluteEnd = -1;
        return true;
    }

    // Placeholder for applying conditions from AST
    private bool ApplyConditions(IReadOnlyList<IModifier> modifiers, ReadOnlySpan<char> value)
    {
        foreach(var modifierNode in modifiers)
        {
             // Skip boundary-defining modifiers we already processed
             // Skip simple modifiers like '?'
             if (modifierNode is Modifier modWithArgs)
             {
                 var modNameLower = modWithArgs.Name.ToLowerInvariant();
                 // TODO: Check if modNameLower is a boundary condition name based on mapping
                 bool isBoundaryCondition = IsBoundaryModifierName(modNameLower);
                 if (isBoundaryCondition) continue;

                 // TODO: Translate non-boundary modifier name + args to StringConditionKind and evaluate
                 // Example: if (modNameLower == "int") { if (!value.IsInt32()) return false; }
                 // Need a full mapping/evaluation logic here based on StringCondition.cs
                 if (!EvaluateStringCondition(modNameLower, modWithArgs.Arguments, value))
                 {
                     return false;
                 }
             }
        }
        return true;
    }

    // Helper to check if a modifier name corresponds to a boundary condition
    private bool IsBoundaryModifierName(string lowerName)
    {
        // TODO: Make this robust based on actual boundary logic
        return lowerName switch
        {
            "equals" or "eq" or "starts-with" or "starts" or "ends-with" or "ends" or "prefix" or "suffix" or "len" => true,
            _ => false
        };
    }

    // Evaluates string conditions (non-boundary affecting)
    private bool EvaluateStringCondition(string name, ArgumentList? argsAst, ReadOnlySpan<char> value)
    {
        var tokens = argsAst?.Tokens ?? (IReadOnlyList<ImazenRoutingParser.TokenViewModel>)Enumerable.Empty<ImazenRoutingParser.TokenViewModel>(); 
        bool ignoreCase = GetEffectiveIgnoreCase(name, NormalizeModifierName(name)); // Use effective ignore case for conditions too
        var conditionName = NormalizeModifierName(name); // Normalize condition name as well

        ImazenRoutingToken expectedSeparator;
        switch (conditionName)
        {
            case "equals":
            case "contains": // Alias includes
            case "starts-with":
            case "ends-with":
                expectedSeparator = ImazenRoutingToken.PIPE; // These can take string arrays delimited by pipe
                break;
            default:
                expectedSeparator = ImazenRoutingToken.COMMA;
                break;
        }
        
        var arguments = ExtractArguments(tokens.ToList(), expectedSeparator, conditionName == "allow" || conditionName == "starts-with-chars"); 
        if (arguments == null && (conditionName != "alpha" && conditionName != "alphanumeric" && 
                                 conditionName != "alpha-lower" && conditionName != "alpha-upper" && 
                                 conditionName != "hex" && conditionName != "int32" && 
                                 conditionName != "int64" && conditionName != "uint32" && 
                                 conditionName != "uint64" && conditionName != "guid" && 
                                 conditionName != "image-ext-supported"))
        {
            // If arguments are required but couldn't be parsed, it's a failure.
            // Boolean checks without args are handled in the switch.
            return false; 
        }
        
        
        switch (conditionName)
        {
            // --- Boolean checks (no args needed by ExtractArguments) ---
            case "alpha": return value.IsEnglishAlphabet();
            case "alphanumeric": return value.IsNumbersAndEnglishAlphabet();
            case "alpha-lower": return value.IsLowercaseEnglishAlphabet();
            case "alpha-upper": return value.IsUppercaseEnglishAlphabet();
            case "hex": /*case "hexadecimal":*/ return value.IsHexadecimal(); // hexadecimal is normalized to hex
            case "int32": /*case "int": case "i32": case "integer":*/ return value.IsInt32();
            case "int64": /*case "long": case "i64":*/ return value.IsInt64();
            case "uint32": /*case "uint": case "u32":*/ return value.IsU32();
            case "uint64": /*case "u64":*/ return value.IsU64();
            case "guid": return value.IsGuid();
            case "image-ext-supported": return _context.EndsWithSupportedImageExtension(value);

            // --- Conditions using extracted string arguments ---
            case "equals": 
                if (arguments == null || arguments.Count == 0) return false; 
                if (arguments.Count == 1) return ignoreCase ? value.EqualsOrdinalIgnoreCase(arguments[0]) : value.EqualsOrdinal(arguments[0]);
                return ignoreCase ? value.EqualsAnyOrdinalIgnoreCase(arguments.ToArray()) : value.EqualsAnyOrdinal(arguments.ToArray());
            
            case "starts-with":
                if (arguments == null || arguments.Count == 0) return false;
                if (arguments.Count == 1) return ignoreCase ? value.StartsWithOrdinalIgnoreCase(arguments[0]) : value.StartsWithOrdinal(arguments[0]);
                return ignoreCase ? value.StartsWithAnyOrdinalIgnoreCase(arguments.ToArray()) : value.StartsWithAnyOrdinal(arguments.ToArray());

            case "ends-with":
                if (arguments == null || arguments.Count == 0) return false;
                if (arguments.Count == 1) return ignoreCase ? value.EndsWithOrdinalIgnoreCase(arguments[0]) : value.EndsWithOrdinal(arguments[0]);
                return ignoreCase ? value.EndsWithAnyOrdinalIgnoreCase(arguments.ToArray()) : value.EndsWithAnyOrdinal(arguments.ToArray());

            case "contains": // includes
                if (arguments == null || arguments.Count == 0) return false; 
                if (arguments.Count == 1) return ignoreCase ? value.IncludesOrdinalIgnoreCase(arguments[0]) : value.IncludesOrdinal(arguments[0]);
                return ignoreCase ? value.IncludesAnyOrdinalIgnoreCase(arguments.ToArray()) : value.IncludesAnyOrdinal(arguments.ToArray());
            
             case "range": // integer-range
                if (arguments == null) return false;
                ParseIntRange(arguments, out var minInt, out var maxInt);
                return value.IsInIntegerRangeInclusive(minInt, maxInt);
            
            case "len": // length (condition context)
                 { 
                    if (arguments == null) return false;
                    ParseIntRange(arguments, out var minLen, out var maxLen);
                    return value.LengthWithinInclusive(minLen, maxLen);
                 }
            case "allow": // only
                 { 
                    if (arguments == null || arguments.Count != 1) return false; // Expect a single raw char class string
                    string charClassRawString = arguments[0];
                    if (CharacterClass.TryParseInterned(charClassRawString.AsMemory(), true, out var cc, out _))
                    {
                        return value.IsCharClass(cc);
                    }
                    return false;
                 }
            case "starts-with-chars":
                 { 
                     if (arguments == null || arguments.Count != 2) return false; // Expects: count, charClassString
                     
                     string countArg = arguments[0];
                     string charClassArgRaw = arguments[1];
                     
                     if (int.TryParse(countArg, out int count) && 
                         CharacterClass.TryParseInterned(charClassArgRaw.AsMemory(), true, out var cc2, out _))
                     {
                         return value.StartsWithNCharClass(cc2, count);
                     }
                     return false;
                 }
            default:
                // If the condition isn't recognized as one that uses arguments or is a known boolean one,
                // it might be a custom or future condition. Defaulting to true might be too permissive.
                // Consider throwing an error or returning false if strictness is desired.
                // For now, if it reached here and wasn't a known arg-less boolean, and args were null (as per check above),
                // it implies an unknown condition. The original code defaulted to true for unknown modifiers.
                // However, if arguments were expected (i.e., arguments != null here but condition not matched),
                // that would be an error handled by the null check of `arguments` at the start for most cases.
                // This path mainly covers unknown conditions that *don't* take arguments according to their definition.
                // This could be a simple flag-like condition not yet implemented.
                // The safest is to return false for unknown conditions that are not boundary conditions.
                Debug.WriteLine($"Warning: Unhandled condition '{conditionName}' in MatcherAstEvaluator.EvaluateStringCondition");
                return false; 
        }
    }

    // Updated Helper to extract argument strings from TokenViewModel list
    private List<string>? ExtractArguments(
        IReadOnlyList<ImazenRoutingParser.TokenViewModel> allTokens, 
        ImazenRoutingToken expectedSeparator,
        bool rawCharClassAsSingleArg = false)
    {
        var arguments = new List<string>();
        StringBuilder currentArgContent = new StringBuilder();
        bool separatorEncounteredInCurrentGroup = false;
        ImazenRoutingToken? firstSeparatorUsed = null;

        for (int i = 0; i < allTokens.Count; i++)
        {
            var tokenVm = allTokens[i];

            if (tokenVm.IsCharacterClass)
            {
                if (rawCharClassAsSingleArg) {
                    if (currentArgContent.Length > 0) // Char class must be its own argument if raw
                    {
                        // This implies something like "text[abc]", which is not typical for `allow` or `starts-with-chars`
                        // Expected: `allow([abc])` or `starts-with-chars(1,[abc])` where `[abc]` is a full arg.
                        return null; // Invalid structure prior to char class as raw arg
                    }
                    arguments.Add(tokenVm.CharClass!.RawValue); // Add the raw `[abc]` string
                    currentArgContent.Clear();
                    separatorEncounteredInCurrentGroup = false; // Reset for next argument
                    if (i < allTokens.Count -1 && !(allTokens[i+1].RawToken?.TokenID == expectedSeparator) && 
                        (allTokens[i+1].RawToken?.TokenID != ImazenRoutingToken.RPAREN)) // Check for RPAREN for last arg
                    {
                        // If char class is not the last argument, it must be followed by a separator.
                        // Or if it is the last, it should be followed by RPAREN (implicitly handled by parser structure)
                        return null; // Missing separator after char class as raw arg
                    }
                    continue; // Move to next token
                }
                // If not rawCharClassAsSingleArg, treat [ and ] as part of a normal string arg if not separated.
                currentArgContent.Append(tokenVm.CharClass!.RawValue); 
                continue;
            }

            var token = tokenVm.RawToken;
            if (token == null) continue; // Should not happen if IsCharacterClass is false

            bool isCurrentTokenSeparator = token.TokenID == expectedSeparator;
            bool isOtherSeparator = (token.TokenID == ImazenRoutingToken.COMMA || token.TokenID == ImazenRoutingToken.PIPE) && !isCurrentTokenSeparator;

            if (isCurrentTokenSeparator)
            {
                if (firstSeparatorUsed == null) firstSeparatorUsed = token.TokenID;
                else if (firstSeparatorUsed != token.TokenID) return null; // Mixed separators

                arguments.Add(currentArgContent.ToString());
                currentArgContent.Clear();
                separatorEncounteredInCurrentGroup = true;
            }
            else if (isOtherSeparator)
            {
                return null; // Wrong separator used
            }
            else // Regular token part of an argument
            {
                if (separatorEncounteredInCurrentGroup && currentArgContent.Length == 0)
                {
                    // We found a separator, this token starts a new argument.
                    separatorEncounteredInCurrentGroup = false; 
                }
                 
                if (token.TokenID == ImazenRoutingToken.ESCAPE_SEQUENCE)
                {
                    if (token.Value.Length > 1) currentArgContent.Append(token.Value[1]); // Basic unescape
                }
                else if (token.TokenID == ImazenRoutingToken.INT)
                {
                    currentArgContent.Append(token.IntValue);
                }
                else
                {
                    currentArgContent.Append(token.Value);
                }
            }
        }

        // Add the last accumulated argument
        if (currentArgContent.Length > 0 || allTokens.Count == 0 || separatorEncounteredInCurrentGroup || (allTokens.Any() && !arguments.Any() && !allTokens.Last().IsCharacterClass) )
        {
             // Add if content, or if it was an empty list of tokens (yields one empty arg), 
             // or if last thing was a separator (yields empty arg), 
             // or if there were tokens but no args yet and last token wasn't a char class that got its own arg slot.
            arguments.Add(currentArgContent.ToString());
        }
        
        // If only one argument was expected and it was a char class handled by rawCharClassAsSingleArg,
        // arguments list might be empty if the char class was the *only* token. Correct this.
        if (rawCharClassAsSingleArg && allTokens.Count == 1 && allTokens[0].IsCharacterClass && arguments.Count == 0)
        {
            arguments.Add(allTokens[0].CharClass!.RawValue);
        }


        return arguments;
    }

    // Updated Helper to parse int range arguments (takes List<string>)
    private void ParseIntRange(List<string> args, out int? min, out int? max)
    {
         min = null;
         max = null;
         // Check for null or empty list first
        if (args == null || args.Count == 0) 
        { 
            return; 
        }

        if (args.Count == 1)
        {
             if (int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
             { min = max = val; }
        }
        else if (args.Count == 2)
        {
             if (string.IsNullOrEmpty(args[0]) && !string.IsNullOrEmpty(args[1])) {
                 if (int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxVal)) { max = maxVal; }
            }
            else if (!string.IsNullOrEmpty(args[0]) && string.IsNullOrEmpty(args[1])) {
                 if (int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minVal)) { min = minVal; }
            }
            else if (!string.IsNullOrEmpty(args[0]) && !string.IsNullOrEmpty(args[1])) {
                 if (int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minVal)) { min = minVal; }
                 if (int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxVal)) { max = maxVal; }
            }
        }
        // If args.Count > 2, it's invalid for range/length, min/max remain null
    }

     public readonly record struct MatchExpressionCapture(string Name, ReadOnlyMemory<char> Value);
} 
