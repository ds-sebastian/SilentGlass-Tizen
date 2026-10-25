using System;
using System.Collections.Generic;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Apple-style surfaces (tvOS 26 "Liquid Glass" direction) and the measurements used to judge their cost.
//  glass    0 = flat fills (pre-v18 look)  1 = glass tint + lit edge; real backdrop blur only where the backdrop moves
//           (Inputs sheet, toast)          2 = real backdrop blur on every glass surface (cost comparison only)
//  shadow   small focus-only drop shadow (tvOS lift) - off by default: barely visible on the OLED-black wallpaper
//           and the one measurable cost (see below)
// Measured on the S90D (GPU busy while redrawing every frame, whole GPU; 30 fps in every mode):
//   flat 16-18%  |  glass+squircle 20%  |  +focus shadows 23%  |  +always-on soft shadows 27-28%
//   real blur on every glass surface 25% (DALi caches the blurred backdrop while it is static)
// Memory (RSS/PSS/GPU) and key handling were unchanged within noise.
//  squircle Apple's continuous corners instead of circular arcs
// Read once at start from /tmp/quiet-home-live.conf (same file as the live-preview tunables), never on the key path.
public sealed partial class QuietHome
{
 sealed class StyleConf { public int Glass=1,Bench=0; public bool Shadow=false,Squircle=true; public string Source="default"; }
 StyleConf style=new StyleConf();
 static StyleConf ReadStyleConf()
 {
  var c=new StyleConf();
  try{
   if(!System.IO.File.Exists(LiveConfPath))return c;
   c.Source="file";
   foreach(var line in System.IO.File.ReadAllLines(LiveConfPath)){
    var kv=line.Split('=');if(kv.Length!=2)continue;string k=kv[0].Trim(),v=kv[1].Trim();
    if(k=="glass"&&int.TryParse(v,out int g))c.Glass=Math.Max(0,Math.Min(2,g));
    else if(k=="shadow")c.Shadow=v=="1";
    else if(k=="squircle")c.Squircle=v=="1";
    else if(k=="bench"&&int.TryParse(v,out int sec))c.Bench=Math.Max(0,Math.Min(120,sec));
   }
  }catch(Exception ex){c.Source="error:"+ex.GetType().Name;}
  return c;
 }
 string StyleName=>"glass"+style.Glass+(style.Shadow?"+shadow":"")+(style.Squircle?"+squircle":"");

 // ---- corners: squircle where the corner is a corner (never on circles or capsules) ----
 static readonly Vector4 Squareness=new Vector4(.6f,.6f,.6f,.6f);
 void Corners(View v,int radius,int w,int h){if(style.Squircle&&radius>0&&radius*2<Math.Min(w,h))v.CornerSquareness=Squareness;}

 // ---- glass: a light tint brighter at the top (light catching the surface) and a fine lit rim ----
 PropertyMap GlassMap(int radius,bool dark)
 {
  var stops=new PropertyArray();
  if(dark){stops.PushBack(new PropertyValue(new Vector4(.22f,.22f,.26f,.80f)));stops.PushBack(new PropertyValue(new Vector4(.11f,.11f,.13f,.88f)));}
  else{stops.PushBack(new PropertyValue(new Vector4(1,1,1,.17f)));stops.PushBack(new PropertyValue(new Vector4(1,1,1,.06f)));}
  var offsets=new PropertyArray();offsets.PushBack(new PropertyValue(0f));offsets.PushBack(new PropertyValue(1f));
  var m=new PropertyMap();
  m.Add(Visual.Property.Type,new PropertyValue((int)Visual.Type.Gradient));
  m.Add(GradientVisualProperty.StartPosition,new PropertyValue(new Vector2(0,-.5f)));
  m.Add(GradientVisualProperty.EndPosition,new PropertyValue(new Vector2(0,.5f)));
  m.Add(GradientVisualProperty.StopOffset,new PropertyValue(offsets));
  m.Add(GradientVisualProperty.StopColor,new PropertyValue(stops));
  m.Add(Visual.Property.CornerRadius,new PropertyValue(new Vector4(radius,radius,radius,radius)));
  return m;
 }
 readonly Dictionary<(int,bool),PropertyMap> glassMaps=new Dictionary<(int,bool),PropertyMap>();
 static readonly Color Rim=new Color(1,1,1,.22f);
 int blurSurfaces;
 // creation: tint, rim, corners and (when the backdrop can change, or glass=2) a real GPU backdrop blur
 void Glass(View v,int radius,int w,int h,bool movingBackdrop,Color flat,bool dark=false)
 {
  if(style.Glass==0){v.BackgroundColor=flat;return;}
  GlassFill(v,radius,dark);
  if(movingBackdrop||style.Glass==2){try{v.SetRenderEffect(RenderEffect.CreateBackgroundBlurEffect(movingBackdrop?60:40));blurSurfaces++;}catch{}}
  Corners(v,radius,w,h);
 }
 // restyle (focus moved away): cached map, no new effect
 void GlassFill(View v,int radius,bool dark=false)
 {
  if(style.Glass==0){v.BackgroundColor=tileFill;return;}
  if(!glassMaps.TryGetValue((radius,dark),out var m))glassMaps[(radius,dark)]=m=GlassMap(radius,dark);
  v.Background=m;v.CornerRadius=new Vector4(radius,radius,radius,radius);
  v.BorderlineWidth=1.5f;v.BorderlineColor=Rim;
 }

 // ---- shadows: tvOS lift, on the focused card only. Measured on the S90D: soft shadows are the one costly
 // part of the look (always-on hero shadows at blur 36-70 took GPU busy from ~17% to ~27% while redrawing), so
 // they are small, focus-only, and off unless shadow=1. ----
 Shadow heroFocus,tileFocus;
 void ApplyShadow(Slot s,bool focused)
 {
  if(!style.Shadow)return;
  heroFocus??=new Shadow(32,new Color(0,0,0,.55f),new Vector2(0,16));
  tileFocus??=new Shadow(24,new Color(0,0,0,.5f),new Vector2(0,10));
  try{s.Card.BoxShadow=focused?(s.Kind==1?heroFocus:tileFocus):null;}catch{}
 }

 // ---- measurements ----
 // bench=N (seconds): keep redrawing every frame for N s after start/resume - the worst case for glass, since
 // every backdrop blur re-runs each frame. Frame rate and GPU busy time are read from outside (compositor,
 // Mali DVFS counters); page build and key handling times are recorded here.
 readonly Dictionary<int,long> buildMsByPage=new Dictionary<int,long>();
 void BenchRender(){if(style.Bench>0){try{Window.Default.KeepRendering(style.Bench);}catch{}}}
 string BuildStats(){var parts=new List<string>();foreach(var kv in buildMsByPage)parts.Add(kv.Key+":"+kv.Value);return string.Join(",",parts);}
}
