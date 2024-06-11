using System.Reflection;
using System.Text;
using Imazen.Abstractions.AssemblyAttributes;

namespace Imazen.Routing.Helpers;

internal static class AssemblyHelpers
{

    private static bool TryGetVersionedWithAssemblies(this Assembly a, out string[]? patterns, out Exception? error)
    {
        patterns = null;
        error = null;
        try
        {
            var attrs = a.GetCustomAttributes(typeof(VersionedWithAssembliesAttribute), false);
            if (attrs.Length > 0)
            {
                patterns = ((VersionedWithAssembliesAttribute)attrs[0]).AssemblyPatterns;
                return true;
            }
        }
        catch (Exception e)
        {
            error = e;
        }

        return false;
    }

    private readonly record struct AssemblyVersionInfo
    {
        internal string? CompatibilityVersion { get; init; }
        internal string? AssemblyVersion { get; init; }
        internal string? FileVersion { get; init; }
        internal string? InformationalVersion { get; init; }
        internal string? FullName { get; init; }
        internal string? Commit { get; init; }
        internal string? BuildDate { get; init; }
        internal string? NugetPackageName { get; init; }
        internal Exception? AccessError { get; init; }

        public bool IsFileNotFoundException => AccessError is FileNotFoundException;

        public bool HasCompatibilityVersion => !string.IsNullOrWhiteSpace(CompatibilityVersion);

        public static AssemblyVersionInfo FromAssembly(Assembly a)
        {
            // catch exceptions (missing dependencies, etc)
            try
            {
                Exception? error = null;
                var fullName = a.FullName;
                string? version;
                try
                {
                    version = a.GetName().Version?.ToString();
                }
                catch (Exception e)
                {
                    version = null;
                    error = e;
                }

                var fileVersion = a.TryGetFirstAttribute<AssemblyFileVersionAttribute>(ref error)?.Version;
                var infoVersion = a.TryGetFirstAttribute<AssemblyInformationalVersionAttribute>(ref error)
                    ?.InformationalVersion;
                var buildDate = a.TryGetFirstAttribute<BuildDateAttribute>(ref error)?.Value
#pragma warning disable CS0618 // Type or member is obsolete
                                ?? a.TryGetFirstAttribute<Imazen.Common.Licensing.BuildDateAttribute>(ref error)?.Value;
#pragma warning restore CS0618 // Type or member is obsolete
                var commit = a.TryGetFirstAttribute<CommitAttribute>(ref error)?.Value 
#pragma warning disable CS0618 // Type or member is obsolete
                             ?? a.TryGetFirstAttribute<Imazen.Common.Licensing.CommitAttribute>(ref error)?.Value;
#pragma warning restore CS0618 // Type or member is obsolete
                var nugetPackageName = a.TryGetFirstAttribute<NugetPackageAttribute>(ref error)?.PackageName;
                return new AssemblyVersionInfo
                {
                    FileVersion = fileVersion,
                    InformationalVersion = infoVersion,
                    FullName = fullName,
                    AssemblyVersion = version,
                    BuildDate = buildDate,
                    Commit = commit,
                    NugetPackageName = nugetPackageName,
                    CompatibilityVersion = string.IsNullOrWhiteSpace(fileVersion) ? null : fileVersion,
                };

            }
            catch (Exception e)
            {
                return new AssemblyVersionInfo
                {
                    AccessError = e
                };
            }


        }
    }

    private class AssemblyGroup
    {
        private List<string> Patterns { get; } = [];
        internal List<Assembly> Assemblies { get; } = [];

        internal List<AssemblyVersionInfo> VersionInfo { get; } = [];
        public int AssemblyCount => Assemblies.Count;

        internal bool HasMissingAssemblyVersionData;
        internal List<AssemblyVersionInfo>? MissingCompatibilityVersionData;
        internal bool HasMismatchedFileVersions;
        internal List<AssemblyVersionInfo>? MismatchedFileVersions;

        private bool calculated;

        internal void CalculateFinal()
        {
            if (calculated) return;
            calculated = true;

            HasMissingAssemblyVersionData = VersionInfo.Any(v => !v.HasCompatibilityVersion);
            if (HasMissingAssemblyVersionData)
            {
                MissingCompatibilityVersionData = VersionInfo.Where(v => !v.HasCompatibilityVersion).ToList();
            }

            HasMismatchedFileVersions = VersionInfo.GroupBy(v => v.CompatibilityVersion).Count() > 1;
            if (HasMismatchedFileVersions)
            {
                MismatchedFileVersions = VersionInfo.Where(v => v.HasCompatibilityVersion)
                    .OrderBy(v => v.CompatibilityVersion).ToList();
            }
        }

        private static bool PatternMatches(string pattern, string? patternOrAssembly)
        {
            if (string.IsNullOrWhiteSpace(patternOrAssembly)) return false;
            if (pattern.Equals(patternOrAssembly, StringComparison.OrdinalIgnoreCase)) return true;
            return pattern.EndsWith("*") &&
                   patternOrAssembly!.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        }

        internal bool Matches(string pattern)
        {
            return Patterns.Any(p => PatternMatches(pattern, p)) ||
                   VersionInfo.Any(v => PatternMatches(pattern, v.FullName ?? ""));
        }

        public void Add(Assembly a)
        {
            if (Assemblies.Contains(a)) return;
            Assemblies.Add(a);
            var info = AssemblyVersionInfo.FromAssembly(a);
            VersionInfo.Add(info);
            if (!a.TryGetVersionedWithAssemblies(out var patterns, out _)) return;
            if (patterns != null) Patterns.AddRange(patterns);
        }


    }

