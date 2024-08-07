# Design of route matcher syntax

This system never backtracks, ensuring that the matching process is always O(n) in the length of the path. Not all conditions are involved in capturing; many are validated after input is segmented.


When a querystring is specified in the expression, it is structurally parsed and matched regardless of 
how the querystring is arranged, and extra unspecified keys are ignored.

`/images/{sku:int}/{image_id:int}.{format:only(jpg|png|gif)}?w={width:int:?}`
`/san/productimages/{{image_id}.{{format}}?format=webp&w={{width:default(40)}}`

`/images/{sku:int}/{image_id:int}.{format:only(jpg|png|gif)}?w={w:int:?}&width={width:int:?}&http-accept={:contains(image/webp)}`


`/san/productimages/{{image_id}.{{format}}?format=webp&w={{width:or-var(w):default(40)}}`


# MatchExpression Syntax Reference

## Segment Boundary Matching

** These affect where captures start and stop, and how the input is divided up **

- `equals(string)`: Matches a segment that equals the specified string.
- `equals-i(string)`: Matches a segment that equals the specified string, ignoring case.
- `starts-with(string)`: Matches a segment that starts with the specified string.
- `starts-with-i(string)`: Matches a segment that starts with the specified string, ignoring case.
- `ends-with(string)`: Matches a segment that ends with the specified string.
- `ends-with-i(string)`: Matches a segment that ends with the specified string, ignoring case.
- `len(int)`: Matches a segment with a fixed length specified by the integer.
- `equals(char)`: Matches a segment that equals the specified character.
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
- `hex()`: Matches a segment that contains only hexadecimal characters.
- `int32()`: Matches a segment that represents a valid 32-bit integer.
- `int64()`: Matches a segment that represents a valid 64-bit integer.
- `guid()`: Matches a segment that represents a valid GUID.
- `equals(string1|string2|...)`: Matches a segment that equals one of the specified strings.
- `equals-i(string1|string2|...)`: Matches a segment that equals one of the specified strings, ignoring case.
- `starts-with(string1|string2|...)`: Matches a segment that starts with one of the specified strings.
- `starts-with-i(string1|string2|...)`: Matches a segment that starts with one of the specified strings, ignoring case.
- `ends-with(string1|string2|...)`: Matches a segment that ends with one of the specified strings.
- `ends-with-i(string1|string2|...)`: Matches a segment that ends with one of the specified strings, ignoring case.
- `contains(string)`: Matches a segment that contains the specified string.
- `contains-i(string)`: Matches a segment that contains the specified string, ignoring case.
- `contains(string1|string2|...)`: Matches a segment that contains one of the specified strings.
- `contains-i(string1|string2|...)`: Matches a segment that contains one of the specified strings, ignoring case.
- `range(min,max)`: Matches a segment that represents an integer within the specified range (inclusive).
- `range(min,)`: Matches a segment that represents an integer greater than or equal to the specified minimum value.
- `range(,max)`: Matches a segment that represents an integer less than or equal to the specified maximum value.
- `length(min,max)`: Matches a segment with a length within the specified range (inclusive).
- `length(min,)`: Matches a segment with a length greater than or equal to the specified minimum length.
- `length(,max)`: Matches a segment with a length less than or equal to the specified maximum length.
- `image-ext-supported()`: Matches a segment that represents a supported image file extension.
- `allowed-chars(CharacterClass)`: Matches a segment that contains only characters from the specified character class.
- `starts-with-chars(count,CharacterClass)`: Matches a segment that starts with a specified number of characters from the given character class.
- `image-ext-supported()`: Matches a segment that represents a supported (for image processing) image file extension.

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
* `require-accept-webp` Only matches if the Accept header is present and includes `image/webp` specifically.

## Escaping Special Characters

Special characters like `{`, `}`, `:`, `?`, `*`, `[`, `]`, `(`, `)`, `|`, and `\` can be escaped using a backslash (`\`) to match them literally in segment conditions or literals.

## URL rewriting and querystring merging

# URL templates

Variables can be inserted in target strings using ${name} or ${name:transform:transform2}

### Transformations
* `lower` e.g. {var:lower}
* `upper`
* more to come

## Flags

* `[stop-here]` - prevents application of further rewrite rules
* 


TODO: sha256/auth stuff

process_image=true
pass_through=true
allow_pass_through=true
stop_here=true
case_sensitive=true/false (IIS/ASP.NET default to insensitive, but it's a bad default)



/images/{seo_string_ignored}/{sku:guid}/{image_id:int:range(0,1111111)}{width:int:after(_):optional}.{format:only(jpg|png|gif)}
/azure/centralstorage/skus/{sku:lower}/images/{image_id}.{format}?format=avif&w={width}

/images/{path:has_supported_image_type}
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

alpha, alphanumeric, alphalower, alphaupper, guid, hex, int, only([a-zA-Z0-9_\:\,]), only(^/) len(3), length(3), length(0,3),starts_with_only(3,a-z), until(/), after(/): optional/?, equals(string), everything/**
 ends_with((.jpg|.png|.gif)), includes(), supported_image_type
ends_with(.jpg|.png|.gif), until(), after(), includes(), 

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

We were thinking we could save as a GUID and have some mapping, where we asked for image “St-Croix-Legend-Extreme-Rod….” and we somehow did a lookup to see what the actual GUID image name was but seems we would introduce more issues, like needing to make ImageResizer and handler and not a module to do the lookup and creating an extra database call per image. Doesn’t seem like a great solution, any ideas? Just use the descriptive name?

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