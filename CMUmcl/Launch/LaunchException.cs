using System;

namespace CMUmcl.Launch
{
    /// <summary>Raised when game launch fails, with a friendly Chinese message.</summary>
    public sealed class LaunchException : Exception
    {
        public bool RequiresLogin => InnerException is CMUmcl.Auth.YggdrasilException auth && auth.RequiresLogin;
        public LaunchException(string message) : base(message) { }
        public LaunchException(string message, Exception inner) : base(message, inner) { }
    }
}
