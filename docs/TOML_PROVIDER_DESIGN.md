# TOML Provider Configuration Design

## Overview

This document describes the TOML configuration schema for blob providers and routes in Imageflow Server. The design separates config-time values from runtime values and uses a parser system for flexible route-to-provider parameter mapping.

## Variable Reference Syntax

Two distinct syntaxes for variable references:

### Config-time Interpolation: `${prefix.name}`

Resolved when TOML is parsed. Prefixes:
- `${env.NAME}` - environment variable
- `${app.NAME}` - app-provided variable (e.g., `approot`)
- `${vars.NAME}` - user-defined variable
- `${folders.NAME}` - folder variable (validated to exist)
- `${files.NAME}` - file variable (validated to exist)
- `${secrets.NAME}` - secret variable (redacted in diagnostics)

### Route-time Templates: `{name}` or `{name:constraint}`

Resolved at request time from URL captures or parser extractions.
- `{path}` - captures/extracts value into `path`
- `{bucket:equals(a|b|c)}` - with validation constraint
- `{id:int}` - integer constraint
- `{name:alpha}` - alphabetic constraint
- `{path*}` - glob/remainder capture

## Provider Configuration

### Syntax: `[providers.NAME]`

Each provider is a named table, not an array:

```toml
[providers.s3-products]
type = "s3"

# config.* - Resolved at construct time or when config file changes
# Uses ${...} for env/secret interpolation
config.region = "us-east-1"
config.endpoint = "https://s3.us-east-1.amazonaws.com"
config.access_key_id = "${secrets.aws_key}"
config.secret_access_key = "${secrets.aws_secret}"

# params.* - Runtime parameters provided by routes
# Templates define validation constraints
# MUST use named variables, not just {:constraint}
params.bucket = "{bucket:equals(prod|staging|dev)}"
params.key = "{key}"

# path.parsers - Extract params from route template output
# ALL parsers MUST define ALL required params (bucket and key here)
# First matching parser wins
path.parsers = [
    "{bucket}/{key}",
    "{key} [bucket=default-bucket]",
]
```

### Config vs Params

| Aspect | `config.*` | `params.*` |
|--------|-----------|------------|
| When resolved | Config parse time / file change | Request time |
| Interpolation | `${...}` config-time | `{...}` route-time |
| Source | TOML file, env vars | Route captures, parsers |
| Changes require | App restart (or hot-reload) | Nothing |

### Parser Rules

1. **All parsers must define all required params** - Every parser in `path.parsers[]` must provide values for all `params.*` defined by the provider

2. **First match wins** - Parsers are tried in order; first successful match is used

3. **Static values via flags** - Parsers can set static param values using `[key=value]` flag syntax:
   ```toml
   path.parsers = [
       "{bucket}/{key}",                           # extracts both
       "{key} [bucket=default-bucket]",            # extracts key, static bucket
       "archive/{key} [bucket=archive, region=us-west-2]",  # static values
   ]
   ```

4. **Named variables required** - Param patterns must use named variables:
   ```toml
   # VALID
   params.bucket = "{bucket:equals(a|b|c)}"

   # INVALID - no name
   params.bucket = "{:equals(a|b|c)}"
   ```

5. **Parser vars must match param names** - Variables extracted by parsers must exactly match `params.*` names

## Route Configuration

### Basic Syntax

```toml
[[routes]]
route = "<match> => <template> [flags]"
```

Or without template for `pass-vars`:

```toml
[[routes]]
route = "<match> => [provider=name, pass-vars]"
```

### Route Flags

- `provider=NAME` - Target provider (required)
- `pass-vars` - Bypass template/parsers, pass route vars directly to provider
- `v1` - Version flag
- `ignore-case` - Case-insensitive matching
- `key=value` - Static param values (merged with parser extractions)

### Examples

