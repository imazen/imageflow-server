using EnumFastToStringGenerated;

namespace Imazen.Routing.Matching;

[EnumGenerator]
public enum StringConditionKind: byte
{
    Uninitialized = 0,
    [Display(Name = "true")]
    True,
    /// <summary>
    /// Case-insensitive (a-zA-Z)
    /// </summary>
    [Display(Name = "alpha")]
    EnglishAlphabet,
    [Display(Name = "alphanumeric")]
    NumbersAndEnglishAlphabet,
    [Display(Name = "alpha-lower")]
    LowercaseEnglishAlphabet,
    [Display(Name = "alpha-upper")]
    UppercaseEnglishAlphabet,
    /// <summary>
    /// Case-insensitive (a-f0-9A-F)
    /// </summary>
    [Display(Name = "hex")]
    Hexadecimal,
    [Display(Name = "int32")]
    Int32,
    [Display(Name = "int64")]
    Int64,
    [Display(Name = "uint32")]
    UInt32,
    [Display(Name = "uint64")]
    UInt64,
    [Display(Name = "range")]
    IntegerRange,
    [Display(Name = "allow")]
    CharClass,
    [Display(Name = "starts-with-chars")]
    StartsWithNCharClass,
    [Display(Name = "length")]
    CharLength,
    [Display(Name = "guid")]
    Guid,
    [Display(Name = "equals")]
    EqualsOrdinal,
    [Display(Name = "equals-i")]
    EqualsOrdinalIgnoreCase,
    [Display(Name = "equals")]
    EqualsAnyOrdinal,
    [Display(Name = "equals-i")]
    EqualsAnyOrdinalIgnoreCase,
    [Display(Name = "starts-with")]
    StartsWithOrdinal,
    [Display(Name = "starts-with")]
    StartsWithChar,
    [Display(Name = "starts-with")]
    StartsWithCharClass,
    [Display(Name = "starts-with-i")]
    StartsWithCharClassIgnoreCase,
    [Display(Name = "starts-with-i")]
    StartsWithOrdinalIgnoreCase,
    [Display(Name = "starts-with")]
    StartsWithAnyOrdinal,
    [Display(Name = "starts-with-i")]
    StartsWithAnyOrdinalIgnoreCase,
    [Display(Name = "ends-with")]
    EndsWithOrdinal,
    [Display(Name = "ends-with")]
    EndsWithChar,
    [Display(Name = "ends-with")]
    EndsWithCharClass,
    [Display(Name = "ends-with-i")]
    EndsWithCharClassIgnoreCase,
    [Display(Name = "ends-with-i")]
    EndsWithOrdinalIgnoreCase,
    [Display(Name = "ends-with")]
    EndsWithAnyOrdinal,
    [Display(Name = "ends-with-i")]
    EndsWithAnyOrdinalIgnoreCase,
    [Display(Name = "contains")]
    IncludesOrdinal,
    [Display(Name = "contains-i")]
    IncludesOrdinalIgnoreCase,
    [Display(Name = "contains")]
    IncludesAnyOrdinal,
    [Display(Name = "contains-i")]
    // IncludesAnyOrdinalIgnoreCase,
    // [Display(Name = "image-ext-supported")]
    // EndsWithSupportedImageExtension
}
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Method | AttributeTargets.Class,
    AllowMultiple = false)]
internal sealed class DisplayAttribute : Attribute
{
    public string? Name { get; set; }
}