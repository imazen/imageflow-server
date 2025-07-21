# The problem

Regular expressions are wonderful, except when they're not. 

1. They can be unclear and error-prone, leading to bugs and potential security issues.
2. Mistakes can cause catastrophic backtracking and let a smart bulb overwhelm a 32-core behometh. Take this simple regex rewrite rule, designed to match a series of skus: 

```regex
/sku-list/(\w+\d+)+/
-> /display-products.aspx?series=$1
```
When the incoming URL is `/sku-list/dress1251shirt726cap1362pants944top2154sweater72344/`, the regex engine only takes 2 milliseconds to match, a performance issue not noticeable in QA. But when an inbound like omits the trailing '/', suddenly each request is pegging a CPU core until it times out (1 second on ASP.NET 10), capping your server throughput at a handful of requests per second. 

Now, the author and reader certainly would spot and prevent such an issue in the regex - if they permitted links in such a poor format to exist in the first place. But it might, perhaps, be a bit of an oversight to make regexes the *primary* syntax.


## A better syntax for routing and rewriting

**Goals**
1. Easy and clear for humans to read and write, meeting 95% of use cases.
2. Impossible to express extremely complicated patterns - no repeating groups or backtracking, no need to escape ? and . 
3. More capable for validating numeric input and transforming/mapping values.
4. Clear templating syntax, capable of fallbacks, validations, mappings, defaults, and optionals. 
5. Constant-time matching O(n), making even tiny performance issues impossible.
4. Flexible, so it can be used for routing, rewriting, and other purposes.
5. Intuitive, structural/smart matching and transformation of querystrings.
6. Captures are named, and stop when the next bit can match. `/article_{slug}_` won't match `/article_day_1`, but `/article_{slug}` will. (Like lazy evaluation)
7. No magical behavior. Unfortunately, this includes special treatment for optional segments. `/articles/{slug:?}/` will match `/articles//`, but you probably meant `/articles/{slug:suffix(/):?}`

** Examples **
`/images/{slug:chars([a-zA-Z0-9_-])}/{sku:int}/{image_id:int}.{format:only(jpg|png|gif)}?w={width:int:?}`
`/san/productimages/{image_id}.{format}?format=webp&w={width:default(40)}`



# MatchExpression Syntax Reference

## Segment Boundary Matching

** These affect where captures start and stop, and how the input is divided up **

- `equals(string)` (aliases: `eq`, ``): Matches a segment that equals the specified string.
- `equals-i(string)` (aliases: `eq-i`): Matches a segment that equals the specified string, ignoring case.
- `starts-with(string)` (alias: `starts`): Matches a segment that starts with the specified string.
- `starts-with-i(string)` (alias: `starts-i`): Matches a segment that starts with the specified string, ignoring case.
- `ends-with(string)` (alias: `ends`): Matches a segment that ends with the specified string.
- `ends-with-i(string)` (alias: `ends-i`): Matches a segment that ends with the specified string, ignoring case.
- `len(int)`: Matches a segment with a fixed length specified by the integer. (*Note: `length` is the canonical condition name, but `len` is used for the boundary.*)
- `equals(char)` (aliases: `eq`): Matches a segment that equals the specified character.
- `prefix(string)`: Matches a segment that starts with the specified string, not including it in the captured value.
- `prefix-i(string)`: Matches a segment that starts with the specified string, ignoring case and not including it in the captured value.
- `suffix(string)`: Matches a segment that ends with the specified string, not including it in the captured value.
- `suffix-i(string)`: Matches a segment that ends with the specified string, ignoring case and not including it in the captured value.

## After-matching Segment Conditions

** These are validated *after* the input is parsed into matching segments. Thus, they do not affect
where a capture starts and stops. **

