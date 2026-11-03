using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CMUmcl.Launch
{
    /// <summary>
    /// Consumes Java stdout/stderr and retains only a small, fixed set of
    /// classification counters. Raw output, command lines, tokens and log text
    /// are never stored or exported.
    /// </summary>
    public sealed class GameProcessDiagnostics
    {
        private static readonly (string Name, Regex Pattern)[] Classifiers =
        {
            ("UnsupportedClassVersion", new Regex(@"UnsupportedClassVersionError", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            ("OutOfMemory", new Regex(@"OutOfMemoryError|Could not reserve enough space", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            ("ClassNotFound", new Regex(@"ClassNotFoundException", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            ("NoClassDefFound", new Regex(@"NoClassDefFoundError", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            ("NativeLibrary", new Regex(@"UnsatisfiedLinkError|Failed to load native library", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            ("Initializer", new Regex(@"ExceptionInInitializerError|MixinApplyError", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            ("Glfw", new Regex(@"GLFW error\s+\d+|Failed to create window", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            ("OpenGL", new Regex(@"WGL:|OpenGL.*(?:support|driver|context)|Failed to initialize OpenGL", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            ("Vulkan", new Regex(@"Vulkan|VK_ERROR_[A-Z_]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            ("ModCompatibility", new Regex(@"Incompatible mod set|Mod resolution|requires version", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        };

        private readonly Process _process;
        private readonly ConcurrentDictionary<string, int> _categories = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _codes = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private int _lines;
        private int _started;

        private GameProcessDiagnostics(Process process) => _process = process;

        public static GameProcessDiagnostics Prepare(Process process)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            ProcessStartInfo start = process.StartInfo;
            start.UseShellExecute = false;
            start.CreateNoWindow = false;
            start.WindowStyle = ProcessWindowStyle.Normal;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
            return new GameProcessDiagnostics(process);
        }

        public void BeginRead()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;
            _process.OutputDataReceived += (_, e) => Inspect(e.Data);
            _process.ErrorDataReceived += (_, e) => Inspect(e.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        private void Inspect(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            Interlocked.Increment(ref _lines);
            foreach ((string name, Regex pattern) in Classifiers)
            {
                if (pattern.IsMatch(line))
                    _categories.AddOrUpdate(name, 1, (_, count) => count + 1);
            }

            foreach (Match match in Regex.Matches(line, @"\bVK_ERROR_[A-Z_]+\b", RegexOptions.CultureInvariant))
                _codes.AddOrUpdate(match.Value, 1, (_, count) => count + 1);
        }

        /// <summary>Returns a bounded, user-safe summary. Never includes raw output.</summary>
        public string Describe()
        {
            var parts = _categories
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + " x" + pair.Value)
                .ToList();
            if (_codes.Count != 0)
                parts.Add("VulkanCodes=" + string.Join(",", _codes.Keys.OrderBy(value => value, StringComparer.Ordinal)));
            if (parts.Count == 0)
                return _lines == 0 ? "No Java output classification was captured." : "Java output did not match a known error category.";
            return "Java output categories: " + string.Join("; ", parts) + ".";
        }
    }
}
