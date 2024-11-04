using Imazen.Common.Storage;
#pragma warning disable CS0618 // Type or member is obsolete

namespace Imageflow.Server.LegacyBlob
{
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
}
