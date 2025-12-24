# Claude Code Session Tracking

This file tracks work done, documentation status, and remaining tasks.

## Recent Work (December 2024)

### Completed

- [x] Created `docs/TOML_PROVIDER_DESIGN.md` - v2 config schema design
- [x] Created `docs/SYNTAX_STATUS.md` - implementation vs documentation status
- [x] Created `CONFIGURATION_v2.md` - user-facing v2 config documentation
- [x] Added rewrite rules section to TOML design
- [x] Added host/subdomain matching (in match expression, not flags)
- [x] Added header conditions (`[[routes.header_conditions]]`)
- [x] Changed `accept.format` to `accept.format=auto`
- [x] Audited all markdown files in codebase

### In Progress

- [ ] IOptionsMonitor pattern for configuration (WIP in git status)
- [ ] TOML configuration provider implementation

## Documentation Status

### Verified / Current

| File | Status | Notes |
|------|--------|-------|
| `docs/TOML_PROVIDER_DESIGN.md` | CURRENT | v2 design spec |
| `docs/SYNTAX_STATUS.md` | CURRENT | Implementation status tracking |
| `CONFIGURATION_v2.md` | CURRENT | User docs for v2 config |
| `DEVNOTES.md` | CURRENT | Small dev notes, still relevant |

### Needs Update

| File | Status | Issue |
|------|--------|-------|
| `CONFIGURATION.md` | OUTDATED | Only v1 syntax, missing providers/rewrites/expressions |
| `src/Imazen.Routing/Matching/matching.md` | PARTIAL | Some flags documented but not implemented |
| `src/Imazen.Routing/Layers/routing_design.md` | ASPIRATIONAL | TypeScript/Rust gen, POST/PUT not implemented |

### Unverified / Suspect

| File | Status | Issue |
|------|--------|-------|
| `src/Imazen.Routing/Matching/ebnf.md` | SUSPECT | Written for csly parser, not verified against hand-coded impl |
| `src/Imazen.Routing/Matching/dive.md` | SUSPECT | Same origin as ebnf.md, may not match current code |

### Should Delete / Archive

| File | Status | Reason |
|------|--------|--------|
| `simplify.md` | DELETE | Documents dropped csly/sly parser, references deleted files |
| `src/Imazen.Routing/Caching/design.md` | DELETE | First line says "all of this is outdated and should be ignored" |
| `roadmap.md` | ARCHIVE | Old aspirational goals, largely obsolete |
| `src/design_scratch.md` | ARCHIVE | Brainstorming notes, not documentation |

### Not Reviewed

- `README.md` - main readme, may need update
- `CHANGES.md` - changelog
- `src/Imageflow.Server.Storage.*/README.md` - storage provider docs
- `examples/*/README.md` - example docs
- `external/*` - third-party, not relevant

## Implementation Notes

### Two Condition Systems

There are two separate condition systems that should NOT be unified:

1. **`StringCondition` / `StringConditionKind`** - Used for route template matching
2. **`IFastCond` / `Conditions`** - Used for fast path preconditions in routing layers

### Parser History

- **Current**: Hand-coded parser in `Matching/` folder
- **Dropped**: csly/sly parser (commit `6f1d23f`) - 1,989 lines deleted

### Accept Header Implementation

| Flag | Status |
|------|--------|
| `accept.format` (now `accept.format=auto`) | IMPLEMENTED (`ParsingOptions.cs:73`) |
| `import-accept-header` | PARSED but not connected |
| `require-accept-webp/avif/jxl` | PARSED but no runtime logic |

## TODO

### High Priority

- [ ] Implement TOML configuration provider
- [ ] Implement route expression parsing in config
- [ ] Implement provider configuration binding
- [ ] Wire up rewrites in request pipeline
- [ ] Create intuitive parallel C# configuration methods for routes, rewrites, and providers (fluent API parity with TOML)

### Medium Priority

- [ ] Verify ebnf.md against hand-coded parser
- [ ] Verify dive.md against current implementation
- [ ] Update CONFIGURATION.md or mark as legacy
- [ ] Implement `require-accept-*` flags
- [ ] Implement host/subdomain matching in parser

