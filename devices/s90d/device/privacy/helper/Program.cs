// Native metadata bindings adapted from invaliddayta/s90c-tools PkgInfoProbe.cs (MIT),
// generalized from the Phase TV Plus helper. Read metadata for given app IDs and reload the
// owner app-info cache. No launch/terminate/uninstall API.
using System;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class AppMetadata
{
    private const string PackageLibrary = "/usr/lib/libpkgmgr-info.so.0";
    private const uint OwnerUid = 5001;
    private const uint OwnerGid = 100; // this TV's `id owner`

    [DllImport(PackageLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int pkgmgrinfo_appinfo_get_usr_appinfo(string id, uint uid, out IntPtr info);
    [DllImport(PackageLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int pkgmgrinfo_appinfo_get_usr_disabled_appinfo(string id, uint uid, out IntPtr info);
    [DllImport(PackageLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int pkgmgrinfo_appinfo_get_exec(IntPtr info, out IntPtr value);
    [DllImport(PackageLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int pkgmgrinfo_appinfo_is_disabled(IntPtr info, out byte value);
    [DllImport(PackageLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int pkgmgrinfo_appinfo_destroy_appinfo(IntPtr info);
    [DllImport("/usr/lib/libaul.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aul_reload_appinfo();
    [DllImport("libc.so.6")] private static extern int setgroups(UIntPtr count, IntPtr groups);
    [DllImport("libc.so.6")] private static extern int setgid(uint gid);
    [DllImport("libc.so.6")] private static extern int setuid(uint uid);
    [DllImport("libc.so.6")] private static extern uint getuid();
    [DllImport("libc.so.6")] private static extern uint getgid();

    private static void RequireSuccess(int code, string operation)
    {
        if (code != 0) throw new InvalidOperationException(operation + " returned " + code);
    }

    private static void BecomeOwner()
    {
        RequireSuccess(setgroups(UIntPtr.Zero, IntPtr.Zero), "setgroups");
        RequireSuccess(setgid(OwnerGid), "setgid");
        RequireSuccess(setuid(OwnerUid), "setuid");
        if (getuid() != OwnerUid || getgid() != OwnerGid)
            throw new InvalidOperationException("Owner identity mismatch");
    }

    private static bool ValidId(string id)
    {
        if (id.Length == 0 || id.Length > 128) return false;
        foreach (char c in id)
            if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')) return false;
        return true;
    }

    private static string Read(string appId)
    {
        int code = pkgmgrinfo_appinfo_get_usr_appinfo(appId, OwnerUid, out IntPtr info);
        bool usedDisabledLookup = false;
        if (code < 0)
        {
            code = pkgmgrinfo_appinfo_get_usr_disabled_appinfo(appId, OwnerUid, out info);
            usedDisabledLookup = true;
        }
        if (code != 0)
            return JsonSerializer.Serialize(new { app_id = appId, error = "get appinfo returned " + code });
        try
        {
            RequireSuccess(pkgmgrinfo_appinfo_get_exec(info, out IntPtr exec), "get executable");
            RequireSuccess(pkgmgrinfo_appinfo_is_disabled(info, out byte disabled), "get disabled");
            if (disabled > 1) throw new InvalidOperationException("Unexpected native boolean");
            return JsonSerializer.Serialize(new {
                app_id = appId, caller_uid = getuid(), disabled = disabled == 1,
                executable = Marshal.PtrToStringAnsi(exec), disabled_lookup = usedDisabledLookup
            });
        }
        finally { pkgmgrinfo_appinfo_destroy_appinfo(info); }
    }

    private static int Main(string[] args)
    {
        if (args.Length == 0 || (args[0] != "read-root" && args[0] != "read-owner" &&
            args[0] != "reload-owner" && args[0] != "--self-test") ||
            (args[0] == "reload-owner" && args.Length != 1) ||
            ((args[0] == "read-root" || args[0] == "read-owner") && args.Length < 2))
        {
            Console.Error.WriteLine("Usage: AppMetadata read-root|read-owner APPID... | reload-owner | --self-test");
            return 2;
        }
        for (int i = 1; i < args.Length; i++)
            if (!ValidId(args[i])) { Console.Error.WriteLine("invalid app id"); return 2; }
        if (args[0] == "--self-test")
        {
            bool ok = ValidId("com.samsung.tv.acr") && ValidId("AQKO41xyKP.AmazonAlexa") && !ValidId("a;b") && !ValidId("");
            using JsonDocument parsed = JsonDocument.Parse(JsonSerializer.Serialize(new { app_id = "x", disabled = true }));
            ok &= parsed.RootElement.GetProperty("disabled").GetBoolean();
            Console.WriteLine(ok ? "Offline self-test passed; native APIs require TV verification." : "self-test FAILED");
            return ok ? 0 : 1;
        }
        try
        {
            if (args[0] != "read-root") BecomeOwner();
            if (args[0] == "reload-owner")
            {
                int code = aul_reload_appinfo();
                Console.WriteLine(JsonSerializer.Serialize(new {
                    operation = "aul_reload_appinfo", result = code, caller_uid = getuid(), caller_gid = getgid()
                }));
                RequireSuccess(code, "aul_reload_appinfo");
                return 0;
            }
            for (int i = 1; i < args.Length; i++) Console.WriteLine(Read(args[i]));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 1;
        }
    }
}
