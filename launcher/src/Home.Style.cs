using System;
using System.Collections.Generic;
using System.Linq;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Look and measurement tunables, read once at start from /tmp/quiet-home-live.conf (never on the key path):
//  squircle=0|1  continuous (Apple) corners          vsync=N  render every Nth vsync (1 = 60 fps; DALi's default here is 2)
//  herofade=1    unfocused heroes fade instead of darkening
//  wall=photo|gradient  wallmode=auto|day|night  lat= lon=   (Home.Wallpaper.cs)
//  bench=N       keep redrawing every frame for N s after start/resume: the worst case, for GPU/fps measurements
// Measured on the S90D while redrawing every frame (devices/s90d/bench/): flat UI 14% GPU @30 fps, 35% @60;
// Liquid Glass 54.5% @60 with the gradient backdrop, 47.6% @60 with the photo's pre-blurred frost texture.
// Real backdrop blur is used only where the backdrop moves (Inputs sheet, toast); shadows stay small (Apple-style).
public sealed partial class QuietHome
{
 sealed class StyleConf { public int Bench,Vsync=1; public bool Squircle=true,HeroFade; public string Wall="photo",WallMode="auto",Source="default"; public double Lat=40.7,Lon=-74.0; }
 StyleConf style=new StyleConf();
 static StyleConf ReadStyleConf()
 {
  var c=HomeLogic.ReadConf(LiveConfPath);
  return new StyleConf{
   Source=c.Count>0?"file":"default",Squircle=HomeLogic.Flag(c,"squircle",true),HeroFade=HomeLogic.Flag(c,"herofade",false),
   Vsync=HomeLogic.Int(c,"vsync",1,1,4),Bench=HomeLogic.Int(c,"bench",0,0,120),
   Wall=HomeLogic.Pick(c,"wall","photo","photo","gradient"),WallMode=HomeLogic.Pick(c,"wallmode","auto","auto","day","night"),
   Lat=HomeLogic.Num(c,"lat",40.7),Lon=HomeLogic.Num(c,"lon",-74.0)};
 }
 string StyleName=>"liquid+wall-"+WallName+"+vsync"+style.Vsync+(style.Squircle?"+squircle":"")+(style.HeroFade?"+herofade":"");

 // ---- corners: squircle where the corner is a corner (never on circles or capsules) ----
 static readonly Vector4 Squareness=new Vector4(.6f,.6f,.6f,.6f);
 void Corners(View v,int radius,int w,int h){if(style.Squircle&&radius>0&&radius*2<Math.Min(w,h))v.CornerSquareness=Squareness;}

 // ---- floating dark glass (Inputs sheet, toast): tint brighter at the top + a real backdrop blur, because the
 // content behind them can move. The glass rim and shadow come from a RimOverlay on top. ----
 PropertyMap frostedMap;
 int blurSurfaces;
 void Frosted(View v,int radius,int w,int h)
 {
  if(frostedMap==null){
   var stops=new PropertyArray();stops.PushBack(new PropertyValue(new Vector4(.24f,.24f,.28f,.56f)));stops.PushBack(new PropertyValue(new Vector4(.10f,.10f,.12f,.70f)));
   var offsets=new PropertyArray();offsets.PushBack(new PropertyValue(0f));offsets.PushBack(new PropertyValue(1f));
   frostedMap=new PropertyMap();
   frostedMap.Add(Visual.Property.Type,new PropertyValue((int)Visual.Type.Gradient));
   frostedMap.Add(GradientVisualProperty.StartPosition,new PropertyValue(new Vector2(0,-.5f)));
   frostedMap.Add(GradientVisualProperty.EndPosition,new PropertyValue(new Vector2(0,.5f)));
   frostedMap.Add(GradientVisualProperty.StopOffset,new PropertyValue(offsets));
   frostedMap.Add(GradientVisualProperty.StopColor,new PropertyValue(stops));
   frostedMap.Add(Visual.Property.CornerRadius,new PropertyValue(new Vector4(radius,radius,radius,radius)));
  }
  v.Background=frostedMap;v.CornerRadius=new Vector4(radius,radius,radius,radius);
  try{v.SetRenderEffect(RenderEffect.CreateBackgroundBlurEffect(40));blurSurfaces++;}catch{}
  Corners(v,radius,w,h);
 }

 // ---- measurements: page build and key times are recorded here; fps and GPU busy are read from outside ----
 readonly Dictionary<int,long> buildMsByPage=new Dictionary<int,long>();
 void BenchRender(){if(style.Bench>0){try{Window.Default.KeepRendering(style.Bench);}catch{}}}
 string BuildStats()=>string.Join(",",buildMsByPage.Select(kv=>kv.Key+":"+kv.Value));
}
