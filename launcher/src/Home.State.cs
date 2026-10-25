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
 void WriteState(string activity)
 {
  stateTimer?.Stop();
  try{File.WriteAllText(StateFile,DateTime.UtcNow.ToString("o")+"\npage="+page+"\nselected="+selected+"\narea="+area+"\nactivity="+activity+"\nkey="+lastKey
   +"\nfont_loaded="+fontLoaded+"\nstyle=tvos-dark-v13\nbuild_ms="+buildMilliseconds
   +"\nkey_count="+keyCount+"\nkey_ms_last="+Ms(keyTicksLast)+"\nkey_ms_avg="+(keyCount>0?Ms(keyTicksTotal/keyCount):"0")+"\nkey_ms_max="+Ms(keyTicksMax)
   +"\nlibrary_page="+libraryPage+"\nlibrary_count="+library.Count+"\nlibrary_error="+libraryError
   +"\nselected_name="+(selected>=0&&selected<slots.Count?slots[selected].Name:"")
   +"\nartwork_cached="+artworkCache.Count+"\nartwork_ready="+ArtworkReadyCount()+"\nartwork_pending="+ArtworkPending()+"\nartwork_views_created="+artworkViewsCreated+"\npage_artwork_cold="+pageArtworkCold
   +"\nprocess_id="+ProcessId+"\nediting="+editing+"\nvolume="+currentVolume+"\nmute="+currentMute
   +"\nbrightness="+SettingValue("brightness")+"\ngame-mode="+SettingValue("game-mode")+"\nsleeptimer="+SettingValue("sleeptimer")+"\nsound-mode="+SettingValue("external-sound-mode")+"\nsoundoutput="+SettingValue("soundoutput")+"\npicturemode="+SettingValue("picturemode")+"\ngame-mode-auto="+SettingValue("game-mode-auto")
   +"\ninfo_markers="+infoMarkers+"\ninfo_disabled_apps="+infoDisabled+"\ninfo_live="+lastRateText+" | "+lastCpuText+"\ninfo_thread="+infoThread
   +"\nsource="+lastSource+"\ninputs="+InputsState()+"\ninputs_watch="+inputsWatch+"\nsource_list_events="+sourceListEvents+"\nsource_type_events="+sourceTypeEvents+"\nsources_us="+sourcesMicros+"\ninputs_open="+(inputsLayer!=null)+"\nhero_ports="+watchPort+","+playPort+"\nforeground="+lastForeground+"\nforeground_watch="+foregroundWatch+"\nambient_returns="+ambientReturns
   +"\ndimmed="+dimmed+"\nidle_s="+(Environment.TickCount64-lastActivity)/1000+"\nstyle_theme=dark\n");}catch{}
 }
}