```toml
[[routes]]
# Template output "prod/images/photo.jpg"
# Parser "{bucket}/{key}" extracts bucket=prod, key=images/photo.jpg
route = "/cdn/{env}/{path*} => {env}/{path} [provider=s3-products]"

[[routes]]
# Template output "images/photo.jpg"
# Parser "{key} [bucket=default-bucket]" matches
route = "/images/{path*} => {path} [provider=s3-products]"

[[routes]]
# Static bucket via route flag (overrides/conflicts with parser = error)
route = "/static/{path*} => {path} [provider=s3-products, bucket=static-assets]"

[[routes]]
# pass-vars: skip template and parsers
# Route var names must exactly match provider params
route = "/direct/{bucket}/{key*} => [provider=s3-products, pass-vars]"
```

### Conflict Handling

Route flags and parser extractions must never conflict:
- Config-time error if conflict is detectable (static values)
- Request-time error if conflict occurs dynamically

```toml
# ERROR: Parser extracts bucket, flag also sets bucket
route = "/cdn/{bucket}/{path*} => {bucket}/{path} [provider=s3-products, bucket=override]"

# OK: Parser uses [bucket=x], no conflict with route
route = "/cdn/{path*} => {path} [provider=s3-products]"
# (parser "{key} [bucket=default]" provides bucket)
```

## Provider Types and Schemas

Provider types are defined in C# and declare:
1. What `config.*` keys they accept
2. What `params.*` they require
3. Validation logic beyond TOML constraints

### Filesystem Provider

```toml
[providers.local]
type = "filesystem"
config.root = "${app.approot}/images"
params.path = "{path}"
path.parsers = ["{path}"]
```

### S3 Provider

```toml
[providers.s3-main]
type = "s3"
config.region = "us-east-1"
config.access_key_id = "${secrets.aws_key}"
config.secret_access_key = "${secrets.aws_secret}"
params.bucket = "{bucket:equals(images|assets|uploads)}"
params.key = "{key}"
path.parsers = [
    "{bucket}/{key}",
    "{key} [bucket=default]",
]
```

### Azure Blob Provider

```toml
[providers.azure-main]
type = "azure_blob"
config.connection_string = "${secrets.azure_connection}"
params.container = "{container:equals(images|cache)}"
params.blob = "{blob}"
path.parsers = [
    "{container}/{blob}",
    "{blob} [container=images]",
]
```

### HTTP Client Provider

```toml
[providers.upstream]
type = "http_client"
config.base_url = "https://origin.example.com"
config.timeout_ms = 30000
config.max_redirects = 5
params.path = "{path}"
path.parsers = ["{path}"]
```

## Validation Flow

### Config Parse Time

1. Parse TOML structure
2. Resolve all `${...}` interpolations
3. Validate provider definitions:
   - All `params.*` have named variables
   - All `path.parsers` define all required params
   - No conflicting static values
4. Validate routes:
   - Referenced providers exist
   - `pass-vars` routes have matching var names
   - No detectable flag/parser conflicts

### Request Time

1. Route matches incoming URL, captures variables
2. If `pass-vars` flag:
   - Validate captured vars match provider params
   - Pass directly to provider
3. Otherwise:
   - Apply route template to produce path string
   - Try provider parsers in order until match
   - Extract variables from matching parser
   - Merge with parser's static `[key=value]` flags
   - Merge with route's `[key=value]` flags (error on conflict)
4. Validate final params against `params.*` constraints
5. Call provider with validated params

## Backward Compatibility

### Legacy `map_to_physical_folder`

```toml
[[routes]]
prefix = '/images/'
map_to_physical_folder = '${app.approot}\images\'
```

Internally expands to:

```toml
[providers._legacy_fs_0]
type = "filesystem"
config.root = "${app.approot}\\images\\"
params.path = "{path}"
path.parsers = ["{path}"]

[[routes]]
route = "/images/{path*} => {path} [provider=_legacy_fs_0]"
```

## Complete Example

