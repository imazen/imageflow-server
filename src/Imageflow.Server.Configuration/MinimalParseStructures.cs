namespace Imageflow.Server.Configuration.Parsing;

using Tomlyn.Model;
using Imageflow.Fluent;

#nullable enable


internal interface IValidationCapable{
    public void Validate(ValidationContext c);
}

internal abstract class ConfigSectionBase : ITomlMetadataProvider, IValidationCapable{
    public TomlPropertiesMetadata? PropertiesMetadata { get; set; }
    
    public abstract void Validate(ValidationContext c);
}

internal class MissingConfigPropertyException : Exception
{
    public string? PropertyName { get; init; }
    // within table
    public string? ParentFullName { get; init; }
    public ConfigTextContext? TextContext { get; init; }

    public string? FailureMessage { get; init; }

    internal string FailureSegment => FailureMessage != null ? $"{FailureMessage} ({ParentFullName}.{PropertyName})" : $"Missing required property {PropertyName} = ... in [{ParentFullName}]";
    public override string Message => $"{FailureSegment} {TextContext}";
}

internal class InvalidConfigPropertyException : Exception
{
    public string? PropertyName { get; init; }

    public string? PropertyValue { get; init; }
    // within table
    public string? ParentFullName { get; init; }

    public string? FailedExpectation { get; init; }
    public ConfigTextContext? TextContext { get; init; }

    public string? FailureMessage { get; init; }

    internal string FailureSegment => FailureMessage ?? $"failed {FailedExpectation}";
    public override string Message => $"Invalid [{ParentFullName}] {PropertyName} = '{PropertyValue}' in ({FailureSegment}) {TextContext}";
}




internal class ImageflowServer : ITomlMetadataProvider, IValidationCapable
{
    public TomlPropertiesMetadata? PropertiesMetadata { get; set; }
    public string? ConfigSchema { get; set; }

    public void Validate(ValidationContext c)
    {
        c.Require(this, ConfigSchema);
        if (ConfigSchema != null) {
            // Parse the config schema version into MAJOR.MINOR or MAJOR
            var versionSegments = ConfigSchema.Split('.');
            var major = int.Parse(versionSegments[0]);
            var minor = versionSegments.Length > 1 ? int.Parse(versionSegments[1]) : 0;
            c.ValidateRecursive(this,ConfigSchema, major == c.ConfigSchemaMajorVersion);
            c.ValidateRecursive(this,ConfigSchema, minor <= c.ConfigSchemaMinorVersion);
        }
    }
}

internal class LicenseSection : ITomlMetadataProvider, IValidationCapable
{
    public TomlPropertiesMetadata? PropertiesMetadata { get; set; }
    public string? Enforcement { get; set; }
    public string? Key { get; set; }

    public void Validate(ValidationContext c)
    {
        var enforcement = Enforcement?.ToLowerInvariant();
        // enforcement must be one of the following: "watermark", "http_402_error" or "http_422_error"
        c.ValidateRecursive(this, Enforcement, enforcement == "watermark" || enforcement == "http_402_error" || enforcement == "http_422_error");

        // We're leaving the key validation to the license validator
    }
}


internal class RouteBase : ITomlMetadataProvider, IValidationCapable
{
    public TomlPropertiesMetadata? PropertiesMetadata { get; set; }

    // public string? Prefix { get; set; }
    public bool? PrefixCaseSensitive { get; set; }
    public bool? LowercasePathRemainder { get; set; }
   // public RouteSource? Source { get; set; }
    public string? CacheControl { get; set; }
    public string? ApplyDefaultCommands { get; set; }
    public bool? ApplyDefaultCommandsToQuerylessUrls { get; set; }
    // public string? ApplyOverrideCommands { get; set; }
    // public string? RequireSignature { get; set; }

    // public List<HeaderPair>? ResponseHeaders { get; set; }

