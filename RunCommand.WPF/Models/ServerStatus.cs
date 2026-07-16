namespace RunCommand.WPF.Models
{
    public enum ServerStatus
    {
        Unknown,   // never checked - gray
        Checking,  // check in progress - amber/animated
        Online,    // reachable - green
        Offline,   // unreachable / timeout - red
        AuthFailed // reachable but login/auth failed - orange
    }
}
