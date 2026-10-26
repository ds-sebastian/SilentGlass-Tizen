using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Liquid Glass (tvOS/iOS 26 direction) as one GPU shader per surface - no screen readback, no blur passes.
// Each glass surface shows the backdrop behind it bent toward its centre near the rim (lensing) with a slight
// colour split, a crisp specular edge lit from the top left plus a fainter glint on the opposite corner, a
// top-lit body, an analytic soft shadow (no blur buffer) and dithering against OLED banding.
// Backdrop: the photo wallpaper's pre-blurred frost texture (Home.Wallpaper.cs), or - gradient wallpaper - the
// same radial gradients evaluated mathematically.
// Kinds: 0 clear glass panel, 1 rim + shadow overlay (over artwork, video or a DALi-blurred sheet), 2 shadow only.
public sealed partial class QuietHome
{
 const string GlassVertex = @"
attribute highp vec2 aPosition;
attribute highp float aDrift;
uniform highp mat4 uMvpMatrix;
uniform highp vec3 uSize;
uniform highp float uExtent;
uniform highp float uInset;
uniform highp vec4 uPlace;
varying highp vec2 vLocal;
varying mediump vec3 vWall;
" + WallGlsl + @"
void main()
{
  // aDrift +1: corner pushed out by uExtent (shadow room); -1: pulled in by uInset (inner edge of a ring mesh)
  vLocal = aPosition*uSize.xy+sign(aPosition)*(aDrift>0.0 ? uExtent : -uInset);
  vWall = wall(uPlace.xy+uSize.xy*0.5+vLocal);  // smooth gradients: interpolating the corners is exact enough
  gl_Position = uMvpMatrix*vec4(vLocal,0.0,1.0);
}";

 // must match free-background.svg and glow-*.svg (objectBoundingBox radial gradients on 1920x1080)
 const string WallGlsl = @"
uniform highp float uGlowW;
uniform highp float uGlowP;
highp vec3 layer(highp vec3 c, highp vec2 s, highp vec2 ctr, highp vec2 rad, highp vec3 col, highp float op)
{
  highp float a = op*clamp(1.0-length((s-ctr)/rad),0.0,1.0);
  return mix(c,col,a);
}
highp vec3 wall(highp vec2 s)
{
  highp vec3 c = vec3(0.0);
  c = layer(c,s,vec2(960.0,-162.0),vec2(1632.0,918.0),vec3(0.169,0.247,0.561),0.42);
  c = layer(c,s,vec2(96.0,1134.0),vec2(1056.0,594.0),vec3(0.059,0.302,0.227),0.30);
  c = layer(c,s,vec2(1862.4,1080.0),vec2(960.0,540.0),vec3(0.290,0.137,0.400),0.28);
  if(uGlowW>0.002){
    c = layer(c,s,vec2(230.4,270.0),vec2(1248.0,702.0),vec3(0.310,0.682,0.173),0.55*uGlowW);
    c = layer(c,s,vec2(576.0,972.0),vec2(960.0,540.0),vec3(0.122,0.478,0.333),0.25*uGlowW);
  }
  if(uGlowP>0.002){
    c = layer(c,s,vec2(1632.0,216.0),vec2(1152.0,648.0),vec3(1.0,0.231,0.333),0.50*uGlowP);
    c = layer(c,s,vec2(1440.0,1026.0),vec2(960.0,540.0),vec3(0.122,0.698,1.0),0.35*uGlowP);
  }
  return c;
}
highp float dither(highp vec2 p){ return (fract(52.9829189*fract(dot(p,vec2(0.06711056,0.00583715))))-0.5)/255.0; }
";

