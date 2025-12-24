# TOML Configuration v2 for Imageflow Server

This document describes the v2 configuration schema with support for providers, route expressions, rewrites, and header conditions.

**Status: DRAFT - Not yet implemented**

## Quick Start

```toml
[imageflow_server]
config_schema = "2"

[license]
enforcement = "watermark"
key = """[YOUR LICENSE KEY]"""

# Simple filesystem provider
[providers.local]
type = "filesystem"
config.root = "${app.approot}/images"
params.path = "{path}"
path.parsers = ["{path}"]

# Route with expression syntax
[[routes]]
route = "/images/{path*} => {path} [provider=local]"

[route_defaults]
cache_control = "public, max-age=2592000"
apply_default_commands = "quality=76&webp.quality=70"

[disk_cache]
enabled = true
folder = "${env.TEMP}/ImageflowCache"
cache_size_mb = 30000
```

## Variable Interpolation

### Config-time: `${prefix.name}`

Resolved when TOML is parsed:

| Prefix | Source | Example |
|--------|--------|---------|
| `${env.NAME}` | Environment variable | `${env.AWS_ACCESS_KEY_ID}` |
| `${app.NAME}` | App-provided | `${app.approot}` |
| `${vars.NAME}` | User-defined variable | `${vars.ImageRoot}` |
| `${folders.NAME}` | Folder (validated to exist) | `${folders.cache}` |
| `${files.NAME}` | File (validated to exist) | `${files.license}` |
| `${secrets.NAME}` | Secret (redacted in diagnostics) | `${secrets.aws_key}` |

### Route-time: `{name}` or `{name:constraint}`

Resolved at request time from URL captures:

| Syntax | Meaning |
|--------|---------|
| `{path}` | Capture into `path` |
| `{path*}` | Glob capture (remainder) |
| `{id:int}` | Integer constraint |
| `{bucket:equals(a\|b\|c)}` | Enumeration constraint |

## Providers

### Filesystem

```toml
[providers.local]
type = "filesystem"
config.root = "${app.approot}/images"
params.path = "{path}"
path.parsers = ["{path}"]
```

### S3

```toml
[providers.s3-main]
type = "s3"
config.region = "us-east-1"
config.access_key_id = "${secrets.aws_key}"
config.secret_access_key = "${secrets.aws_secret}"
params.bucket = "{bucket:equals(images|assets)}"
params.key = "{key}"
path.parsers = [
    "{bucket}/{key}",
    "{key} [bucket=default-bucket]",
]
```

### Azure Blob

```toml
[providers.azure]
type = "azure_blob"
config.connection_string = "${secrets.azure_connection}"
params.container = "{container}"
params.blob = "{blob}"
path.parsers = ["{container}/{blob}"]
```

### HTTP Client

```toml
[providers.upstream]
type = "http_client"
config.base_url = "https://origin.example.com"
config.timeout_ms = 30000
params.path = "{path}"
path.parsers = ["{path}"]
```

## Routes

### Expression Syntax

```
route = "<match> => <template> [flags]"
```

### Examples

```toml
[[routes]]
# Basic route to filesystem
route = "/images/{path*} => {path} [provider=local]"

[[routes]]
# Route with bucket extraction
route = "/cdn/{bucket}/{path*} => {bucket}/{path} [provider=s3-main]"

[[routes]]
# Pass variables directly (skip template/parsers)
route = "/direct/{bucket}/{key*} => [provider=s3-main, pass-vars]"

[[routes]]
# Case-insensitive matching
route = "/Assets/{path*} => {path} [provider=local, ignore-case]"
```

### Route Flags

| Flag | Meaning |
|------|---------|
| `provider=NAME` | Target provider (required) |
| `pass-vars` | Pass route vars directly to provider |
| `ignore-case` / `i` | Case-insensitive matching |
| `v1` | Version flag |

## Rewrites

Rewrites run **before** routes and modify the request URL.

### Internal Rewrites

```toml
[[rewrites]]
rewrite = "/old/{path*} => /new/{path}"

[[rewrites]]
# Normalize trailing slashes
rewrite = "/{path*}/ => /{path} [last]"
```

### Redirects

