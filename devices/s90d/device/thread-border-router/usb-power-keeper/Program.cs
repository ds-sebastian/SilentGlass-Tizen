using System;
using System.Runtime.InteropServices;
using System.Threading;

// Keeps the TV's USB port power on through standby, so the Thread radio (a USB device) stays up.
// In standby, storaged powers the USB ports down unless a live process has asked it not to
// (org.tizen.system.storage.BlockManager.UsbDeviceChangePortPower; storaged records the caller's pid and drops the
// request once that process is gone). Samsung's iot-hub-control asks only while the SmartThings hub is on.
// This process asks, then stays alive and asks again every few minutes (a wake can reset the request).
// Usage: quiet-otbr-usbpower [on|off|once-on|once-off]   (default: on, keep running)
static class Keeper
{
    const string Lib = "libdbus-1.so.3";
    [DllImport(Lib)] static extern void dbus_error_init(IntPtr e);
    [DllImport(Lib)] static extern void dbus_error_free(IntPtr e);
    [DllImport(Lib)] static extern int dbus_error_is_set(IntPtr e);
    [DllImport(Lib)] static extern IntPtr dbus_bus_get_private(int type, IntPtr e);
    [DllImport(Lib)] static extern void dbus_connection_set_exit_on_disconnect(IntPtr c, int exit);
    [DllImport(Lib)] static extern IntPtr dbus_message_new_method_call(string dest, string path, string iface, string method);
    [DllImport(Lib)] static extern void dbus_message_iter_init_append(IntPtr msg, IntPtr iter);
    [DllImport(Lib)] static extern int dbus_message_iter_append_basic(IntPtr iter, int type, ref int value);
    [DllImport(Lib)] static extern IntPtr dbus_connection_send_with_reply_and_block(IntPtr c, IntPtr m, int timeoutMs, IntPtr e);
    [DllImport(Lib)] static extern int dbus_message_iter_init(IntPtr m, IntPtr iter);
    [DllImport(Lib)] static extern int dbus_message_iter_get_arg_type(IntPtr iter);
    [DllImport(Lib)] static extern void dbus_message_iter_get_basic(IntPtr iter, out int value);
    [DllImport(Lib)] static extern void dbus_message_unref(IntPtr m);
    [DllImport(Lib)] static extern int dbus_connection_read_write(IntPtr c, int timeoutMs);

    static int Request(IntPtr conn, int on)
    {
        IntPtr err = Marshal.AllocHGlobal(64), iter = Marshal.AllocHGlobal(256);
        try {
            dbus_error_init(err);
            var msg = dbus_message_new_method_call("org.tizen.system.storage", "/Org/Tizen/System/Storage/Block/Manager",
                "org.tizen.system.storage.BlockManager", "UsbDeviceChangePortPower");
            dbus_message_iter_init_append(msg, iter);
            dbus_message_iter_append_basic(iter, 'i', ref on);
            var reply = dbus_connection_send_with_reply_and_block(conn, msg, 10000, err);
            dbus_message_unref(msg);
            if (dbus_error_is_set(err) != 0) {
                Log("error " + Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(err, 0)) + ": " + Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(err, IntPtr.Size)));
                dbus_error_free(err);
                return -1;
            }
            int result = int.MinValue;
            if (dbus_message_iter_init(reply, iter) != 0 && dbus_message_iter_get_arg_type(iter) == 'i') dbus_message_iter_get_basic(iter, out result);
            dbus_message_unref(reply);
            return result;
        } finally { Marshal.FreeHGlobal(err); Marshal.FreeHGlobal(iter); }
    }

    static void Log(string s) => Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " usbpower: " + s);

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "on";
        int on = mode.EndsWith("off") ? 0 : 1;
        IntPtr err = Marshal.AllocHGlobal(64);
        dbus_error_init(err);
        IntPtr conn = dbus_bus_get_private(1 /* system bus */, err);
        if (conn == IntPtr.Zero) { Log("no system bus: " + Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(err, IntPtr.Size))); return 2; }
        dbus_connection_set_exit_on_disconnect(conn, 0);
        int r = Request(conn, on);
        Log("request port power " + (on == 1 ? "on" : "off") + " -> " + (r == int.MinValue ? "ok" : r.ToString()));
        if (mode.StartsWith("once")) return r == -1 ? 1 : 0;
        for (long n = 1; ; n++) {
            // service the connection while waiting; repeat the request every 5 minutes
            for (int i = 0; i < 300; i++) { dbus_connection_read_write(conn, 0); Thread.Sleep(1000); }
            int again = Request(conn, on);
            if (again == -1 || n % 12 == 0) Log("re-request -> " + (again == int.MinValue ? "ok" : again.ToString()));
        }
    }
}
