using System.Collections.Generic;
using System.IO;
using System.Text;
using Imageflow.Fluent;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Imageflow.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SecurityPolicyConfigurationBinder"/>.
    /// Verifies JSON binding produces equivalent <see cref="SecurityPolicyOptions"/>
    /// and that malformed input surfaces as
    /// <see cref="SecurityPolicyValidationException"/>.
    /// </summary>
    public class SecurityPolicyConfigurationBinderTests
    {
        private static IConfiguration BuildConfig(string json)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return new ConfigurationBuilder().AddJsonStream(stream).Build();
        }

        [Fact]
        public void MissingSection_ReturnsNull()
        {
            var config = BuildConfig("{\"Something\": true}");
            var section = config.GetSection(SecurityPolicyConfigurationBinder.DefaultSectionPath);
            var bound = SecurityPolicyConfigurationBinder.Bind(section);
            Assert.Null(bound);
        }

        [Fact]
        public void FormatsOptInEncode_Binds()
        {
            const string json = "{"
                + "\"Imageflow\":{\"Security\":{"
                + "  \"Formats\":{ \"OptInEncode\": [ \"avif\" ] }"
                + "}}}";
            var config = BuildConfig(json);
            var section = config.GetSection(SecurityPolicyConfigurationBinder.DefaultSectionPath);
            var bound = SecurityPolicyConfigurationBinder.Bind(section);
            Assert.NotNull(bound);
            Assert.NotNull(bound!.Formats);
            Assert.NotNull(bound.Formats!.OptInEncode);
            Assert.Single(bound.Formats.OptInEncode!);
            Assert.Equal(ImageFormat.Avif, bound.Formats.OptInEncode![0]);
        }

        [Fact]
        public void DenyLists_Bind()
        {
            const string json = "{"
                + "\"Imageflow\":{\"Security\":{"
                + "  \"Formats\":{"
                + "    \"DenyEncode\": [ \"webp\" ],"
                + "    \"DenyDecode\": [ \"heic\" ]"
                + "  }"
                + "}}}";
            var config = BuildConfig(json);
            var section = config.GetSection(SecurityPolicyConfigurationBinder.DefaultSectionPath);
            var bound = SecurityPolicyConfigurationBinder.Bind(section);
            Assert.NotNull(bound);
            Assert.Contains(ImageFormat.Webp, bound!.Formats!.DenyEncode!);
            Assert.Contains(ImageFormat.Heic, bound.Formats.DenyDecode!);
        }

        [Fact]
        public void UnknownFormat_RaisesValidationException()
        {
            const string json = "{"
                + "\"Imageflow\":{\"Security\":{"
                + "  \"Formats\":{ \"DenyEncode\": [ \"nonexistent_fmt\" ] }"
                + "}}}";
            var config = BuildConfig(json);
            var section = config.GetSection(SecurityPolicyConfigurationBinder.DefaultSectionPath);
            var ex = Assert.Throws<SecurityPolicyValidationException>(
                () => SecurityPolicyConfigurationBinder.Bind(section));
            Assert.Contains("nonexistent_fmt", ex.Message);
        }

        [Fact]
        public void CodecDenyEncoders_Bind()
        {
            const string json = "{"
                + "\"Imageflow\":{\"Security\":{"
                + "  \"Codecs\":{ \"DenyEncoders\": [ \"mozjpeg_encoder\" ] }"
                + "}}}";
            var config = BuildConfig(json);
            var section = config.GetSection(SecurityPolicyConfigurationBinder.DefaultSectionPath);
            var bound = SecurityPolicyConfigurationBinder.Bind(section);
            Assert.NotNull(bound);
            Assert.Contains(NamedEncoderName.MozjpegEncoder, bound!.Codecs!.DenyEncoders!);
        }

        [Fact]
        public void UnknownCodec_RaisesValidationException()
        {
            const string json = "{"
                + "\"Imageflow\":{\"Security\":{"
                + "  \"Codecs\":{ \"DenyEncoders\": [ \"bogus_codec\" ] }"
                + "}}}";
            var config = BuildConfig(json);
            var section = config.GetSection(SecurityPolicyConfigurationBinder.DefaultSectionPath);
            Assert.Throws<SecurityPolicyValidationException>(
                () => SecurityPolicyConfigurationBinder.Bind(section));
        }

        [Fact]
        public void ScalarLimits_Bind()
        {
            const string json = "{"
                + "\"Imageflow\":{\"Security\":{"
                + "  \"MaxDecodeSize\":{ \"W\": 8000, \"H\": 8000, \"Megapixels\": 50 },"
                + "  \"MaxInputFileBytes\": 268435456,"
                + "  \"MaxJsonBytes\": 67108864"
                + "}}}";
            var config = BuildConfig(json);
            var section = config.GetSection(SecurityPolicyConfigurationBinder.DefaultSectionPath);
            var bound = SecurityPolicyConfigurationBinder.Bind(section);
            Assert.NotNull(bound);
            Assert.NotNull(bound!.MaxDecodeSize);
            Assert.Equal(8000u, bound.MaxDecodeSize!.Value.MaxWidth);
            Assert.Equal(8000u, bound.MaxDecodeSize.Value.MaxHeight);
            Assert.Equal(50f, bound.MaxDecodeSize.Value.MaxMegapixels);
            Assert.Equal(268435456UL, bound.MaxInputFileBytes);
            Assert.Equal(67108864UL, bound.MaxJsonBytes);
        }

        [Fact]
        public void AllowAndDenyTogether_RaisesValidationException()
        {
            const string json = "{"
                + "\"Imageflow\":{\"Security\":{"
                + "  \"Formats\":{"
                + "    \"AllowEncode\": [ \"jpeg\" ],"
                + "    \"DenyEncode\":  [ \"webp\" ]"
                + "  }"
                + "}}}";
            var config = BuildConfig(json);
            var section = config.GetSection(SecurityPolicyConfigurationBinder.DefaultSectionPath);
            Assert.Throws<SecurityPolicyValidationException>(
                () => SecurityPolicyConfigurationBinder.Bind(section));
        }

        [Fact]
        public void TableForm_Binds()
        {
            const string json = "{"
                + "\"Imageflow\":{\"Security\":{"
                + "  \"Formats\":{"
                + "    \"Formats\":{"
                + "      \"avif\":{ \"Decode\": true, \"Encode\": true },"
                + "      \"jxl\": { \"Decode\": true, \"Encode\": false }"
                + "    }"
                + "  }"
                + "}}}";
            var config = BuildConfig(json);
            var section = config.GetSection(SecurityPolicyConfigurationBinder.DefaultSectionPath);
            var bound = SecurityPolicyConfigurationBinder.Bind(section);
            Assert.NotNull(bound);
            Assert.NotNull(bound!.Formats!.Formats);
            Assert.True(bound.Formats.Formats!.ContainsKey(ImageFormat.Avif));
            Assert.True(bound.Formats.Formats[ImageFormat.Avif].Encode);
            Assert.False(bound.Formats.Formats[ImageFormat.Jxl].Encode);
        }
    }
}
