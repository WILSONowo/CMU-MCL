// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace CMUmcl.Community
{
    /// <summary>Normalized community-source failure (HTTP status, rate-limit, parse, timeout).</summary>
    internal sealed class CommunitySourceException : Exception
    {
        public ResourceSourceId SourceId { get; }
        public string Reason { get; }
        public int? HttpStatus { get; }

        public CommunitySourceException(ResourceSourceId sourceId, string reason, int? httpStatus = null, Exception? inner = null)
            : base(reason, inner)
        {
            SourceId = sourceId;
            Reason = reason;
            HttpStatus = httpStatus;
        }
    }
}
