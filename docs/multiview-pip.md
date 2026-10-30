# S90D PiP and Multi View

**Research, not a shipped SilentGlass feature.** A temporary controller launched eight layouts on a
QN65S90DAFXZA, Tizen 9, firmware `T-PTMDAKUC-0090-REL-202605141701` (2026-10-30).
The tests used one live HDMI input and an unconnected AirPlay/mirroring pane. They do **not** establish
successful two-live-video playback, dual audio or a complete replacement for Samsung's engine.

## Capabilities and limits

| Area | Finding | Evidence |
|---|---|---|
| Layouts | Four PiP corners, equal/unequal splits, HDMI as main or inset | Live geometry + hardware video-plane checks |
| HDMI inputs | Firmware exposes **one** simultaneous HDMI receiver | Feature flags + Samsung's multi-HDMI eligibility code |
| Decode slots | UHD capability belongs to Screen2; visible size is a separate choice | Feature flag + `ScreenResourceControl` |
| Resolution/rate | Samsung lists S90D as two screens: 2K60 + 4K60 | Published specification; refresh rate not measured here |
| Preset changes | Exit/re-entry works; resending a different composition while active was ignored | Live test + relaunch-handler inspection |
| Focus and exit | External focus request followed by Select targets the HDMI pane; viewer launch exits Multi View | Live tests |
| Audio | Mixing, speaker-route and balance APIs exist; per-pane access remains unresolved | Shipped code + failed probe queries |
| Custom camera source | Worth testing, not established | App metadata alone is insufficient |

This does not establish simultaneous console + streaming-box HDMI playback. Changing a feature flag is
not evidence of another receiver. No two-HDMI bypass, EDID edit or proprietary-file replacement was attempted.

### Read-only capability discovery

Read keys with `/usr/bin/fconfigd -g com.samsung/featureconf/<suffix>`:

| Suffix | Observed value | Interpretation |
|---|---|---|
| `multiview.support` | `true` | Multi View available |
| `multiview.tv_hdmi_seperation_support` | `true` | Separate TV/HDMI viewer support; keep Samsung's spelling |
| `soc.num_of_simultaneous_supported_hdmi_rx` | `1` | One simultaneous HDMI input |
| `4hdmi_multiview.support` | `false` | No four-HDMI Multi View |
| `multiview.4k_screen_position` | `2` | `TWOSCREEN_RIGHT`: Screen2 gets UHD capability |
| `multiview.apps_support_position` | `1` | Raw firmware capability value; not a general permission to run any app |
| `multiview.dualsound` | `true` | Advertised dual-sound support, not proof our controller can route it |
| `multiview.edid_write_support` | `true` | Vendor EDID-handling capability; no manual EDID changes tested |

`MVMultiHDMIManager.IsDeviceMultiHDMISupport()` requires TV/HDMI separation **and** an HDMI count greater
than one. This build fails the second condition. All values are build-specific, not Samsung-wide defaults.

## Stock controls

- **Pane selection:** arrows select a pane; Select opens its contextual controls.
- **Content control:** keys operate the selected app/device instead of navigating panes. Samsung documents
  holding Return to return to Multi View, and holding Select to enter content selection.
- **Screen size** and **PiP position** are different submenus.
- **Maximize** retains the Multi View session; **Full screen** exits it.
- **Adding Content…** can be a waiting pane, not a second playing source. Mirroring needs a sender.

These distinctions explain interaction complexity, not a proven cause of any particular intermittent bug.
No crash, sustained frame-rate regression or two-live-stream failure was established by this investigation.

## Live layout results

All eight cases passed programmatic checks of requested geometry and the presence of a 3840×2160 HDMI
source on a hardware `VD` plane. Dimensions below are the compositor's **1920×1080 logical space**,
not the physical panel resolution. Screen1 was the waiting mobile pane; Screen2 was HDMI 3.