- `alpha()`: Matches a segment that contains only alphabetic characters.
- `alpha-lower()`: Matches a segment that contains only lowercase alphabetic characters.
- `alpha-upper()`: Matches a segment that contains only uppercase alphabetic characters.
- `alphanumeric()`: Matches a segment that contains only alphanumeric characters.
- `hex()` (alias: `hexadecimal`): Matches a segment that contains only hexadecimal characters.
- `int32()` (aliases: `int`, `i32`, `integer`): Matches a segment that represents a valid 32-bit integer.
- `int64()` (aliases: `long`, `i64`): Matches a segment that represents a valid 64-bit integer.
- `uint32()` (aliases: `uint`, `u32`): Matches a segment that represents a valid unsigned 32-bit integer.
- `uint64()` (aliases: `u64`): Matches a segment that represents a valid unsigned 64-bit integer.
- `guid()`: Matches a segment that represents a valid GUID.
- `equals(string1|string2|...)` (alias: `eq`): Matches a segment that equals one of the specified strings.
- `equals-i(string1|string2|...)` (alias: `eq-i`): Matches a segment that equals one of the specified strings, ignoring case.
- `starts-with(string1|string2|...)` (alias: `starts`): Matches a segment that starts with one of the specified strings.
- `starts-with-i(string1|string2|...)` (alias: `starts-i`): Matches a segment that starts with one of the specified strings, ignoring case.
- `ends-with(string1|string2|...)` (alias: `ends`): Matches a segment that ends with one of the specified strings.
- `ends-with-i(string1|string2|...)` (alias: `ends-i`): Matches a segment that ends with one of the specified strings, ignoring case.
- `contains(string)` (alias: `includes`): Matches a segment that contains the specified string.
- `contains-i(string)` (alias: `includes-i`): Matches a segment that contains the specified string, ignoring case.
- `contains(string1|string2|...)` (alias: `includes`): Matches a segment that contains one of the specified strings.
- `contains-i(string1|string2|...)` (alias: `includes-i`): Matches a segment that contains one of the specified strings, ignoring case.
- `range(min,max)` (alias: `integer-range`): Matches a segment that represents an integer within the specified range (inclusive).
- `range(min,)` (alias: `integer-range`): Matches a segment that represents an integer greater than or equal to the specified minimum value.
- `range(,max)` (alias: `integer-range`): Matches a segment that represents an integer less than or equal to the specified maximum value.
- `length(min,max)` (alias: `len`): Matches a segment with a length within the specified range (inclusive).
- `length(min,)` (alias: `len`): Matches a segment with a length greater than or equal to the specified minimum length.
- `length(,max)` (alias: `len`): Matches a segment with a length less than or equal to the specified maximum length.

- `allow(CharacterClass)` (alias: `only`): Matches a segment that contains only characters from the specified character class. (*Note: `allow` is the canonical name used internally.*)
- `starts-with-chars(count,CharacterClass)` (aliases: `starts-with-only`, `starts-chars`): Matches a segment that starts with a specified number of characters from the given character class.


## Optional and Wildcard Segments

- `{segment:condition1:condition2:...:?}`: Marks a segment as optional by appending `?` to the end of the segment conditions.
- `{?}`: Matches any segment optionally.

## Character Classes

Character classes can be specified using square bracket notation, such as `[a-zA-Z]` to match alphabetic characters or `[0-9]` to match digits. Character classes are not affected by [ignore-case]

## Expression flags

At the end of the match express, you can specify `[flags,commma-separated]`

* `ignore-case` Makes path matching case-insensitive, except for character classes.
* `case-sensitive` Makes path matching case-sensitive
* `raw` Matches the raw path and querystring together, rather than structurally parsing and matching the querystring
* `sort-raw-query-first` Alphabetically sorts the querystring key/value pairs before performing raw matching
* `ignore-path` Applies the given query matcher to all paths.
* `import-accept-header` Searches the accept header for image/webp, image/avif, and image/jxl and translates them to &accept.webp=1, &accept.avif=1, &accept.jxl=1
* `require-accept-webp` Only matches if the Accept header is present and includes `image/webp` specifically.
* `require-accept-avif` Only matches if the Accept header is present and includes `image/avif` specifically.
* `require-accept-jxl` Only matches if the Accept header is present and includes `image/jxl` specifically.

## Escaping Special Characters

