using System;
using System.Collections.Generic;
using System.Globalization;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Home over the picture (tvOS 26 direction: "keep the focus on what's playing"). When Home is pressed while an input
// has a picture, that input keeps playing full screen and SilentGlass draws only what is needed over it:
//  dock      one glass tray at the bottom: the Watch/Play inputs, Inputs / Controls / Info, and - Apps tab on - a
//            second, smaller row of apps in the same tray
//  Controls  a Control Center column on the right; the picture stays in view, so brightness, contrast and picture
//            mode can be judged while they change
//  Info      the photo wallpaper at 92% over the picture: text-heavy, so nearly opaque (Apple's "regular" glass)
// How: the window stays a normal app window, made transparent with the wm.tvs.win.use.tvs hint, and a full-screen
// FLUX VideoCanvasView sits at the very back - the live-preview mechanism at 1920x1080 instead of card size.
// The video plane is separate hardware: it cannot be blurred, so glass over it is a dark tint plus the rim shader,
// and a soft dimming band keeps text legible (Apple: dim bright content behind clear glass, ~35%).
// Off switches: Info "Home over video", or overlay=0 in /tmp/quiet-home-live.conf. Any failure falls back to the
// full-screen Home.
public sealed partial class QuietHome
{
 bool over;
 View wallLayer,veil;
 VideoCanvasViewHolder backView;
 Tizen.NUI.Timer backTimer;
 long resumedAt;
 int veilKind=-2;
 string overState="off";
 bool WantOverlay()=>prefs.Overlay&&style.Overlay&&CurrentHasSignal&&page!=1;

 static void Vis(View v,bool on){if(on)v.Show();else v.Hide();}
 void EnsureLiveWindow()
 {
  if(liveWindow)return;
  if(!tvsHintSet){tvsHint=Window.Default.AddAuxiliaryHint("wm.tvs.win.use.tvs","1");tvsHintSet=true;}
  else Window.Default.SetAuxiliaryHintValue(tvsHint,"1");
  Window.Default.SetTransparency(true);liveWindow=true;
 }
 // full-screen picture behind everything; re-asserted later because the viewer can reset the plane after a re-sync
 void ShowVideoBack()
 {
  if(backView!=null){PushBack();return;}
  HideLive();
  try{
   EnsureLiveWindow();
   tvWindow??=new Tizen.TV.Broadcast.TVWindowControl.TVWindowControlAPI();
   var canvas=new Tizen.TV.FLUX.VideoCanvasView{Size2D=new Size2D(1920,1080),Position2D=new Position2D(0,0)};
   Window.Default.Add(canvas);canvas.LowerToBottom();
   canvas.DisableAutoUpdateVideoGeometry=true;
   canvas.SetPlayer(tvWindow,Tizen.TV.FLUX.VideoCanvasView.VideoSyncMode.None);
   backView=new VideoCanvasViewHolder{View=canvas};
   PushBack();UnmuteLive();
   backTimer?.Stop();backTimer=new Tizen.NUI.Timer(250);backTimer.Tick+=(s,e)=>{PushBack();UnmuteLive();return false;};backTimer.Start();
   overState="on";
  }catch(Exception ex){overState="failed:"+ex.GetType().Name+":"+ex.Message;DropVideoBack();}
 }
 void PushBack()
 {
  try{(backView?.View as Tizen.TV.FLUX.VideoCanvasView)?.ForceUpdateVideoGeometry(0,0,1920,1080);}
  catch(Exception ex){overState="geometry:"+ex.GetType().Name;}
 }
 void DropVideoBack()
 {
  backTimer?.Stop();
  if(backView==null)return;
  try{Window.Default.Remove(backView.View);backView.View.Dispose();}catch{}
  backView=null;
 }
 // full screen <-> over the picture; true when the presentation changed (the caller rebuilds the page)
 bool SetOverlay(bool on)
 {
  if(on)ShowVideoBack();          // also re-attaches after a pause dropped it
  if(on&&backView==null)on=false; // no picture could be attached: full screen instead
  if(on==over)return false;
  over=on;Vis(tabBar,!on);
  if(!on){DropVideoBack();SetVeil(-1);Vis(wallLayer,true);wallLayer.Opacity=1;}
  MarkState(on?"overlay_on":"overlay_off");
  return true;
 }
 void PresentOverlayPage()
 {
  bool info=page==3;
  Vis(wallLayer,info);wallLayer.Opacity=info?.92f:1;
  SetVeil(page==0?0:page==2?1:-1);
 }
 // dimming behind the glass: bottom band for the dock (plus a little at the top for the clock), right edge for Controls
 void SetVeil(int kind)
 {
  if(kind==veilKind)return;veilKind=kind;
  if(veil!=null){Window.Default.Remove(veil);veil.Dispose();veil=null;}
  if(kind<0)return;
  veil=new View{Size2D=new Size2D(1920,1080)};
  var stops=new PropertyArray();var offsets=new PropertyArray();
  var pts=kind==0?new[]{(0f,.30f),(.14f,0f),(.48f,0f),(.70f,.36f),(1f,.70f)}:new[]{(0f,0f),(.42f,0f),(.64f,.42f),(1f,.68f)};
  foreach(var (o,a) in pts){offsets.PushBack(new PropertyValue(o));stops.PushBack(new PropertyValue(new Vector4(0,0,0,a)));}
  var m=new PropertyMap();
  m.Add(Visual.Property.Type,new PropertyValue((int)Visual.Type.Gradient));
  m.Add(GradientVisualProperty.StartPosition,new PropertyValue(kind==0?new Vector2(0,-.5f):new Vector2(-.5f,0)));
  m.Add(GradientVisualProperty.EndPosition,new PropertyValue(kind==0?new Vector2(0,.5f):new Vector2(.5f,0)));
  m.Add(GradientVisualProperty.StopOffset,new PropertyValue(offsets));
  m.Add(GradientVisualProperty.StopColor,new PropertyValue(stops));
  veil.Background=m;
  Window.Default.Add(veil);veil.LowerToBottom();
  backView?.View.LowerToBottom();
 }
 // back from an app or the viewer: re-attach the picture (pausing dropped it), or go full screen if it is gone
 void ResumeOverlay(){if(over||page==0&&WantOverlay()){if(SetOverlay(WantOverlay())){if(over){page=0;selected=DockFocus();}BuildPage(false);}}}
 // Home or Back on the dock: hand the screen straight back to the picture (same source: no re-sync)
 void Dismiss(){MarkState("overlay_dismiss");Launch("org.tizen.tv-viewer");}
 int DockFocus(){int i=slots.FindIndex(s=>s.Action=="hdmi:"+lastSource);return i<0?0:i;}