```toml
[imageflow_server]
config_schema = "2"

# Variables
[vars]
secrets.aws_key = "${env.AWS_ACCESS_KEY_ID}"
secrets.aws_secret = "${env.AWS_SECRET_ACCESS_KEY}"
folders.local_images = "${app.approot}/images"

# Providers
[providers.local]
type = "filesystem"
config.root = "${folders.local_images}"
params.path = "{path}"
path.parsers = ["{path}"]

[providers.s3-media]
type = "s3"
config.region = "us-east-1"
config.access_key_id = "${secrets.aws_key}"
config.secret_access_key = "${secrets.aws_secret}"
params.bucket = "{bucket:equals(images|video|audio)}"
params.key = "{key}"
path.parsers = [
    "{bucket}/{key}",
    "media/{key} [bucket=images]",
]

[providers.s3-archive]
type = "s3"
config.region = "us-west-2"
config.access_key_id = "${secrets.aws_key}"
config.secret_access_key = "${secrets.aws_secret}"
params.bucket = "{bucket}"
params.key = "{key}"
path.parsers = ["{bucket}/{key}"]

# Routes
[[routes]]
route = "/local/{path*} => {path} [provider=local]"

[[routes]]
route = "/media/{type:equals(images|video|audio)}/{path*} => {type}/{path} [provider=s3-media]"

[[routes]]
route = "/cdn/{bucket}/{path*} => {bucket}/{path} [provider=s3-media]"

[[routes]]
route = "/archive/{bucket}/{path*} => [provider=s3-archive, pass-vars]"

# Route defaults
[route_defaults]
cache_control = "public, max-age=2592000"
apply_default_commands = "quality=76&webp.quality=70"
```

## Rewrite Rules

Rewrites run **before** routes. They modify the request URL before provider routing occurs.

### Syntax

Same match/template syntax as routes, but with `rewrite` or `redirect` key:

```toml
[[rewrites]]
# Internal rewrite - modifies URL, continues processing
rewrite = "/old/{path*} => /new/{path}"

[[rewrites]]
# Redirect - sends HTTP redirect to client
redirect = "/legacy/{id:int} => /modern/item/{id} [status=301]"

[[rewrites]]
# External redirect
redirect = "/ext/{path*} => https://cdn.example.com/{path} [status=302]"
```

### Rewrite vs Redirect

| Key | Behavior |
|-----|----------|
| `rewrite` | Internal URL modification, client doesn't see it, processing continues |
| `redirect` | HTTP redirect response sent to client, processing stops |

### Rewrite Flags

| Flag | Meaning |
|------|---------|
| `status=301` | Permanent redirect (for `redirect` only) |
| `status=302` | Temporary redirect (for `redirect` only, default) |
| `status=307` | Temporary redirect, preserve method |
| `status=308` | Permanent redirect, preserve method |
| `stop` | Stop processing after this rewrite (no further rewrites or routes) |
| `last` | Stop rewrites, proceed directly to routes |
| `ignore-case` | Case-insensitive matching |

### Querystring Handling

By default, rewrites **preserve** the original querystring. Flags modify this:

| Flag | Behavior |
|------|----------|
| (default) | Append unmatched query params to output |
| `[query-replace]` | Only include query params from template, drop others |
| `[query-prohibit-excess]` | Fail if input has query params not in template |

```toml
[[rewrites]]
# Preserves querystring: /old/foo?a=1&b=2 => /new/foo?a=1&b=2
rewrite = "/old/{path*} => /new/{path}"

[[rewrites]]
# Captures and re-emits specific param, preserves others
# /search?q=test&page=2 => /find?query=test&page=2
rewrite = "/search?q={term} => /find?query={term}"

[[rewrites]]
# Replaces querystring entirely
# /api?a=1&b=2 => /v2?version=2  (drops a, b)
rewrite = "/api => /v2?version=2 [query-replace]"
```

### Examples

```toml
# SEO: permanent redirect from old URLs
[[rewrites]]
redirect = "/blog/{year:int}/{month:int}/{slug} => /posts/{slug} [status=301]"

# Normalize: remove trailing slashes
[[rewrites]]
rewrite = "/{path*}/ => /{path} [last]"

# Legacy API version redirect
[[rewrites]]
redirect = "/api/v1/{path*} => /api/v3/{path} [status=308]"

# Multi-tenant: add tenant prefix from subdomain (if subdomain matching added)
# [[rewrites]]
# rewrite = "/{path*} => /{tenant}/{path}" # tenant from host match

# External CDN redirect
[[rewrites]]
redirect = "/cdn/{path*} => https://cdn.example.com/assets/{path} [status=302]"

# Case normalization
[[rewrites]]
rewrite = "/{path*} => /{path:lower} [ignore-case, last]"
```

