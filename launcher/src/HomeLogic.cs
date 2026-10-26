using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

// Pure Quiet Home logic with no NUI types, so tests/CatalogTests.cs can run it off the TV:
// the runtime conf file, Watch/Play input choices, the live video rectangle and sunrise/sunset.
public static class HomeLogic
{
 // ---- /tmp/quiet-home-live.conf: key=value lines; missing or unreadable = defaults ----
 public static Dictionary<string,string> ReadConf(string path)
 {
  var d=new Dictionary<string,string>();
  try{if(File.Exists(path))d=ParseConf(File.ReadAllText(path));}catch{}
  return d;
 }
 public static Dictionary<string,string> ParseConf(string text)
 {
  var d=new Dictionary<string,string>();
  foreach(var line in (text??"").Split('\n')){int i=line.IndexOf('=');if(i>0)d[line.Substring(0,i).Trim()]=line.Substring(i+1).Trim();}
  return d;
 }
 public static bool Flag(Dictionary<string,string> c,string key,bool fallback)=>c.TryGetValue(key,out var v)?v=="1":fallback;
 public static int Int(Dictionary<string,string> c,string key,int fallback,int min,int max)=>
  c.TryGetValue(key,out var v)&&int.TryParse(v,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)?Math.Clamp(n,min,max):fallback;
 public static double Num(Dictionary<string,string> c,string key,double fallback)=>
  c.TryGetValue(key,out var v)&&double.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n)?n:fallback;
 public static string Pick(Dictionary<string,string> c,string key,string fallback,params string[] allowed)=>
  c.TryGetValue(key,out var v)&&Array.IndexOf(allowed,v)>=0?v:fallback;

 // ---- Watch / Play inputs: 0 = Automatic (by the TV's input name), else an HDMI source type 13-16 ----
 public const int Hdmi1=13,Hdmi4=16;
 public static bool IsHdmi(int type)=>type>=Hdmi1&&type<=Hdmi4;
 // user choices made on the TV (Controls and Info pages); unknown or missing keys keep their defaults, so older
 // files load unchanged
 public sealed class Prefs
 {
  public int Watch,Play;          // 0 = Automatic
  public bool HideApps,Live=true,Dim=true,Overlay=true;
  public string Wall="auto";      // auto (sunrise/sunset) | day | night
 }
 public static readonly string[] WallModes={"auto","day","night"};
 public static Prefs ParsePrefs(string text)
 {
  var c=ParseConf(text);
  int Port(string k)=>c.TryGetValue(k,out var v)&&int.TryParse(v,out int n)&&(n==0||IsHdmi(n))?n:0;
  return new Prefs{Watch=Port("watch"),Play=Port("play"),HideApps=Flag(c,"hideapps",false),Live=Flag(c,"live",true),Overlay=Flag(c,"overlay",true),Dim=Flag(c,"dim",true),Wall=Pick(c,"wall","auto",WallModes)};
 }
 public static string FormatPrefs(Prefs p)=>
  "watch="+p.Watch+"\nplay="+p.Play+"\nhideapps="+(p.HideApps?1:0)+"\nlive="+(p.Live?1:0)+"\noverlay="+(p.Overlay?1:0)+"\ndim="+(p.Dim?1:0)+"\nwall="+p.Wall+"\n";
 public static string NextWall(string mode)=>WallModes[(Array.IndexOf(WallModes,mode)+1)%WallModes.Length];
 // tab bar Left/Right over Home 0, Apps 1, Controls 2, Info 3: a hidden Apps tab is skipped, the ends stop
 public static int NextTab(int page,int d,bool hideApps){int n=page+d;if(hideApps&&n==1)n+=d;return n<0||n>3?page:n;}
 // Select cycles Automatic -> HDMI 1..4 -> Automatic, skipping the input the other card already opens
 public static int NextPort(int current,int otherCard)
 {
  int n=current;
  do n=n==0?Hdmi1:n>=Hdmi4?0:n+1; while(n!=0&&n==otherCard);
  return n;
 }

 // ---- live preview: the smallest 16:9 rectangle covering the card, centred on it, plus `bleed` px per side.
 // The bleed sits behind the card's own artwork, so a plane a pixel short never shows a dark edge. ----
 public static (int x,int y,int w,int h) VideoRect(int x,int y,int w,int h,int bleed)
 {
  if(w*9>=h*16){int vh=w*9/16;y-=(vh-h)/2;h=vh;}
  else{int vw=h*16/9;x-=(vw-w)/2;w=vw;}
  return (x-bleed,y-bleed,w+2*bleed,h+2*bleed);
 }

 // ---- sunrise / sunset in minutes after UTC midnight (NOAA approximation, within a couple of minutes) ----
 public static (double rise,double set) SunTimes(DateTime utc,double lat,double lon)
 {
  double g=2*Math.PI/365*(utc.DayOfYear-1);
  double eq=229.18*(0.000075+0.001868*Math.Cos(g)-0.032077*Math.Sin(g)-0.014615*Math.Cos(2*g)-0.040849*Math.Sin(2*g));
  double de=0.006918-0.399912*Math.Cos(g)+0.070257*Math.Sin(g)-0.006758*Math.Cos(2*g)+0.000907*Math.Sin(2*g)-0.002697*Math.Cos(3*g)+0.00148*Math.Sin(3*g);
  double la=lat*Math.PI/180,c=Math.Cos(90.833*Math.PI/180)/(Math.Cos(la)*Math.Cos(de))-Math.Tan(la)*Math.Tan(de);
  double ha=Math.Acos(Math.Clamp(c,-1,1))*180/Math.PI;
  static double M(double m)=>((m%1440)+1440)%1440;
  return (M(720-4*(lon+ha)-eq),M(720-4*(lon-ha)-eq));
 }
 // UTC based, so the TV's time zone setting cannot break it; the day may wrap past UTC midnight
 public static bool IsNight(DateTime utc,double lat,double lon)
 {
  var (rise,set)=SunTimes(utc,lat,lon);double now=utc.Hour*60+utc.Minute;
  return !(rise<set?now>=rise&&now<set:now>=rise||now<set);
 }
}
