# Killbits — format and codec gating

Imageflow Server ships a three-layer killbits system that lets operators narrow which image formats and named codecs can decode or encode on their server.

The three layers are:

1. **Compile ceiling** — set by which features the native imageflow runtime was built with. Can only deny; never widens.
2. **Trusted policy** — set once at server startup from `Imageflow:Security` in configuration. Narrows the compile ceiling.
3. **Per-job security** — attached to each request before it crosses to the native side. Can only narrow further; can never widen.

A format or codec is only usable when **every** layer allows it.

## Default behavior

By default, Imageflow Server denies **AVIF encode** and **JXL encode** even when the native build supports them. Operators must explicitly opt in. This is a deliberate change from previous releases, where anything the native build included was enabled.

You can see the default list without reading source: `Imageflow.Server.SecurityPolicyOptions.ServerRiskyEncodeDefault`.

Decode is not restricted by the server default — only encode.

## Enabling the killbits surface

Add a `Security` block under `Imageflow` in `appsettings.json`:

```jsonc
{
  "Imageflow": {
    "Security": {
      "MaxDecodeSize": { "W": 8000, "H": 8000, "Megapixels": 50 },
      "MaxInputFileBytes": 268435456,
      "MaxJsonBytes": 67108864,

      "Formats": {
        "OptInEncode": [ "avif", "jxl" ]
      },

      "Codecs": {
        "DenyEncoders": [ "mozjpeg_encoder" ]
      }
    }
  }
}
```

Bind it into the middleware options once at startup:

```csharp
var policy = SecurityPolicyConfigurationBinder.Bind(
    configuration.GetSection(SecurityPolicyConfigurationBinder.DefaultSectionPath));

app.UseImageflow(new ImageflowMiddlewareOptions()
    // ... existing config ...
    .SetSecurityPolicyOptions(policy));
```

If `SecurityPolicyOptions` is `null` (no `Imageflow:Security` section), the server behaves exactly like previous releases — no policy is sent to the native runtime and no server-default deny list is applied.

## Turning AVIF / JXL encoding on

List the formats you want to allow under `Formats:OptInEncode`:

```jsonc
{
  "Imageflow": {
    "Security": {
      "Formats": {
        "OptInEncode": [ "avif" ]
      }
    }
  }
}
```

AVIF encode will now be allowed. JXL encode stays denied (because it's still on the server's risky-encode list and not opted into). The opposite is also fine:

```jsonc
"OptInEncode": [ "jxl" ]
```

allows JXL while AVIF remains denied.

## Expressing policy via allow-list, deny-list, or table

Three mutually-exclusive shapes are accepted under `Formats`:

### Allow-list — only these formats are usable

```jsonc
"Formats": {
  "AllowEncode": [ "jpeg", "png", "webp" ],
  "AllowDecode": [ "jpeg", "png", "webp", "gif" ]
}
```

When you use an allow-list, the server default does **not** apply — you've taken explicit control. Anything not on your list is denied.

### Deny-list — everything except these

```jsonc
"Formats": {
  "DenyEncode": [ "webp" ]
}
```

The server default (AVIF, JXL) is merged into your list: the final deny list becomes `[avif, jxl, webp]`. Use `OptInEncode` to subtract from the server default:

```jsonc
"Formats": {
  "OptInEncode": [ "avif" ],
  "DenyEncode":  [ "webp" ]
}
```

Effective deny list: `[jxl, webp]`. AVIF is opted in; JXL is still denied.

### Table form — explicit per-format decode/encode flags

```jsonc
"Formats": {
  "Formats": {
    "avif": { "Decode": true, "Encode": true },
    "jxl":  { "Decode": true, "Encode": false }
  }
}
```

Any format you mention is operator-controlled — the server default no longer applies to it. Formats you don't mention inherit the server default; unmentioned risky formats are injected into the table as encode-false entries.

## Named codec gating

Use `Codecs` to allow or deny specific named encoder / decoder backends:

```jsonc
"Codecs": {
  "DenyEncoders": [ "mozjpeg_encoder" ]
}
```

The codec names are snake_case and match the native imageflow enum. Use `allow_*` / `deny_*` pairs:

- `AllowEncoders` / `DenyEncoders` — mutually exclusive
- `AllowDecoders` / `DenyDecoders` — mutually exclusive

## Diagnosing "why not WebP?"

On startup, the server logs a single structured line tagged `killbits-policy` summarizing the effective grid:

```
killbits-policy server_risky_encode=[avif,jxl] decode_allowed=[jpeg,png,gif,webp,avif,jxl,bmp,tiff,pnm] encode_allowed=[jpeg,png,gif,webp] trusted_policy_set=True
```

`grep killbits-policy` in your log to answer "which codecs are enabled" without reading source.

For deeper diagnostics, enable the response header:

```csharp
options.SetExposeNetSupportHeader(true);
```

Responses will carry `X-Imageflow-Net-Support: formats=jpeg,png,webp,...`. Off by default — the grid is operator-diagnostic information and may leak build details to end users.

## Error shape

When a killbits denial blocks a request, the server returns HTTP 422 with a JSON body:

```json
{
  "error": "encode_not_available",
  "format": "avif",
  "reasons": [ "denied_by_trusted_policy" ]
}
```

The `error` tag is one of `codec_not_available`, `decode_not_available`, or `encode_not_available`. Clients can distinguish these from other 4xx responses (auth, missing file) and surface an appropriate message.

## Invalid configuration

Mutual-exclusion violations (`AllowEncode` + `DenyEncode`, list-form + table-form, etc.) are caught at startup binding time and raise `SecurityPolicyValidationException`. The message points at the offending config path. The server does **not** start in a degraded mode — invalid config means the server doesn't run.

## Reference

- `Imageflow.Server.SecurityPolicyOptions` — the policy shape.
- `Imageflow.Server.SecurityPolicyConfigurationBinder` — binds from `IConfiguration`.
- `Imageflow.Server.SecurityPolicyValidator` — the startup validator (used internally).
- `Imageflow.Server.ImageflowMiddlewareOptions.SetSecurityPolicyOptions` — opt in.
