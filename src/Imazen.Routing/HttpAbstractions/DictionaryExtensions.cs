using Microsoft.Extensions.Primitives;

namespace Imazen.Routing.HttpAbstractions;

public static class DictionaryExtensions
{
    public static Dictionary<string,string> ToStringDictionary(this IReadOnlyQueryWrapper query)
    {
        var d = new Dictionary<string,string>(query.Count);
        foreach (var kvp in query)
        {
            d[kvp.Key] = kvp.Value.ToString();
        }
        return d;
    }

      public static Dictionary<string,StringValues> ToStringValuesDictionary(this IReadOnlyQueryWrapper query)
    {
#if NET6_0_OR_GREATER
        var d = new Dictionary<string,StringValues>(query);
#else
        var d = new Dictionary<string,StringValues>(query.Count);
        foreach (var kvp in query)
        {
            d[kvp.Key] = kvp.Value;
        }
#endif
        return d;
    }
    public static Dictionary<string,string> ToStringDictionary(this IDictionary<string,StringValues> query)
    {
        var d = new Dictionary<string,string>(query.Count);
        foreach (var kvp in query)
        {
            d[kvp.Key] = kvp.Value.ToString();
        }
        return d;
    }

    public static Dictionary<string, StringValues> ToStringValuesDictionary(
        this IEnumerable<KeyValuePair<string, string>> query)
    {
        var d = new Dictionary<string, StringValues>();
        foreach (var kvp in query)
        {
            // parse the string into a StringValues, delimited by commas
            d[kvp.Key] = new StringValues(kvp.Value.Split(','));
        }
        return d;
    }
}