Special characters like `{`, `}`, `:`, `?`, `*`, `[`, `]`, `(`, `)`, `|`, and `\` can be escaped using a backslash (`\`) to match them literally in segment conditions or literals. `\` should be escaped as `\\`. Unrecognized escape sequences are errors.

## URL rewriting and querystring merging

# URL templates

Variables can be inserted in target strings using ${name} or ${name:transform:transform2}

### Transformations
* `lower` e.g. {var:lower}
* `upper`
* `map(oldvalue,newvalue)`
* `or-var(fallback_var_name)` 
* `default(fallback_value)`
* `equals(value1|value2|value3)` (Previously `allow`)
* `map-default`

TODO: clamp(0,2) (numeric)
TODO: prepend(prefix)
TODO: append(suffix)
TODO: replace(old,new)



## Flags

* `[stop-here]` - prevents application of further rewrite rules
* `[ignore-case]` - Makes path matching case-insensitive, except for character classes.
* `[case-sensitive]` - Makes path matching case-sensitive
* `[keep-query]` - prevents application of further rewrite rules
* `[copy-path]` - prevents application of further rewrite rules

TODO: sha256/auth stuff

process_image=true
pass_through=true
allow_pass_through=true
stop_here=true
case_sensitive=true/false (IIS/ASP.NET default to insensitive, but it's a bad default)



/images/{seo_string_ignored}/{sku:guid}/{image_id:int:range(0,1111111)}{width:int:after(_):optional}.{format:only(jpg|png|gif)}
/azure/centralstorage/skus/{sku:lower}/images/{image_id}.{format}?format=avif&w={width}

/images{path:has_supported_image_type}
/azure/container/{path}







Variables in match strings will be 
{name:condition1:condition2}
They will terminate their matching when the character that follows them is reached. We explain that variables implictly match until their last character

"/images/{id:int:until(/):optional}seoname"
"/images/{id:int}/seoname"
"/image_{id:int}_seoname"
"/image_{id:int}_{w:int}_seoname"
"/image_{id:int}_{w:int:until(_):optional}seoname"
"/image_{id:int}_{w:int:until(_)}/{}"

A trailing ? means the variable (and its trailing character (leading might be also useful?)) is optional.

Partial matches
match_path="/images/{path}"
remove_matched_part_for_children

or
consume_prefix "/images/"


match_path_extension
match_path
match_path_and_query
match_query




## conditions 

alpha, alphanumeric, alphalower, alphaupper, guid, hex, int, chars([a-zA-Z0-9_\\:\\,]), chars([^/]) len(3), length(3), length(0,3),starts_with_chars(3,a-z), equals(string), contains()
 ends_with((.jpg|.png|.gif)), includes(), supported_image_type
ends_with(.jpg|.png|.gif), contains(), 

until and after specify trailing and leading characters that are part of the matching group, but are only useful if combined with `optional`.

# Escaping characters

JSON/TOML escapes include 
\"
\\
\/ (JSON only)
\b
\f
\n
\r
\t
\u followed by four-hex-digits
\UXXXXXXXX - unicode         (U+XXXXXXXX) (TOML only?)

Test with url decoded foreign strings too

# Real-world examples

Setting a max size by folder or authentication status..

We were thinking we could save as a GUID and have some mapping, where we asked for image "St-Croix-Legend-Extreme-Rod…." and we somehow did a lookup to see what the actual GUID image name was but seems we would introduce more issues, like needing to make ImageResizer and handler and not a module to do the lookup and creating an extra database call per image. Doesn't seem like a great solution, any ideas? Just use the descriptive name?

2. You're free to use any URL rewriting solution, the provided Config.Current.Pipeline.Rewrite event, or the included Presets plugin: http://imageresizing.net/plugins/presets

You can also add a Rewrite handler and check the HOST header if you wanted
to use subdomains instead of prefixes. Prefixes are probably better though.



## Example Accept header values
image/avif,image/webp,*/*
image/webp,*/*
*/*
image/png,image/*;q=0.8,*/*;q=0.5
image/webp,image/png,image/svg+xml,image/*;q=0.8,video/*;q=0.8,*/*;q=0.5
image/png,image/svg+xml,image/*;q=0.8,video/*;q=0.8,*/*;q=0.5
image/avif,image/webp,image/apng,image/*,*/*;q=0.8

video
video/webm,video/ogg,video/*;q=0.9,application/ogg;q=0.7,audio/*;q=0.6,*/*;q=0.5    audio/webm,audio/ogg,audio/wav,audio/*;q=0.9,application/ogg;q=0.7,video/*;q=0.6,*/*;q=0.5
*/*