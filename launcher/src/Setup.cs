using System;
using System.IO;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Advanced settings from a phone (Info > Advanced settings): SilentGlass asks the bridge to open its setup page
// (setup-request); the bridge answers in setup.json with the address and a one-time code and keeps it current (phone
// connected, saved, error). The sheet shows a QR code of the address, the code to type on the phone, and the state.
// Back closes it, and the bridge closes the page (setup-close). The page only works while the sheet is open.
public sealed partial class SilentGlass
{
    View setupSheet; ImageView setupQr; TextLabel setupAddress, setupCode, setupLine; View setupLight;
    string setupState = "closed", setupUrl = "", setupSeen = ""; int qrN, setupGen;
    FileSystemWatcher dataWatch;
    static string DataDir => System.IO.Path.GetDirectoryName(DataFile("x"));

    // one watcher on the data directory: notifications, the setup page and settings saved from the phone arrive here
    void WatchData()
    {
        try {
            dataWatch = new FileSystemWatcher(DataDir) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
            void On(string name) {
                if (name == Shared.NoticeFile) OnUi(() => TakeNotice("watch"));
                else if (name == Shared.SetupFile) OnUi(ReadSetup);
                else if (name == Shared.PrefsFile || name == Shared.LocationFile) OnUi(PrefsChanged);
            }
            dataWatch.Created += (s, e) => On(e.Name); dataWatch.Changed += (s, e) => On(e.Name); dataWatch.Renamed += (s, e) => On(e.Name);
            dataWatch.EnableRaisingEvents = true;
        } catch (Exception e) { noticeState = "watch-failed:" + e.GetType().Name; }
    }

    void OpenSetup()
    {
        try { File.Delete(DataFile(Shared.SetupFile)); File.WriteAllText(DataFile(Shared.SetupRequest), DateTime.UtcNow.ToString("o") + "\n"); } catch { }
        ClearOverlay(); mode = Mode.Setup; Place(0, 0, 1920, 1080);
        oRoot.Add(new View { Size2D = new Size2D(1920, 1080), BackgroundColor = new Color(0, 0, 0, .55f) }); oRoot.Add(oLayer);
        const int w = 900, h = 520, tx = 430, tw = w - tx - 48, foot = h - 40 - 34; int x = (1920 - w) / 2, y = (1080 - h) / 2 - 40;
        setupSheet = Glass(x, y, w, h, 44, true); oLayer.Add(setupSheet);
        var tile = new View { Position2D = new Position2D(48, 48), Size2D = new Size2D(340, 340), CornerRadius = Round(28), BackgroundColor = White }; Corners(tile, 28, 340, 340);
        setupQr = new ImageView { Position2D = new Position2D(20, 20), Size2D = new Size2D(300, 300) }; tile.Add(setupQr); setupSheet.Add(tile);
        var title = Label("Advanced settings", tx, 48, tw, 104, 44, White, "Display"); title.MultiLine = true; setupSheet.Add(title);
        setupAddress = Label("Scan with your phone, or open", tx, 166, tw, 76, 27, Faint); setupAddress.MultiLine = true; setupSheet.Add(setupAddress);
        setupCode = Label("", tx, 262, tw, 90, 72, White, "Display"); setupSheet.Add(setupCode);
        setupLight = Dot(48, foot + 10, 14, Amber); setupSheet.Add(setupLight);
        setupLine = Label("Starting…", 76, foot, w - 76 - 48 - 200, 34, 26, White); setupSheet.Add(setupLine);
        var close = Label("Back to close", w - 48 - 200, foot, 200, 34, 24, Faint); close.HorizontalAlignment = HorizontalAlignment.End; setupSheet.Add(close);
        FadeIn(setupSheet); setupState = "starting"; setupUrl = setupSeen = ""; Note("setup");
        // the bridge answers within a second; if it does not, it is not installed or not running
        int g = ++setupGen;
        After(6000, () => { if (g == setupGen && mode == Mode.Setup && setupState == "starting") SetupLine("The SilentGlass service is not running on this TV.", Amber); });
        ReadSetup();
    }
    void SetupLine(string text, Color c) { if (setupLine == null) return; setupLine.Text = text; setupLight.BackgroundColor = c; }
    void ReadSetup()
    {
        if (!overlayShown || mode != Mode.Setup || setupSheet == null) return;
        Logic.Json c;
        try { c = new Logic.Json(File.ReadAllText(DataFile(Shared.SetupFile))); } catch { return; }
        string state = c["state"], url = c["url"], code = c["code"], message = c["message"], seen = state + "|" + url + code + "|" + message;
        if (seen == setupSeen) return;  // the watcher reports one write several times
        setupSeen = seen; setupState = state;
        if (url.Length > 0 && code.Length == 6 && url + code != setupUrl) {
            setupUrl = url + code;
            try {
                // a new file name each time: DALi caches images by URL
                foreach (var old in Directory.GetFiles(DataDir, "setup-qr-*.svg")) File.Delete(old);
                string file = DataFile("setup-qr-" + (++qrN) + ".svg");
                // the address only: the phone always asks for the code, which is only on this screen. A new ?s= each
                // time, so the phone loads the page fresh instead of switching to an old tab with the same address
                string session = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
                File.WriteAllText(file, Qr.Svg(Qr.Encode(url + "?s=" + session)));
                setupQr.ResourceUrl = file;
            } catch (Exception e) { Note("qr-failed:" + e.GetType().Name); }
            setupAddress.Text = "Scan with your phone, or open\n" + url.Replace("http://", "").TrimEnd('/') + ", then enter";
            setupCode.Text = code.Substring(0, 3) + " " + code.Substring(3);
        }
        switch (state) {
            case "waiting": SetupLine("Waiting for your phone…", Amber); break;
            case "phone": SetupLine("Your phone is connected - finish there.", Live); break;
            case "saved": SetupLine(message.Length > 0 ? message : "Saved.", Live); break;
            case "error": SetupLine(message.Length > 0 ? message : "Something went wrong.", Amber); break;
            case "closed":  // timed out, or too many wrong codes: back to Info shortly
                SetupLine("Closed. Open it again from Info.", Grey);
                int g = setupGen;
                After(4000, () => { if (g == setupGen && overlayShown && mode == Mode.Setup) CloseSetup(); });
                break;
        }
        Note("setup:" + state);
    }
    void CloseSetupServer()
    {
        if (setupState == "closed") return;
        setupState = "closed"; setupUrl = setupSeen = ""; setupGen++;
        try { File.WriteAllText(DataFile(Shared.SetupClose), DateTime.UtcNow.ToString("o") + "\n"); } catch { }
    }
    void CloseSetup() { CloseSetupServer(); OpenInfo(); rowSel = rows.FindIndex(r => r.Name == "Advanced settings"); UpdatePanel(); }
    // the phone saved SilentGlass's settings (or the location); our own saves land here too and change nothing
    void PrefsChanged()
    {
        string before = Logic.FormatPrefs(prefs);
        LoadPrefs();
        if (Logic.FormatPrefs(prefs) == before) return;
        ResolvePorts(); CheckWallpaper();
        if (!overlayShown) return;
        if (mode == Mode.Bar) OpenBar(true); else if (mode == Mode.Info) UpdatePanel();
    }
}
