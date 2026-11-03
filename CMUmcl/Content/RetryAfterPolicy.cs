using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace CMUmcl.Content
{
    internal static class RetryAfterPolicy
    {
        internal static TimeSpan Delay(HttpResponseMessage response, TimeSpan fallback, DateTimeOffset now)
        {
            if (!response.Headers.TryGetValues("Retry-After", out var values)) return fallback;
            string[] headers = values.Take(2).ToArray();
            if (headers.Length != 1 || headers[0].Length > 128 || !RetryConditionHeaderValue.TryParse(headers[0], out var parsed)) return fallback;
            TimeSpan delay = parsed.Delta ?? (parsed.Date!.Value - now);
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        // Long valid Retry-After values must not overflow Task.Delay or cause an early retry.
        internal static async System.Threading.Tasks.Task WaitAsync(TimeSpan delay, System.Threading.CancellationToken ct)
        {
            while (delay > TimeSpan.Zero)
            {
                TimeSpan part = delay > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : delay;
                await System.Threading.Tasks.Task.Delay(part, ct).ConfigureAwait(false);
                delay -= part;
            }
            ct.ThrowIfCancellationRequested();
        }
    }
}