| Requested `style` | Measured pane | Width × height | x, y |
|---|---|---|---|
| `L_PIP_SL_TOPLEFT_LL` | Mobile inset | 568 × 320 | 56, 56 |
| `L_PIP_SL_TOPRIGHT_LL` | Mobile inset | 568 × 320 | 1296, 56 |
| `L_PIP_SL_BOTTOMLEFT_LL` | Mobile inset | 568 × 320 | 56, 704 |
| `L_PIP_SL_BOTTOMRIGHT_LL` | Mobile inset | 568 × 320 | 1296, 704 |
| `L_Screen2_DEFAULT_LL` | HDMI, equal split | 960 × 544 | 960, 268 |
| `L_Screen2_SL_LL` | HDMI, larger right | 1280 × 720 | 640, 180 |
| `L_Screen2_LS_LL` | HDMI, smaller right | 640 × 360 | 1280, 360 |
| `L_PIP_LS_BOTTOMRIGHT_LL` | HDMI inset | 568 × 320 | 1296, 704 |

`SL` means Screen1 small / Screen2 large; `LS` reverses those roles. The shipped templates also contain
other orientation/corner combinations; their presence is not a live test of every combination.

## How it works (for developers)

### Components

| Component | Observed path / role |
|---|---|
| Host | `/usr/apps/com.samsung.tv.multiscreen/bin/MultiScreen.dll`: CoBA host/lifecycle |
| UI and engine control | `/usr/apps/com.samsung.tv.coba.msview/shared/res/MSView.dll`: virtual screens, layouts, focus, audio, exit |
| Chooser | `/usr/apps/com.samsung.tv.multiviewsetupapp/bin/MultiViewSetupApp.dll`: builds source/layout requests |
| Shared launch/request API | `/usr/share/dotnet.tizen/tvappapi/Tizen.TV.Service.ScreenOnScreen.dll` |
| Status wrapper | `/usr/share/dotnet.tizen/framework/Tizen.TV.Multiview.dll`, backed by `/usr/lib/libcapi-multiview-tv.so` |
| Audio wrapper | `/usr/share/dotnet.tizen/framework/Tizen.TV.Multimedia.dll` |
| Templates | `/usr/apps/com.samsung.tv.multiscreen/res/Layout/MVLayoutPIP.json`, `MVLayout2Screen.json` |

Virtual screens and hardware video planes handle composition; the video is not copied into a UI texture.
Private Samsung APIs and execution context matter: a working owner-session call is not proof that an
ordinary third-party web app has the same privileges.

### Launch a composition

The tested owner-session route uses native `app_control_*` from `libcapi-appfw-app-control.so.0`:

1. `app_control_create`, `app_control_set_app_id`, `app_control_set_operation`.
2. `app_control_add_extra_data` for each extra below.
3. `app_control_send_launch_request`, then `app_control_destroy`.
4. Read back the actual composition; a successful send alone is insufficient.

| Field | Value |
|---|---|
| App id | `com.samsung.tv.multiscreen` |
| Operation | `http://tizen.org/appcontrol/operation/default` |
| Extra `LaunchBy` | `ExternalLaunch` |
| Extra `LaunchData` | JSON composition |

Minimal reproduction of the tested phone-inset / HDMI-main request:

```json
{
  "launchby": "MMO",
  "callerId": "com.samsung.tv.bixby-promotion",
  "presetName": "",
  "style": "L_PIP_SL_TOPRIGHT_LL",
  "mledStyle": "NONE",
  "runTime": 0,
  "changedName": false,
  "initialFocus": 2,
  "app_list": [
    {
      "zone_pos": 1,
      "app_id": "com.samsung.tv.airplay-app-2",
      "source_info": {"app_id": "", "change_source": ""},
      "deeplink": "",
      "launchParams": {}
    },
    {
      "zone_pos": 2,
      "app_id": "org.tizen.tv-viewer-sub1",
      "source_info": {
        "app_id": "org.tizen.tv-viewer-sub1",
        "change_source": "HDMI3"
      },
      "deeplink": "",
      "launchParams": {}
    }
  ]
}
```

