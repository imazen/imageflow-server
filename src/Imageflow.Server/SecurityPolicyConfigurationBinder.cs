#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Imageflow.Fluent;
using Microsoft.Extensions.Configuration;

namespace Imageflow.Server
{
    /// <summary>
    /// Binds <see cref="SecurityPolicyOptions"/> from an
    /// <see cref="IConfiguration"/> section (typically
    /// <c>Imageflow:Security</c>).
    /// </summary>
    /// <remarks>
    /// This is deliberately hand-rolled instead of using
    /// <c>Configuration.Bind</c>. The built-in binder can't easily express
    /// dictionaries keyed by an enum plus a strongly-typed struct value
    /// (<see cref="FormatPermissions"/>), and silent binder failures would
    /// defeat the "fail loudly" goal of the startup validator.
    ///
    /// Recognized JSON shape:
    /// <code>
    /// {
    ///   "Imageflow": {
    ///     "Security": {
    ///       "MaxDecodeSize": { "W": 8000, "H": 8000, "Megapixels": 50 },
    ///       "MaxInputFileBytes": 268435456,
    ///       "MaxJsonBytes": 67108864,
    ///       "Formats": {
    ///         "OptInEncode": [ "avif", "jxl" ],
    ///         "DenyEncode":  [ "webp" ],
    ///         "AllowDecode": null,
    ///         "DenyDecode":  null
    ///       },
    ///       "Codecs": {
    ///         "DenyEncoders": [ "mozjpeg_encoder" ]
    ///       }
    ///     }
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public static class SecurityPolicyConfigurationBinder
    {
        /// <summary>Default section path: <c>Imageflow:Security</c>.</summary>
        public const string DefaultSectionPath = "Imageflow:Security";

        /// <summary>Bind <see cref="SecurityPolicyOptions"/> from <paramref name="section"/>.</summary>
        /// <returns><c>null</c> when the section doesn't exist (meaning: no security policy configured).</returns>
        /// <exception cref="SecurityPolicyValidationException">when the config is malformed or inconsistent.</exception>
        public static SecurityPolicyOptions? Bind(IConfiguration section)
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            var opts = new SecurityPolicyOptions();
            var anyValue = false;

            if (TryReadSize(section.GetSection("MaxDecodeSize"), out var maxDecode))
            {
                opts.MaxDecodeSize = maxDecode; anyValue = true;
            }
            if (TryReadSize(section.GetSection("MaxFrameSize"), out var maxFrame))
            {
                opts.MaxFrameSize = maxFrame; anyValue = true;
            }
            if (TryReadSize(section.GetSection("MaxEncodeSize"), out var maxEncode))
            {
                opts.MaxEncodeSize = maxEncode; anyValue = true;
            }
            if (TryReadUlong(section["MaxInputFileBytes"], nameof(SecurityPolicyOptions.MaxInputFileBytes), out var mibb))
            {
                opts.MaxInputFileBytes = mibb; anyValue = true;
            }
            if (TryReadUlong(section["MaxJsonBytes"], nameof(SecurityPolicyOptions.MaxJsonBytes), out var mjb))
            {
                opts.MaxJsonBytes = mjb; anyValue = true;
            }
            if (TryReadUlong(section["MaxTotalFilePixels"], nameof(SecurityPolicyOptions.MaxTotalFilePixels), out var mtfp))
            {
                opts.MaxTotalFilePixels = mtfp; anyValue = true;
            }

            var formatsSection = section.GetSection("Formats");
            if (formatsSection.Exists())
            {
                opts.Formats = BindFormatOptions(formatsSection);
                anyValue = true;
            }

            var codecsSection = section.GetSection("Codecs");
            if (codecsSection.Exists())
            {
                opts.Codecs = BindCodecOptions(codecsSection);
                anyValue = true;
            }

            // ServerRiskyEncode override — rare, but surfaced so specialized
            // deployments can change the defaults without replacing
            // SecurityPolicyOptions wholesale.
            var riskyValues = ReadFormatList(section.GetSection("ServerRiskyEncode"), "ServerRiskyEncode");
            if (riskyValues != null)
            {
                opts.ServerRiskyEncode = riskyValues;
                anyValue = true;
            }

            if (!anyValue)
            {
                return null;
            }

            try
            {
                opts.Validate();
            }
            catch (SecurityPolicyValidationException)
            {
                throw;
            }

            return opts;
        }

