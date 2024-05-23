using Microsoft.Extensions.Primitives;

namespace Imazen.Routing.HttpAbstractions;


/// <summary>
/// Can be created over an IQueryCollection or NameValueCollection or IEnumerable<KeyValuePair<string,StringValues>> or Dictionary<string,string>
/// </summary>
public interface IReadOnlyQueryWrapper : IReadOnlyCollection<KeyValuePair<string,StringValues>>
{
    bool TryGetValue(string key, out string? value);
    bool TryGetValue(string key, out StringValues value);
    bool ContainsKey(string key);
    StringValues this[string key] { get; }
    IEnumerable<string> Keys { get; }
    
}