```toml
[[rewrites]]
redirect = "/legacy/{id:int} => /modern/{id} [status=301]"

[[rewrites]]
# External redirect
redirect = "/cdn/{path*} => https://cdn.example.com/{path} [status=302]"
```

### Rewrite Flags

| Flag | Meaning |
|------|---------|
| `status=301/302/307/308` | Redirect status code |
| `stop` | Stop all processing |
| `last` | Stop rewrites, proceed to routes |
| `continue` | Allow multiple rewrites to chain |
| `ignore-case` | Case-insensitive matching |

### Querystring Handling

| Flag | Behavior |
|------|----------|
| (default) | Preserve original querystring |
| `query-replace` | Only include template query params |
| `query-prohibit-excess` | Fail if unexpected query params |

## Host and Subdomain Matching

Match on full URL including scheme and host:

```toml
[[routes]]
# Any scheme, specific host
route = "://cdn.example.com/images/{path*} => {path} [provider=local]"

[[routes]]
# Capture subdomain
route = "://{tenant}.example.com/{path*} => {tenant}/{path} [provider=s3-multi]"

[[rewrites]]
# Normalize www
redirect = "://www.example.com/{path*} => https://example.com/{path} [status=301]"
```

## Header Conditions

```toml
[[routes]]
route = "/images/{path*} => avif/{path} [provider=s3-cdn]"
[[routes.header_conditions]]
header = "Accept"
must = "contain"
value = "image/avif"
```

### Condition Operators

| Operator | Meaning |
|----------|---------|
| `exist` | Header must be present |
| `not-exist` | Header must not be present |
| `equal` / `equal-i` | Exact match (case-sensitive/insensitive) |
| `contain` / `contain-i` | Contains substring |
| `start-with` / `end-with` | Prefix/suffix match |
| `match` | Regex match |

### Accept Header Shorthand

```toml
[[routes]]
# Adds accept.webp=1, accept.avif=1, accept.jxl=1 based on Accept header
route = "/images/{path*} => {path} [provider=local, accept.format=auto]"
```

## Legacy Compatibility

The v1 syntax still works:

```toml
[[routes]]
prefix = '/images/'
map_to_physical_folder = '${app.approot}\images\'
```

This internally expands to the v2 provider/route syntax.

## Complete Example

```toml
[imageflow_server]
config_schema = "2"

[license]
enforcement = "watermark"
key = """[LICENSE KEY]"""

# Variables
[vars]
secrets.aws_key = "${env.AWS_ACCESS_KEY_ID}"
secrets.aws_secret = "${env.AWS_SECRET_ACCESS_KEY}"

# Providers
[providers.local]
type = "filesystem"
config.root = "${app.approot}/images"
params.path = "{path}"
path.parsers = ["{path}"]

[providers.s3-media]
type = "s3"
config.region = "us-east-1"
config.access_key_id = "${secrets.aws_key}"
config.secret_access_key = "${secrets.aws_secret}"
params.bucket = "{bucket:equals(images|video)}"
params.key = "{key}"
path.parsers = [
    "{bucket}/{key}",
    "media/{key} [bucket=images]",
]

# Rewrites
[[rewrites]]
redirect = "/old-images/{path*} => /images/{path} [status=301]"

[[rewrites]]
rewrite = "/{path*}/ => /{path}"

# Routes
[[routes]]
route = "/images/{path*} => {path} [provider=local]"

[[routes]]
route = "/media/{type}/{path*} => {type}/{path} [provider=s3-media]"

[[routes]]
route = "/cdn/{bucket}/{path*} => {bucket}/{path} [provider=s3-media]"

# Defaults
[route_defaults]
cache_control = "public, max-age=2592000"
apply_default_commands = "quality=76&webp.quality=70"

# Cache
[disk_cache]
enabled = true
folder = "${env.TEMP}/ImageflowCache"
cache_size_mb = 30000

# Diagnostics
[diagnostics]
allow_localhost = true

[development.diagnostics]
allow_anyhost = true
```

## See Also

- [TOML_PROVIDER_DESIGN.md](docs/TOML_PROVIDER_DESIGN.md) - Detailed design document
- [SYNTAX_STATUS.md](docs/SYNTAX_STATUS.md) - Implementation status of matching/templating syntax