        private static FormatOptions BindFormatOptions(IConfiguration section)
        {
            var opts = new FormatOptions
            {
                OptInEncode = ReadFormatList(section.GetSection("OptInEncode"), "Formats:OptInEncode"),
                AllowDecode = ReadFormatList(section.GetSection("AllowDecode"), "Formats:AllowDecode"),
                DenyDecode = ReadFormatList(section.GetSection("DenyDecode"), "Formats:DenyDecode"),
                AllowEncode = ReadFormatList(section.GetSection("AllowEncode"), "Formats:AllowEncode"),
                DenyEncode = ReadFormatList(section.GetSection("DenyEncode"), "Formats:DenyEncode"),
            };

            var tableSection = section.GetSection("Formats");
            if (tableSection.Exists())
            {
                var table = new Dictionary<ImageFormat, FormatPermissions>();
                foreach (var entry in tableSection.GetChildren())
                {
                    if (!ImageFormatParser.TryParse(entry.Key, out var format))
                    {
                        throw new SecurityPolicyValidationException(
                            $"Imageflow:Security:Formats:Formats — unknown format '{entry.Key}'. "
                            + "Expected one of: jpeg, png, gif, webp, avif, jxl, heic, bmp, tiff, pnm.");
                    }
                    var decode = ReadBool(entry["Decode"], $"Formats:Formats:{entry.Key}:Decode");
                    var encode = ReadBool(entry["Encode"], $"Formats:Formats:{entry.Key}:Encode");
                    table[format] = new FormatPermissions(decode ?? true, encode ?? true);
                }
                if (table.Count > 0)
                {
                    opts.Formats = table;
                }
            }

            return opts;
        }

        private static CodecOptions BindCodecOptions(IConfiguration section)
        {
            return new CodecOptions
            {
                AllowEncoders = ReadEncoderList(section.GetSection("AllowEncoders"), "Codecs:AllowEncoders"),
                DenyEncoders = ReadEncoderList(section.GetSection("DenyEncoders"), "Codecs:DenyEncoders"),
                AllowDecoders = ReadDecoderList(section.GetSection("AllowDecoders"), "Codecs:AllowDecoders"),
                DenyDecoders = ReadDecoderList(section.GetSection("DenyDecoders"), "Codecs:DenyDecoders"),
            };
        }

