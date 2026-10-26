using System;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Photo wallpaper with day/night switching at local sunrise/sunset.
//  wall-{day,night}.jpg        1920x1080 JPEG (decoded as YUV planes on this TV: ~3 MB instead of 8 MB RGBA)
//  wall-{day,night}-frost.png  480x270, pre-blurred: the glass shader samples it (and lenses it) instead of
//                              blurring the screen - the backdrop never moves, so the blur is computed once, offline
// The images are not in git (Apple artwork); without them Quiet Home falls back to the gradient wallpaper.
// Conf: wall=photo|gradient|shader, wallmode=auto|day|night, lat=, lon= (city level is plenty).
public sealed partial class QuietHome
{
 ImageView wallpaper;
 Texture frostTexture;TextureSet frostSet;
 bool wallNight;
 bool PhotoWall=>style.Wall=="photo"&&System.IO.File.Exists(Root+"wallpapers/wall-day.jpg")&&System.IO.File.Exists(Root+"wallpapers/wall-night.jpg");

 // sunrise/sunset in minutes after UTC midnight (NOAA approximation, within a couple of minutes)
 internal static (double rise,double set) SunTimes(DateTime utc,double lat,double lon)
 {
  double g=2*Math.PI/365*(utc.DayOfYear-1);
  double eq=229.18*(0.000075+0.001868*Math.Cos(g)-0.032077*Math.Sin(g)-0.014615*Math.Cos(2*g)-0.040849*Math.Sin(2*g));
  double de=0.006918-0.399912*Math.Cos(g)+0.070257*Math.Sin(g)-0.006758*Math.Cos(2*g)+0.000907*Math.Sin(2*g)-0.002697*Math.Cos(3*g)+0.00148*Math.Sin(3*g);
  double la=lat*Math.PI/180,c=Math.Cos(90.833*Math.PI/180)/(Math.Cos(la)*Math.Cos(de))-Math.Tan(la)*Math.Tan(de);
  double ha=Math.Acos(Math.Max(-1,Math.Min(1,c)))*180/Math.PI;
  double M(double m)=>((m%1440)+1440)%1440;
  return (M(720-4*(lon+ha)-eq),M(720-4*(lon-ha)-eq));
 }
 // UTC based, so the TV's time zone setting does not matter
 internal static bool IsNight(DateTime utc,double lat,double lon)
 {
  var (rise,set)=SunTimes(utc,lat,lon);double now=utc.Hour*60+utc.Minute;
  bool day=rise<set?now>=rise&&now<set:now>=rise||now<set; // the day can wrap past UTC midnight
  return !day;
 }
 bool WantNight()=>style.WallMode=="night"||style.WallMode!="day"&&IsNight(DateTime.UtcNow,style.Lat,style.Lon);

 View BuildWallpaper()
 {
  if(Liquid&&style.Wall=="shader")return ProceduralWallpaper();
  if(PhotoWall){
   wallNight=WantNight();
   wallpaper=new ImageView{ResourceUrl=WallUrl(wallNight),Size2D=new Size2D(1920,1080)};
   LoadFrost(wallNight);
   return wallpaper;
  }
  // gradient art: smooth, so half resolution scaled up looks the same and uses a quarter of the memory
  var v=new ImageView{ResourceUrl=Root+"free-background.svg",Size2D=new Size2D(1920,1080),DesiredWidth=960,DesiredHeight=540};
  return v;
 }
 string WallUrl(bool night)=>Root+"wallpapers/wall-"+(night?"night":"day")+".jpg";
 void LoadFrost(bool night)
 {
  try{
   using var pixels=ImageLoader.LoadImageFromFile(Root+"wallpapers/wall-"+(night?"night":"day")+"-frost.png");
   using var data=PixelBuffer.Convert(pixels);
   var tex=new Texture(TextureType.TEXTURE_2D,data.GetPixelFormat(),data.GetWidth(),data.GetHeight());
   tex.Upload(data);
   frostSet??=new TextureSet();
   frostSet.SetTexture(0,tex);   // every glass renderer shares this set, so they all switch at once
   frostTexture?.Dispose();frostTexture=tex;
  }catch(Exception ex){frostTexture=null;wallState="frost-failed:"+ex.GetType().Name;}
 }
 string wallState="";
 // clock tick / resume: swap at sunrise and sunset
 void CheckWallpaper()
 {
  if(wallpaper==null)return;
  bool night=WantNight();if(night==wallNight)return;
  wallNight=night;wallpaper.ResourceUrl=WallUrl(night);LoadFrost(night);
 }
 string WallName=>wallpaper==null?(style.Wall=="shader"&&Liquid?"shader":"gradient"):(wallNight?"night":"day")+(frostTexture==null?"-nofrost":"")+wallState;
}
