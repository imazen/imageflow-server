
namespace Imageflow.Server.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Imageflow.Server.Configuration.Parsing;
using Imageflow.Server.Configuration.Execution;


public interface IAppConfigurator{
    bool RestartWhenThisFileChanges { get; }

    void ConfigureApp(IApplicationBuilder app, IWebHostEnvironment env);
    void    ConfigureServices(IServiceCollection services);

    // For diagnostic purposes
    Dictionary<string, string> GetComputedConfiguration(bool redactSecrets);
}

public interface ITomlParseResult{
    IAppConfigurator GetAppConfigurator();
}

public enum DeploymentEnvironment{
    Production,
    Staging,
    Development
}

internal class PhysicalConfigFilesystem : IAbstractFileMethods
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
}

public class TomlParserContext{
    public TomlParserContext(DeploymentEnvironment activeEnvironment, [NotNull]Dictionary<string,string> appVariables,  [NotNull]Func<string,string?> getEnvironmentVariable, IAbstractFileMethods? filesystem){
        this.AppVariables = appVariables;
        this.ActiveEnvironment = activeEnvironment;
        GetEnvironmentVariable = getEnvironmentVariable;
        Filesystem = filesystem ?? new PhysicalConfigFilesystem();
    }
    public TomlParserContext(IWebHostEnvironment env){
        // Get all the app variables
        AppVariables = new Dictionary<string, string>
        {
            { "approot", env.ContentRootPath },
            { "wwwroot", env.WebRootPath }
        };
        // Get the environment
        //Parse, case insensitive, env.EnvironmentName
        ActiveEnvironment = env.EnvironmentName.ToLowerInvariant() switch
        {
            "production" => DeploymentEnvironment.Production,
            "prod" => DeploymentEnvironment.Production,
            "staging" => DeploymentEnvironment.Staging,
            "development" => DeploymentEnvironment.Development,
            "dev" => DeploymentEnvironment.Development,
            _ => throw new Exception($"Unknown environment {env.EnvironmentName}")
        };
        GetEnvironmentVariable = (key) => System.Environment.GetEnvironmentVariable(key);
        Filesystem = new PhysicalConfigFilesystem();
    }

    public IAbstractFileMethods Filesystem {get; init;}
    public DeploymentEnvironment ActiveEnvironment {get; init;}
    public Func<string,string?> GetEnvironmentVariable { get; init; }
    public Dictionary<string, string> AppVariables { get; init;}
}

public class TomlParser
{
    public static ITomlParseResult LoadAndParse(string path, TomlParserContext context){
        // Read the file
        var tomlText = File.ReadAllText(path);
        return Parse(tomlText, path, context);
    }

    public static ITomlParseResult Parse(string tomlText, string sourcePath, TomlParserContext context){
        return new TomlParseResult(tomlText, sourcePath, context);
    }
        
}

internal class TomlParseResult : ITomlParseResult
{
    TomlParserContext context;
    string sourcePath;
    ImageflowConfig ParsedConfig;
    ConfigRedactor Redactor { get; init; }

    ExecutorContext executorContext;

    DefaultVarLookup varLookup;

    internal TomlParseResult(string tomlText, string sourcePath, TomlParserContext context)
    {
        this.sourcePath = sourcePath;
        this.context = context;
        varLookup = new DefaultVarLookup(context.AppVariables,context.GetEnvironmentVariable);
        
        
       // First, preprocess all names that start with 'production.' or 'development.' into the appropriate section
        var preprocessor = new TomlEnvironmentPreprocessor(tomlText,context.ActiveEnvironment.ToString().ToLowerInvariant(), sourcePath);
        var preprocessedToml = preprocessor.Preprocess();

        // Collect secrets for redaction
        Redactor = new ConfigRedactor(preprocessedToml.ProcessedModel);

        // Parse to model
        ParsedConfig = Tomlyn.Toml.ToModel<ImageflowConfig>(preprocessedToml.ProcessedText);

        var interpolator = new StringInteroplation(varLookup, sourcePath);

        
        //this.varsParser = new VarsParser(config.Vars, context);

        executorContext = new ExecutorContext(sourcePath, interpolator.InterpolateString, Redactor, context.Filesystem);
        
        var validatorContext = new ValidationContext(){
            FileName = sourcePath,
            FileContents = tomlText,
            Redactor = Redactor
        };
        // Mid-validation
        ParsedConfig.Validate(validatorContext);
    }


    public IAppConfigurator GetAppConfigurator()
    {
        return new Executor(ParsedConfig, executorContext);
    }
}