### Processing Order

1. **Rewrites** execute in order, top to bottom
2. First matching rewrite applies (unless `continue` flag added - TBD)
3. If rewrite matches:
   - `redirect` → send redirect response, stop
   - `rewrite` → modify URL, continue to next rewrite (unless `last` or `stop`)
4. After all rewrites, **routes** execute against final URL

### Complete Example with Rewrites

```toml
[imageflow_server]
config_schema = "2"

# Rewrites (run first)
[[rewrites]]
# Permanent redirect for old blog URLs
redirect = "/blog/{year}/{month}/{slug} => /posts/{slug} [status=301]"

[[rewrites]]
# Normalize trailing slashes
rewrite = "/{path*}/ => /{path}"

[[rewrites]]
# Legacy image paths
rewrite = "/img/{path*} => /images/{path} [last]"

# Providers
[providers.local]
type = "filesystem"
config.root = "${app.approot}/images"
params.path = "{path}"
path.parsers = ["{path}"]

# Routes (run after rewrites)
[[routes]]
route = "/images/{path*} => {path} [provider=local]"

[[routes]]
route = "/posts/{slug} => posts/{slug} [provider=local]"
```

## Host and Subdomain Matching

Routes and rewrites can match on the request host, enabling multi-tenancy and CDN configurations.

### Syntax

```toml
[[routes]]
# Match specific host
route = "/images/{path*} => {path} [provider=local, host=cdn.example.com]"

[[routes]]
# Match subdomain pattern - captures {tenant} variable
route = "/images/{path*} => {tenant}/{path} [provider=s3-multi, host={tenant}.example.com]"

[[rewrites]]
# Redirect based on subdomain
redirect = "/{path*} => https://main.example.com/{tenant}/{path} [host={tenant}.cdn.example.com, status=301]"
```

### Host Flags

| Flag | Meaning |
|------|---------|
| `host=example.com` | Match exact host |
| `host={var}.example.com` | Capture subdomain into variable |
| `host=*.example.com` | Wildcard subdomain match (no capture) |
| `host-i` | Case-insensitive host matching (default) |
| `host-case-sensitive` | Case-sensitive host matching |

## Accept Header Detection

Routes can detect browser format support from the Accept header and expose it as query parameters.

### Flags

| Flag | Meaning |
|------|---------|
| `accept.format` | Import Accept header, adds `accept.webp=1`, `accept.avif=1`, `accept.jxl=1` to query |
| `require-accept-webp` | Only match if Accept header includes `image/webp` |
| `require-accept-avif` | Only match if Accept header includes `image/avif` |
| `require-accept-jxl` | Only match if Accept header includes `image/jxl` |

### Usage

```toml
[[routes]]
# Import Accept header as query params for downstream processing
route = "/images/{path*} => {path} [provider=local, accept.format]"

[[routes]]
# Only serve WebP variant if browser supports it
route = "/optimized/{path*} => webp/{path} [provider=s3-cdn, require-accept-webp]"
```

### How It Works

When `accept.format` is enabled, the Accept header is parsed and the following query parameters are added if the corresponding MIME type is present:

| Accept Header Contains | Query Param Added |
|------------------------|-------------------|
| `image/webp` | `accept.webp=1` |
| `image/avif` | `accept.avif=1` |
| `image/jxl` | `accept.jxl=1` |

Example Accept headers:
```
image/avif,image/webp,*/*              → accept.avif=1&accept.webp=1
image/webp,*/*                          → accept.webp=1
image/png,image/*;q=0.8,*/*;q=0.5       → (nothing added)
```

## Open Questions

1. **Hot-reload of `config.*`** - Should changing config values trigger provider reconstruction without full restart?

2. **Parser regex support** - Should parsers support full regex, or just the current `{var}` / `{var:constraint}` syntax?

3. **Default param values** - Should `params.*` support defaults for optional params?
   ```toml
   params.format = "{format:default(jpg):equals(jpg|png|webp)}"
   ```

4. **Multi-region with single provider** - Parser `[region=x]` sets config value - is this allowed, or must config be static?