 const string GlassFragment = @"
precision highp float;
varying highp vec2 vLocal;
varying mediump vec3 vWall;
uniform highp vec3 uSize;
uniform lowp vec4 uColor;
uniform highp vec4 uShape;   // radius, -, bevel width, kind
uniform highp vec4 uPlace;   // screen x,y of the surface's top-left, refraction px, tint
uniform highp vec4 uShadow;  // alpha, y offset, blur, extra alpha when focused
uniform highp float uFocus;  // 0..1: lifts the surface into bright glass (tvOS focus)
uniform highp float uRing;   // 0..1: bright edge ring (focus on surfaces that stay dark)
" + WallGlsl + @"
highp float sdBox(highp vec2 p, highp vec2 b, highp float r)
{
  highp vec2 q = abs(p)-b+vec2(r);
  return min(max(q.x,q.y),0.0)+length(max(q,vec2(0.0)))-r;
}
void main()
{
  highp vec2 b = uSize.xy*0.5;
  highp float r = min(uShape.x,min(b.x,b.y));
  highp float d = sdBox(vLocal,b,r);
  highp float cover = clamp(0.5-d,0.0,1.0);
  int kind = int(uShape.w+0.5);

  highp float sa = uShadow.x+uShadow.w*uFocus;
  highp float shadow = 0.0;
  if(sa>0.0&&d>-1.0){
    highp float ds = sdBox(vLocal-vec2(0.0,uShadow.y*(1.0+0.6*uFocus)),b,r);
    highp float s = 1.0-smoothstep(-0.3*uShadow.z,uShadow.z*(1.0+0.4*uFocus),ds);
    shadow = sa*s*s*(1.0-cover);
  }
  if(cover<=0.0||kind==2){ gl_FragColor = vec4(0.0,0.0,0.0,shadow*uColor.a); return; }

  highp vec2 q = abs(vLocal)-b+vec2(r);
  highp vec2 n = max(q.x,q.y)>0.0 ? sign(vLocal)*normalize(max(q,vec2(0.0))+vec2(0.0001))
                                  : (q.x>q.y ? vec2(sign(vLocal.x),0.0) : vec2(0.0,sign(vLocal.y)));
  highp float depth = -d;
  highp float e = 1.0-smoothstep(0.0,uShape.z,depth);
  highp vec2 L = vec2(-0.5547,-0.8321);
  highp float rim = 1.0-smoothstep(0.7,2.8,depth);
  highp float lit = max(dot(n,L),0.0);
  highp float back = max(dot(n,-L),0.0);
  highp float spec = rim*(0.18+0.85*lit*sqrt(lit)+0.45*back*back*back)+e*e*(0.24*lit+0.04)+e*(0.10*back*back);
  highp float ring = rim*uRing;

  if(kind==1){
    highp float a = clamp(spec*(1.0+1.2*uFocus)+rim*0.70*uFocus+ring*0.8,0.0,1.0)*cover;
    highp float outA = a+shadow*(1.0-a);
    gl_FragColor = vec4(vec3(a/max(outA,0.0001)),outA*uColor.a);
    return;
  }

  highp vec2 s = uPlace.xy+b+vLocal;
  highp vec3 bg;
  // lensing only in the bevel; the colour split only in its outer part; the flat inside uses the vertex value
  if(e>0.4){
    highp vec2 off = -n*e*e*uPlace.z;
    bg = vec3(wall(s+off*1.3).r,wall(s+off).g,wall(s+off*0.7).b);
  } else if(e>0.004) bg = wall(s-n*e*e*uPlace.z);
  else bg = vWall;
  bg *= 1.0-0.35*smoothstep(0.25,0.7,dot(bg,vec3(0.299,0.587,0.114)));
  highp float top = 1.0-clamp((vLocal.y+b.y)/uSize.y,0.0,1.0);
  highp vec3 col = mix(bg*1.2,vec3(1.0),uPlace.w*(0.7+0.6*top));
  col += spec;
  col *= 1.0-0.22*e*e*(1.0-lit);           // thick-glass edge darkens away from the light
  col = mix(col,vec3(1.0),ring*0.85);
  highp vec3 lifted = mix(vec3(0.98,0.985,0.99),vec3(0.84,0.85,0.88),1.0-top)+spec*0.35;  // bright glass, top-lit
  col = mix(col,lifted,uFocus*0.95);
  col += dither(gl_FragCoord.xy);
  gl_FragColor = vec4(col*cover/max(cover+shadow*(1.0-cover),0.0001),(cover+shadow*(1.0-cover))*uColor.a);
}";

 // photo wallpaper: the backdrop is the pre-blurred frost texture (one fetch; the flat inside is not even dependent)
 const string FrostGlsl = @"
uniform sampler2D sFrost;
highp vec3 wall(highp vec2 s){ return texture2D(sFrost,s*vec2(1.0/1920.0,1.0/1080.0)).rgb; }
highp float dither(highp vec2 p){ return (fract(52.9829189*fract(dot(p,vec2(0.06711056,0.00583715))))-0.5)/255.0; }
";
 static string FrostVertex=>GlassVertex.Replace(WallGlsl,"").Replace("varying mediump vec3 vWall;","varying highp vec2 vUV;")
  .Replace("vWall = wall(uPlace.xy+uSize.xy*0.5+vLocal);","vUV = (uPlace.xy+uSize.xy*0.5+vLocal)*vec2(1.0/1920.0,1.0/1080.0);");
 static string FrostFragment=>GlassFragment.Replace(WallGlsl,FrostGlsl).Replace("varying mediump vec3 vWall;","varying highp vec2 vUV;")
  .Replace("else bg = vWall;","else bg = texture2D(sFrost,vUV).rgb;");

