#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Imageflow.Fluent;

namespace Imageflow.Server
{
    /// <summary>
    /// Server-layer representation of the three-layer killbits policy, plus
    /// resource-scalar limits. Binds from <c>Imageflow:Security</c> in
    /// <c>appsettings.json</c>, or can be constructed programmatically.
    /// </summary>
    /// <remarks>
    /// Two things live together here:
    /// <list type="bullet">
    ///   <item><description>Trusted-policy scalars + killbits (what the
    ///     server signs off on globally). Allow-list forms are permitted at
    ///     this layer.</description></item>
    ///   <item><description>Server-default risky-encode list
    ///     (AVIF / JXL by default). Operators must opt in explicitly for
    ///     those formats to be usable.</description></item>
    /// </list>
    /// Call <see cref="BuildEffectiveSecurityOptions"/> to produce a
    /// narrowing-only <see cref="SecurityOptions"/> suitable for
    /// <c>Build001.security</c> / <c>Execute001.security</c>. The same helper
    /// is used by <see cref="ImageflowMiddlewareOptions.SecurityPolicyOptions"/>
    /// at startup for trusted-context <see cref="Imageflow.Bindings.JobContext.SetPolicy"/>
    /// validation.
    /// </remarks>
    public sealed class SecurityPolicyOptions
    {
        /// <summary>Server-default risky encode formats. See <see cref="ServerRiskyEncodeDefault"/>.</summary>
        /// <remarks>
        /// Replacing this list is an explicit operator choice. Prefer
        /// leaving it alone and toggling <see cref="FormatOptions.OptInEncode"/>.
        /// </remarks>
        public IReadOnlyList<ImageFormat> ServerRiskyEncode { get; set; } = ServerRiskyEncodeDefault;

        /// <summary>
        /// The default set of formats the server denies encode for unless the
        /// operator opts in. Currently {AVIF, JXL}. Exposed as a constant so
        /// operators can see what's blocked without reading source.
        /// </summary>
        public static readonly IReadOnlyList<ImageFormat> ServerRiskyEncodeDefault = new[]
        {
            ImageFormat.Avif,
            ImageFormat.Jxl,
        };

        // --- Scalar resource limits (forwarded verbatim to trusted policy) ---

        /// <summary>Max per-frame decode dimensions. <c>null</c> leaves the native default.</summary>
        public FrameSizeLimit? MaxDecodeSize { get; set; }

        /// <summary>Max per-frame working buffer. <c>null</c> leaves the native default.</summary>
        public FrameSizeLimit? MaxFrameSize { get; set; }

        /// <summary>Max encode dimensions. <c>null</c> leaves the native default.</summary>
        public FrameSizeLimit? MaxEncodeSize { get; set; }

        /// <summary>Bytes cap on a single codec input stream. <c>null</c> leaves native default (256 MiB).</summary>
        public ulong? MaxInputFileBytes { get; set; }

        /// <summary>JSON payload size cap. <c>null</c> leaves native default (64 MiB).</summary>
        public ulong? MaxJsonBytes { get; set; }

        /// <summary>Total decoded pixels across every frame. <c>null</c> leaves native default.</summary>
        public ulong? MaxTotalFilePixels { get; set; }

        // --- Killbits ---

        /// <summary>Per-format decode/encode permissions. Supports allow-list, deny-list, or table forms.</summary>
        public FormatOptions? Formats { get; set; }

        /// <summary>Per-codec permissions. Supports allow-list or deny-list forms.</summary>
        public CodecOptions? Codecs { get; set; }

