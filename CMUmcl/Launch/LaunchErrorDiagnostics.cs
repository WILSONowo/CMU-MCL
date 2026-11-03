using System;
using System.Collections.Generic;

namespace CMUmcl.Launch
{
    /// <summary>Returns a bounded exception type/HRESULT chain without messages, paths or command lines.</summary>
    public static class LaunchErrorDiagnostics
    {
        public static string Describe(Exception error)
        {
            var types = new List<string>();
            Exception? current = error;
            while (current != null && types.Count < 4)
            {
                types.Add(current.GetType().Name + " (0x" + current.HResult.ToString("X8") + ")");
                current = current.InnerException;
            }
            return "Launch exception types: " + string.Join(" <- ", types) + ".";
        }
    }
}
