namespace Imageflow.Server.Storage.AzureBlob.Caching;

internal class ContainerExistenceCache
{
    private Tuple<string, bool>[] containerExistenceCache;
    
    public ContainerExistenceCache(IEnumerable<string> containerNames)
    {
        var unique = containerNames.Distinct().ToArray();
        containerExistenceCache = new Tuple<string, bool>[unique.Length];
        for (int i = 0; i < unique.Length; i++)
        {
            containerExistenceCache[i] = new Tuple<string, bool>(unique[i], false);
        }
    }
    
    public bool Maybe(string containerName)
    {
        var result = containerExistenceCache.FirstOrDefault(x => x.Item1 == containerName);
        if (result == null)
        {
            return false;
        }
        return result.Item2;
    }
    public void Set(string containerName, bool exists)
    {
        for (int i = 0; i < containerExistenceCache.Length; i++)
        {
            if (containerExistenceCache[i].Item1 == containerName)
            {
                containerExistenceCache[i] = new Tuple<string, bool>(containerName, exists);
            }
        }
    }
}