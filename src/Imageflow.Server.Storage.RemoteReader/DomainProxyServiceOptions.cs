
public class DomainProxyServiceOptions
{
    public List<DomainProxyPrefix> Prefixes { get; set; } = new List<DomainProxyPrefix>();

    public string DefaultHttpClientName { get; set; } = "default";

    public DomainProxyServiceOptions()
    {
        Prefixes = new List<DomainProxyPrefix>();
    }
    public DomainProxyServiceOptions AddPrefix(string prefix, string remoteUriBase, bool ignorePrefixCase, string httpClientName = null)
    {
        Prefixes.Add(new DomainProxyPrefix(prefix, remoteUriBase, ignorePrefixCase, httpClientName));
        return this;
    }

    public DomainProxyServiceOptions AddPrefix(string prefix, string remoteUriBase)
    {
        Prefixes.Add(new DomainProxyPrefix(prefix, remoteUriBase, true, null));
        return this;
    }
}