        private static List<ImageFormat>? ReadFormatList(IConfigurationSection section, string pathForError)
        {
            if (!section.Exists())
            {
                return null;
            }
            var list = new List<ImageFormat>();
            foreach (var child in section.GetChildren())
            {
                var raw = child.Value;
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }
                if (!ImageFormatParser.TryParse(raw!.ToLowerInvariant(), out var format))
                {
                    throw new SecurityPolicyValidationException(
                        $"Imageflow:Security:{pathForError} — unknown format '{raw}'. "
                        + "Expected one of: jpeg, png, gif, webp, avif, jxl, heic, bmp, tiff, pnm.");
                }
                list.Add(format);
            }
            return list.Count == 0 ? null : list;
        }

        private static List<NamedEncoderName>? ReadEncoderList(IConfigurationSection section, string pathForError)
        {
            if (!section.Exists())
            {
                return null;
            }
            var list = new List<NamedEncoderName>();
            foreach (var child in section.GetChildren())
            {
                var raw = child.Value;
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }
                if (!NamedCodecParsers.TryParseEncoder(raw!.ToLowerInvariant(), out var enc))
                {
                    throw new SecurityPolicyValidationException(
                        $"Imageflow:Security:{pathForError} — unknown encoder '{raw}'. "
                        + "Use snake_case (e.g. 'mozjpeg_encoder').");
                }
                list.Add(enc);
            }
            return list.Count == 0 ? null : list;
        }

        private static List<NamedDecoderName>? ReadDecoderList(IConfigurationSection section, string pathForError)
        {
            if (!section.Exists())
            {
                return null;
            }
            var list = new List<NamedDecoderName>();
            foreach (var child in section.GetChildren())
            {
                var raw = child.Value;
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }
                if (!NamedCodecParsers.TryParseDecoder(raw!.ToLowerInvariant(), out var dec))
                {
                    throw new SecurityPolicyValidationException(
                        $"Imageflow:Security:{pathForError} — unknown decoder '{raw}'. "
                        + "Use snake_case (e.g. 'zen_png_decoder').");
                }
                list.Add(dec);
            }
            return list.Count == 0 ? null : list;
        }

        private static bool TryReadSize(IConfigurationSection section, out FrameSizeLimit? limit)
        {
            limit = null;
            if (!section.Exists())
            {
                return false;
            }
            var w = TryUint(section["W"]) ?? TryUint(section["Width"]) ?? 0u;
            var h = TryUint(section["H"]) ?? TryUint(section["Height"]) ?? 0u;
            var mpRaw = section["Megapixels"] ?? section["MP"];
            if (!float.TryParse(mpRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var mp))
            {
                mp = 0f;
            }
            limit = new FrameSizeLimit(w, h, mp);
            return true;
        }

        private static uint? TryUint(string? s) =>
            uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (uint?)null;

        private static bool TryReadUlong(string? raw, string fieldName, out ulong? value)
        {
            value = null;
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }
            if (!ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new SecurityPolicyValidationException(
                    $"Imageflow:Security:{fieldName} — expected a non-negative integer, got '{raw}'.");
            }
            value = parsed;
            return true;
        }

        private static bool? ReadBool(string? raw, string pathForError)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }
            if (!bool.TryParse(raw, out var b))
            {
                throw new SecurityPolicyValidationException(
                    $"Imageflow:Security:{pathForError} — expected true/false, got '{raw}'.");
            }
            return b;
        }
    }

    /// <summary>snake_case parser for <see cref="ImageFormat"/>. Mirrors the internal table in imageflow-dotnet.</summary>
    internal static class ImageFormatParser
    {
        public static bool TryParse(string? snakeCase, out ImageFormat format)
        {
            switch (snakeCase)
            {
                case "jpeg": format = ImageFormat.Jpeg; return true;
                case "png":  format = ImageFormat.Png;  return true;
                case "gif":  format = ImageFormat.Gif;  return true;
                case "webp": format = ImageFormat.Webp; return true;
                case "avif": format = ImageFormat.Avif; return true;
                case "jxl":  format = ImageFormat.Jxl;  return true;
                case "heic": format = ImageFormat.Heic; return true;
                case "bmp":  format = ImageFormat.Bmp;  return true;
                case "tiff": format = ImageFormat.Tiff; return true;
                case "pnm":  format = ImageFormat.Pnm;  return true;
                default: format = default; return false;
            }
        }
    }

    /// <summary>
    /// Name parsers for <see cref="NamedEncoderName"/> / <see cref="NamedDecoderName"/>.
    /// Exists because the per-enum <c>TryParse</c> helpers on the
    /// imageflow-dotnet side are internal — we re-declare the tables here to
    /// keep the Imageflow.Server project's only dependency on the public API.
    /// </summary>
    internal static class NamedCodecParsers
    {
        public static bool TryParseEncoder(string? snakeCase, out NamedEncoderName encoder)
        {
            switch (snakeCase)
            {
                case "mozjpeg_encoder":    encoder = NamedEncoderName.MozjpegEncoder; return true;
                case "zen_jpeg_encoder":   encoder = NamedEncoderName.ZenJpegEncoder; return true;
                case "mozjpeg_rs_encoder": encoder = NamedEncoderName.MozjpegRsEncoder; return true;
                case "libpng_encoder":     encoder = NamedEncoderName.LibpngEncoder; return true;
                case "lodepng_encoder":    encoder = NamedEncoderName.LodepngEncoder; return true;
                case "pngquant_encoder":   encoder = NamedEncoderName.PngquantEncoder; return true;
                case "zen_png_encoder":    encoder = NamedEncoderName.ZenPngEncoder; return true;
                case "webp_encoder":       encoder = NamedEncoderName.WebpEncoder; return true;
                case "zen_webp_encoder":   encoder = NamedEncoderName.ZenWebpEncoder; return true;
                case "gif_encoder":        encoder = NamedEncoderName.GifEncoder; return true;
                case "zen_gif_encoder":    encoder = NamedEncoderName.ZenGifEncoder; return true;
                case "zen_avif_encoder":   encoder = NamedEncoderName.ZenAvifEncoder; return true;
                case "zen_jxl_encoder":    encoder = NamedEncoderName.ZenJxlEncoder; return true;
                case "zen_bmp_encoder":    encoder = NamedEncoderName.ZenBmpEncoder; return true;
                default: encoder = default; return false;
            }
        }

        public static bool TryParseDecoder(string? snakeCase, out NamedDecoderName decoder)
        {
            switch (snakeCase)
            {
                case "mozjpeg_rs_decoder":   decoder = NamedDecoderName.MozjpegRsDecoder; return true;
                case "image_rs_jpeg_decoder":decoder = NamedDecoderName.ImageRsJpegDecoder; return true;
                case "zen_jpeg_decoder":     decoder = NamedDecoderName.ZenJpegDecoder; return true;
                case "libpng_decoder":       decoder = NamedDecoderName.LibpngDecoder; return true;
                case "image_rs_png_decoder": decoder = NamedDecoderName.ImageRsPngDecoder; return true;
                case "zen_png_decoder":      decoder = NamedDecoderName.ZenPngDecoder; return true;
                case "gif_rs_decoder":       decoder = NamedDecoderName.GifRsDecoder; return true;
                case "zen_gif_decoder":      decoder = NamedDecoderName.ZenGifDecoder; return true;
                case "webp_decoder":         decoder = NamedDecoderName.WebpDecoder; return true;
                case "zen_webp_decoder":     decoder = NamedDecoderName.ZenWebpDecoder; return true;
                case "zen_avif_decoder":     decoder = NamedDecoderName.ZenAvifDecoder; return true;
                case "zen_jxl_decoder":      decoder = NamedDecoderName.ZenJxlDecoder; return true;
                case "zen_bmp_decoder":      decoder = NamedDecoderName.ZenBmpDecoder; return true;
                default: decoder = default; return false;
            }
        }
    }
}
