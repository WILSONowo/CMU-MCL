// SPDX-License-Identifier: GPL-3.0-only
namespace CMUmcl.Community
{
    /// <summary>
    /// Instance loader state.
    /// Vanilla: instance.json exists and the normalized loader is exactly "vanilla" (explicit vanilla).
    /// Known:   instance.json exists and the loader is a recognized loader (only fabric/forge/neoforge/quilt).
    /// Unknown: instance.json missing/unreadable, or the loader is empty/blank/unrecognized
    ///          (e.g. liteloader/optifine/broken). Never silently treated as vanilla or known.
    /// </summary>
    internal enum LoaderState { Vanilla, Known, Unknown }

    /// <summary>Project-level compatibility hint inferred from the search summary only.</summary>
    internal enum CompatibilityHint { Compatible, Incompatible, Unknown }
}
