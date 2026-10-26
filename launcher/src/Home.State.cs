using System;
using System.Diagnostics;
using System.IO;

// Diagnostics for the Pi-side checks: /tmp/quiet-home-state.txt. Writes are coalesced (at most one
// per 600 ms, never on the key path); OnPause flushes immediately.
public sealed partial class QuietHome
{
 const string StateFile="/tmp/quiet-home-state.txt";
 static readonly int ProcessId=Environment.ProcessId;
 Tizen.NUI.Timer stateTimer;
 string stateActivity="";
 long keyCount,keyTicksTotal,keyTicksMax,keyTicksLast;
 void RecordKeyTime(long ticks){keyCount++;keyTicksTotal+=ticks;keyTicksLast=ticks;if(ticks>keyTicksMax)keyTicksMax=ticks;}
 static string Ms(long ticks)=>(ticks*1000.0/Stopwatch.Frequency).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);
 void MarkState(string activity)
 {
  stateActivity=activity;
  if(stateTimer==null){stateTimer=new Tizen.NUI.Timer(600);stateTimer.Tick+=(s,e)=>{WriteState(stateActivity);return false;};}
  if(!stateTimer.IsRunning())stateTimer.Start();
 }
 const string StyleVersion="liquid-v25";
 void WriteState(string activity)
 {
  stateTimer?.Stop();
  var b=new System.Text.StringBuilder(DateTime.UtcNow.ToString("o"));
  void Put(string key,object value)=>b.Append('\n').Append(key).Append('=').Append(value);
  Put("page",page);Put("selected",selected);Put("area",area);Put("activity",activity);Put("key",lastKey);
  Put("font_loaded",fontLoaded);Put("style",StyleVersion);Put("build_ms",buildMilliseconds);
  Put("key_count",keyCount);Put("key_ms_last",Ms(keyTicksLast));Put("key_ms_avg",keyCount>0?Ms(keyTicksTotal/keyCount):"0");Put("key_ms_max",Ms(keyTicksMax));
  Put("library_page",libraryPage);Put("library_count",library.Count);Put("library_error",libraryError);
  Put("selected_name",selected>=0&&selected<slots.Count?slots[selected].Name:"");
  Put("artwork_cached",artworkCache.Count);Put("artwork_ready",ArtworkReadyCount());Put("artwork_pending",ArtworkPending());
  Put("artwork_views_created",artworkViewsCreated);Put("page_artwork_cold",pageArtworkCold);
  Put("process_id",ProcessId);Put("editing",editing);Put("volume",currentVolume);Put("mute",currentMute);
  foreach(var (key,id) in new[]{("brightness","brightness"),("game-mode","game-mode"),("pixel-shift","pixel-shift-menu"),("sleeptimer","sleeptimer"),
   ("sound-mode","external-sound-mode"),("soundoutput","soundoutput"),("picturemode","picturemode"),("game-mode-auto","game-mode-auto")})Put(key,SettingValue(id));
  Put("hero_ports",watchPort+","+playPort+"("+prefs.Watch+","+prefs.Play+")");Put("apps_tab",prefs.HideApps?"hidden":"shown");Put("prefs",HomeLogic.FormatPrefs(prefs).Trim().Replace('\n',','));Put("thread_switch",ThreadSwitchable()?ThreadWanted()?"on":"off":"absent");
  Put("info_markers",infoMarkers);Put("info_disabled_apps",infoDisabled);Put("info_live",lastRateText+" | "+lastCpuText);Put("info_thread",infoThread);
  Put("source",lastSource);Put("inputs",InputsState());Put("inputs_watch",inputsWatch);Put("source_list_events",sourceListEvents);
  Put("source_type_events",sourceTypeEvents);Put("sources_us",sourcesMicros);Put("inputs_open",inputsLayer!=null);
  Put("foreground",lastForeground);Put("foreground_watch",foregroundWatch);Put("ambient_returns",ambientReturns);
  Put("signal",signal);Put("signal_events",signalEvents);Put("style_conf",StyleName+"("+style.Source+")");Put("blur_created",blurSurfaces);
  Put("bench_s",style.Bench);Put("build_ms_by_page",BuildStats());Put("live",liveState);Put("live_geometry",liveGeometry);Put("live_unmute",liveUnmute);
  Put("dimmed",dimmed);Put("idle_s",(Environment.TickCount64-lastActivity)/1000);
  b.Append('\n');
  try{File.WriteAllText(StateFile,b.ToString());}catch{}
 }
}