This is a **screen-changing request**, not a read-only probe. HDMI 3 and the sub-viewer id must be checked
on the target. The JSON's `callerId` is a data field, not a grant of the named app's identity or permissions.
`ExternalRequestControl` resolves the appropriate viewer through `MVMultiHDMIManager`.

**Launch traps:**

- Samsung's `MVLauncher.LaunchMultiView(MVLaunchData)` helper sends `LaunchBy=MVSetupApp`. From the temporary
  owner process that request opened default split instead of the requested PiP. `ExternalLaunch` worked.
  `NUIApp.GetLaunchType` also considers the actual caller and `launchstyle`.
- Resending `ExternalLaunch` while active returned success but did not replace the composition. The relaunch
  handler accepts `ExternalAdd` as a separate add-source operation, not a general layout replacement.
- Exit to the viewer, then re-enter for the tested preset-switch route. HDMI resynchronizes: **smooth live
  movement/resizing is not established**.

### Focus and exit

- Focus: send app-control to the same Multi View app with `ExternalRequest=ChangeFocus` and
  `RequestData={"ScreenPosition":"Screen2"}`. This matches `MVRequester.SendRequest`,
  `MVRequestType.ChangeFocus` and `MVRequestChangeFocusData`.
- After that request, Select placed contextual controls on the HDMI inset in the live test. This is not
  evidence that every app accepts content-control input or that HDMI-CEC passthrough is fully validated.
- Exit: launch `org.tizen.tv-viewer` with the default operation. Repeatedly verified Multi View off,
  HDMI 3 restored, and a full-screen hardware video plane. Do not equate maximizing a pane with exiting.

### State and verification

| Interface | What it established / limitation |
|---|---|
| `vconftool get memory/multiscreen/info` | JSON with `mode`, `HighQuality`, `LowQuality`, `off_appid`, `focused_appid` |
| `vconftool get memory/multiscreen/state` | Transition JSON with `ver`, `begin.style`, `end.style`, `status` |
| `enlightenment_info -topvwins` | The second, EC window list exposes transformed virtual-screen geometry |
| `enlightenment_info -hwc_wins` | `VD` source dimensions and destination rectangle establish HDMI placement |

- Legacy state labels lose detail: all four mobile-inset corners reported `PIM_1_REVERSE`; the reverse
  arrangement reported `PIM_1`. Do not recover a corner from that label alone.
- HQ/LQ virtual-screen identifiers are not proven audio-zone identifiers. An empty `focused_appid` is not
  proof no pane has focus.
- UI screenshots do not contain HDMI pixels. Wait for a settled `VD` plane after source resynchronization;
  the initial samples of some cases had geometry before the plane returned.
- The one-shot owner probe's native `multiview_api_get_mode` / `multiview_api_get_current_layout_id` returned
  success with mode 0 / `FullScreen` while direct vconf readback showed active Multi View. A GLib loop did not
  resolve it; `multiview_api_add_mode_changed_cb` and `multiview_api_add_layout_changed_cb` returned `-2`.
  Treat this as an **unresolved probe/access/initialization issue**, not a demonstrated stock-firmware bug.
- Other observed native status entry points include `multiview_api_create/destroy`,
  `multiview_api_get_focused_appid`, `multiview_api_is_support_multiview` and
  `multiview_api_is_layout_pip_mode`. Exports are not evidence every call works in a third-party context.

### Audio API boundary

Samsung's `Tizen.TV.Multimedia.Audio` uses `/usr/lib/libcapi-media-sound-manager-tv.so.0`:

- `sound_manager_get_multiview_mixing_mode`
- `sound_manager_set_multiview_mixing_mode`, `sound_manager_set_multiview_mixing_mode_with_zone_id`
- `sound_manager_get_multiview_speaker_selection_with_zone_id`,
  `sound_manager_set_multiview_speaker_selection_with_zone_id`
