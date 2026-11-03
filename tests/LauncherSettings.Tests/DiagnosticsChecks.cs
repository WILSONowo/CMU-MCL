using System.Diagnostics;
using System.Linq;
using CMUmcl.Launch;

internal static partial class Program
{
    private static void GameDiagnosticsChecks()
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("cmd.exe")
            {
                Arguments = "/d /c \"echo UnsupportedClassVersionError & echo GLFW error 65542\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        var diagnostics = GameProcessDiagnostics.Prepare(process);
        process.Start();
        diagnostics.BeginRead();
        process.WaitForExit();
        string summary = diagnostics.Describe();
        Check(summary.Contains("UnsupportedClassVersion", StringComparison.Ordinal), "Java diagnostics classifies unsupported class version");
        Check(summary.Contains("Glfw", StringComparison.Ordinal), "Java diagnostics classifies GLFW error");
        Check(!summary.Contains("echo", StringComparison.OrdinalIgnoreCase) && !summary.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase), "Java diagnostics never exposes raw output or command line");
    }
}
