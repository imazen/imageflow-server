using System;
using System.Collections.Generic;
using System.IO;
using Imageflow.Fluent;
using System.Text.Json;

namespace Imageflow.Server
{
    public class NamedWatermark
    {
        public NamedWatermark(string name, string virtualPath, WatermarkOptions watermark)
        {
            Name = name;
            VirtualPath = virtualPath;
            Watermark = watermark;
            serialized = null;
        }
        public string Name { get; }
        public string VirtualPath { get; }
        public WatermarkOptions Watermark { get; }
        
        
        private readonly string[] serialized;

        internal IEnumerable<string> Serialized()
        {
            if (serialized != null) return serialized;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(Watermark.ToImageflowDynamic(0));
            var json64 = Convert.ToBase64String(bytes);
            return new[] { Name, VirtualPath, json64 };
        }
    }
}