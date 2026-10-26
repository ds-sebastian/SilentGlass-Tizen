using System;
using System.IO;

// User choices that must survive restarts and reboots: which HDMI input Watch and Play open.
// Stored in the app's own data directory (/tmp is cleared on reboot). 0 = Automatic (by the TV's input name).
public sealed partial class QuietHome
{
 sealed class Prefs { public int WatchPort,PlayPort; }
 Prefs prefs=LoadPrefs();
 static string PrefsPath()
 {
  try{return System.IO.Path.Combine(Tizen.Applications.Application.Current.DirectoryInfo.Data,"quiet-home-prefs.txt");}
  catch{return "/tmp/quiet-home-prefs.txt";}
 }
 static Prefs LoadPrefs()
 {
  var p=new Prefs();
  try{
   var path=PrefsPath();if(!File.Exists(path))return p;
   foreach(var line in File.ReadAllLines(path)){
    var kv=line.Split('=');if(kv.Length!=2||!int.TryParse(kv[1].Trim(),out int v)||(v!=0&&(v<13||v>16)))continue;
    if(kv[0].Trim()=="watch")p.WatchPort=v;else if(kv[0].Trim()=="play")p.PlayPort=v;
   }
  }catch{}
  return p;
 }
 void SavePrefs(){try{File.WriteAllText(PrefsPath(),"watch="+prefs.WatchPort+"\nplay="+prefs.PlayPort+"\n");}catch(Exception ex){ShowError("Could not save: "+ex.GetType().Name);}}

 static string HdmiName(int type)=>"HDMI "+(type-12);
 string InputName(int type){var i=inputs.Find(x=>x.Type==type);return i!=null&&i.Known&&!string.IsNullOrWhiteSpace(i.Name)?i.Name:HdmiName(type);}
 // the card subtitle: nicely cased brand name when the input is the expected device, else the TV's input name
 string HeroSubtitle(int port,string word,string pretty)
 {
  string n=InputName(port);
  return n.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0?pretty:n;
 }
 string HeroPortValue(int chosen,int resolved)=>chosen==0?"Auto · "+InputName(resolved):InputName(chosen);
 // Select cycles Automatic -> HDMI 1 -> ... -> HDMI 4 -> Automatic; Home rebuilds with the new mapping
 void CycleHeroPort(bool watch)
 {
  int cur=watch?prefs.WatchPort:prefs.PlayPort;
  int next=cur==0?13:cur>=16?0:cur+1;
  if(watch)prefs.WatchPort=next;else prefs.PlayPort=next;
  SavePrefs();ResolveHeroPorts();RefreshTileValues();
  MarkState("heroport:"+(watch?"watch":"play")+":"+next);
 }
}