    private static List<AssemblyGroup> GetVersionedWithAssemblyGroups(this Assembly[] list)
    {
        var groups = new List<AssemblyGroup>();
        foreach (var a in list)
        {
            var assemblyName = a.GetName().Name;
            if (string.IsNullOrWhiteSpace(assemblyName)) continue;
            var group = groups.FirstOrDefault(g => g.Matches(assemblyName));
            if (group == null)
            {
                group = new AssemblyGroup();
                groups.Add(group);
            }
            group.Add(a);
        }
        foreach (var group in groups)
        {
            group.CalculateFinal();
        }

        return groups;
    }

    internal static string ExplainVersionMismatches(this Assembly[] loadedAssemblies, bool listSuccesses = false)
    {
        var groups = loadedAssemblies.GetVersionedWithAssemblyGroups();

        var sb = new StringBuilder();
        foreach (var group in groups)
        {
            bool ok = group is { HasMissingAssemblyVersionData: false, HasMismatchedFileVersions: false };
            if (ok && group.AssemblyCount > 1 && listSuccesses)
            {
                // {x} assemblies have compatible versions (version)
                sb.AppendLine(
                    $"OK: {group.Assemblies.Count} assemblies have compatible versions ({group.VersionInfo[0].CompatibilityVersion})");
            }

            if (group.HasMissingAssemblyVersionData)
            {
                sb.AppendLine(
                    $"ERROR: Missing compatibility version for {group.MissingCompatibilityVersionData?.Count} assemblies:");
                foreach (var info in group.MissingCompatibilityVersionData!)
                {
                    sb.AppendLine($"ERROR:  {info.FullName} {info.AccessError?.Message}");
                }
            }

            if (group.HasMismatchedFileVersions)
            {
                sb.AppendLine($"ERROR: Mismatched file versions for {group.MismatchedFileVersions?.Count} assemblies:");
                foreach (var info in group.MismatchedFileVersions!)
                {
                    sb.AppendLine($"   {info.FullName} ({info.FileVersion})");
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static T? GetFirstAttribute<T>(this ICustomAttributeProvider a)
    {
        try
        {
            var attrs = a.GetCustomAttributes(typeof(T), false);
            if (attrs.Length > 0) return (T)attrs[0];
        }
        catch (FileNotFoundException)
        {
            //Missing dependencies
        }
        // ReSharper disable once EmptyGeneralCatchClause
        catch (Exception)
        {
        }

        return default;
    }

    private static T? TryGetFirstAttribute<T>(this ICustomAttributeProvider a, ref Exception? error)
    {
        try
        {
            var attrs = a.GetCustomAttributes(typeof(T), false);
            if (attrs.Length > 0) return (T)attrs[0];
        }
        catch (Exception e)
        {
            error = e;
        }

        return default;
    }

    public static Exception? GetExceptionForReading<T>(this Assembly a)
    {
        try
        {
            var unused = a.GetCustomAttributes(typeof(T), false);
        }
        catch (Exception e)
        {
            return e;
        }

        return null;
    }

    public static string? GetInformationalVersion(this Assembly a)
    {
        return GetFirstAttribute<AssemblyInformationalVersionAttribute>(a)?.InformationalVersion;
    }

    public static string? GetFileVersion(this Assembly a)
    {
        return GetFirstAttribute<AssemblyFileVersionAttribute>(a)?.Version;
    }




    // Imazen.Abstractions.AssemblyAttributes.VersionedWithAssemblies
    // Imazen.Abstractions.AssemblyAttributes.BuildDate

    public static string? GetBuildDate(this Assembly a)
    {
        return GetFirstAttribute<BuildDateAttribute>(a)?.Value 
#pragma warning disable CS0618 // Type or member is obsolete
            ?? GetFirstAttribute<Imazen.Common.Licensing.BuildDateAttribute>(a)?.Value;
#pragma warning restore CS0618 // Type or member is obsolete
    }

    public static string[]? GetVersionedWithAssemblies(this Assembly a)
    {
        return GetFirstAttribute<VersionedWithAssembliesAttribute>(a)?.AssemblyPatterns;
    }

    public static string? GetCommit(this Assembly a)
    {
        return GetFirstAttribute<CommitAttribute>(a)?.Value 
#pragma warning disable CS0618 // Type or member is obsolete
               ?? GetFirstAttribute<Imazen.Common.Licensing.CommitAttribute>(a)?.Value;
#pragma warning restore CS0618 // Type or member is obsolete
    }

    // Get nuget package name for an assembly

    public static string? GetNugetPackageName(this Assembly a)
    {
        return GetFirstAttribute<NugetPackageAttribute>(a)?.PackageName;
    }

    public static IReadOnlyCollection<KeyValuePair<string, string>> GetMetadataPairs(this Assembly a)
    {
        // sus, autogenerated, where's the try catch and why no casting?
        var pairs = new List<KeyValuePair<string, string>>();
        var attrs = a.GetCustomAttributesData();
        foreach (var attr in attrs)
        {
            if (attr.AttributeType == typeof(AssemblyMetadataAttribute))
            {

                var key = attr.ConstructorArguments[0].Value?.ToString();
                var value = attr.ConstructorArguments[1].Value?.ToString();
                if (key != null && value != null)
                {
                    pairs.Add(new KeyValuePair<string, string>(key, value));
                }
            }
        }

        return pairs;
    }
}