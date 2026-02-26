using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Imazen.Caching;
using Moq;

namespace Imageflow.Server.Storage.AzureBlob.Tests;

public class AzureBlobCacheProviderTests
{
    private static (AzureBlobCacheProvider provider, Mock<BlobContainerClient> containerMock) CreateProvider(
        string? prefix = null)
    {
        var containerMock = new Mock<BlobContainerClient>();
        containerMock.Setup(c => c.AccountName).Returns("testaccount");
        containerMock.Setup(c => c.Name).Returns("test-container");
        var provider = new AzureBlobCacheProvider("azure-test", containerMock.Object, prefix);
        return (provider, containerMock);
    }

    [Fact]
    public void Constructor_SetsNameAndCapabilities()
    {
        var (provider, _) = CreateProvider();
        Assert.Equal("azure-test", provider.Name);
        Assert.False(provider.Capabilities.RequiresInlineExecution);
        Assert.Equal("azure:testaccount:test-container", provider.Capabilities.LatencyZone);
        Assert.False(provider.Capabilities.IsLocal);
    }

    [Fact]
    public async Task FetchAsync_Miss_ReturnsNull()
    {
        var (provider, containerMock) = CreateProvider();
        var key = CacheKey.FromStrings("/nonexistent", "params");

        var blobMock = new Mock<BlobClient>();
        blobMock.Setup(b => b.DownloadStreamingAsync(
                It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));

        containerMock.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(blobMock.Object);

        var result = await provider.FetchAsync(key);
        Assert.Null(result);
    }

    [Fact]
    public async Task StoreAsync_UploadsBlobWithTagsAndContentType()
    {
        var (provider, containerMock) = CreateProvider();
        var key = CacheKey.FromStrings("/images/test.jpg", "w=400");
        var data = Encoding.UTF8.GetBytes("image bytes");

        var blobMock = new Mock<BlobClient>();
        blobMock.Setup(b => b.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        containerMock.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(blobMock.Object);

        await provider.StoreAsync(key, data, new CacheEntryMetadata { ContentType = "image/jpeg" });

        blobMock.Verify(b => b.UploadAsync(
            It.IsAny<Stream>(),
            It.Is<BlobUploadOptions>(o =>
                o.HttpHeaders.ContentType == "image/jpeg" &&
                o.Tags != null &&
                o.Tags.ContainsKey("source_prefix")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateAsync_DeletesBlob_ReturnsTrueIfExisted()
    {
        var (provider, containerMock) = CreateProvider();
        var key = CacheKey.FromStrings("/images/delete-me.jpg", "w=100");

        var blobMock = new Mock<BlobClient>();
        // Response<bool>.Value = true means blob existed and was deleted
        var responseMock = new Mock<Response<bool>>();
        responseMock.Setup(r => r.Value).Returns(true);
        blobMock.Setup(b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        containerMock.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(blobMock.Object);

        var removed = await provider.InvalidateAsync(key);
        Assert.True(removed);
    }

    [Fact]
    public async Task InvalidateAsync_ReturnsFalseIfNotFound()
    {
        var (provider, containerMock) = CreateProvider();
        var key = CacheKey.FromStrings("/images/missing.jpg", "w=100");

        var blobMock = new Mock<BlobClient>();
        var responseMock = new Mock<Response<bool>>();
        responseMock.Setup(r => r.Value).Returns(false);
        blobMock.Setup(b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        containerMock.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(blobMock.Object);

        var removed = await provider.InvalidateAsync(key);
        Assert.False(removed);
    }

    [Fact]
    public void WantsToStore_AcceptsFreshAndMissed_RejectsNotQueried()
    {
        var (provider, _) = CreateProvider();
        var key = CacheKey.FromStrings("/test", "params");

        Assert.True(provider.WantsToStore(key, 1000, CacheStoreReason.FreshlyCreated));
        Assert.True(provider.WantsToStore(key, 1000, CacheStoreReason.Missed));
        Assert.False(provider.WantsToStore(key, 1000, CacheStoreReason.NotQueried));
    }

    [Fact]
    public async Task HealthCheckAsync_ReturnsTrueWhenContainerExists()
    {
        var (provider, containerMock) = CreateProvider();
        var responseMock = new Mock<Response<bool>>();
        responseMock.Setup(r => r.Value).Returns(true);
        containerMock.Setup(c => c.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        Assert.True(await provider.HealthCheckAsync());
    }

    [Fact]
    public async Task HealthCheckAsync_ReturnsFalseWhenContainerMissing()
    {
        var (provider, containerMock) = CreateProvider();
        var responseMock = new Mock<Response<bool>>();
        responseMock.Setup(r => r.Value).Returns(false);
        containerMock.Setup(c => c.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        Assert.False(await provider.HealthCheckAsync());
    }

    [Fact]
    public async Task HealthCheckAsync_ReturnsFalseOnException()
    {
        var (provider, containerMock) = CreateProvider();
        containerMock.Setup(c => c.ExistsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "Forbidden"));

        Assert.False(await provider.HealthCheckAsync());
    }

    [Fact]
    public void StoragePath_WithPrefix_PrependsPrefixToKey()
    {
        var (provider, containerMock) = CreateProvider("cache/");
        var key = CacheKey.FromStrings("/images/test.jpg", "w=100");

        var blobMock = new Mock<BlobClient>();
        blobMock.Setup(b => b.DownloadStreamingAsync(
                It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));

        containerMock.Setup(c => c.GetBlobClient(It.Is<string>(s => s.StartsWith("cache/"))))
            .Returns(blobMock.Object);

        // Will be null (miss) but verifies the key includes prefix
        _ = provider.FetchAsync(key);

        containerMock.Verify(c => c.GetBlobClient(It.Is<string>(s => s.StartsWith("cache/"))), Times.Once);
    }
}
