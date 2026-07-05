using System.Runtime.InteropServices;
using System.Text;

namespace RenegadeServer.Xeno;

public static class Bridge
{
    [DllImport("Xeno", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "Initialize")]
    private static extern void InitializeDll(bool useConsole);

    [DllImport("Xeno", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "Version")]
    private static extern IntPtr VersionDll();

    [DllImport("Xeno", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "GetClients")]
    private static extern IntPtr GetClientsDll();

    [DllImport("Xeno", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "Attach")]
    private static extern void AttachDll();

    [DllImport("Xeno", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "Execute")]
    private static extern void ExecuteDll(IntPtr script, IntPtr pids, int count);

    [DllImport("Xeno", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "SetSetting")]
    private static extern void SetSettingDll(int id, int value);

    public static void Initialize(bool useConsole) => InitializeDll(useConsole);
    public static string GetVersion() => Marshal.PtrToStringAnsi(VersionDll()) ?? "";
    public static string GetClientsJson() => Marshal.PtrToStringAnsi(GetClientsDll()) ?? "[]";
    public static void Attach() => AttachDll();
    public static void SetSetting(int id, int value) => SetSettingDll(id, value);

    public static void Execute(string script, int[] pids)
    {
        var utf8 = Encoding.UTF8.GetBytes(script + "\0");
        var pinned = GCHandle.Alloc(utf8, GCHandleType.Pinned);
        var pidBuf = Marshal.AllocHGlobal(pids.Length * sizeof(int));
        try
        {
            Marshal.Copy(pids, 0, pidBuf, pids.Length);
            ExecuteDll(pinned.AddrOfPinnedObject(), pidBuf, pids.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(pidBuf);
            pinned.Free();
        }
    }
}