 static Geometry glassQuad,glassRing;
 static PropertyBuffer Vertices(float[] v)
 {
  var fmt=new PropertyMap();fmt.Add("aPosition",new PropertyValue((int)PropertyType.Vector2));fmt.Add("aDrift",new PropertyValue((int)PropertyType.Float));
  var buf=new PropertyBuffer(fmt);
  var h=GCHandle.Alloc(v,GCHandleType.Pinned);
  try{buf.SetData(h.AddrOfPinnedObject(),(uint)(v.Length/3));}finally{h.Free();}
  return buf;
 }
 // edge band only: outer rectangle (+shadow room) minus an inner rectangle inset past the corner radius and bevel,
 // so rims and shadows never shade the inside of big cards
 static Geometry Ring()
 {
  if(glassRing!=null)return glassRing;
  float[] v={-.5f,-.5f,1, .5f,-.5f,1, .5f,.5f,1, -.5f,.5f,1,  -.5f,-.5f,-1, .5f,-.5f,-1, .5f,.5f,-1, -.5f,.5f,-1};
  var g=new Geometry();g.AddVertexBuffer(Vertices(v));
  g.SetIndexBuffer(new ushort[]{0,1,5,0,5,4, 1,2,6,1,6,5, 2,3,7,2,7,6, 3,0,4,3,4,7},24);g.SetType(Geometry.Type.TRIANGLES);
  return glassRing=g;
 }
 Shader glassShader;
 readonly List<Renderer> pageRenderers=new List<Renderer>();

 static Geometry Quad()
 {
  if(glassQuad!=null)return glassQuad;
  var g=new Geometry();g.AddVertexBuffer(Vertices(new float[]{-.5f,-.5f,1, .5f,-.5f,1, -.5f,.5f,1, .5f,.5f,1}));g.SetType(Geometry.Type.TRIANGLE_STRIP);
  return glassQuad=g;
 }
 Shader GlassShader()
 {
  if(glassShader!=null)return glassShader;
  glassShader=frostSet!=null?new Shader(FrostVertex,FrostFragment):new Shader(GlassVertex,GlassFragment);
  glassShader.RegisterProperty("uGlowW",new PropertyValue(0f));glassShader.RegisterProperty("uGlowP",new PropertyValue(0f));
  return glassShader;
 }

 // a glass renderer on `host`; screen origin is the surface's top-left (for the wallpaper behind it)
 Renderer AddGlass(View host,int kind,int x,int y,int radius,bool persistent=false,float tint=.075f,float shadow=.12f,float shadowY=6,float blur=18,float focusShadow=.16f,float bevel=22,float refract=28)
 {
  // rims and shadow-only passes use the ring mesh; glass panels need their inside too
  var r=new Renderer(kind==0?Quad():Ring(),GlassShader()){BlendMode=2};
  if(kind==0&&frostSet!=null)r.SetTextures(frostSet);
  float ext=shadow>0||focusShadow>0?blur*1.4f+shadowY*1.6f+4:2;
  r.RegisterProperty("uExtent",new PropertyValue(ext));
  r.RegisterProperty("uInset",new PropertyValue((float)Math.Max(radius,bevel)+4));
  r.RegisterProperty("uShape",new PropertyValue(new Vector4(radius,0,bevel,kind)));
  r.RegisterProperty("uPlace",new PropertyValue(new Vector4(x,y,refract,tint)));
  r.RegisterProperty("uShadow",new PropertyValue(new Vector4(shadow,shadowY,blur,focusShadow)));
  r.RegisterProperty("uFocus",new PropertyValue(0f));
  r.RegisterProperty("uRing",new PropertyValue(0f));
  host.AddRenderer(r);
  if(!persistent)pageRenderers.Add(r);
  return r;
 }
 // a child overlay that follows its parent's size (rim + shadow over artwork, video or a blurred sheet)
 // Apple-style shadows: none (or barely any) at rest, a soft low-contrast one when focused or floating
 Renderer RimOverlay(View parent,int radius,float shadow=.12f,float shadowY=6,float blur=18,bool persistent=false,float focusShadow=.16f)
 {
  var v=new View{Name="glass-rim",WidthResizePolicy=ResizePolicyType.FillToParent,HeightResizePolicy=ResizePolicyType.FillToParent};
  parent.Add(v);
  return AddGlass(v,1,0,0,radius,persistent,0,shadow,shadowY,blur,focusShadow);
 }
 void ReleasePageRenderers(){foreach(var r in pageRenderers){try{r.Dispose();}catch{}}pageRenderers.Clear();}

 // focus glow behind the heroes, as seen through the glass (gradient wallpaper only; the photo has none)
 void GlowTo(float watch,float play,bool animate)
 {
  if(glassShader==null||wallpaper!=null)return;
  if(animate){var a=FocusAnimation();a.AnimateTo(glassShader,"uGlowW",watch);a.AnimateTo(glassShader,"uGlowP",play);}
  else{glassShader.SetProperty("uGlowW",new PropertyValue(watch));glassShader.SetProperty("uGlowP",new PropertyValue(play));}
 }
 void GlassFocus(Renderer r,float focus,float ring,bool animate)
 {
  if(r==null)return;
  if(animate){var a=FocusAnimation();a.AnimateTo(r,"uFocus",focus);a.AnimateTo(r,"uRing",ring);}
  else{r.SetProperty("uFocus",new PropertyValue(focus));r.SetProperty("uRing",new PropertyValue(ring));}
 }
}
