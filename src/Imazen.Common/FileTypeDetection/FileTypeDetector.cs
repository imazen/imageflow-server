namespace Imazen.Common.FileTypeDetection{

    public class FileTypeDetector
    {
        /// <summary>
        /// Provides a good guess as to what mime type the file is - based on the first 12 bytes.
        /// Only image, video, audio, pdf, and font files are supported. Text-based formats are not supported.
        /// If the type is not recognized, returns null.
        /// 
        /// NOTE: The first 12 bytes are not always enough to decide between different file codecs when containers are used
        /// For example, this cannot tell between WebM (assumed) vs other matroska formats,
        /// between different mpeg and mp4 formats, and between ogg video (assumed) and audio types.
        /// Assumptions are made in these cases in favor of the most common formats.
        /// </summary>
        /// <param name="first12Bytes"></param>
        /// <returns></returns>
        public string? GuessMimeType(byte[] first12Bytes)
        {
            return MagicBytes.GetImageContentType(first12Bytes);
        }
        
        public static string? GuessMimeType(Span<byte> first12Bytes)
        {
            return MagicBytes.GetImageContentType(first12Bytes);
        }
    }

    internal static class MagicBytes
    {
        internal enum ImageFormat
        {
            Jpeg,
            Gif,
            Png,
            WebP,
            Tiff,
            Avif,
            AvifSequence,
            Heif,
            HeifSequence,
            Heic,
            HeicSequence,
            Bitmap,
            Ico,
            QuickTimeMovie,
            M4V,
            Woff,
            Woff2,
            OpenTypeFont,
            PostScript,
            Mp3,
            Pdf,
            FLIF,
            AIFF,
            TrueTypeFont,
            JpegXL,
            Jpeg2000,
            MP4,
            OggContainer,
            Mpeg1,
            Mpeg2,
            MatroskaOrWebM,
            WaveformAudio,
            AudioVideoInterleave,
            Flac,
            M4P,
            M4B,
            M4A,
            F4V,
            F4P,
            F4B,
            F4A,
            Aac,
            ThreeGPP
        }

        /// <summary>
        /// Returns null if not a recognized file type
        /// </summary>
        /// <param name="first12Bytes">First 12 or more bytes of the file</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private static ImageFormat? GetImageFormat(Span<byte> first12Bytes)
        {
            
            // Useful resources: https://chromium.googlesource.com/chromium/src/+/HEAD/net/base/mime_sniffer.cc
            // https://github.com/velocityzen/FileType/blob/master/Sources/FileType/FileTypeMatch.swift
            // https://en.wikipedia.org/wiki/List_of_file_signatures
            // https://mimetype.io/
            
            // We may want to support these from Chrome's sniffer, at some point, after research
            // MAGIC_MASK("video/mpeg", "\x00\x00\x01\xB0", "\xFF\xFF\xFF\xF0"),
            // MAGIC_MASK("audio/mpeg", "\xFF\xE0", "\xFF\xE0"),
            // MAGIC_NUMBER("video/quicktime", "....moov"),
            // MAGIC_NUMBER("application/x-shockwave-flash", "CWS"),
            // MAGIC_NUMBER("application/x-shockwave-flash", "FWS"),
            // MAGIC_NUMBER("video/x-flv", "FLV"),

            
            // Choosing not to detect mime types for text, svg, javascript, or executable formats
            // 00 61 73 6D (WebAssembly)
            
            // With just 12 bytes, we also can't tell PNG from APNG, Ogg audio from video, or what's in a matroska or mpeg container
            
            
            var bytes = first12Bytes;
            if (bytes.Length < 12) throw new ArgumentException("The byte array must contain at least 12 bytes", 
                nameof(first12Bytes));

            if (bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
            {
                //bytes[3] could be E0 or E1 for standard jpeg or exif respectively.
                //0xE2 or 0xE8 would be a CIFF or SPIFF image
                //We'll avoid being picky in case whichever codec you use can get the jpeg data from a ciff/spiff/etc
                return ImageFormat.Jpeg;
            }

            if (bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8' &&
                (bytes[4] == '9' || bytes[4] == '7') && bytes[5] == 'a')
            {
                return ImageFormat.Gif;
            }

            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47 &&
                bytes[4] == 0x0d && bytes[5] == 0x0a && bytes[6] == 0x1a && bytes[7] == 0x0a)
            {
                return ImageFormat.Png;
            }

            if (bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F')
            {
                if (bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
                {
                    return ImageFormat.WebP;
                }
                if (bytes[8] == 'W' && bytes[9] == 'A' && bytes[10] == 'V' && bytes[11] == 'E')
                {
                    return ImageFormat.WaveformAudio;
                }
                if (bytes[8] == 'A' && bytes[9] == 'V' && bytes[10] == 'I' && bytes[11] == 0x20)
                {
                    return ImageFormat.AudioVideoInterleave;
                }
            }

            if ((bytes[0] == 'M' && bytes[1] == 'M' && bytes[2] == 0x00 && bytes[3] == '*') ||
                (bytes[0] == 'I' && bytes[1] == 'I' && bytes[2] == '*' && bytes[3] == 0x00) ||
                (bytes[0] == 'I' && bytes[1] == ' ' && bytes[2] == 'I'))
            {
                // From chrome's mime sniffer we got MAGIC_NUMBER("image/tiff", "I I"),
                return ImageFormat.Tiff;
            }

            if (bytes[0] == 0x00  && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00)
            {
                return ImageFormat.Ico;
            }

            if (bytes[0] == 'w' && bytes[1] == 'O' && bytes[2] == 'F'){
                if (bytes[3] == 'F') return ImageFormat.Woff;
                if (bytes[3] == '2') return ImageFormat.Woff2;
            }

            if (bytes[0] == 'O' && bytes[1] == 'T' && bytes[2] == 'T' && bytes[3] == 'O')
            {
                return ImageFormat.OpenTypeFont;
            }
            
            if (bytes[0] == 'f' && bytes[1] == 'L' && bytes[2] == 'a' && bytes[3] == 'C')
            {
                return ImageFormat.Flac;
            }
            if (bytes[0] == 0x00  && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00)
            {
                return ImageFormat.TrueTypeFont;
            }
            
    
            if (bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
            {
                return ImageFormat.MatroskaOrWebM;
            }

            if (bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F' && bytes[4] == '-')
            {
                return ImageFormat.Pdf;
            }            
            if (bytes[0] == '%' && bytes[1] == '!' && bytes[2] == 'P' && bytes[3] == 'S' && bytes[4] == '-' && bytes[5] == 'A'
                && bytes[6] == 'd' && bytes[7] == 'o' && bytes[8] == 'b' && bytes[9] == 'e' && bytes[10] == '-')
            {
                return ImageFormat.PostScript;
            }
            
            if (bytes[0] == 'F' && bytes[1] == 'L' && bytes[2] == 'I' && bytes[3] == 'F')
            {
                return ImageFormat.FLIF;
            }
            
            if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x00)
            {
                if (bytes[3] == 0x0C 
                    && bytes[7] == 0x20 && bytes[8] == 0x0D && bytes[9] == 0x0A && bytes[10] == 0x87
                    && bytes[11] == 0x0A)
                {
                    if (bytes[4] == 0x4A && bytes[5] == 0x58 && bytes[6] == 0x4C)
                    {
                        return ImageFormat.JpegXL;
                    }

                    if (bytes[4] == 0x6A && bytes[5] == 0x50 && bytes[6] == 0x20)
                    {
                        return ImageFormat.Jpeg2000;
                    }
                }
                
                
            }

            if (bytes[0] == 0xFF && bytes[1] == 0x0A)
            {
                return ImageFormat.JpegXL;
            }
            
            if (bytes[0] == 'B' && bytes[1] == 'M')
            {
                return ImageFormat.Bitmap;
            }

            
            if (bytes[0] == 'O' && bytes[1] == 'g' && bytes[2] == 'g' && bytes[3] == 'S' && bytes[4] == 0x00)
            {
                return ImageFormat.OggContainer; // Could be audio or video, no idea, Chrome presumes audio
            }
            if (bytes[0] == 0x49 && bytes[1] == 0x44 && bytes[2] == 0x33)
            {
                return ImageFormat.Mp3;
            }

            if (bytes[0] == 0xFF && (bytes[1] == 0xFB || bytes[1] == 0xF3 || bytes[1] == 0xF2))
            {
                return ImageFormat.Mp3;
            }

            if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0xBA)
            {
                if ((bytes[4] & 0xF1) == 0x21)
                {
                   // MPEG-PS, MPEG-1 Part 1
                   return ImageFormat.Mpeg1;
                }

                if ((bytes[4] & 0xC4) == 0x44)
                {
                    //MPEG-PS, MPEG-2 Part 1
                    return ImageFormat.Mpeg2;
                }
            }
            
 
            if (bytes[0] == 0xFF && (bytes[1] == 0xF1 || bytes[1] == 0xF9))
            {
                return ImageFormat.Aac;
            }
            
            if (bytes[0] == 'F' && bytes[1] == 'O' && bytes[2] == 'R' && bytes[3] == 'M')
            {
                return ImageFormat.AIFF;
            }
            if (bytes[4] == 'f' && bytes[5] == 't' && bytes[6] == 'y' && bytes[7] == 'p')
            {
                if (bytes[8] == '3' && bytes[9] == 'g')
                {
                    return ImageFormat.ThreeGPP;
                }
                if (bytes[8] == 'a' && bytes[9] == 'v' && bytes[10] == 'i')
                {
                    if (bytes[11] == 'f')
                    {
                        return ImageFormat.Avif;
                    }

                    if (bytes[11] == 's')
                    {
                        return ImageFormat.AvifSequence;
                    }
                }

                // HEIF/HEIC has.. a lot of variations
                // http://nokiatech.github.io/heif/technical.html
                // https://mimetype.io/image/heic
                if (bytes[8] == 'm' && bytes[10] == 'f' && bytes[11] == '1')
                {
                    if (bytes[9] == 'i')
                        return ImageFormat.Heif;
                    if (bytes[9] == 's')
                        return ImageFormat.HeifSequence;
                }


                if (bytes[8] == 'h' && bytes[9] == 'e')
                {
                    if (bytes[10] == 'i')
                    {
                        if (bytes[11] == 'c' ||
                            bytes[11] == 'x' ||
                            bytes[11] == 'm' ||
                            bytes[11] == 's')
                            return ImageFormat.Heic;
                    }

                    if (bytes[10] == 'v')
                    {
                        if (bytes[11] == 'c' ||
                            bytes[11] == 'x' ||
                            bytes[11] == 'm' ||
                            bytes[11] == 's')
                            return ImageFormat.HeicSequence;
                    }

                }

                if (bytes[8] == 'q' && bytes[9] == 't')
                {

                    return ImageFormat.QuickTimeMovie;
                }

                if (bytes[8] == 'M' && bytes[9] == '4')
                {
                    if (bytes[10] == 'V')
                    {
                        return ImageFormat.M4V;
                    }

                    if (bytes[10] == 'P')
                    {
                        return ImageFormat.M4P;
                    }

                    if (bytes[10] == 'B')
                    {
                        return ImageFormat.M4B;
                    }

                    if (bytes[10] == 'A')
                    {
                        return ImageFormat.M4A;
                    }
                }

                if (bytes[8] == 'F' && bytes[9] == '4')
                {
                    //These are adobe flash video/audio formats, meh..
                    if (bytes[10] == 'V')
                    {
                        return ImageFormat.F4V;
                    }

                    if (bytes[10] == 'P')
                    {
                        return ImageFormat.F4P;
                    }

                    if (bytes[10] == 'B')
                    {
                        return ImageFormat.F4B;
                    }

                    if (bytes[10] == 'A')
                    {
                        return ImageFormat.F4A;
                    }
                }

                if (bytes[8] == 'm' && bytes[9] == 'm' && bytes[10] == 'p' && bytes[11] == '4')
                {
                    return ImageFormat.MP4;
                }
                if (bytes[8] == 'i' && bytes[9] == 's' && bytes[10] == 'o' && bytes[11] == 'm')
                {
                    return ImageFormat.MP4;
                }

                if (bytes[8] == 'a' && bytes[9] == 'v' && bytes[10] == 'c' && bytes[11] == 'l')
                {
                    return ImageFormat.ThreeGPP;
                }

                if (bytes[8] == '3' && bytes[9] == 'g')
                {
                    return ImageFormat.ThreeGPP;
                }
            }
    
            //TODO: Add 3GP -> https://developer.mozilla.org/en-US/docs/Web/Media/Formats/Containers#3gp
            
            return null;
        }
        internal static string? GetImageContentType(Span<byte> first12Bytes)
        {
            switch (GetImageFormat(first12Bytes))
            {
                case ImageFormat.Jpeg:
                    return "image/jpeg";
                case ImageFormat.Gif:
                    return "image/gif";
                case ImageFormat.Png:
                    return "image/png";
                case ImageFormat.WebP:
                    return "image/webp";
                case ImageFormat.Tiff:
                    return "image/tiff";
                case ImageFormat.Bitmap:
                    return "image/bmp";
                case ImageFormat.Avif:
                    return "image/avif";
                case ImageFormat.AvifSequence:
                    return "image/avif-sequence";
                case ImageFormat.Heif:
                    return "image/heif";
                case ImageFormat.HeifSequence:
                    return "image/heif-sequence";
                case ImageFormat.Heic:
                    return "image/heic";
                case ImageFormat.HeicSequence:
                    return "image/heic-sequence";
                case ImageFormat.Ico:
                    return "image/x-icon";
                case ImageFormat.Woff:
                    return "font/woff";
                case ImageFormat.Woff2:
                    return "font/woff2";
                case ImageFormat.OpenTypeFont:
                    return "font/otf";
                case ImageFormat.PostScript:
                    return "application/postscript";
                case ImageFormat.Pdf:
                    return "application/pdf";
                case ImageFormat.TrueTypeFont:
                    return "font/ttf";
                case ImageFormat.JpegXL:
                    return "image/jxl";
                case ImageFormat.Jpeg2000:
                    return "image/jp2";
                case ImageFormat.MP4:
                    return "video/mp4"; //or application/mp4
                case ImageFormat.OggContainer:
                    return "audio/ogg"; //There are more specific options for video/audio but we can't differentiate
                case ImageFormat.WaveformAudio:
                    return "audio/wav";
                case ImageFormat.AudioVideoInterleave:
                    return "video/x-msvideo";
                case ImageFormat.Mp3:
                    return "audio/mpeg";
                case ImageFormat.QuickTimeMovie:
                    return "video/quicktime";

                case ImageFormat.FLIF:
                    return "image/flif";
                case ImageFormat.AIFF:
                    return "audio/aiff";
                case ImageFormat.Flac:
                    return "audio/flac";
                case ImageFormat.Mpeg1:
                    return "video/mpeg"; // could be "audio/mpeg", we can't know
                case ImageFormat.Mpeg2:
                    return "video/mpeg"; // could be "audio/mpeg", we can't know
                case ImageFormat.MatroskaOrWebM:
                    return "video/webm"; //Chrome makes the presumption that matroska files are WebM, even if untrue
                case ImageFormat.M4P:
                    return "audio/mp4";
                case ImageFormat.M4B:
                    return "audio/mp4";
                case ImageFormat.M4V:
                    return "video/mp4";
                case ImageFormat.M4A:
                    return "audio/mp4";
                case ImageFormat.F4V:
                    return "video/mp4";
                case ImageFormat.F4P:
                    return "audio/mp4";
                case ImageFormat.F4B:
                    return "audio/mp4";
                case ImageFormat.F4A:
                    return "audio/mp4";
                case ImageFormat.Aac:
                    return "audio/aac";
                case ImageFormat.ThreeGPP:
                    return "video/3gpp";
                case null:
                    break;

                
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return null;
        }
        
        /// <summary>
        /// Returns true if Imageflow can likely decode the image based on the given file header
        /// </summary>
        /// <param name="first12Bytes"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        internal static bool IsDecodable(byte[] first12Bytes)
        {
            switch (GetImageFormat(first12Bytes))
            {
                case ImageFormat.Jpeg:
                case ImageFormat.Gif:
                case ImageFormat.Png:
                case ImageFormat.WebP:
                    return true;
                case null:
                    return false;
                default:
                    throw new NotImplementedException();
            }
        }
    }
}