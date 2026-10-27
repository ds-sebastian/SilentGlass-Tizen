using System;
using System.IO;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Setting up Home Assistant from a phone (Info > Home Assistant): SilentGlass asks the bridge to open its setup page
// (data/setup-request); the bridge answers in data/setup.json with the address and a one-time code, and keeps it
// current (phone connected, saved, error). The sheet shows a QR code of the address, the code to type on the phone, and the
// state. Back closes it, and the bridge closes the page (data/setup-close). The page only works while this is open.
public sealed partial class SilentGlass
{
    View setupSheet; ImageView setupQr; TextLabel setupAddress, setupCode, setupLine; View setupLight;
    string setupState = "closed", setupUrl = ""; int qrN; Tizen.NUI.Timer setupWait;
    FileSystemWatcher dataWatch;

    // one watcher on the data directory: notifications and the setup page both arrive here from the bridge
    void WatchData()
    {
        try {
            dataWatch = new FileSystemWatcher(System.IO.Path.GetDirectoryName(DataFile("x"))) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
            void On(string name) {
                if (name == "notice.json") OnUi(() => TakeNotice("watch"));
                else if (name == "setup.json") OnUi(ReadSetup);
            }
            dataWatch.Created += (s, e) => On(e.Name); dataWatch.Changed += (s, e) => On(e.Name); dataWatch.Renamed += (s, e) => On(e.Name);
            dataWatch.EnableRaisingEvents = true;
        } catch (Exception e) { noticeState = "watch-failed:" + e.GetType().Name; }
    }

    void OpenSetup()
    {
        try { File.Delete(DataFile("setup.json")); File.WriteAllText(DataFile("setup-request"), DateTime.UtcNow.ToString("o") + "\n"); } catch { }
        ClearOverlay(); mode = Mode.Setup; Place(0, 0, 1920, 1080);
        oRoot.Add(new View { Size2D = new Size2D(1920, 1080), BackgroundColor = new Color(0, 0, 0, .55f) }); oRoot.Add(oLayer);
        const int w = 900, h = 520; int x = (1920 - w) / 2, y = (1080 - h) / 2 - 40;
        setupSheet = Glass(x, y, w, h, 44, true); oLayer.Add(setupSheet);
        var tile = new View { Position2D = new Position2D(48, 48), Size2D = new Size2D(340, 340), CornerRadius = Round(28), BackgroundColor = White }; Corners(tile, 28, 340, 340);
        setupQr = new ImageView { Position2D = new Position2D(20, 20), Size2D = new Size2D(300, 300) }; tile.Add(setupQr); setupSheet.Add(tile);
        var title = Label("Set up Home Assistant", 430, 48, w - 478, 104, 44, White, "Display"); title.MultiLine = true; setupSheet.Add(title);
        setupAddress = Label("Scan with your phone, or open", 430, 166, w - 478, 76, 27, Faint); setupAddress.MultiLine = true; setupSheet.Add(setupAddress);
        setupCode = Label("", 430, 262, w - 478, 90, 72, White, "Display"); setupSheet.Add(setupCode);
        setupLight = Dot(48, h - 40 - 34 + 10, 14, Amber); setupSheet.Add(setupLight);
        setupLine = Label("Starting…", 76, h - 40 - 34, w - 76 - 48 - 200, 34, 26, White); setupSheet.Add(setupLine);
        var close = Label("Back to close", w - 48 - 200, h - 40 - 34, 200, 34, 24, Faint); close.HorizontalAlignment = HorizontalAlignment.End; setupSheet.Add(close);
        FadeIn(setupSheet); setupState = "starting"; Note("setup");
        // the bridge answers within a second; if it does not, it is not installed or not running
        setupWait?.Stop(); setupWait = new Tizen.NUI.Timer(6000);
        setupWait.Tick += (s, e) => { if (mode == Mode.Setup && setupState == "starting") SetupLine("The Home Assistant bridge is not running on this TV.", Amber); return false; };
        setupWait.Start();
        ReadSetup();
    }
    void SetupLine(string text, Color c) { if (setupLine == null) return; setupLine.Text = text; setupLight.BackgroundColor = c; }
    void ReadSetup()
    {
        if (!overlayShown || mode != Mode.Setup || setupSheet == null) return;
        Logic.Json c;
        try { c = new Logic.Json(File.ReadAllText(DataFile("setup.json"))); } catch { return; }
        setupState = c["state"];
        string url = c["url"], code = c["code"];
        if (url.Length > 0 && code.Length == 6 && url != setupUrl) {
            setupUrl = url;
            try {
                // a new file name each time: DALi caches images by URL
                foreach (var old in Directory.GetFiles(System.IO.Path.GetDirectoryName(DataFile("x")), "setup-qr-*.svg")) File.Delete(old);
                string file = DataFile("setup-qr-" + (++qrN) + ".svg");
                // the address only: the phone always asks for the code, which is only on this screen
                File.WriteAllText(file, Qr.Svg(Qr.Encode(url)));
                setupQr.ResourceUrl = file;
            } catch (Exception e) { Note("qr-failed:" + e.GetType().Name); }
            setupAddress.Text = "Scan with your phone, or open\n" + url.Replace("http://", "").TrimEnd('/') + ", then enter";
            setupCode.Text = code.Substring(0, 3) + " " + code.Substring(3);
        }
        switch (setupState) {
            case "waiting": SetupLine("Waiting for your phone…", Amber); break;
            case "phone": SetupLine("Your phone is connected - finish there.", Live); break;
            case "saved": SetupLine(c["message"].Length > 0 ? c["message"] : "Saved.", Live); break;
            case "error": SetupLine(c["message"].Length > 0 ? c["message"] : "Something went wrong.", Amber); break;
            case "closed":  // timed out, or too many wrong codes: back to Info shortly
                SetupLine("Closed. Open it again from Info.", Grey);
                var back = new Tizen.NUI.Timer(4000);
                back.Tick += (s, e) => { if (overlayShown && mode == Mode.Setup) CloseSetup(); return false; };
                back.Start(); break;
        }
        Note("setup:" + setupState);
    }
    void CloseSetupServer()
    {
        if (setupState == "closed") return;
        setupState = "closed"; setupUrl = ""; setupWait?.Stop();
        try { File.WriteAllText(DataFile("setup-close"), DateTime.UtcNow.ToString("o") + "\n"); } catch { }
    }
    void CloseSetup() { CloseSetupServer(); OpenInfo(); rowSel = rows.FindIndex(r => r.Name == "Home Assistant"); UpdatePanel(); }
}