    public virtual void Validate(ValidationContext c)
    {
        // Add your validations here
        //c.Require(this, Prefix);
        //c.Require(this, Source);
        //c.ValidateRecursive(this, Source, true);

    }
}


internal class RouteSection : RouteBase
{
    public string? Prefix { get; set; }
    //public RouteSource? Source { get; set; }
    public string? MapToPhysicalFolder { get; set; }

    public bool? AllowExtensionlessUrls { get; set; }

    public override void Validate(ValidationContext c)
    {
        // Add your validations here
        c.Require(this, Prefix);
        //c.Require(this, Source);
        //c.ValidateRecursive(this, Source, true);
        base.Validate(c);

    }
}

internal class HybridCacheSection
{
    public bool? Enabled { get; set; }
    public string? Folder { get; set; }
    public int? CacheSizeMb { get; set; }
    public int? WriteQueueRamMb { get; set; }
    
    public int? SecondsUntilEvictable { get; set; } // Not available in public plugin options yet
    public int? EvictionSweepSizeMb { get; set; }

    public int? DatabaseShards { get; set; }

    public void Validate(ValidationContext c)
    {
        // Add your validations here
    }
}

/*
# ======  Diagnostics access ======
# Prefix keys with 'production.', 'staging.', or 'development.' 
# to merge with the appropriate section based on the current environment
# Defaults to 'production' if DOTNET_ENVIRONMENT and ASPNETCORE_ENVIRONMENT are not set
[diagnostics]
# Access anywhere with /imageflow.debug?password=[[redacted]]
allow_with_password = "[[redacted]]"
# Allow localhost without a password
allow_localhost = true
allow_anyhost = false
*/

internal class Diagnostics : ITomlMetadataProvider, IValidationCapable
{
    public TomlPropertiesMetadata? PropertiesMetadata { get; set; }
    public string? AllowWithPassword { get; set; }
    public bool? AllowLocalhost { get; set; }
    public bool? AllowAnyhost { get; set; }

    public void Validate(ValidationContext c)
    {
        if (AllowWithPassword != null){
            c.ValidateRecursive(this, AllowWithPassword, AllowWithPassword.Length > 12, "[diagnostics].allow_with_password must be at least 12 characters long if set");
        }
        // confusing with development., etc. We're safe with false defaults.
        // c.Require(this, AllowLocalhost);
        // c.Require(this, AllowAnyhost);
    }

}


internal class AspNetServer :  ITomlMetadataProvider, IValidationCapable {

    public TomlPropertiesMetadata? PropertiesMetadata { get; set; }
    public bool? UseDeveloperExceptionPage { get; set; }
    public string? UseExceptionHandler { get; set; } 
    public bool? UseHsts { get; set; }
    public bool? UseHttpsRedirection { get; set; } 
    public bool? RestartWhenThisFileChanges { get; set; }
    public AspNetRewriteRuleSection? ApacheModRewrite { get; set; } 
    //public AspNetRewriteRuleSection? IisUrlRewrite { get; set; } 
    public void Validate(ValidationContext c) {
        c.ValidateRecursive(this,UseDeveloperExceptionPage, UseDeveloperExceptionPage == true || !string.IsNullOrWhiteSpace(UseExceptionHandler));
    }
}



  internal class AspNetRewriteRuleSection : ITomlMetadataProvider, IValidationCapable
{
    public TomlPropertiesMetadata? PropertiesMetadata { get; set; }

    // public string? Text { get; set; } // Or name it content for consistency?
    public string? File { get; set; }

    public void Validate(ValidationContext c)
    {
        // if (Text == null && File == null){
        //     throw c.InvalidProperty(Text, "either text or path must be specified");
        // }
        // if (Path != null){
        //     c.ValidateRecursive(this, Path, File.Exists(Path)); // TODO, expand string variables
        // }
    }
}




internal class StaticResponse : ITomlMetadataProvider, IValidationCapable
{
    public TomlPropertiesMetadata? PropertiesMetadata { get; set; }
    

