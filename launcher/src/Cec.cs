using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// HDMI-CEC through the TV's own service (org.tizen.tv.hdmicec on the system bus), read in-process with GDBus.
// Which devices the TV currently lists, and on which port; per port, the power answer of the device plugged into it.
static class Cec
{
    const string Lib = "libgio-2.0.so.0", GLib = "libglib-2.0.so.0";
    [DllImport(Lib)] static extern IntPtr g_bus_get_sync(int busType, IntPtr cancellable, out IntPtr error);
    [DllImport(Lib)] static extern IntPtr g_dbus_connection_call_sync(IntPtr connection, string busName, string objectPath, string interfaceName, string methodName,
        IntPtr parameters, IntPtr replyType, int flags, int timeoutMs, IntPtr cancellable, out IntPtr error);
    [DllImport(GLib)] static extern IntPtr g_variant_new_int32(int value);
    [DllImport(GLib)] static extern IntPtr g_variant_new_boolean(int value);
    [DllImport(GLib)] static extern IntPtr g_variant_new_tuple(IntPtr[] children, UIntPtr count);
    [DllImport(GLib)] static extern UIntPtr g_variant_n_children(IntPtr value);
    [DllImport(GLib)] static extern IntPtr g_variant_get_child_value(IntPtr value, UIntPtr index);
    [DllImport(GLib)] static extern IntPtr g_variant_get_type_string(IntPtr value);
    [DllImport(GLib)] static extern int g_variant_get_int32(IntPtr value);
    [DllImport(GLib)] static extern IntPtr g_variant_get_string(IntPtr value, IntPtr length);
    [DllImport(GLib)] static extern void g_variant_unref(IntPtr value);
    [DllImport(GLib)] static extern void g_error_free(IntPtr error);

    static IntPtr bus;
    public static string Error = "";

    static string Message(IntPtr error)
    {
        if (error == IntPtr.Zero) return "unknown";
        string m = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "unknown";  // GError { domain, code, message }
        g_error_free(error); return m;
    }
    // one call: its out values (int or string), or null on an error
    static List<object> Call(string method, IntPtr arg)
    {
        if (bus == IntPtr.Zero) { bus = g_bus_get_sync(1, IntPtr.Zero, out var e); if (bus == IntPtr.Zero) { Error = "bus: " + Message(e); return null; } }
        var r = g_dbus_connection_call_sync(bus, "org.tizen.tv.hdmicec", "/org/tizen/tv/hdmicec", "org.tizen.tv.HdmiCec", method,
            g_variant_new_tuple(new[] { arg }, (UIntPtr)1), IntPtr.Zero, 0, 2000, IntPtr.Zero, out var err);
        if (r == IntPtr.Zero) { Error = method + ": " + Message(err); return null; }
        var values = new List<object>();
        try {
            for (int i = 0, n = (int)g_variant_n_children(r); i < n; i++) {
                var c = g_variant_get_child_value(r, (UIntPtr)i);
                string t = Marshal.PtrToStringAnsi(g_variant_get_type_string(c));
                values.Add(t == "i" ? g_variant_get_int32(c) : t == "s" ? Marshal.PtrToStringUTF8(g_variant_get_string(c, IntPtr.Zero)) ?? "" : null);
                g_variant_unref(c);
            }
        } finally { g_variant_unref(r); }
        return values;
    }
    static bool Ok(List<object> r, int n) => r != null && r.Count >= n && r[0] is int code && code == 0;

    // the devices the TV lists now: (HDMI port, CEC type such as PLAYBACK_DEVICE1 or AUDIO_SYSTEM); null when CEC is unreachable
    public static List<(int port, string type)> Devices()
    {
        var count = Call("GetDeviceCount", g_variant_new_boolean(1));
        if (!Ok(count, 2)) return null;
        var list = new List<(int, string)>();
        for (int i = 0; i < Math.Min((int)count[1], 15); i++) {
            var port = Call("GetDevicePortNum", g_variant_new_int32(i)); var type = Call("GetDeviceType", g_variant_new_int32(i));
            if (Ok(port, 2) && Ok(type, 2) && port[1] is int p && type[1] is string t) list.Add((p, t));
        }
        return list;
    }
    // the power answer on a port (0 on, 1 standby, 2/3 switching; -1 none). Behind a receiver it is the receiver's.
    public static int Power(int port) { var r = Call("GetDevicePowerStatusByHdmiPort", g_variant_new_int32(port)); return r != null && r.Count > 0 && r[0] is int v ? v : -1; }
}