 // ---- clear glass over video: a dark tint (the plane can't be blurred) + the rim shader; bright glass on focus ----
 (View fill,Renderer rim) VideoGlass(View card,int w,int h,int radius,float tint=.40f)
 {
  card.BackgroundColor=new Color(.10f,.10f,.12f,tint);
  var fill=new View{WidthResizePolicy=ResizePolicyType.FillToParent,HeightResizePolicy=ResizePolicyType.FillToParent,
   BackgroundColor=new Color(.965f,.97f,.98f,1),CornerRadius=new Vector4(radius,radius,radius,radius),Opacity=0};
  Corners(fill,radius,w,h);card.Add(fill);
  return (fill,RimOverlay(card,radius,.10f,6,18,focusShadow:.24f));
 }

 // ---- dock ----
 const int TileW=288,TileH=162,Btn=112,AppW=216,AppH=122,DockPad=36,DockBottom=64,DockGap=40,MaxDockApps=7;
 void BuildDock()
 {
  var apps=prefs.HideApps?new List<LibraryApp>():library.FindAll(a=>!a.Utility);
  int nApps=Math.Min(apps.Count,MaxDockApps);
  int row0=2*TileW+DockGap+56+3*Btn+2*DockGap,row1=nApps>0?nApps*AppW+(nApps-1)*24:0;
  int trayW=Math.Max(row0,row1)+2*DockPad,trayH=DockPad+TileH+(nApps>0?28+AppH:0)+DockPad;
  int tx=(1920-trayW)/2,ty=1080-DockBottom-trayH;
  var tray=Panel(tx,ty,trayW,trayH,new Color(.09f,.09f,.11f,.34f),44);
  RimOverlay(tray,44,.16f,10,30,focusShadow:0);
  int x=(1920-row0)/2,y=ty+DockPad;
  DockInput(watchPort,"feature-watch-wide.svg",x,y);x+=TileW+DockGap;
  DockInput(playPort,"feature-play-wide.svg",x,y);x+=TileW+56;
  int by=y+(TileH-(Btn+8+30))/2;
  RoundButton("Inputs","inputs","inputs",x,by,0);x+=Btn+DockGap;
  RoundButton("Controls","page:2","settings",x,by,0);x+=Btn+DockGap;
  RoundButton("Info","page:3","info",x,by,0);
  if(nApps>0){int ax=(1920-row1)/2,ay=y+TileH+28;for(int i=0;i<nApps;i++)DockApp(apps[i],ax+i*(AppW+24),ay);}
  UpdateHeroBadges(false);
 }
 void DockInput(int port,string art,int x,int y)
 {
  var card=Card(x,y,TileW,TileH,new Color(0,0,0,1),24);
  card.Add(new ImageView{ResourceUrl=Root+art,Size2D=new Size2D(TileW,TileH),CornerRadius=new Vector4(24,24,24,24)});
  var name=Label(InputName(port),22,TileH-58,TileW-44,44,28,white,"Semibold");name.Ellipsis=false;card.Add(name);
  // status dot, no words (same meaning as on the full-screen cards)
  var badge=new View{Position2D=new Position2D(14,14),Size2D=new Size2D(30,30),BackgroundColor=new Color(0,0,0,.45f),CornerRadius=new Vector4(15,15,15,15),Opacity=0};
  var dot=new View{Position2D=new Position2D(8,8),Size2D=new Size2D(14,14),CornerRadius=new Vector4(7,7,7,7)};badge.Add(dot);card.Add(badge);
  var rim=RimOverlay(card,24,0,10,22,focusShadow:.3f);
  slots.Add(new Slot{Card=card,Rim=rim,Title=name,Name=InputName(port),Action="hdmi:"+port,Row=0,X=x,Y=y,W=TileW,H=TileH,Kind=3,Badge=badge,BadgeDot=dot});
 }
 // round glass button with its name underneath (always shown: no guessing what a glyph means)
 void RoundButton(string name,string action,string glyph,int x,int y,int row)
 {
  var card=Card(x,y,Btn,Btn,clear,Btn/2);
  var (fill,rim)=VideoGlass(card,Btn,Btn,Btn/2);
  var g=new ImageView{ResourceUrl=Root+"glyph-"+glyph+".svg",Position2D=new Position2D(28,28),Size2D=new Size2D(56,56)};card.Add(g);
  var label=Text(name,x-40,y+Btn+8,Btn+80,30,22,muted,"Semibold");label.HorizontalAlignment=HorizontalAlignment.Center;
  slots.Add(new Slot{Card=card,Rim=rim,Fill=fill,Glyph=g,Title=label,Name=name,Action=action,Row=row,X=x,Y=y,W=Btn,H=Btn,Kind=4});
 }
 void DockApp(LibraryApp app,int x,int y)
 {
  var card=Card(x,y,AppW,AppH,HexColor(app.Background),18);
  AddAppArtwork(card,app,AppW,AppH);
  var rim=RimOverlay(card,18,0,8,18,focusShadow:.3f);
  slots.Add(new Slot{Card=card,Rim=rim,Name=app.Name,Action="app:"+app.Id,Row=1,X=x,Y=y,W=AppW,H=AppH,Kind=3});
 }

