using System.Runtime.InteropServices;

namespace Quantara.Tools;

public static class WindowsPowerRequest
{
    private const uint Continuous = 0x80000000;

    private const uint SystemRequired = 0x00000001;

    public static bool KeepAwake() => SetThreadExecutionState(Continuous | SystemRequired) != 0;

    public static bool Release() => SetThreadExecutionState(Continuous) != 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint SetThreadExecutionState(uint flags);
}
