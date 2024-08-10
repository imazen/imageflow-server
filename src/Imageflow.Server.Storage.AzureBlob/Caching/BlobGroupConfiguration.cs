
// Contains BlobGroupLifecycle and BlobGroupLocation
using System;
namespace Imageflow.Server.Storage.AzureBlob.Caching
{
    internal readonly struct BlobGroupConfiguration
    {
        internal readonly BlobGroupLocation Location;
        internal readonly BlobGroupLifecycle Lifecycle;

        internal readonly bool UpdateLifecycleRules;
        internal readonly bool CreateContainerIfMissing;

        internal BlobGroupConfiguration(BlobGroupLocation location, BlobGroupLifecycle lifecycle, bool createContainerIfMissing, bool updateLifecycleRules){
            //location cannot be empty
            if (location.ContainerName == null)
            {
                throw new ArgumentNullException(nameof(location));
            }
            Location = location;
            Lifecycle = lifecycle;
            
            this.CreateContainerIfMissing = createContainerIfMissing;
            this.UpdateLifecycleRules = updateLifecycleRules;
        }
    }
}