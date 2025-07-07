using System;
using System.IO;
using System.Net.Http;
using Imazen.Common.Storage;


namespace Imageflow.Server.Storage.RemoteReader
{
    #pragma warning disable CS0618
    public class RemoteReaderBlob : IBlobData
    {
        private readonly HttpResponseMessage response;

        internal RemoteReaderBlob(HttpResponseMessage r)
        {
            response = r;
        }

        public bool? Exists => true;

        public DateTime? LastModifiedDateUtc => response.Headers.Date?.UtcDateTime;

        public void Dispose()
        {
            response.Dispose();
        }

        public Stream OpenRead()
        {
            return response.Content.ReadAsStreamAsync().Result;
        }
    }
    #pragma warning restore CS0618
}
