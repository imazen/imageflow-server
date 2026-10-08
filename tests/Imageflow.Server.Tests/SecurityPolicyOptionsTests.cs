using System.Collections.Generic;
using System.Linq;
using Imageflow.Fluent;
using Xunit;

namespace Imageflow.Server.Tests
{
    /// <summary>
    /// Unit tests for the three-layer killbits surface on
    /// <see cref="SecurityPolicyOptions"/>. Verifies:
    /// - mutual-exclusion validation
    /// - AVIF/JXL opt-in translation
    /// - allow-list / deny-list / table form merges
    /// - explicit operator control disables server default per format
    /// </summary>
    public class SecurityPolicyOptionsTests
    {
        [Fact]
        public void Default_DeniesAvifAndJxlEncode()
        {
            var policy = new SecurityPolicyOptions();
            var effective = policy.BuildEffectiveSecurityOptions();
            Assert.NotNull(effective.Formats);
            Assert.NotNull(effective.Formats!.DenyEncode);
            Assert.Contains(ImageFormat.Avif, effective.Formats.DenyEncode!);
            Assert.Contains(ImageFormat.Jxl, effective.Formats.DenyEncode!);
            Assert.Null(effective.Formats.AllowEncode);
            Assert.Null(effective.Formats.AllowDecode);
            Assert.Null(effective.Formats.Formats);
        }

        [Fact]
        public void OptInAvif_StillDeniesJxl()
        {
            var policy = new SecurityPolicyOptions
            {
                Formats = new FormatOptions
                {
                    OptInEncode = new List<ImageFormat> { ImageFormat.Avif },
                },
            };
            var effective = policy.BuildEffectiveSecurityOptions();
            Assert.NotNull(effective.Formats!.DenyEncode);
            Assert.DoesNotContain(ImageFormat.Avif, effective.Formats.DenyEncode!);
            Assert.Contains(ImageFormat.Jxl, effective.Formats.DenyEncode!);
        }

        [Fact]
        public void OptInBoth_DeniesNeither()
        {
            var policy = new SecurityPolicyOptions
            {
                Formats = new FormatOptions
                {
                    OptInEncode = new List<ImageFormat> { ImageFormat.Avif, ImageFormat.Jxl },
                },
            };
            var effective = policy.BuildEffectiveSecurityOptions();
            // Nothing to deny beyond the opt-ins; no killbits required.
            Assert.True(effective.Formats == null
                || effective.Formats.DenyEncode == null
                || (!effective.Formats.DenyEncode!.Contains(ImageFormat.Avif)
                    && !effective.Formats.DenyEncode!.Contains(ImageFormat.Jxl)));
        }

        [Fact]
        public void AllowEncodeList_DisablesServerDefaultForAvifAndJxl()
        {
            var policy = new SecurityPolicyOptions
            {
                Formats = new FormatOptions
                {
                    AllowEncode = new List<ImageFormat> { ImageFormat.Jpeg, ImageFormat.Png },
                },
            };
            var effective = policy.BuildEffectiveSecurityOptions();
            Assert.NotNull(effective.Formats);
            Assert.NotNull(effective.Formats!.AllowEncode);
            Assert.Equal(2, effective.Formats.AllowEncode!.Count);
            Assert.Contains(ImageFormat.Jpeg, effective.Formats.AllowEncode);
            Assert.Contains(ImageFormat.Png, effective.Formats.AllowEncode);
            // Allow-list and deny-list are mutually exclusive — no server-default deny list.
            Assert.Null(effective.Formats.DenyEncode);
        }

        [Fact]
        public void TableFormMentionsAvif_DisablesServerDefaultForAvif()
        {
            // Explicit operator decision: AVIF encode allowed via table.
            var policy = new SecurityPolicyOptions
            {
                Formats = new FormatOptions
                {
                    Formats = new Dictionary<ImageFormat, FormatPermissions>
                    {
                        { ImageFormat.Avif, new FormatPermissions(decode: true, encode: true) },
                    },
                },
            };
            var effective = policy.BuildEffectiveSecurityOptions();
            Assert.NotNull(effective.Formats);
            Assert.NotNull(effective.Formats!.Formats);
            Assert.True(effective.Formats.Formats![ImageFormat.Avif].Encode);
            // JXL wasn't mentioned → server default still applies, but since
            // we're in table form, the default is injected as a table entry.
            Assert.True(effective.Formats.Formats.ContainsKey(ImageFormat.Jxl));
            Assert.False(effective.Formats.Formats[ImageFormat.Jxl].Encode);
            Assert.Null(effective.Formats.AllowEncode);
            Assert.Null(effective.Formats.DenyEncode);
        }

