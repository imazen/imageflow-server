namespace Imazen.Shared.Tests.Common;

using System;
using System.IO;
using Xunit;
using Imazen.Common.FileTypeDetection;

public class FileTypeDetectionTests
{
    [Theory]
    [InlineData("jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData("jpeg_exif", new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 }, "image/jpeg")]
    [InlineData("gif87a", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 }, "image/gif")]
    [InlineData("gif89a", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
    [InlineData("png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData("webp", new byte[] {
        0x52, 0x49, 0x46, 0x46, // RIFF
        0x00, 0x00, 0x00, 0x00, // File size placeholder
        0x57, 0x45, 0x42, 0x50  // WEBP
    }, "image/webp")]
    [InlineData("tiff_little", new byte[] { 0x49, 0x49, 0x2A, 0x00 }, "image/tiff")]
    [InlineData("tiff_big", new byte[] { 0x4D, 0x4D, 0x00, 0x2A }, "image/tiff")]
    [InlineData("tiff_old", new byte[] { 0x49, 0x20, 0x49 }, "image/tiff")]
    [InlineData("bmp", new byte[] { 0x42, 0x4D }, "image/bmp")]
    [InlineData("ico", new byte[] { 0x00, 0x00, 0x01, 0x00 }, "image/x-icon")]
    [InlineData("avif", new byte[] {
        0x00, 0x00, 0x00, 0x20, // Box size
        0x66, 0x74, 0x79, 0x70, // ftyp
        0x61, 0x76, 0x69, 0x66  // avif
    }, "image/avif")]
    [InlineData("avif_sequence", new byte[] {
        0x00, 0x00, 0x00, 0x20, // Box size
        0x66, 0x74, 0x79, 0x70, // ftyp
        0x61, 0x76, 0x69, 0x73  // avis
    }, "image/avif-sequence")]
    [InlineData("heif", new byte[] {
        0x00, 0x00, 0x00, 0x20, // Box size
        0x66, 0x74, 0x79, 0x70, // ftyp
        0x6D, 0x69, 0x66, 0x31  // mif1
    }, "image/heif")]
    [InlineData("heif_sequence", new byte[] {
        0x00, 0x00, 0x00, 0x20, // Box size
        0x66, 0x74, 0x79, 0x70, // ftyp
        0x6D, 0x73, 0x66, 0x31  // msf1
    }, "image/heif-sequence")]
    [InlineData("heic", new byte[] {
        0x00, 0x00, 0x00, 0x20, // Box size
        0x66, 0x74, 0x79, 0x70, // ftyp
        0x68, 0x65, 0x69, 0x63  // heic
    }, "image/heic")]
    [InlineData("heic_sequence", new byte[] {
        0x00, 0x00, 0x00, 0x20, // Box size
        0x66, 0x74, 0x79, 0x70, // ftyp
        0x68, 0x65, 0x76, 0x63  // hevc
    }, "image/heic-sequence")]
    [InlineData("jpegxl_jpeg", new byte[] {
        0x00, 0x00, 0x00, 0x0C, // Box size
        0x4A, 0x58, 0x4C, 0x20, // JXL
        0x0D, 0x0A, 0x87, 0x0A  // Signature
    }, "image/jxl")]
    [InlineData("jpegxl_jp2", new byte[] {
        0x00, 0x00, 0x00, 0x0C, // Box size
        0x6A, 0x50, 0x20, 0x20, // jP
        0x0D, 0x0A, 0x87, 0x0A  // Signature
    }, "image/jp2")]
    [InlineData("jpegxl_ff0a", new byte[] { 0xFF, 0x0A }, "image/jxl")]
    [InlineData("jpeg2000", new byte[] {
        0x00, 0x00, 0x00, 0x0C, // Box size
        0x6A, 0x50, 0x20, 0x20, // jP
        0x0D, 0x0A, 0x87, 0x0A  // Signature
    }, "image/jp2")]
    [InlineData("flif", new byte[] { 0x46, 0x4C, 0x49, 0x46 }, "image/flif")]
    [InlineData("woff", new byte[] { 0x77, 0x4F, 0x46, 0x46 }, "font/woff")]
    [InlineData("woff2", new byte[] { 0x77, 0x4F, 0x46, 0x32 }, "font/woff2")]
    [InlineData("otf", new byte[] { 0x4F, 0x54, 0x54, 0x4F }, "font/otf")]
    [InlineData("ttf", new byte[] { 0x00, 0x01, 0x00, 0x00 }, "font/ttf")]
    [InlineData("pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, "application/pdf")]
    [InlineData("postscript", new byte[] { 0x25, 0x21, 0x50, 0x53, 0x2D, 0x41, 0x64, 0x6F, 0x62, 0x65, 0x2D }, "application/postscript")]
    [InlineData("mp3_id3", new byte[] { 0x49, 0x44, 0x33 }, "audio/mpeg")]
    [InlineData("mp3_fffb", new byte[] { 0xFF, 0xFB }, "audio/mpeg")]
    [InlineData("mp3_fff3", new byte[] { 0xFF, 0xF3 }, "audio/mpeg")]
    [InlineData("mp3_fff2", new byte[] { 0xFF, 0xF2 }, "audio/mpeg")]
    [InlineData("aac", new byte[] { 0xFF, 0xF1 }, "audio/aac")]
    [InlineData("aac_f9", new byte[] { 0xFF, 0xF9 }, "audio/aac")]
    [InlineData("aiff", new byte[] { 0x46, 0x4F, 0x52, 0x4D }, "audio/aiff")]
    [InlineData("flac", new byte[] { 0x66, 0x4C, 0x61, 0x43 }, "audio/flac")]
    [InlineData("wav", new byte[] {
        0x52, 0x49, 0x46, 0x46, // RIFF
        0x00, 0x00, 0x00, 0x00, // File size placeholder
        0x57, 0x41, 0x56, 0x45  // WAVE
    }, "audio/wav")]
    [InlineData("ogg", new byte[] { 0x4F, 0x67, 0x67, 0x53, 0x00 }, "audio/ogg")]
    [InlineData("mpeg1", new byte[] { 0x00, 0x00, 0x01, 0xBA, 0x21 }, "video/mpeg")]
    [InlineData("mpeg2", new byte[] { 0x00, 0x00, 0x01, 0xBA, 0x44 }, "video/mpeg")]
    [InlineData("matroska", new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }, "video/webm")]
    [InlineData("avi", new byte[] {
        0x52, 0x49, 0x46, 0x46, // RIFF
        0x00, 0x00, 0x00, 0x00, // File size placeholder
        0x41, 0x56, 0x49, 0x20  // AVI
    }, "video/x-msvideo")]
    [InlineData("quicktime", new byte[] {
        0x00, 0x00, 0x00, 0x20, // Box size
        0x66, 0x74, 0x79, 0x70, // ftyp
        0x71, 0x74, 0x20, 0x20  // qt
    }, "video/quicktime")]
    [InlineData("3gpp_ftyp3g", new byte[] {
        0x00, 0x00, 0x00, 0x20, // Box size
        0x66, 0x74, 0x79, 0x70, // ftyp
        0x33, 0x67, 0x20, 0x20  // 3g
    }, "video/3gpp")]
    [InlineData("3gpp_ftyp3gp", new byte[] {
        0x00, 0x00, 0x00, 0x20, // Box size
        0x66, 0x74, 0x79, 0x70, // ftyp
        0x33, 0x67, 0x70, 0x20  // 3gp
    }, "video/3gpp")]
    public void GetImageContentType_ReturnsCorrectMimeType(string formatName, byte[] signature, string expectedMimeType)
    {
        // Arrange - create a 12-byte array with the signature
        var data = new byte[12];
        Array.Copy(signature, 0, data, 0, Math.Min(signature.Length, 12));

        // Act
        var result = Imazen.Common.FileTypeDetection.FileTypeDetector.GuessMimeType(data);

        // Assert
        Assert.Equal(expectedMimeType, result);
    }

    [Fact]
    public void GetImageContentType_UnknownSignature_ReturnsNull()
    {
        // Arrange
        var unknownData = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B };

        // Act
        var result = Imazen.Common.FileTypeDetection.FileTypeDetector.GuessMimeType(unknownData);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetImageContentType_TooShort_ThrowsArgumentException()
    {
        // Arrange
        var shortData = new byte[] { 0x01, 0x02, 0x03 };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => Imazen.Common.FileTypeDetection.FileTypeDetector.GuessMimeType(shortData));
    }

    [Theory]
    [InlineData("m4v", new byte[] {
        0x00, 0x00, 0x00, 0x20, // Box size
        0x66, 0x74, 0x79, 0x70, // ftyp
        0x4D, 0x34, 0x56, 0x20  // M4V
    }, "video/mp4")]
    [InlineData("m4a", new byte[] {
        0x00, 0x00, 0x00, 0x20, // Box size
        0x66, 0x74, 0x79, 0x70, // ftyp
        0x4D, 0x34, 0x41, 0x20  // M4A
    }, "audio/mp4")]
    [InlineData("m4p", new byte[] {
        0x00, 0x00, 0x00, 0x20, // Box size
        0x66, 0x74, 0x79, 0x70, // ftyp
        0x4D, 0x34, 0x50, 0x20  // M4P
    }, "audio/mp4")]
    [InlineData("m4b", new byte[] {
        0x00, 0x00, 0x00, 0x20, // Box size
        0x66, 0x74, 0x79, 0x70, // ftyp
        0x4D, 0x34, 0x42, 0x20  // M4B
    }, "audio/mp4")]
    public void GetImageContentType_Mp4Formats(string formatName, byte[] signature, string expectedMimeType)
    {
        // Arrange - create a 12-byte array with the signature
        var data = new byte[12];
        Array.Copy(signature, 0, data, 0, Math.Min(signature.Length, 12));

        // Act
        var result = Imazen.Common.FileTypeDetection.FileTypeDetector.GuessMimeType(data);

        // Assert
        Assert.Equal(expectedMimeType, result);
    }
}
