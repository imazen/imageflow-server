namespace Imageflow.Server.LegacyBlob
{
    public class RemoteReaderServiceOptions
    {
        internal readonly List<string> Prefixes = new List<string>();
        public required string SigningKey { get; set; }
        
        /// <summary>
        /// Sometimes you need to support multiple signing keys; either for different users or for
        /// phasing out a leaked key. Trying multiple keys during each request adds a bit a processing time;
        /// benchmark performance if you have many.
        /// </summary>
        public IEnumerable<string>? SigningKeys { get; set; }
        public bool IgnorePrefixCase { get; set; }

        public Func<Uri, string> HttpClientSelector { get; set; } = _ => "";

        public RemoteReaderServiceOptions AddPrefix(string prefix)
        {
            prefix = prefix.TrimStart('/').TrimEnd('/');
            if (prefix.Length == 0)
            {
                throw new ArgumentException("Prefix cannot be /", nameof(prefix));
            }

            prefix = '/' + prefix + '/';

            Prefixes.Add(prefix);
            return this;
        }
    }
}