        [Fact]
        public void DenyEncodeList_MergesWithServerDefault()
        {
            var policy = new SecurityPolicyOptions
            {
                Formats = new FormatOptions
                {
                    DenyEncode = new List<ImageFormat> { ImageFormat.Webp },
                },
            };
            var effective = policy.BuildEffectiveSecurityOptions();
            Assert.NotNull(effective.Formats!.DenyEncode);
            Assert.Contains(ImageFormat.Webp, effective.Formats.DenyEncode!);
            Assert.Contains(ImageFormat.Avif, effective.Formats.DenyEncode!);
            Assert.Contains(ImageFormat.Jxl, effective.Formats.DenyEncode!);
        }

        [Fact]
        public void DenyEncodeList_DuplicatesAreFoldedOut()
        {
            var policy = new SecurityPolicyOptions
            {
                Formats = new FormatOptions
                {
                    DenyEncode = new List<ImageFormat> { ImageFormat.Avif, ImageFormat.Webp },
                },
            };
            var effective = policy.BuildEffectiveSecurityOptions();
            var avifCount = effective.Formats!.DenyEncode!.Count(f => f == ImageFormat.Avif);
            Assert.Equal(1, avifCount);
        }

        [Fact]
        public void AllowAndDenyForDecode_RaisesValidationException()
        {
            var policy = new SecurityPolicyOptions
            {
                Formats = new FormatOptions
                {
                    AllowDecode = new List<ImageFormat> { ImageFormat.Jpeg },
                    DenyDecode = new List<ImageFormat> { ImageFormat.Png },
                },
            };
            var ex = Assert.Throws<SecurityPolicyValidationException>(() => policy.Validate());
            Assert.Contains("decode", ex.Message);
        }

        [Fact]
        public void ListAndTable_RaisesValidationException()
        {
            var policy = new SecurityPolicyOptions
            {
                Formats = new FormatOptions
                {
                    AllowDecode = new List<ImageFormat> { ImageFormat.Jpeg },
                    Formats = new Dictionary<ImageFormat, FormatPermissions>
                    {
                        { ImageFormat.Webp, new FormatPermissions(true, true) },
                    },
                },
            };
            var ex = Assert.Throws<SecurityPolicyValidationException>(() => policy.Validate());
            Assert.Contains("single form", ex.Message);
        }

        [Fact]
        public void CodecAllowAndDeny_RaisesValidationException()
        {
            var policy = new SecurityPolicyOptions
            {
                Codecs = new CodecOptions
                {
                    AllowEncoders = new List<NamedEncoderName> { NamedEncoderName.ZenJpegEncoder },
                    DenyEncoders = new List<NamedEncoderName> { NamedEncoderName.MozjpegEncoder },
                },
            };
            Assert.Throws<SecurityPolicyValidationException>(() => policy.Validate());
        }

        [Fact]
        public void CustomRiskyEncodeList_TakesEffect()
        {
            var policy = new SecurityPolicyOptions
            {
                ServerRiskyEncode = new[] { ImageFormat.Webp },
            };
            var effective = policy.BuildEffectiveSecurityOptions();
            Assert.NotNull(effective.Formats!.DenyEncode);
            Assert.Contains(ImageFormat.Webp, effective.Formats.DenyEncode!);
            Assert.DoesNotContain(ImageFormat.Avif, effective.Formats.DenyEncode!);
        }

        [Fact]
        public void Scalars_PassThroughToEffective()
        {
            var policy = new SecurityPolicyOptions
            {
                MaxDecodeSize = new FrameSizeLimit(8000, 8000, 50f),
                MaxInputFileBytes = 256 * 1024 * 1024,
                MaxJsonBytes = 64 * 1024 * 1024,
            };
            var effective = policy.BuildEffectiveSecurityOptions();
            Assert.NotNull(effective.MaxDecodeSize);
            Assert.Equal(8000u, effective.MaxDecodeSize!.Value.MaxWidth);
            Assert.Equal(256UL * 1024 * 1024, effective.MaxInputFileBytes);
            Assert.Equal(64UL * 1024 * 1024, effective.MaxJsonBytes);
        }

        [Fact]
        public void CodecDenyEncoders_SurfacesInEffective()
        {
            var policy = new SecurityPolicyOptions
            {
                Codecs = new CodecOptions
                {
                    DenyEncoders = new List<NamedEncoderName> { NamedEncoderName.MozjpegEncoder },
                },
            };
            var effective = policy.BuildEffectiveSecurityOptions();
            Assert.NotNull(effective.Codecs);
            Assert.Contains(NamedEncoderName.MozjpegEncoder, effective.Codecs!.DenyEncoders!);
        }
    }
}