### Low Priority

- [ ] Delete obsolete docs (simplify.md, caching/design.md)
- [ ] Archive roadmap.md
- [ ] Review and update README.md
- [ ] Implement `continue` flag for rewrite chaining

## Test Coverage Analysis

### Current Coverage

| Area | File | Status |
|------|------|--------|
| Match expression parsing | `MatchExpressionParsingTests.cs` | ✅ Good |
| Match expression matching | `MatchExpressionTests.cs` | ✅ Good |
| Template expressions | `TemplatingExpressionTests.cs` | ✅ Good |
| Routing expressions (`=> [v1]`) | `RoutingExpressionParserTests.cs` | ✅ Basic |
| End-to-end routing | `RoutingExpressionEngineTests.cs` | ✅ Good |
| Character classes | `CharacterClassTests.cs` | ✅ Good |
| TOML preprocessing | `TomlPreprocessorTests.cs` | ✅ Exists |
| TOML integration | `TomlIntegrationTests.cs` | ⏸️ SKIPPED |
| Invalid TOML config | `InvalidTomlConfigurationTests.cs` | ⏸️ SKIPPED |

### Coverage Gaps - Tests Needed

| Area | Suggested File | Priority |
|------|----------------|----------|
| Provider configuration | `ProviderConfigurationTests.cs` | HIGH |
| Rewrite rules | `RewriteRuleTests.cs` | HIGH |
| Host/subdomain matching | `HostMatchingTests.cs` | MEDIUM |
| Header conditions | `HeaderConditionTests.cs` | MEDIUM |
| Variable interpolation | `VariableInterpolationTests.cs` | MEDIUM |
| C# fluent API | `FluentApiTests.cs` | MEDIUM |
| IOptionsMonitor integration | `OptionsMonitorTests.cs` | HIGH |

### Test File Locations

```
tests/
├── Imageflow.Server.Configuration.Tests/
│   ├── ProviderConfigurationTests.cs     # NEW - provider parsing
│   ├── RewriteRuleTests.cs               # NEW - rewrite/redirect
│   ├── HeaderConditionTests.cs           # NEW - header conditions
│   ├── VariableInterpolationTests.cs     # NEW - ${env.X} etc
│   ├── OptionsMonitorTests.cs            # NEW - hot reload
│   ├── TomlIntegrationTests.cs           # UNSKIP
│   └── InvalidTomlConfigurationTests.cs  # UNSKIP
├── ImazenShared.Tests/Routing/Matching/
│   └── HostMatchingTests.cs              # NEW - ://host patterns
```

## IOptionsMonitor Integration Plan

### Current Architecture (to be replaced)

```
TomlParser.LoadAndParse(path)
    → TomlParseResult
        → Executor (IAppConfigurator)
            → ConfigureServices() / ConfigureApp()
```

**Problem**: Bypasses Microsoft.Extensions.Configuration, no hot-reload, no layering.

### Target Architecture

```
Configuration Sources (layered, later wins):
├── appsettings.json                 # Base defaults
├── appsettings.{Environment}.json   # Environment overrides
├── imageflow.toml                   # Main config file
├── Environment variables            # IMAGEFLOW__* prefix
└── Command line args                # --key=value

         ↓ Microsoft.Extensions.Configuration

IConfiguration (unified key-value store)

         ↓ Options pattern binding

IOptions<ImageflowServerOptions>
IOptionsMonitor<ImageflowServerOptions>  ← Hot reload!

         ↓ Post-configuration validation

Parsed & Validated Configuration
├── Providers → IRoutedBlobProvider instances
├── Routes → RoutingExpression instances
├── Rewrites → RewriteRule instances
└── Ready for request handling
```

### Options Classes Design

