using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Imazen.Caching;
using Moq;

namespace Imageflow.Server.Storage.S3.Tests;

public class S3CacheProviderTests
{
    private static S3CacheProvider CreateProvider(Mock<IAmazonS3>? mockClient = null, string? prefix = null)
    {
        var client = mockClient ?? new Mock<IAmazonS3>();
        return new S3CacheProvider("s3-test", client.Object, "test-bucket", prefix, "us-east-1");
    }

    [Fact]
    public void Constructor_SetsNameAndCapabilities()
    {
        var provider = CreateProvider();
        Assert.Equal("s3-test", provider.Name);
        Assert.False(provider.Capabilities.RequiresInlineExecution);
        Assert.Equal("s3:us-east-1:test-bucket", provider.Capabilities.LatencyZone);
        Assert.False(provider.Capabilities.IsLocal);
    }

    [Fact]
    public async Task FetchAsync_Hit_ReturnsStreamResult()
    {
        var mock = new Mock<IAmazonS3>();
        var data = Encoding.UTF8.GetBytes("cached image data");
        var key = CacheKey.FromStrings("/images/photo.jpg", "w=800&format=webp");

        mock.Setup(c => c.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                HttpStatusCode = HttpStatusCode.OK,
                ResponseStream = new MemoryStream(data),
                Headers = { ContentType = "image/webp" }
            });

        var provider = CreateProvider(mock);
        var result = await provider.FetchAsync(key);

        Assert.NotNull(result);
        Assert.NotNull(result!.DataStream);
        Assert.Equal("image/webp", result.Metadata.ContentType);

        using (result)
        {
            using var ms = new MemoryStream();
            await result.DataStream!.CopyToAsync(ms);
            Assert.Equal(data, ms.ToArray());
        }

        // Verify the correct key was used
        mock.Verify(c => c.GetObjectAsync(
            It.Is<GetObjectRequest>(r => r.BucketName == "test-bucket" && r.Key == key.ToStoragePath()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FetchAsync_Hit_WithPrefix_UsesCorrectKey()
    {
        var mock = new Mock<IAmazonS3>();
        var key = CacheKey.FromStrings("/test", "params");

        mock.Setup(c => c.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                HttpStatusCode = HttpStatusCode.OK,
                ResponseStream = new MemoryStream(new byte[] { 1 }),
                Headers = { ContentType = "image/png" }
            });

        var provider = CreateProvider(mock, prefix: "cache/");
        var result = await provider.FetchAsync(key);
        Assert.NotNull(result);
        result!.Dispose();

        mock.Verify(c => c.GetObjectAsync(
            It.Is<GetObjectRequest>(r => r.Key.StartsWith("cache/")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FetchAsync_Miss_ReturnsNull()
    {
        var mock = new Mock<IAmazonS3>();
        var key = CacheKey.FromStrings("/nonexistent", "params");

        mock.Setup(c => c.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("Not Found")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchKey"
            });

        var provider = CreateProvider(mock);
        var result = await provider.FetchAsync(key);
        Assert.Null(result);
    }

    [Fact]
    public async Task StoreAsync_PutsObjectWithCorrectMetadata()
    {
        var mock = new Mock<IAmazonS3>();
        var key = CacheKey.FromStrings("/images/test.jpg", "w=400");
        var data = Encoding.UTF8.GetBytes("image bytes");

        mock.Setup(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });

        var provider = CreateProvider(mock);
        await provider.StoreAsync(key, data, new CacheEntryMetadata { ContentType = "image/jpeg" });

        mock.Verify(c => c.PutObjectAsync(
            It.Is<PutObjectRequest>(r =>
                r.BucketName == "test-bucket" &&
                r.Key == key.ToStoragePath() &&
                r.ContentType == "image/jpeg"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateAsync_DeletesObject()
    {
        var mock = new Mock<IAmazonS3>();
        var key = CacheKey.FromStrings("/images/delete-me.jpg", "w=100");

        mock.Setup(c => c.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.NoContent });

        var provider = CreateProvider(mock);
        var removed = await provider.InvalidateAsync(key);
        Assert.True(removed);

        mock.Verify(c => c.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(r => r.BucketName == "test-bucket" && r.Key == key.ToStoragePath()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PurgeBySourceAsync_ListsAndBatchDeletes()
    {
        var mock = new Mock<IAmazonS3>();
        var key1 = CacheKey.FromStrings("/images/photo.jpg", "w=100");
        var key2 = CacheKey.FromStrings("/images/photo.jpg", "w=200");
        var sourceHash = key1.SourceHash;

        // Setup list to return 2 objects
        mock.Setup(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = new System.Collections.Generic.List<S3Object>
                {
                    new S3Object { Key = key1.ToStoragePath() },
                    new S3Object { Key = key2.ToStoragePath() }
                },
                NextContinuationToken = null
            });

        mock.Setup(c => c.DeleteObjectsAsync(It.IsAny<DeleteObjectsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectsResponse());

        var provider = CreateProvider(mock);
        var count = await provider.PurgeBySourceAsync(sourceHash);

        Assert.Equal(2, count);
        mock.Verify(c => c.DeleteObjectsAsync(
            It.Is<DeleteObjectsRequest>(r => r.Objects.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void WantsToStore_AcceptsFreshAndMissed_RejectsNotQueried()
    {
        var provider = CreateProvider();
        var key = CacheKey.FromStrings("/test", "params");

        Assert.True(provider.WantsToStore(key, 1000, CacheStoreReason.FreshlyCreated));
        Assert.True(provider.WantsToStore(key, 1000, CacheStoreReason.Missed));
        Assert.False(provider.WantsToStore(key, 1000, CacheStoreReason.NotQueried));
    }

    [Fact]
    public async Task HealthCheckAsync_ReturnsTrueOnSuccess()
    {
        var mock = new Mock<IAmazonS3>();
        mock.Setup(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response());

        var provider = CreateProvider(mock);
        Assert.True(await provider.HealthCheckAsync());
    }

    [Fact]
    public async Task HealthCheckAsync_ReturnsFalseOnError()
    {
        var mock = new Mock<IAmazonS3>();
        mock.Setup(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("Access Denied") { StatusCode = HttpStatusCode.Forbidden });

        var provider = CreateProvider(mock);
        Assert.False(await provider.HealthCheckAsync());
    }
}
