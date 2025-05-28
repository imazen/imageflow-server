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
        if (modifier is SimpleModifier sm) { if (sm.Name == "?") { start.IsOptional = true; } }
        else if (modifier is Modifier mwa)
        {
            var argTokens = mwa.Arguments?.Tokens ?? [];
            bool useIgnoreCase = _parsingOptions.PathParsingOptions.OrdinalIgnoreCase; 
            var modName = mwa.Name.ToLowerInvariant();
            var args = ExtractArguments(argTokens, ImazenRoutingToken.COMMA); // Default extraction
            // TODO: Need better arg extraction based on modName

            if (args != null && args.Count == 1) 
            {
                 string arg = args[0]; 
                 switch(modName)
                 {
                     case "equals": case "eq": 
                        start.Kind = BoundaryKind.Literal; start.LiteralValue = arg; start.IgnoreCase = useIgnoreCase; start.IncludeInVariable = true;
                        start.MatchesEntireSegment = true; // Mark start as equals
                        end.Kind = BoundaryKind.Literal; end.MatchesEntireSegment = true; end.UseNextSegmentAsEnd = false;
                        break;
                     case "starts-with": case "starts": 
                        start.Kind = BoundaryKind.Literal; start.LiteralValue = arg; start.IgnoreCase = useIgnoreCase; start.IncludeInVariable = true;
                        start.MatchesEntireSegment = false; // Reset if previously equals
                        end.Kind = BoundaryKind.Default; end.UseNextSegmentAsEnd = true; end.MatchesEntireSegment = false;
                        break;
                     case "ends-with": case "ends": 
                        end.Kind = BoundaryKind.Literal; end.LiteralValue = arg; end.IgnoreCase = useIgnoreCase; end.IncludeInVariable = true;
                        end.UseNextSegmentAsEnd = false; end.MatchesEntireSegment = false;
                        start.Kind = BoundaryKind.Default; start.MatchesEntireSegment = false; // Reset start
                        break;
                     case "prefix": 
                        start.Kind = BoundaryKind.Prefix; start.LiteralValue = arg; start.IgnoreCase = useIgnoreCase; start.IncludeInVariable = false;
                        start.MatchesEntireSegment = false;
                        end.Kind = BoundaryKind.Default; end.UseNextSegmentAsEnd = true; end.MatchesEntireSegment = false; 
                         break;
                     case "suffix": 
                         end.Kind = BoundaryKind.Suffix; end.LiteralValue = arg; end.IgnoreCase = useIgnoreCase; end.IncludeInVariable = false;
                         end.UseNextSegmentAsEnd = false; end.MatchesEntireSegment = false;
                         start.Kind = BoundaryKind.Default; start.MatchesEntireSegment = false;
                         break;
                     case "len": 
                         if (int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out int len) && len > 0)
                         {
                             end.Kind = BoundaryKind.FixedLength; end.FixedLength = len;
                             end.IncludeInVariable = true; 
                             end.UseNextSegmentAsEnd = false;
                             end.MatchesEntireSegment = false;
                             start.Kind = BoundaryKind.Default; start.MatchesEntireSegment = false;
                         }
                         break;
                 }
            }
        }
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
        bool ignoreCase = _parsingOptions.PathParsingOptions.OrdinalIgnoreCase; 
        var conditionName = name.ToLowerInvariant();

        var expectedSeparator = (conditionName == "equals" || conditionName == "contains" || conditionName == "includes") 
                               ? ImazenRoutingToken.PIPE 
                               : ImazenRoutingToken.COMMA;

        // Extract string arguments based on the expected separator
        var arguments = ExtractArguments(tokens.ToList(), expectedSeparator); // Convert to List for ExtractArguments
        if (arguments == null) return false; 
        
        switch (conditionName)
        {
            // --- Boolean checks (no args needed) ---
            case "alpha": return value.IsEnglishAlphabet();
            case "alphanumeric": return value.IsNumbersAndEnglishAlphabet();
            case "alpha-lower": return value.IsLowercaseEnglishAlphabet();
            case "alpha-upper": return value.IsUppercaseEnglishAlphabet();
            case "hex": case "hexadecimal": return value.IsHexadecimal();
            case "int32": case "int": case "i32": case "integer": return value.IsInt32();
            case "int64": case "long": case "i64": return value.IsInt64();
            case "uint32": case "uint": case "u32": return value.IsU32();
            case "uint64": case "u64": return value.IsU64();
            case "guid": return value.IsGuid();
            case "image-ext-supported": return _context.EndsWithSupportedImageExtension(value);

            // --- Conditions using extracted string arguments ---
            case "equals": 
                if (arguments.Count == 0) return false; 
                if (arguments.Count == 1) return ignoreCase ? value.EqualsOrdinalIgnoreCase(arguments[0]) : value.EqualsOrdinal(arguments[0]);
                return ignoreCase ? value.EqualsAnyOrdinalIgnoreCase(arguments.ToArray()) : value.EqualsAnyOrdinal(arguments.ToArray());
            case "contains": case "includes":
                if (arguments.Count == 0) return false; 
                if (arguments.Count == 1) return ignoreCase ? value.IncludesOrdinalIgnoreCase(arguments[0]) : value.IncludesOrdinal(arguments[0]);
                return ignoreCase ? value.IncludesAnyOrdinalIgnoreCase(arguments.ToArray()) : value.IncludesAnyOrdinal(arguments.ToArray());
             case "range": case "integer-range":
                ParseIntRange(arguments, out var minInt, out var maxInt);
                return value.IsInIntegerRangeInclusive(minInt, maxInt);
             case "length":
                 { 
                    var lengthArgs = ExtractArguments(tokens, expectedSeparator); // Use determined separator (COMMA)
                    if (lengthArgs == null) return false;
                    ParseIntRange(lengthArgs, out var minLen, out var maxLen);
                    return value.LengthWithinInclusive(minLen, maxLen);
                 }
            case "allow": case "only":
                 { 
                    // ArgumentList should contain a single TokenViewModel which is a CharacterClassViewModel
                    if (tokens.Count != 1 || !tokens[0].IsCharacterClass) return false; 
                    
                    string charClassRawString = tokens[0].CharClass!.RawValue;

                    if (CharacterClass.TryParseInterned(charClassRawString.AsMemory(), true, out var cc, out _))
                    {
                        return value.IsCharClass(cc);
                    }
                    // Log error?
                    return false;
                 }
            case "starts-with-chars":
                 { 
                     // Expects two arguments: INT, CharacterClass
                     if (tokens.Count != 2) return false;

                     // First arg should be an INT token
                     var countTokenVm = tokens[0];
                     string? countArg = null;
                     if (!countTokenVm.IsCharacterClass && countTokenVm.RawToken?.TokenID == ImazenRoutingToken.INT) {
                         countArg = countTokenVm.RawToken.Value; // Or IntValue.ToString()?
                     } else { return false; } // First arg is not an INT
                     
                     // Second arg should be a CharacterClassViewModel
                     var charClassVm = tokens[1];
                     if (!charClassVm.IsCharacterClass) return false; // Second arg is not a char class
                     string charClassArgRaw = charClassVm.CharClass!.RawValue;
                     
                     if (int.TryParse(countArg, out int count) && 
                         CharacterClass.TryParseInterned(charClassArgRaw.AsMemory(), true, out var cc2, out _))
                     {
                         return value.StartsWithNCharClass(cc2, count);
                     }
                     // Log error?
                     return false;
                 }
            default:
                return true; 
        }
    }

    // Updated Helper to extract argument strings from TokenViewModel list
    // Accepts List<TokenViewModel> 
    private List<string>? ExtractArguments(IReadOnlyList<ImazenRoutingParser.TokenViewModel> allTokens, ImazenRoutingToken expectedSeparator)
    {
        var arguments = new List<string>();
        StringBuilder currentArg = new StringBuilder();
        bool separatorFound = false;
        ImazenRoutingToken? firstSeparator = null;
        bool isInsideCharClass = false;

        foreach (var tokenVm in allTokens)
        {
            // If we hit a character class, treat its reconstructed value as a single argument
            if (tokenVm.IsCharacterClass) {
                if (currentArg.Length > 0) { // Cannot have char class follow other parts in same arg slot
                    arguments.Add(currentArg.ToString());
                    currentArg.Clear();
                    // We need a separator before the char class if not first arg
                    if (!separatorFound) return null; 
                }
                arguments.Add(tokenVm.CharClass!.RawValue);
                // Char class is a full argument, force looking for a separator next if more tokens exist
                separatorFound = true; 
                continue; 
            }

            var token = tokenVm.RawToken;
            if (token == null) continue; 

            bool isSeparator = token.TokenID == ImazenRoutingToken.COMMA || token.TokenID == ImazenRoutingToken.PIPE;
            
            if (isSeparator)
            {
                if (!separatorFound) firstSeparator = token.TokenID;
                if (separatorFound && token.TokenID != firstSeparator) return null; 
                if (token.TokenID != expectedSeparator) return null;

                separatorFound = true;
                arguments.Add(currentArg.ToString());
                currentArg.Clear();
            }
            else
            {
                 // Append value, handling basic escapes - needed for non-char-class args
                 if (token.TokenID == ImazenRoutingToken.ESCAPE_SEQUENCE)
                 {
                     if (token.Value.Length > 1) currentArg.Append(token.Value[1]); // Unescape
                 }
                  else if (token.TokenID == ImazenRoutingToken.INT) 
                 {
                     currentArg.Append(token.IntValue);
                 }
                 else
                 {
                     currentArg.Append(token.Value);
                 }
            }
        }
        // Add the last argument accumulated in StringBuilder
        if (currentArg.Length > 0 || (arguments.Count == 0 && allTokens.Count > 0) || separatorFound) 
        { 
            arguments.Add(currentArg.ToString());
        }

        // Final separator check
        if (separatorFound && firstSeparator != expectedSeparator) return null;

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