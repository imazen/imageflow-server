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

## Build & Test

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run specific test project
dotnet test tests/ImazenShared.Tests/ImazenShared.Tests.csproj
```

## Git Branch

Current: `refactorblob` (ahead of origin by several commits)