        /// <summary>
        /// Run mutual-exclusion validation. Call this at config-bind time so
        /// misconfiguration surfaces before the server tries to talk to
        /// imageflow.
        /// </summary>
        /// <exception cref="SecurityPolicyValidationException">when invariants are violated.</exception>
        public void Validate()
        {
            try
            {
                Formats?.Validate();
                Codecs?.Validate();
            }
            catch (ArgumentException ex)
            {
                throw new SecurityPolicyValidationException(
                    $"Imageflow:Security — {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Compute the narrowing-only <see cref="SecurityOptions"/> the server
        /// should apply to every job. Merges:
        /// <list type="number">
        ///   <item><description>Server-default risky encode list (AVIF+JXL),
        ///     minus any operator opt-ins.</description></item>
        ///   <item><description>Operator-supplied allow/deny/table forms.</description></item>
        ///   <item><description>Scalar limits.</description></item>
        /// </list>
        /// If the operator supplied <see cref="FormatOptions.AllowEncode"/>
        /// or mentioned one of the risky formats explicitly in
        /// <see cref="FormatOptions.Formats"/>, the server default is
        /// disabled for that format (the operator took control).
        /// </summary>
        public SecurityOptions BuildEffectiveSecurityOptions()
        {
            Validate();

            var effective = new SecurityOptions
            {
                MaxDecodeSize = MaxDecodeSize,
                MaxFrameSize = MaxFrameSize,
                MaxEncodeSize = MaxEncodeSize,
                MaxInputFileBytes = MaxInputFileBytes,
                MaxJsonBytes = MaxJsonBytes,
                MaxTotalFilePixels = MaxTotalFilePixels,
            };

            var formats = Formats ?? new FormatOptions();
            var codecs = Codecs ?? new CodecOptions();

            // ---- Merge format killbits ----

            // If the operator set AllowEncode, their list wins — the server
            // default is not applied. Similarly, if they set a table that
            // mentions a risky format, that format is operator-controlled.
            var operatorControlledEncode = BuildOperatorControlledEncodeSet(formats);
            var effectiveRiskyDefault = ServerRiskyEncode
                .Where(f => !operatorControlledEncode.Contains(f))
                .Where(f => formats.OptInEncode == null || !formats.OptInEncode.Contains(f))
                .ToList();

            var formatKillbits = new FormatKillbits
            {
                AllowDecode = formats.AllowDecode?.ToList(),
                AllowEncode = formats.AllowEncode?.ToList(),
                Formats = formats.Formats == null
                    ? null
                    : new Dictionary<ImageFormat, FormatPermissions>(formats.Formats),
            };

            // DenyDecode merges operator's DenyDecode verbatim (no server default).
            if (formats.DenyDecode != null && formats.DenyDecode.Count > 0)
            {
                formatKillbits.DenyDecode = formats.DenyDecode.ToList();
            }

            // DenyEncode merges operator's DenyEncode plus the effective risky default.
            var denyEncode = new List<ImageFormat>();
            if (effectiveRiskyDefault.Count > 0)
            {
                denyEncode.AddRange(effectiveRiskyDefault);
            }
            if (formats.DenyEncode != null)
            {
                foreach (var f in formats.DenyEncode)
                {
                    if (!denyEncode.Contains(f))
                    {
                        denyEncode.Add(f);
                    }
                }
            }
            if (denyEncode.Count > 0)
            {
                formatKillbits.DenyEncode = denyEncode;
            }

            // If the operator used table form, AllowEncode/AllowDecode/DenyEncode/DenyDecode
            // must all be null (mutual exclusion). Push the server default into the table
            // instead.
            if (formats.Formats != null)
            {
                formatKillbits.AllowEncode = null;
                formatKillbits.AllowDecode = null;
                formatKillbits.DenyEncode = null;
                formatKillbits.DenyDecode = null;
                var mergedTable = new Dictionary<ImageFormat, FormatPermissions>();
                foreach (var kv in formats.Formats)
                {
                    mergedTable[kv.Key] = kv.Value;
                }
                foreach (var risky in effectiveRiskyDefault)
                {
                    if (!mergedTable.ContainsKey(risky))
                    {
                        // deny encode, leave decode at the layer's existing state (true)
                        mergedTable[risky] = new FormatPermissions(decode: true, encode: false);
                    }
                }
                formatKillbits.Formats = mergedTable;
            }

            // Skip sending empty killbits — the native side treats a missing
            // block and an empty block identically, but a missing block keeps
            // the wire payload small and avoids confusing operators reading
            // the startup log.
            if (HasAnyFormatEntry(formatKillbits))
            {
                effective.Formats = formatKillbits;
            }

            // ---- Merge codec killbits ----

            var codecKillbits = new CodecKillbits
            {
                AllowEncoders = codecs.AllowEncoders?.ToList(),
                DenyEncoders = codecs.DenyEncoders?.ToList(),
                AllowDecoders = codecs.AllowDecoders?.ToList(),
                DenyDecoders = codecs.DenyDecoders?.ToList(),
            };

            if (HasAnyCodecEntry(codecKillbits))
            {
                effective.Codecs = codecKillbits;
            }

            return effective;
        }

        private static HashSet<ImageFormat> BuildOperatorControlledEncodeSet(FormatOptions formats)
        {
            var set = new HashSet<ImageFormat>();
            if (formats.AllowEncode != null)
            {
                foreach (var f in formats.AllowEncode)
                {
                    set.Add(f);
                }
            }
            if (formats.DenyEncode != null)
            {
                foreach (var f in formats.DenyEncode)
                {
                    set.Add(f);
                }
            }
            if (formats.Formats != null)
            {
                foreach (var kv in formats.Formats)
                {
                    set.Add(kv.Key);
                }
            }
            return set;
        }

        private static bool HasAnyFormatEntry(FormatKillbits k) =>
            k.AllowDecode != null || k.DenyDecode != null ||
            k.AllowEncode != null || k.DenyEncode != null ||
            k.Formats != null;

        private static bool HasAnyCodecEntry(CodecKillbits k) =>
            k.AllowEncoders != null || k.DenyEncoders != null ||
            k.AllowDecoders != null || k.DenyDecoders != null;
    }

    /// <summary>
    /// Operator-facing shape for per-format gating. Matches the
    /// <see cref="FormatKillbits"/> mutual-exclusion rules but adds
    /// <see cref="OptInEncode"/> — the server-layer concept that turns on
    /// AVIF/JXL encode without requiring the operator to spell out the full
    /// deny-list.
    /// </summary>
    public sealed class FormatOptions
    {
        /// <summary>Opt in to formats that the server denies by default (currently AVIF and JXL).</summary>
        public IList<ImageFormat>? OptInEncode { get; set; }

        /// <summary>Allow only these formats for decode. Mutually exclusive with <see cref="DenyDecode"/> and <see cref="Formats"/>.</summary>
        public IList<ImageFormat>? AllowDecode { get; set; }

        /// <summary>Deny these formats for decode. Mutually exclusive with <see cref="AllowDecode"/>.</summary>
        public IList<ImageFormat>? DenyDecode { get; set; }

        /// <summary>Allow only these formats for encode. Mutually exclusive with <see cref="DenyEncode"/> and <see cref="Formats"/>.</summary>
        public IList<ImageFormat>? AllowEncode { get; set; }

        /// <summary>Deny these formats for encode (merged with the server's risky default). Mutually exclusive with <see cref="AllowEncode"/>.</summary>
        public IList<ImageFormat>? DenyEncode { get; set; }

        /// <summary>Per-format table form. Mutually exclusive with any of the list forms.</summary>
        public IDictionary<ImageFormat, FormatPermissions>? Formats { get; set; }

        internal void Validate()
        {
            if (AllowDecode != null && DenyDecode != null)
            {
                throw new ArgumentException(
                    "Formats: pick allow or deny for decode, not both", nameof(AllowDecode));
            }
            if (AllowEncode != null && DenyEncode != null)
            {
                throw new ArgumentException(
                    "Formats: pick allow or deny for encode, not both", nameof(AllowEncode));
            }
            var hasList = AllowDecode != null || DenyDecode != null ||
                          AllowEncode != null || DenyEncode != null;
            if (hasList && Formats != null)
            {
                throw new ArgumentException(
                    "Formats: pick a single form (allow/deny lists OR table), not a mix",
                    nameof(Formats));
            }
        }
    }

    /// <summary>Operator-facing shape for per-codec gating.</summary>
    public sealed class CodecOptions
    {
        public IList<NamedEncoderName>? AllowEncoders { get; set; }
        public IList<NamedEncoderName>? DenyEncoders { get; set; }
        public IList<NamedDecoderName>? AllowDecoders { get; set; }
        public IList<NamedDecoderName>? DenyDecoders { get; set; }

        internal void Validate()
        {
            if (AllowEncoders != null && DenyEncoders != null)
            {
                throw new ArgumentException(
                    "Codecs: pick allow or deny for encoders, not both", nameof(AllowEncoders));
            }
            if (AllowDecoders != null && DenyDecoders != null)
            {
                throw new ArgumentException(
                    "Codecs: pick allow or deny for decoders, not both", nameof(AllowDecoders));
            }
        }
    }

    /// <summary>Thrown at startup when <see cref="SecurityPolicyOptions"/> is internally inconsistent.</summary>
    public sealed class SecurityPolicyValidationException : Exception
    {
        public SecurityPolicyValidationException(string message) : base(message) { }
        public SecurityPolicyValidationException(string message, Exception inner) : base(message, inner) { }
    }
}
