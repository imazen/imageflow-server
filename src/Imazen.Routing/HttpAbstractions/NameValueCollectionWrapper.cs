using System.Collections;
using System.Collections.Specialized;
using Microsoft.Extensions.Primitives;

namespace Imazen.Routing.HttpAbstractions;

public class NameValueCollectionWrapper : IReadOnlyQueryWrapper
{
    public NameValueCollectionWrapper(NameValueCollection collection)
    {
        c = collection;
    }

    private readonly NameValueCollection c;

    public bool TryGetValue(string key, out string? value)
    {
        value = c[key];
        return value != null;
    }

    public bool TryGetValue(string key, out StringValues value)
    {
        value = c.GetValues(key);
        return value != StringValues.Empty;
    }

    public bool ContainsKey(string key)
    {
        return c[key] != null;
    }

    public StringValues this[string key] => (StringValues)(c.GetValues(key) ?? Array.Empty<string>());

    public IEnumerable<string> Keys
    {
        get
        {
            foreach (var k in c.AllKeys)
            {
                if (k != null) yield return k;
                yield return "";
            }
        }
    }

    public int Count => c.Count;
    
    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
    {
        foreach (var key in c.AllKeys)
        {
            yield return new KeyValuePair<string, StringValues>(key ?? "", (StringValues)(c.GetValues(key) ?? Array.Empty<string>()));
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}