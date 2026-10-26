using System;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Photo wallpaper with day/night switching at local sunrise/sunset.
//  wall-{day,night}.jpg        1920x1080 JPEG (decoded as YUV planes on this TV: ~3 MB instead of 8 MB RGBA)
//  wall-{day,night}-frost.png  480x270, pre-blurred: the glass shader samples it (and lenses it) instead of
//                              blurring the screen - the backdrop never moves, so the blur is computed once, offline
// The images are not in git (Apple artwork); without them Quiet Home falls back to the gradient wallpaper.
// Conf: wall=photo|gradient, wallmode=auto|day|night, lat=, lon= (city level is plenty).
public sealed partial class QuietHome
{
 ImageView wallpaper;
 Texture frostTexture;TextureSet frostSet;
 bool wallNight;
 bool PhotoWall=>style.Wall=="photo"&&System.IO.File.Exists(Root+"wallpapers/wall-day.jpg")&&System.IO.File.Exists(Root+"wallpapers/wall-night.jpg");

 bool WantNight()=>style.WallMode=="night"||style.WallMode!="day"&&HomeLogic.IsNight(DateTime.UtcNow,style.Lat,style.Lon);

 View BuildWallpaper()
 {
  if(PhotoWall){
   wallNight=WantNight();
   wallpaper=new ImageView{ResourceUrl=WallUrl(wallNight),Size2D=new Size2D(1920,1080)};
   LoadFrost(wallNight);
   return wallpaper;
  }
  // gradient art: smooth, so half resolution scaled up looks the same and uses a quarter of the memory
  return new ImageView{ResourceUrl=Root+"free-background.svg",Size2D=new Size2D(1920,1080),DesiredWidth=960,DesiredHeight=540};
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
   frostTexture?.Dispose();frostTexture=tex;wallState="";
  }catch(Exception ex){wallState="-frost-failed:"+ex.GetType().Name;} // keeps the previous texture, if any
 }
 string wallState="";
 // clock tick / resume: swap at sunrise and sunset
 void CheckWallpaper()
 {
  if(wallpaper==null)return;
  bool night=WantNight();if(night==wallNight)return;
  wallNight=night;wallpaper.ResourceUrl=WallUrl(night);LoadFrost(night);
 }
 string WallName=>wallpaper==null?"gradient":(wallNight?"night":"day")+(frostTexture==null?"-nofrost":"")+wallState;
}
