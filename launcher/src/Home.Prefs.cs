using System;
using System.IO;

// Which HDMI input Watch and Play open (Controls page) and whether the Apps tab is shown (Info page). Survives restarts and reboots: stored in the
// app's own data directory (/tmp is cleared on reboot). 0 = Automatic (by the TV's input name).
public sealed partial class QuietHome
{
 (int WatchPort,int PlayPort,bool HideApps) prefs; // loaded in OnCreate: the app framework (data directory) does not exist yet in a field initializer
 static string PrefsPath()=>System.IO.Path.Combine(Tizen.Applications.Application.Current.DirectoryInfo.Data,"quiet-home-prefs.txt");
 static (int,int,bool) LoadPrefs(){try{return HomeLogic.ParsePrefs(File.ReadAllText(PrefsPath()));}catch{return (0,0,false);}}
 void SavePrefs(){try{File.WriteAllText(PrefsPath(),HomeLogic.FormatPrefs(prefs.WatchPort,prefs.PlayPort,prefs.HideApps));}catch(Exception ex){ShowError("Could not save: "+ex.GetType().Name);}}

 string InputName(int type){var i=inputs.Find(x=>x.Type==type);return i!=null&&i.Known&&!string.IsNullOrWhiteSpace(i.Name)?i.Name:"HDMI "+(type-12);}
 // the card subtitle: nicely cased brand name when the input is the expected device, else the TV's input name
 string HeroSubtitle(int port,string word,string pretty)
 {
  string n=InputName(port);
  return n.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0?pretty:n;
 }
 string HeroPortValue(int chosen,int resolved)=>chosen==0?"Auto · "+InputName(resolved):InputName(chosen);
 // Select cycles Automatic -> HDMI 1..4 -> Automatic (never the other card's input); Home rebuilds with the new mapping
 void CycleHeroPort(bool watch)
 {
  if(watch)prefs.WatchPort=HomeLogic.NextPort(prefs.WatchPort,playPort);else prefs.PlayPort=HomeLogic.NextPort(prefs.PlayPort,watchPort);
  SavePrefs();ResolveHeroPorts();RefreshTileValues();
  MarkState("heroport:"+(watch?"watch":"play")+":"+(watch?prefs.WatchPort:prefs.PlayPort));
 }
 // Info page switch: TV apps are backups (the SHIELD has the real ones), so the Apps tab can be hidden
 bool TabShown(int i)=>i!=1||!prefs.HideApps;
 void ToggleAppsTab()
 {
  prefs.HideApps=!prefs.HideApps;SavePrefs();BuildTabs();BuildPage(false);
  MarkState("apps_tab:"+(prefs.HideApps?"hidden":"shown"));
 }
}