    public required string For { get; set; }
    public string? ContentType { get; set; }
    public string? Content { get; set; }
    public string? File { get; set; }

    public string? CacheControl { get; set; }
    public int? StatusCode { get; set; }
    // public List<HeaderPair>? ResponseHeaders { get; set; }

   
    public void Validate(ValidationContext c)
    {
        c.Require(this, For);
        c.Require(this, ContentType);
        c.Require(this, Content);
        c.Require(this, StatusCode);
        //c.ValidateRecursive(this, ResponseHeaders, true);
    }
}

internal class Security : ITomlMetadataProvider, IValidationCapable
{
    public TomlPropertiesMetadata? PropertiesMetadata { get; set; }
    
    // public bool? OnlyPermitPresets { get; set; }
    public MaxResolution? MaxDecodeResolution { get; set; }
    public MaxResolution? MaxEncodeResolution { get; set; }
    public MaxResolution? MaxFrameResolution { get; set; }

    
    public void Validate(ValidationContext c)
    {
        // Add your validations here
        c.ValidateRecursive(this, MaxDecodeResolution, true);
        c.ValidateRecursive(this, MaxEncodeResolution, true);
        c.ValidateRecursive(this, MaxFrameResolution, true);
    }
}


internal class MaxResolution : ITomlMetadataProvider, IValidationCapable
{
    public TomlPropertiesMetadata? PropertiesMetadata { get; set; }

    public uint? Width { get; set; }
    public uint? Height { get; set; }
    public int? Megapixels { get; set; }

    public void Validate(ValidationContext c)
    {
        c.ValidateRecursive(this, Width, Width > 0);
        c.ValidateRecursive(this, Height, Height > 0);
        c.ValidateRecursive(this, Megapixels, Megapixels > 0);
    }

    // to new FrameSizeLimit
    public FrameSizeLimit ToFrameSizeLimit(){
        // TODO: unsure if 0 means unlimited or not specified
        return new FrameSizeLimit(Width ?? 0, Height ?? 0, Megapixels ?? 0);

    }
}
internal class ImageflowConfig : ITomlMetadataProvider, IValidationCapable
{
    public TomlPropertiesMetadata? PropertiesMetadata { get; set; }
    public ImageflowServer? ImageflowServer { get; set; }
    public LicenseSection? License { get; set; }
    public HybridCacheSection? DiskCache { get; set; }
    public Diagnostics? Diagnostics { get; set; }
    public AspNetServer? AspnetServer { get; set; }
    // public TomlTable? Vars { get; set; }
    // public Dictionary<string,PresetSection>? Presets { get; set; }
    public RouteBase? RouteDefaults { get; set; }

    //public List<SourceSection>? Sources { get; set; }
    public List<RouteSection>? Routes { get; set; }
    
    //public List<Watermark>? Watermarks { get; set; }
    public Security? Security { get; set; }

    //static response
    public List<StaticResponse>? StaticResponse { get; set; }

    public void Validate(ValidationContext c)
    {
        c.Require(this, ImageflowServer);
        c.ValidateRecursive(this, ImageflowServer, true);
        c.Require(this, License);
        c.ValidateRecursive(this, License, true);
        //c.ValidateRecursive(this, HybridCache, true);
        c.ValidateRecursive(this, Diagnostics, true);
        c.ValidateRecursive(this, AspnetServer, true);
        //c.ValidateRecursive(this, Vars, true);
        //c.ValidateRecursive(this, Presets, true);
        // c.ValidateRecursive(this, Sources, true);
        // c.ValidateRecursive(this, Routes, true);
        // c.ValidateRecursive(this, Watermarks, true);
        c.ValidateRecursive(this, Security, true);
        c.ValidateRecursive(this, StaticResponse, true);
    }
}

// List each class and properties above this line
// class MaxResolution { uint? Width, Height, Megapixels }
// class Security { MaxResolution? MaxDecodeResolution, MaxEncodeResolution, MaxFrameResolution }
// 