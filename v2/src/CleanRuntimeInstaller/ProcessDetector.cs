using System;
using System.Diagnostics;
using System.Linq;

namespace Aion2TR.V2.RuntimeInstaller;

public interface IProcessDetector
{
    bool IsGameRunning();
}

public class SystemProcessDetector : IProcessDetector
{
    private static readonly string[] GameProcessNames =
    [
        "AION2",
        "Aion2",
        "Aion2-Win64-Shipping",
        "AION2-Win64-Shipping"
    ];

    public bool IsGameRunning()
    {
        try
        {
            var processes = Process.GetProcesses();
            return processes.Any(p => GameProcessNames.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
