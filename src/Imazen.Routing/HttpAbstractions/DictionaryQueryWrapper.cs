using System.Collections;
using Microsoft.Extensions.Primitives;

namespace Imazen.Routing.HttpAbstractions;

public class DictionaryQueryWrapper : IReadOnlyQueryWrapper
{
    public DictionaryQueryWrapper(IDictionary<string,StringValues> dictionary)
    {
        d = dictionary;
    }

    private readonly IDictionary<string,StringValues> d;
    
    internal IDictionary<string,StringValues> UnderlyingDictionary => d;
    
    public bool TryGetValue(string key, out string? value)
    {
        if (d.TryGetValue(key, out var values))
        {
            value = values;
            return true;
        }
        value = null;
        return false;
    }
    
    public bool TryGetValue(string key, out StringValues value)
    {
        return d.TryGetValue(key, out value);
    }
    
    public bool ContainsKey(string key)
    {
        return d.ContainsKey(key);
    }
    
    public StringValues this[string key] => d[key];
    
    public IEnumerable<string> Keys => d.Keys;
    
    public int Count => d.Count;
    
    
    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
    {
        return d.GetEnumerator();
    }
    
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }


    private static readonly Dictionary<string,StringValues> EmptyDict = new Dictionary<string, StringValues>();
    public static IReadOnlyQueryWrapper Empty { get; } = new DictionaryQueryWrapper(EmptyDict);
}