namespace Imazen.Abstractions.AssemblyAttributes;

/// <summary>
/// Indicates that the assembly should match the version of one or more other assemblies.
/// </summary>
/// <param name="assemblyPatterns"></param>
[AttributeUsage(AttributeTargets.Assembly)]
public class VersionedWithAssembliesAttribute(params string[] assemblyPatterns) : Attribute
{
    public string[] AssemblyPatterns { get; } = assemblyPatterns;

    public override string ToString()
    {
        return $"This assembly should match the version of {string.Join(", ", AssemblyPatterns)}";
    }
}