 // ---- Control Center: a column on the right, the picture stays in view ----
 const int CcX=1180,CcW=680,CcH=104,CcGap=18; // half tiles 331 wide: "Movie (Calibrated)" fits without clipping
 void BuildCenter()
 {
  RefreshSettings();
  int y=128,half=(CcW-CcGap)/2,x2=CcX+half+CcGap,row=0;
  CcTile("Brightness","brightness","brightness",CcX,y,CcW,row++,true);y+=CcH+CcGap;
  CcTile("Contrast","contrast","contrast",CcX,y,CcW,row++,true);y+=CcH+CcGap;
  CcTile("Picture Mode","cycle:picturemode","picture",CcX,y,half,row,false);CcTile("Game Mode","game-mode","game",x2,y,half,row++,false);y+=CcH+CcGap;
  CcTile("Volume","volume","volume",CcX,y,CcW,row++,true);y+=CcH+CcGap;
  CcTile("Sound","output","output",CcX,y,half,row,false);CcTile("Sound Mode","cycle:external-sound-mode","soundmode",x2,y,half,row++,false);y+=CcH+CcGap;
  CcTile("Sleep Timer","sleeptimer","sleep",CcX,y,half,row,false);CcTile("Mute","mute","mute",x2,y,half,row++,false);y+=CcH+CcGap+4;
  int step=(CcW-4*Btn)/3;
  RoundButton("Ambient","ambient","ambient",CcX,y,row);
  RoundButton("Inputs","inputs","inputs",CcX+Btn+step,y,row);
  RoundButton("All Controls","full:2","more",CcX+2*(Btn+step),y,row);
  RoundButton("TV Settings","app:org.tizen.menu","settings",CcX+3*(Btn+step),y,row);
  RefreshTileValues();
 }
 // capsule tile: glyph disc, name, value; level tiles (full width) add a bar that Left/Right moves while editing
 void CcTile(string name,string action,string glyph,int x,int y,int w,int row,bool level)
 {
  var card=Card(x,y,w,CcH,clear,CcH/2);
  var (fill,rim)=VideoGlass(card,w,CcH,CcH/2);
  var disc=new View{Position2D=new Position2D(18,18),Size2D=new Size2D(68,68),BackgroundColor=discFill,CornerRadius=new Vector4(34,34,34,34)};
  var g=new ImageView{ResourceUrl=Root+"glyph-"+glyph+".svg",Position2D=new Position2D(12,12),Size2D=new Size2D(44,44)};disc.Add(g);card.Add(disc);
  var title=Label(name,102,level?14:16,w-(level?320:130),38,24,white,"Semibold");title.Ellipsis=false;card.Add(title);
  TextLabel value;View bar=null;int barW=0;
  if(level){
   value=Label("",w-230,14,194,38,26,white,"Display");value.HorizontalAlignment=HorizontalAlignment.End;
   barW=w-102-36;
   var track=new View{Position2D=new Position2D(102,68),Size2D=new Size2D(barW,8),CornerRadius=new Vector4(4,4,4,4),BackgroundColor=new Color(1,1,1,.22f)};
   bar=new View{Size2D=new Size2D(0,8),CornerRadius=new Vector4(4,4,4,4),BackgroundColor=white};track.Add(bar);card.Add(track);
  }
  else value=Label("",102,52,w-122,34,21,muted);
  value.Ellipsis=false;card.Add(value);
  slots.Add(new Slot{Card=card,Rim=rim,Fill=fill,Glyph=g,Disc=disc,Title=title,Label=value,Bar=bar,BarW=barW,Name=name,Action=action,Row=row,X=x,Y=y,W=w,H=CcH,Kind=5});
 }
 // 0..1 for the level bars; -1 = unknown
 float TileLevel(string a)
 {
  if(a=="volume")return currentVolume<0?-1:currentVolume/100f;
  if(a=="brightness"||a=="contrast"){
   var (mn,mx,_)=Range(a);
   if(mx>mn&&double.TryParse(SettingValue(a),NumberStyles.Float,CultureInfo.InvariantCulture,out var n))return (float)((n-mn)/(mx-mn));
  }
  return -1;
 }
 // focus look for the overlay kinds: 3 artwork tile, 4 round button, 5 capsule tile
 void StyleOverlaySlot(Slot s,bool f,bool animate,ref float scale,ref float lift)
 {
  scale=f?(s.Kind==5?1.035f:1.08f):1;lift=f&&s.Kind==3?-4:0;
  GlassFocus(s.Rim,f?1:0,0,animate);
  if(s.Fill!=null)FadeTo(s.Fill,f?.94f:0,animate);
  if(s.Glyph!=null)s.Glyph.Color=f?dark:white;
  if(s.Disc!=null)s.Disc.BackgroundColor=f?discFocus:discFill;
  if(s.Kind==4)s.Title.TextColor=f?white:muted;
  if(s.Kind==5){
   s.Title.TextColor=f?dark:white;
   s.Label.TextColor=s.Bar!=null?(f?dark:white):(f?valueFocus:muted);
   if(s.Bar!=null){s.Bar.BackgroundColor=f?dark:white;if(s.Bar.GetParent() is View track)track.BackgroundColor=f?new Color(0,0,0,.14f):new Color(1,1,1,.22f);}
  }
 }
}