- `sound_manager_get_multiview_volume_balance`, `sound_manager_set_multiview_volume_balance`

The owner probe read global mix mode (`0`) and receiver route (`2`), but per-zone reads failed with
`-22` / `-26607612`, both with logical virtual-screen ids and compositor resource ids. In the shipped UI,
`ScreenManager.GetRscID` resolves its own screen-model resource id before calling audio APIs. Resolve that
identity and permissions in-app; do not guess setter arguments. No audio setter or live dual-audio test ran.
Separate Bluetooth outputs and balance/mute behaviour remain unverified.

### Source eligibility and integration

- The chooser checks metadata including `multiview_support`, `multiview_support_new`,
  `multiview_min_resource` and `multiview_support_product_ratio`. These are prerequisites, not proof that
  declaring metadata makes an arbitrary camera/RTSP stream or DRM app work.
- `/home/owner/apps_rw/com.samsung.tv.multiscreen/data/disable_preloading` disables boot preloading, not
  on-demand use. The [existing cleanup](../devices/s90d/device/cleanup/multiview-apply.sh) was preserved.
- SilentGlass currently hides its overlay when a non-viewer app becomes foreground. A future Multi View
  controller needs deliberate foreground/no-picture handling and simulator tests; otherwise its wallpaper
  logic could misinterpret an active composition.
- Start with a custom preset/controller UI over the stock engine. Keep source control distinct from pane
  navigation, disclose exit/re-entry interruptions, and validate audio before exposing it.
- Still open: live casting/reconnection, two playing sources, custom camera playback, smooth in-session
  geometry changes, HDR/VRR/Game Mode interactions, soundbar/CEC behaviour and production standby/reboot soak.

## Evidence and sources

- Live owner-authorized test: temporary owner-session .NET controller; eight deduplicated layout cases checked
  programmatically against virtual-screen geometry and a live HDMI `VD` plane. Returned to normal viewing,
  then standby. No launcher/bridge deployment, app enabling, firmware flag edit or persistent service install.
- Normal stock-app use can change its caches/recent composition; this was not an application-data reset.
- Shipped assemblies were copied read-only with matching TV/local SHA-256, inspected as ARM CIL + ReadyToRun,
  and decompiled offline. Selected identities:

| Assembly | SHA-256 |
|---|---|
| `MultiScreen.dll` | `e20f3ebf09dc0acfc3c1320fd2976de28ca39b212564e0c313c36f94d4eeceac` |
| `MultiViewSetupApp.dll` | `bf9f3559ea8fdb4713da3d17b4fbdda472fd4c52cf000c3d355029e6313b209c` |
| `MSView.dll` | `1d30acb9900f6cb46deaa98752e9d7d465a8b6c618a8b1881957dd6f233f4906` |

Vendor binaries, decompiled source, raw observations and private device data are **not** distributed here.
This document records interfaces and bounded results, not original Samsung source or cross-model guarantees.

- [Samsung: Multi View controls and 2024 model limits](https://www.samsung.com/my/support/tv-audio-video/multi-view-on-your-samsung-smart-tv/)
- [Samsung: content-control mode and contextual controls](https://www.samsung.com/uk/support/tv-audio-video/how-to-use-multi-view/)
- [Samsung representative: S90D cannot show two HDMI inputs simultaneously](https://www.bestbuy.com/site/questions/samsung-77-class-s90d-series-oled-4k-uhd-smart-tizen-tv-2024/6578065/question/4ea7f3fd-1a7a-387e-af99-cfa34b763d5a)
- [Samsung developer: TVWindow PiP overlays](https://developer.samsung.com/smarttv/develop/guides/pip-picture-in-picture/showing-pip-overlays.html)
- [Official OverlayPiP sample](https://github.com/SamsungDForum/OverlayPiP): useful public TVWindow overlay example,
  not the complete modern Multi View engine or evidence of multi-HDMI support.
