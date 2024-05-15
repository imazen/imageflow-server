global using IBlobResult = Imazen.Abstractions.Resulting.IDisposableResult<Imazen.Abstractions.Blobs.IBlobWrapper,
    Imazen.Abstractions.Resulting.HttpStatus>;
global using CacheFetchResult = Imazen.Abstractions.Resulting.IResult<Imazen.Abstractions.Blobs.IBlobWrapper, Imazen.Abstractions.BlobCache.IBlobCacheFetchFailure>;

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ImazenShared.Tests")]
[assembly: InternalsVisibleTo("Imageflow.Server")]
[assembly: InternalsVisibleTo("Imageflow.Server.Tests")]
[assembly: InternalsVisibleTo("ImageResizer")]
[assembly: InternalsVisibleTo("ImageResizer.LicensingTests")]
[assembly: InternalsVisibleTo("ImageResizer.HybridCache.Tests")]
[assembly: InternalsVisibleTo("ImageResizer.HybridCache.Benchmark")]
[assembly: InternalsVisibleTo("Imazen.Routing")]
[assembly: InternalsVisibleTo("Imazen.Routing.Tests")]
[assembly: InternalsVisibleTo("Imazen.HybridCache.Tests")]