```csharp
// Root options
public class ImageflowServerOptions
{
    public string ConfigSchema { get; set; } = "2";
    public LicenseOptions License { get; set; }
    public Dictionary<string, ProviderOptions> Providers { get; set; }
    public List<RouteOptions> Routes { get; set; }
    public List<RewriteOptions> Rewrites { get; set; }
    public RouteDefaultsOptions RouteDefaults { get; set; }
    public DiskCacheOptions DiskCache { get; set; }
    public DiagnosticsOptions Diagnostics { get; set; }
}

// Provider options (type-discriminated)
public class ProviderOptions
{
    public string Type { get; set; }  // filesystem, s3, azure_blob, http_client
    public Dictionary<string, string> Config { get; set; }
    public Dictionary<string, string> Params { get; set; }
    public List<string> PathParsers { get; set; }
}

// Route options
public class RouteOptions
{
    public string Route { get; set; }  // "match => template [flags]"
    public List<HeaderConditionOptions> HeaderConditions { get; set; }

    // Legacy v1 support
    public string Prefix { get; set; }
    public string MapToPhysicalFolder { get; set; }
}

// Rewrite options
public class RewriteOptions
{
    public string Rewrite { get; set; }   // Internal rewrite
    public string Redirect { get; set; }  // HTTP redirect
}
```

### Implementation Steps

1. **Create TomlConfigurationProvider**
   - Implement `IConfigurationProvider` and `IConfigurationSource`
   - Flatten TOML to dotted key paths (`Providers:s3-main:Config:region`)
   - Handle `[[arrays]]` → indexed keys (`Routes:0:Route`, `Routes:1:Route`)

2. **Create Options Classes**
   - `ImageflowServerOptions` and nested types
   - Data annotations for validation
   - XML docs for IntelliSense

3. **Create PostConfigureOptions**
   - Parse route expressions from strings
   - Validate provider references exist
   - Resolve `${...}` interpolation
   - Build provider instances

4. **Wire up DI**
   ```csharp
   services.AddOptions<ImageflowServerOptions>()
       .BindConfiguration("ImageflowServer")
       .ValidateDataAnnotations()
       .ValidateOnStart();

   services.AddSingleton<IPostConfigureOptions<ImageflowServerOptions>,
       ImageflowOptionsPostConfigure>();
   ```

