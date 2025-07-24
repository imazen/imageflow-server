using System;
using System.Diagnostics.CodeAnalysis;

namespace Imazen.Routing.Matching.Templating
{
    /// <summary>
    /// Provides utility methods for validating URI paths against security threats in a zero-allocation manner.
    /// </summary>
    public static class TemplateSafety
    {
        /// <summary>
        /// Validates a given path to ensure it is safe from common traversal and injection attacks.
        /// This function operates on spans to avoid memory allocations.
        /// </summary>
        /// <param name="path">The path span to validate. It may be URL-encoded.</param>
        /// <param name="allowedStartString">If not null, the path must start with this literal string. If null, standard root attack validations are applied.</param>
        /// <param name="validationError">When this method returns false, contains a message describing the validation failure.</param>
        /// <returns>True if the path is safe; otherwise, false.</returns>
        public static bool IsPathSafe(ReadOnlySpan<char> path, string? allowedStartString, [NotNullWhen(false)] out string? validationError)
        {
            if (path.IsEmpty)
            {
                validationError = null;
                return true; // An empty path is considered safe.
            }

            // Use a stack-allocated buffer for decoding. If the path is too long, this will fail.
            // For web paths, a 2048 character limit is a reasonable assumption.
            Span<char> decodedPathBuffer = path.Length <= 2048 ? stackalloc char[path.Length] : new char[path.Length];
            if (!TryUrlDecode(path, decodedPathBuffer, out var decodedLength))
            {
                validationError = "Path is too long to process or contains invalid encoding.";
                return false;
            }
            ReadOnlySpan<char> pathToValidate = decodedPathBuffer[..decodedLength];

            // After decoding, perform all subsequent checks on the decoded span.
            
            // 1. Check for Null Bytes or Control Characters.
            foreach (var ch in pathToValidate)
            {
                if (ch == '\0')
                {
                    validationError = "Path contains an invalid null byte.";
                    return false;
                }
                if (char.IsControl(ch))
                {
                    validationError = "Path contains an invalid control character.";
                    return false;
                }
            }

            // 2. Handle the 'allowedStartString' logic.
            if (allowedStartString != null)
            {
                if (!pathToValidate.StartsWith(allowedStartString.AsSpan(), StringComparison.Ordinal))
                {
                    validationError = $"Path does not start with the required prefix '{allowedStartString}'.";
                    return false;
                }
                // We only need to validate the part of the path *after* the prefix.
                pathToValidate = pathToValidate[allowedStartString.Length..];
            }
            else
            {
                // If no prefix is required, validate the whole path for root attacks.

                // Check for absolute paths (root attacks)
                if (pathToValidate.Length > 0 && (pathToValidate[0] == '/' || pathToValidate[0] == '\\'))
                {
                    validationError = "Path must not be absolute (start with '/' or '\\').";
                    return false;
                }
                if (pathToValidate.Length > 1 && pathToValidate[1] == ':' && char.IsLetter(pathToValidate[0]))
                {
                    validationError = "Path appears to be an absolute Windows path with a drive letter.";
                    return false;
                }
            }

            // 3. Check for directory traversal sequences ('..') in the relevant part of the path.
            int start = 0;
            while (start < pathToValidate.Length)
            {
                int separatorIndex = pathToValidate.Slice(start).IndexOfAny('/', '\\');
                ReadOnlySpan<char> segment = separatorIndex == -1
                    ? pathToValidate.Slice(start)
                    : pathToValidate.Slice(start, separatorIndex);

                if (segment.Equals("..".AsSpan(), StringComparison.Ordinal))
                {
                    validationError = "Path contains a directory traversal sequence ('..').";
                    return false;
                }

                if (separatorIndex == -1) break;
                start += separatorIndex + 1;
            }

            validationError = null;
            return true;
        }

        /// <summary>
        /// Decodes a URL-encoded path from a source span to a destination span.
        /// </summary>
        private static bool TryUrlDecode(ReadOnlySpan<char> source, Span<char> destination, out int written)
        {
            written = 0;
            int sourceIndex = 0;

            while (sourceIndex < source.Length)
            {
                if (written >= destination.Length) return false; // Destination buffer is too small.

                char ch = source[sourceIndex];
                if (ch == '%')
                {
                    if (sourceIndex + 2 >= source.Length) return false; // Incomplete escape sequence.
                    
                    if (TryHexToChar(source.Slice(sourceIndex + 1, 2), out char decodedChar))
                    {
                        destination[written++] = decodedChar;
                        sourceIndex += 3;
                    }
                    else
                    {
                        // Malformed hex, copy literally
                        destination[written++] = ch;
                        sourceIndex++;
                    }
                }
                else if (ch == '+')
                {
                    destination[written++] = ' ';
                    sourceIndex++;
                }
                else
                {
                    destination[written++] = ch;
                    sourceIndex++;
                }
            }
            return true;
        }

        private static bool TryHexToChar(ReadOnlySpan<char> hex, out char c)
        {
            c = (char)0;
            if (hex.Length != 2) return false;

            if (!TryHexToValue(hex[0], out int high) || !TryHexToValue(hex[1], out int low))
            {
                return false;
            }
            c = (char)((high << 4) | low);
            return true;
        }

        private static bool TryHexToValue(char hexChar, out int value)
        {
            if (hexChar >= '0' && hexChar <= '9')
            {
                value = hexChar - '0';
                return true;
            }
            if (hexChar >= 'A' && hexChar <= 'F')
            {
                value = hexChar - 'A' + 10;
                return true;
            }
            if (hexChar >= 'a' && hexChar <= 'f')
            {
                value = hexChar - 'a' + 10;
                return true;
            }
            value = 0;
            return false;
        }
    }
}