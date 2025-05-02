using Azure.Storage.Blobs;
using Imageflow.Server.Storage.AzureBlob.Caching;

namespace Imageflow.Server.Storage.AzureBlob
{
    public class AzureBlobServiceOptions
    {   
        [Obsolete("Use BlobServiceClient instead")]
        public BlobClientOptions? BlobClientOptions { get; set; }
        
        [Obsolete("Use BlobServiceClient instead")]
        public string? ConnectionString { get; set; }

        internal readonly List<NamedCacheConfiguration> NamedCaches = new List<NamedCacheConfiguration>();

        internal BlobClientOrName? BlobServiceClient { get; set; }
        public string? UniqueName { get; set; }

        internal readonly List<PrefixMapping> Mappings = new List<PrefixMapping>();
        
        [Obsolete("Use AzureBlobServiceOptions(new BlobServiceClient(connectionString, blobClientOptions)) instead or .ICalledAddBlobServiceClien(blobClientName) instead")]
        public AzureBlobServiceOptions(string connectionString, BlobClientOptions? blobClientOptions = null)
        {
            BlobClientOptions = blobClientOptions ?? new BlobClientOptions();
            ConnectionString = connectionString;
        }

        /// <summary>
        /// If you use the Azure SDK to call AddBlobServiceClient, you can use ICalledAddBlobServiceClient(blobClientName) to create an AzureBlobServiceOptions instance without directly specifying it.
        /// </summary>
        /// <param name="blobServiceClient"></param>
        public AzureBlobServiceOptions(BlobServiceClient blobServiceClient)
        {
            BlobServiceClient = new BlobClientOrName(blobServiceClient);
        }

        /// <summary>
        /// Relies on a separate 
        /// </summary>
        internal AzureBlobServiceOptions()
        {
        }

        /// <summary>
        /// If you use the Azure SDK to call AddBlobServiceClient, you can use this method to create an AzureBlobServiceOptions instance.
        /// https://learn.microsoft.com/en-us/dotnet/azure/sdk/dependency-injection
        /// </summary>
        /// <returns></returns>
        // ReSharper disable once InconsistentNaming
        public static AzureBlobServiceOptions ICalledAddBlobServiceClient(){
            return new AzureBlobServiceOptions();
        }
        // ReSharper disable once InconsistentNaming
        public static AzureBlobServiceOptions ICalledAddBlobServiceClientWithName(string blobClientName){
            return new AzureBlobServiceOptions(){
                BlobServiceClient = new BlobClientOrName(blobClientName)
            };
        }


//turn off obsolete warnings for this line
#pragma warning disable 618
        internal BlobClientOrName? GetOrCreateClient() => BlobServiceClient ?? ((ConnectionString != null && BlobClientOptions != null) ? new BlobClientOrName(new BlobServiceClient(ConnectionString, BlobClientOptions)) : default(BlobClientOrName?));
#pragma warning restore 618
        public AzureBlobServiceOptions MapPrefix(string urlPrefix, string container)
            => MapPrefix(urlPrefix, container, "");

        public AzureBlobServiceOptions MapPrefix(string urlPrefix, string container, bool ignorePrefixCase, bool lowercaseBlobPath)
            => MapPrefix(urlPrefix, container, "", ignorePrefixCase, lowercaseBlobPath);
            
        public AzureBlobServiceOptions MapPrefix(string urlPrefix, string container, string blobPrefix)
            => MapPrefix(urlPrefix, container, blobPrefix, false, false);
        public AzureBlobServiceOptions MapPrefix(string urlPrefix, string container, string blobPrefix, bool ignorePrefixCase, bool lowercaseBlobPath)
        {
            var prefix = urlPrefix.TrimStart('/').TrimEnd('/');
            if (prefix.Length == 0)
            {
                throw new ArgumentException("Prefix cannot be /", nameof(prefix));
            }

            prefix = '/' + prefix + '/';

            blobPrefix = blobPrefix.Trim('/');


            Mappings.Add(new PrefixMapping()
            {
                Container = container, 
                BlobPrefix = blobPrefix, 
                UrlPrefix = prefix, 
                IgnorePrefixCase = ignorePrefixCase,
                LowercaseBlobPath = lowercaseBlobPath
            });
            return this;
        }

        /// <summary>
        /// Adds a named cache location to the AzureBlobService. This allows you to use the same service instance to provide persistence for caching result images.
        /// You must also ensure that the bucket is located in the same AWS region as the Imageflow Server, and that it is not publicly accessible. 
        /// </summary>
        /// <param name="namedCacheConfiguration"></param>
        /// <returns></returns>
        public AzureBlobServiceOptions AddNamedCacheConfiguration(NamedCacheConfiguration namedCacheConfiguration)
        {
            NamedCaches.Add(namedCacheConfiguration);
            return this;
        }
    }
}