5. **Support Hot Reload**
   - Use `IOptionsMonitor<T>` in middleware
   - React to `OnChange` for provider/route updates
   - Graceful transition (don't drop in-flight requests)

### C# Fluent API (Parallel to TOML)

```csharp
services.AddImageflowServer(options =>
{
    // Providers
    options.AddFilesystemProvider("local", provider =>
    {
        provider.Root = "./images";
    });

    options.AddS3Provider("s3-main", provider =>
    {
        provider.Region = "us-east-1";
        provider.AccessKeyId = Environment.GetEnvironmentVariable("AWS_KEY");
        provider.Bucket("{bucket:equals(images|assets)}");
        provider.Key("{key}");
        provider.PathParsers("{bucket}/{key}", "{key} [bucket=default]");
    });

    // Routes
    options.AddRoute("/images/{path*} => {path} [provider=local]");
    options.AddRoute("/cdn/{bucket}/{path*} => {bucket}/{path} [provider=s3-main]");

    // Rewrites
    options.AddRewrite("/old/{path*} => /new/{path}");
    options.AddRedirect("/legacy/{id} => /modern/{id}", status: 301);

    // Route with header condition
    options.AddRoute("/images/{path*} => avif/{path} [provider=s3-cdn]")
        .WhenHeader("Accept", contains: "image/avif");
});
```

### Plugin Architecture for Options

Plugins (S3, Azure, RemoteReader, third-party) need to:
1. Define their own strongly-typed options
2. Use `IOptionsMonitor<T>` for hot-reload
3. Map from TOML `[providers.NAME]` to their options
4. Be discoverable by the core system

#### Current Plugin Pattern (to preserve)

```csharp
// Plugin defines its options
public class S3ServiceOptions { ... }

// Plugin registers via extension method
services.AddImageflowS3Service(options => {
    options.MapPrefix("/images/", "my-bucket");
});
```

#### Target Plugin Pattern

```csharp
// 1. Plugin implements IImageflowProviderFactory
public interface IImageflowProviderFactory
{
    string ProviderType { get; }  // "s3", "azure_blob", "filesystem"
    Type OptionsType { get; }     // typeof(S3ProviderOptions)

    IRoutedBlobProvider Create(
        string providerName,
        IConfiguration configSection,  // Providers:s3-main:Config
        IServiceProvider services);
}

// 2. Plugin registers its factory
services.AddImageflowProviderFactory<S3ProviderFactory>();

// 3. Core discovers factories and wires up options
// For each [providers.X] in config where type="s3":
//   - Find factory with ProviderType == "s3"
//   - Bind config section to factory.OptionsType
//   - Call factory.Create() with bound options
//   - Register as IOptionsMonitor for hot-reload

// 4. Plugin can also register named options
services.AddOptions<S3ProviderOptions>("s3-main")
    .BindConfiguration("Providers:s3-main:Config");
```

#### Named Options for Multiple Instances

```csharp
// Core binds each provider instance to named options
foreach (var (name, providerConfig) in config.Providers)
{
    var factory = factories[providerConfig.Type];
    services.AddOptions(factory.OptionsType, name)
        .BindConfiguration($"Providers:{name}:Config");
}

// Plugin retrieves its options by name
public class S3ProviderFactory : IImageflowProviderFactory
{
    private readonly IOptionsMonitor<S3ProviderOptions> _options;

    public IRoutedBlobProvider Create(string providerName, ...)
    {
        var opts = _options.Get(providerName);  // Named options
        opts.OnChange(() => /* hot-reload logic */);
        return new S3Provider(opts);
    }
}
```

#### TOML to Plugin Options Mapping

```toml
[providers.s3-main]
type = "s3"                              # → Selects S3ProviderFactory
config.region = "us-east-1"              # → S3ProviderOptions.Region
config.access_key_id = "${secrets.key}"  # → S3ProviderOptions.AccessKeyId
params.bucket = "{bucket}"               # → Passed to path parsing, not options
path.parsers = ["{bucket}/{key}"]        # → Passed to path parsing, not options
```

The `config.*` keys map to plugin options. The `params.*` and `path.parsers` are handled by the routing layer, not the plugin.

#### Hot-Reload Flow

```
TOML file changes
    ↓
TomlConfigurationProvider detects change
    ↓
IOptionsMonitor<ImageflowServerOptions>.OnChange fires
    ↓
For each changed provider:
    ↓
IOptionsMonitor<S3ProviderOptions>.Get("s3-main") returns new options
    ↓
IRoutedBlobProviderGroup.OnProvidersChanged fires (already exists!)
    ↓
Routing layer picks up new providers
```

### Key Files to Create/Modify

| File | Purpose |
|------|---------|
| `src/Imazen.Routing/Providers/IImageflowProviderFactory.cs` | NEW - Plugin factory interface |
| `src/Imageflow.Server.Configuration/TomlConfigurationProvider.cs` | NEW - IConfigurationProvider for TOML |
| `src/Imageflow.Server.Configuration/ImageflowServerOptions.cs` | NEW - Root options class |
| `src/Imageflow.Server.Configuration/ImageflowOptionsPostConfigure.cs` | NEW - Post-config validation |
| `src/Imageflow.Server/ImageflowServerBuilderExtensions.cs` | NEW - Fluent API |
| `src/Imageflow.Server.Configuration/Executor.cs` | MODIFY - Use options pattern |
| `src/Imageflow.Server.Storage.S3/S3ProviderFactory.cs` | NEW - S3 plugin factory |
| `src/Imageflow.Server.Storage.S3/S3ProviderOptions.cs` | NEW - S3 config-bound options |

## Build & Test

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run specific test project
dotnet test tests/ImazenShared.Tests/ImazenShared.Tests.csproj

# Run with filter
dotnet test --filter "FullyQualifiedName~Routing"
```

## Git Branch

Current: `refactorblob` (ahead of origin by several commits)
