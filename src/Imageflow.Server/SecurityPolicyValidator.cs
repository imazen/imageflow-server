#nullable enable
using System;
using System.Linq;
using System.Text;
using Imageflow.Bindings;
using Imageflow.Fluent;
using Microsoft.Extensions.Logging;

namespace Imageflow.Server
{
    /// <summary>
    /// Applies a <see cref="SecurityPolicyOptions"/> to a one-shot
    /// <see cref="JobContext"/> at startup, captures the resulting
    /// <see cref="NetSupportResponse"/>, and exposes it as a readonly
    /// snapshot for the rest of the server process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// We don't keep the <see cref="JobContext"/> alive after startup —
    /// <see cref="Imageflow.Fluent.ImageJob"/> creates a fresh context
    /// per job, so there's no persistent context to hold a trusted policy
    /// on. Instead, the server expresses the same policy as a narrowing
    /// per-job security block on every request.
    /// </para>
    /// <para>
    /// The one-shot <c>SetPolicy</c> call at startup serves two purposes:
    /// </para>
    /// <list type="number">
    ///   <item><description>It surfaces bad configuration loudly (the
    ///     native side validates mutual exclusion and reports
    ///     <c>invalid_policy</c>), failing fast rather than silently
    ///     degrading at first request.</description></item>
    ///   <item><description>It captures the <see cref="NetSupportResponse"/>
    ///     grid operators need in order to diagnose "why not WebP?". The
    ///     cached snapshot is published through <see cref="EffectiveNetSupport"/>
    ///     and summarized on a single startup log line tagged
    ///     <c>killbits-policy</c>.</description></item>
    /// </list>
    /// </remarks>
    public sealed class SecurityPolicyValidator
    {
        private NetSupportResponse? _netSupport;
        private SecurityOptions? _effective;

        /// <summary>
        /// Narrowing-only <see cref="SecurityOptions"/> computed from the
        /// operator's <see cref="SecurityPolicyOptions"/>. Safe to attach to
        /// every job via <c>FinishJobBuilder.SetSecurityOptions</c>.
        /// <c>null</c> until <see cref="Apply"/> has run.
        /// </summary>
        public SecurityOptions? EffectiveJobSecurity => _effective;

        /// <summary>
        /// Grid captured at startup after the trusted policy was installed
        /// on a one-shot context. <c>null</c> until <see cref="Apply"/> has
        /// run.
        /// </summary>
        public NetSupportResponse? EffectiveNetSupport => _netSupport;

        /// <summary>
        /// Validate the policy, send it to imageflow on a scratch context,
        /// and record the resulting <see cref="NetSupportResponse"/>.
        /// Logs a single structured line (search log for
        /// <c>killbits-policy</c>) summarizing the effective grid.
        /// </summary>
        /// <exception cref="SecurityPolicyValidationException">
        ///   When the native side rejects the policy (malformed, missing
        ///   endpoint, or unknown format/codec name).
        /// </exception>
        public void Apply(SecurityPolicyOptions options, ILogger? logger = null)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Validate();
            _effective = options.BuildEffectiveSecurityOptions();

            // Spin up a one-shot context solely to validate the policy and
            // grab the grid. Any failure here should take down startup.
            try
            {
                using var ctx = new JobContext();
                var response = ctx.SetPolicy(_effective);
                _netSupport = response.NetSupport;
            }
            catch (ImageflowException ex)
            {
                throw new SecurityPolicyValidationException(
                    "Imageflow rejected the killbits policy at startup. "
                    + "Check Imageflow:Security in configuration. Underlying error: "
                    + ex.Message, ex);
            }

            LogSummary(options, logger);
        }

        private void LogSummary(SecurityPolicyOptions options, ILogger? logger)
        {
            if (logger == null || _netSupport == null)
            {
                return;
            }

            var allowedFormatsDecode = new StringBuilder();
            var allowedFormatsEncode = new StringBuilder();
            foreach (var kv in _netSupport.Formats)
            {
                if (kv.Value.Decode)
                {
                    if (allowedFormatsDecode.Length > 0) allowedFormatsDecode.Append(',');
                    allowedFormatsDecode.Append(kv.Key);
                }
                if (kv.Value.Encode)
                {
                    if (allowedFormatsEncode.Length > 0) allowedFormatsEncode.Append(',');
                    allowedFormatsEncode.Append(kv.Key);
                }
            }

            var riskyStr = options.ServerRiskyEncode.Count == 0
                ? "(none)"
                : string.Join(",", options.ServerRiskyEncode.Select(f => f.ToString().ToLowerInvariant()));

            logger.LogInformation(
                "killbits-policy server_risky_encode=[{Risky}] decode_allowed=[{Decode}] encode_allowed=[{Encode}] trusted_policy_set={Trusted}",
                riskyStr,
                allowedFormatsDecode.ToString(),
                allowedFormatsEncode.ToString(),
                _netSupport.TrustedPolicySet);
        }
    }
}
