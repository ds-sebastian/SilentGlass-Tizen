using System;
using System.Collections.Generic;
using System.Globalization;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Home over the picture (tvOS 26: "keep the focus on what's playing"). When Home is pressed while the TV viewer shows
// an input with a picture, nothing about the picture changes: the viewer stays in front and keeps playing, and
// SilentGlass shows a second, partial, transparent window above it - a notification-level window (the app holds
// http://tizen.org/privilege/window.priority.set), the kind of window Vetro's dock uses. The full-screen window is
// hidden meanwhile, so the viewer is never covered, paused or re-synced, and nothing touches the video plane.
//  bar       one glass capsule at the bottom: Watch and Play (the device each opens underneath), Inputs, Controls,
//            Apps (unless hidden), Info
//  Inputs    a menu anchored above its bar item
//  Apps      the TV's apps floating above the bar (their own menu, so the bar stays short)
//  Controls  a Control Center panel on the right; the picture stays visible on the left while it changes
//  Info, All Controls  the full-screen pages; Back returns to the bar
// The video plane cannot be blurred, so the glass over it is a dark translucent material with a hairline edge and a
// soft dimming gradient behind it (Apple's guidance for clear glass over bright media). No shader rims.
// Layout numbers mirror tools/overlay_mockup.py, which renders the same screens on the Pi.
// Off: Info "Home over video", or overlay=0 in /tmp/quiet-home-live.conf.
public sealed partial class QuietHome
{
 Window ow;View oRoot,oDim,oLayer,oHighlight;
 bool oShown,mainHidden,fromOverlay,freshStart=true; // freshStart: the first Home after launch may find the viewer in front
 int oMode,oSel=-1,oSub; // mode: 0 bar, 1 inputs, 2 apps, 3 controls
 string oState="off",lastOther="";
 Animation oAnimation;Tizen.NUI.Timer oIdle;
 const int BandTop=240,BarH=104,BarR=52,BarBottom=52,ItemH=80,ItemPad=30,GlyphS=40,GlyphGap=14,LabelPx=30;
 const int PopGap=18,MenuRowH=88,PopW=560,PopR=36,CcX=1236,CcW=624,CcR=44,SliderH=88,ValueH=64;
 const int AppW=256,AppH=144,AppGap=24,AppCols=4,MaxApps=12,IdleOverlayMs=45000;
 const string TvViewer="org.tizen.tv-viewer",Self="com.samsung.tv.bixby-promotion";
 readonly Color glassTop=new Color(.15f,.15f,.17f,.70f),glassBottom=new Color(.115f,.115f,.13f,.66f),edge=new Color(1,1,1,.16f);
 readonly Color focusFill=new Color(1,1,1,.95f),focusInk=new Color(.11f,.11f,.12f,1),live=new Color(.19f,.82f,.35f,1),clearColor=new Color(0,0,0,0);

 sealed class OItem { public View Box,Dot,Track,Fill,Switch,Knob; public ImageView Glyph,Check; public TextLabel Label,Value; public string Action; public int X,Y,W,H,Kind; }
 readonly List<OItem> bar=new List<OItem>(),rows=new List<OItem>();

 // ---- when ----
 // the viewer is the app in front (the last non-SilentGlass foreground), it has a picture, and the full-screen Home
 // is not what is on screen right now
 bool OverlayWanted(){ReadSignal();return prefs.Overlay&&style.Overlay&&lastOther==TvViewer&&CurrentHasSignal&&(mainHidden||!visible||freshStart);}
 void NoteForeground(string id){if(!string.IsNullOrEmpty(id)&&id!=Self)lastOther=id;}

 // ---- the two windows ----
 void HideMain(){if(mainHidden)return;HideLive();try{Window.Default.Hide();}catch{}mainHidden=true;visible=false;}
 void ShowMain(){if(!mainHidden)return;mainHidden=false;try{Window.Default.Show();Window.Default.Raise();}catch{}}
 bool EnsureOverlay()
 {
  if(ow!=null)return true;
  try{
   ow=new Window("SilentGlass Overlay",new Rectangle(0,BandTop,1920,1080-BandTop),true);
   ow.Hide();
   ow.Type=WindowType.Notification;
   bool level=ow.SetNotificationLevel(NotificationLevel.Top);
   ow.AddAuxiliaryHint("wm.policy.win.user.geometry","1"); // keep our geometry (not full screen)
   ow.AddAuxiliaryHint("wm.comp.win.effect.enable","0");
   ow.BackgroundColor=clearColor;
   ow.SetAcceptFocus(true);
   ow.KeyEvent+=OnOverlayKey;
   oRoot=new View{Size2D=new Size2D(1920,1080)};ow.Add(oRoot);
   oState="ready:level="+level;return true;
  }catch(Exception ex){oState="failed:"+ex.GetType().Name+":"+ex.Message;ow=null;return false;}
 }
 // the window covers only what is drawn; content is laid out in screen coordinates, shifted by the window origin
 void PlaceOverlay(int x,int y,int w,int h){try{ow.WindowPositionSize=new Rectangle(x,y,w,h);}catch{}oRoot.Position2D=new Position2D(-x,-y);}

 void ShowOverlay(string activity)
 {
  freshStart=false;
  if(!EnsureOverlay()){FullHome(activity);return;}
  HideMain();CloseInputs(false);editing="";
  oSel=-1;OpenBar(true);
  ow.Show();ow.Raise();try{ow.Activate();}catch{}
  oShown=true;OverlayActivity();MarkState(activity+":overlay");
 }
 void HideOverlay(string activity)
 {
  if(!oShown)return;
  oShown=false;oIdle?.Stop();StopAnimation(ref oAnimation);
  try{ow.Hide();}catch{}
  ClearLayer();
  if(activity!=null)MarkState(activity);
 }
 // Info / All Controls from the bar: the full-screen page; Back comes back to the bar
 void OpenFull(int p)
 {
  HideOverlay(null);fromOverlay=true;
  page=p;area=1;selected=0;editing="";ShowMain();BuildPage(false);MarkState("overlay_full:"+p);
 }
 void ReturnToOverlay(){fromOverlay=false;HideMain();ShowOverlay("overlay_return");}
 void OverlayActivity()
 {
  if(oIdle==null){oIdle=new Tizen.NUI.Timer(5000);oIdle.Tick+=(s,e)=>{if(oShown&&Environment.TickCount64-lastActivity>=IdleOverlayMs){HideOverlay("overlay_idle");return false;}return oShown;};}
  lastActivity=Environment.TickCount64;if(!oIdle.IsRunning())oIdle.Start();
 }

 // ---- building blocks ----
 void ClearLayer()
 {
  bar.Clear();rows.Clear();oHighlight=null;
  if(oLayer!=null){oRoot.Remove(oLayer);oLayer.Dispose();oLayer=null;}
  if(oDim!=null){oRoot.Remove(oDim);oDim.Dispose();oDim=null;}
 }
 View OAdd(View v){oLayer.Add(v);return v;}
 static PropertyMap Gradient(bool horizontal,float r,params (float at,Color c)[] stops)
 {
  var s=new PropertyArray();var o=new PropertyArray();
  foreach(var (at,c) in stops){o.PushBack(new PropertyValue(at));s.PushBack(new PropertyValue(new Vector4(c.R,c.G,c.B,c.A)));}
  var m=new PropertyMap();
  m.Add(Visual.Property.Type,new PropertyValue((int)Visual.Type.Gradient));
  m.Add(GradientVisualProperty.StartPosition,new PropertyValue(horizontal?new Vector2(-.5f,0):new Vector2(0,-.5f)));
  m.Add(GradientVisualProperty.EndPosition,new PropertyValue(horizontal?new Vector2(.5f,0):new Vector2(0,.5f)));
  m.Add(GradientVisualProperty.StopOffset,new PropertyValue(o));
  m.Add(GradientVisualProperty.StopColor,new PropertyValue(s));
  if(r>0)m.Add(Visual.Property.CornerRadius,new PropertyValue(new Vector4(r,r,r,r)));
  return m;
 }
 // dark translucent glass with a hairline edge and a faint top light: one view, no shader
 View Glass(int x,int y,int w,int h,int r)
 {
  var v=new View{Position2D=new Position2D(x,y),Size2D=new Size2D(w,h),CornerRadius=new Vector4(r,r,r,r),BorderlineWidth=1.5f,BorderlineColor=edge,BorderlineOffset=-1};
  v.Background=Gradient(false,r,(0f,glassTop),(.4f,glassBottom),(1f,glassBottom));Corners(v,r,w,h);
  return OAdd(v);
 }
 // soft dimming behind the glass: vertical for the bar, horizontal for the Controls panel
 void Dim(bool horizontal,int x,int y,int w,int h,float max)
 {
  oDim=new View{Position2D=new Position2D(x,y),Size2D=new Size2D(w,h)};
  oDim.Background=Gradient(horizontal,0,(0f,clearColor),(1f,new Color(0,0,0,max)));
  oRoot.Add(oDim);oDim.LowerToBottom();
 }
 ImageView Sym(string name,int x,int y,int s){return new ImageView{ResourceUrl=Root+"sym-"+name+".svg",Position2D=new Position2D(x,y),Size2D=new Size2D(s,s),DesiredWidth=s*2,DesiredHeight=s*2};}
 static int TextWidth(TextLabel l){try{return (int)Math.Ceiling(l.NaturalSize.Width);}catch{return l.Text.Length*16;}}
 View LiveDot(int x,int y,int s){return new View{Position2D=new Position2D(x,y),Size2D=new Size2D(s,s),CornerRadius=new Vector4(s/2f,s/2f,s/2f,s/2f),BackgroundColor=live};}
 void Fade(View v,int fromY)
 {
  StopAnimation(ref oAnimation);
  int y=v.Position2D.Y;v.Opacity=0;v.Position2D=new Position2D(v.Position2D.X,y+fromY);
  oAnimation=new Animation(170){DefaultAlphaFunction=Ease()};
  oAnimation.AnimateTo(v,"Opacity",1f);oAnimation.AnimateTo(v,"PositionY",(float)y);oAnimation.Play();
 }

 // ---- bar ----
 int BarTop=>1080-BarBottom-BarH;
 static string PortGlyph(int type,int watch,int play)=>type==watch?"tv":type==play?"gamecontroller":type==0?"antenna":"hdmi";
 View capsule;
 void OpenBar(bool animate)
 {
  ClearLayer();oMode=0;PlaceOverlay(0,BandTop,1920,1080-BandTop);
  RefreshSource();RefreshInputs();ResolveHeroPorts();ReadSignal();
  oLayer=new View{Size2D=new Size2D(1920,1080)};oRoot.Add(oLayer);
  Dim(false,0,BandTop+180,1920,1080-BandTop-180,.5f);
  // Watch / Play keep their names; the device each opens (the TV's input name) is the line underneath
  var items=new List<(string glyph,string label,string sub,string action)>{
   (PortGlyph(watchPort,watchPort,playPort),"Watch",InputName(watchPort),"hdmi:"+watchPort),(PortGlyph(playPort,watchPort,playPort),"Play",InputName(playPort),"hdmi:"+playPort),
   (null,null,null,null),("inputs","Inputs",null,"inputs"),("controls","Controls",null,"controls")};
  if(!prefs.HideApps&&library.Exists(a=>!a.Utility))items.Add(("apps","Apps",null,"apps"));
  items.Add(("info","Info",null,"info"));
  // measure the labels, then centre the capsule
  var labels=new List<(TextLabel title,TextLabel sub,int text)>();int total=24;
  foreach(var it in items){
   if(it.glyph==null){labels.Add((null,null,0));total+=22;continue;}
   var l=Label(it.label,0,0,10,ItemH,LabelPx,white,"Semibold");l.Ellipsis=false;
   TextLabel s=null;if(it.sub!=null){s=Label(it.sub,0,0,10,28,21,white,"Medium");s.Ellipsis=false;}
   int text=Math.Max(TextWidth(l),s==null?0:TextWidth(s)+(HasPicture(it.action)?22:0));
   labels.Add((l,s,text));total+=2*ItemPad+GlyphS+GlyphGap+text;
  }
  int x=960-total/2;
  capsule=Glass(x,BarTop,total,BarH,BarR);
  oHighlight=new View{Size2D=new Size2D(10,ItemH),BackgroundColor=focusFill,CornerRadius=new Vector4(ItemH/2f,ItemH/2f,ItemH/2f,ItemH/2f)};capsule.Add(oHighlight);
  int ix=12;
  for(int i=0;i<items.Count;i++){
   var it=items[i];
   if(it.glyph==null){capsule.Add(new View{Position2D=new Position2D(ix+10,(BarH-40)/2),Size2D=new Size2D(2,40),BackgroundColor=new Color(1,1,1,.22f)});ix+=22;continue;}
   var (l,s,text)=labels[i];int w=2*ItemPad+GlyphS+GlyphGap+text,tx=ix+ItemPad+GlyphS+GlyphGap;
   var g=Sym(it.glyph,ix+ItemPad,(BarH-GlyphS)/2,GlyphS);capsule.Add(g);
   View d=null;
   if(s==null){l.Position2D=new Position2D(tx,(BarH-ItemH)/2);l.Size2D=new Size2D(text+4,ItemH);capsule.Add(l);}
   else{
    l.Position2D=new Position2D(tx,19);l.Size2D=new Size2D(text+4,38);capsule.Add(l);
    s.Position2D=new Position2D(tx,55);s.Size2D=new Size2D(TextWidth(s)+4,28);capsule.Add(s);
    if(HasPicture(it.action)){d=LiveDot(tx+TextWidth(s)+10,55+8,12);capsule.Add(d);}
   }
   bar.Add(new OItem{Glyph=g,Label=l,Value=s,Dot=d,Action=it.action,X=ix,Y=(BarH-ItemH)/2,W=w,H=ItemH});
   ix+=w;
  }
  if(oSel<0||oSel>=bar.Count)oSel=Math.Max(0,bar.FindIndex(b=>b.Action=="hdmi:"+lastSource));
  FocusBar(false);
  if(animate)Fade(capsule,24);
 }
 bool HasPicture(string action)=>action=="hdmi:"+lastSource&&CurrentHasSignal;
 // the focused item is a white capsule; while one of its menus is open the item stays marked in faint white
 void FocusBar(bool animate)
 {
  for(int i=0;i<bar.Count;i++){
   bool f=i==oSel&&oMode==0;bar[i].Label.TextColor=f?focusInk:white;bar[i].Glyph.Color=f?focusInk:white;
   if(bar[i].Value!=null)bar[i].Value.TextColor=f?new Color(.11f,.11f,.12f,.6f):new Color(1,1,1,.62f);
  }
  if(oHighlight==null||oSel<0||oSel>=bar.Count)return;
  var b=bar[oSel];oHighlight.BackgroundColor=oMode==0?focusFill:new Color(1,1,1,.18f);
  if(animate){var a=new Animation(150){DefaultAlphaFunction=Ease()};a.AnimateTo(oHighlight,"PositionX",(float)b.X);a.AnimateTo(oHighlight,"SizeWidth",(float)b.W);a.Finished+=(s,e)=>a.Dispose();a.Play();}
  else{oHighlight.Position2D=new Position2D(b.X,b.Y);oHighlight.Size2D=new Size2D(b.W,b.H);}
 }
 int BarItemCentre(string action){var b=bar.Find(i=>i.Action==action);return b==null?960:capsule.Position2D.X+b.X+b.W/2;}
 void ActivateBar()
 {
  var a=bar[oSel].Action;
  if(a.StartsWith("hdmi:")){int src=int.Parse(a.Substring(5));HideOverlay("overlay_input:"+src);if(src!=lastSource)SelectSource(src);return;}
  if(a=="inputs"){OpenInputsMenu();return;}
  if(a=="apps"){OpenAppsMenu();return;}
  if(a=="controls"){OpenControls();return;}
  if(a=="info")OpenFull(3);
 }

 // ---- Inputs menu, anchored above its bar item ----
 List<InfoStats.InputSource> menuInputs=new List<InfoStats.InputSource>();
 void OpenInputsMenu()
 {
  oMode=1;FocusBar(false);rows.Clear();
  menuInputs=inputs;int h=24+menuInputs.Count*MenuRowH;
  int x=Math.Clamp(BarItemCentre("inputs")-PopW/2,40,1880-PopW),y=BarTop-PopGap-h;
  var pop=Glass(x,y,PopW,h,PopR);
  for(int i=0;i<menuInputs.Count;i++){
   var s=menuInputs[i];bool cur=s.Type==lastSource,off=s.Known&&!s.Connected&&!cur;
   var r=new View{Position2D=new Position2D(12,12+i*MenuRowH),Size2D=new Size2D(PopW-24,MenuRowH),CornerRadius=new Vector4(26,26,26,26),Opacity=off?.45f:1};
   var g=Sym(PortGlyph(s.Type,watchPort,playPort),22,(MenuRowH-GlyphS)/2,GlyphS);r.Add(g);
   bool sub=s.Name!=s.Port;
   var name=Label(s.Name,80,sub?10:0,PopW-24-80-110,sub?44:MenuRowH,30,white,"Semibold");name.Ellipsis=false;r.Add(name);
   TextLabel port=null;if(sub){port=Label(s.Port,80,50,300,30,22,new Color(1,1,1,.6f));r.Add(port);}
   View dot=null;if(cur&&CurrentHasSignal){dot=LiveDot(PopW-24-92,(MenuRowH-12)/2,12);r.Add(dot);}
   ImageView check=null;if(cur){check=Sym("check",PopW-24-62,(MenuRowH-34)/2,34);r.Add(check);}
   pop.Add(r);rows.Add(new OItem{Box=r,Glyph=g,Label=name,Value=port,Dot=dot,Check=check,Action="src:"+s.Type});
  }
  oSub=Math.Max(0,menuInputs.FindIndex(i=>i.Type==lastSource));FocusRows();Fade(pop,16);MarkState("overlay_inputs");
 }
 void FocusRows()
 {
  for(int i=0;i<rows.Count;i++){
   var r=rows[i];bool f=i==oSub;
   r.Box.BackgroundColor=f?focusFill:clearColor;
   var ink=f?focusInk:white;r.Label.TextColor=ink;r.Glyph.Color=ink;
   if(r.Check!=null)r.Check.Color=ink;
   if(r.Value!=null)r.Value.TextColor=r.Kind==0&&r.Track!=null?ink:f?new Color(.11f,.11f,.12f,.62f):new Color(1,1,1,.62f);
   if(r.Track!=null){r.Track.BackgroundColor=f?new Color(0,0,0,.14f):new Color(1,1,1,.22f);r.Fill.BackgroundColor=ink;}
  }
 }

 // ---- Apps: their own menu; artwork floats above the bar (no panel behind it) ----
 List<LibraryApp> menuApps=new List<LibraryApp>();
 void OpenAppsMenu()
 {
  oMode=2;FocusBar(false);rows.Clear();
  menuApps=library.FindAll(a=>!a.Utility);if(menuApps.Count>MaxApps)menuApps.RemoveRange(MaxApps,menuApps.Count-MaxApps);
  int n=menuApps.Count,lines=(n+AppCols-1)/AppCols,cols=Math.Min(AppCols,n);
  int w=cols*AppW+(cols-1)*AppGap,h=lines*(AppH+50)+(lines-1)*18;
  int x0=960-w/2,y0=BarTop-PopGap-20-h;
  oDim.Position2D=new Position2D(0,BandTop);oDim.Size2D=new Size2D(1920,1080-BandTop); // dim further up for the names
  var grid=OAdd(new View{Size2D=new Size2D(1920,1080)});
  for(int i=0;i<n;i++){
   var app=menuApps[i];int x=x0+(i%AppCols)*(AppW+AppGap),y=y0+(i/AppCols)*(AppH+50+18);
   var card=new View{Position2D=new Position2D(x+AppW/2,y+AppH/2),Size2D=new Size2D(AppW,AppH),PivotPoint=new Position(.5f,.5f,.5f),PositionUsesPivotPoint=true,
    BackgroundColor=HexColor(app.Background),CornerRadius=new Vector4(20,20,20,20),BorderlineColor=white};
   Corners(card,20,AppW,AppH);card.Add(NewArtwork(app,AppW,AppH,20));
   var name=Label(app.Name,x-20,y+AppH+10,AppW+40,34,22,new Color(1,1,1,.86f),"Semibold");name.HorizontalAlignment=HorizontalAlignment.Center;name.Ellipsis=false;
   grid.Add(card);grid.Add(name);
   rows.Add(new OItem{Box=card,Label=name,Action="app:"+app.Id});
  }
  oSub=0;FocusApps(false);Fade(grid,16);MarkState("overlay_apps");
 }
 void FocusApps(bool animate)
 {
  for(int i=0;i<rows.Count;i++){
   var r=rows[i];bool f=i==oSub;
   r.Box.BorderlineWidth=f?4:0;r.Label.TextColor=f?white:new Color(1,1,1,.8f);
   var sc=f?new Vector3(1.07f,1.07f,1):new Vector3(1,1,1);
   if(animate){var a=new Animation(150){DefaultAlphaFunction=Ease()};a.AnimateTo(r.Box,"Scale",sc);a.Finished+=(s,e)=>a.Dispose();a.Play();}else r.Box.Scale=sc;
   if(f)r.Box.RaiseToTop();
  }
 }

 // ---- Controls: a Control Center panel on the right ----
 void OpenControls()
 {
  ClearLayer();oMode=3;int wx=CcX-60;PlaceOverlay(wx,0,1920-wx,1080);
  RefreshSettings(true);
  oLayer=new View{Size2D=new Size2D(1920,1080)};oRoot.Add(oLayer);
  Dim(true,wx,0,1920-wx,1080,.45f);
  int h=32+3*SliderH+10*ValueH+3*25,ry=16;
  var panel=Glass(CcX,(1080-h)/2,CcW,h,CcR);
  // kind: 0 level (Left/Right), 1 value (Left/Right/Select cycle), 2 switch, 3 opens something
  void Row(int kind,string glyph,string name,string action)
  {
   int rh=kind==0?SliderH:ValueH,top=kind==0?12:(rh-38)/2;
   var r=new View{Position2D=new Position2D(16,ry),Size2D=new Size2D(CcW-32,rh),CornerRadius=new Vector4(28,28,28,28)};
   var g=Sym(glyph,24,kind==0?14:(rh-38)/2,38);r.Add(g);
   var l=Label(name,82,top-2,300,42,28,white,"Semibold");l.Ellipsis=false;r.Add(l);
   int vw=kind==1?360:280; // value rows: room for "Auto · Nintendo Switch"
   var v=Label("",CcW-32-24-vw-(kind==3?34:0),top-2,vw,42,kind==0?30:26,white,kind==0?"Display":"Medium");v.HorizontalAlignment=HorizontalAlignment.End;v.Ellipsis=false;r.Add(v);
   var item=new OItem{Box=r,Glyph=g,Label=l,Value=v,Action=action,Kind=kind};
   if(kind==0){item.Track=new View{Position2D=new Position2D(82,rh-26),Size2D=new Size2D(CcW-32-82-24,8),CornerRadius=new Vector4(4,4,4,4)};item.Fill=new View{Size2D=new Size2D(0,8),CornerRadius=new Vector4(4,4,4,4)};item.Track.Add(item.Fill);r.Add(item.Track);}
   if(kind==2){item.Switch=new View{Position2D=new Position2D(CcW-32-24-64,(rh-36)/2),Size2D=new Size2D(64,36),CornerRadius=new Vector4(18,18,18,18)};item.Knob=new View{Size2D=new Size2D(30,30),CornerRadius=new Vector4(15,15,15,15),BackgroundColor=white};item.Switch.Add(item.Knob);r.Add(item.Switch);v.Hide();}
   if(kind==3){var c=Sym("chevron",CcW-32-24-26,(rh-26)/2,26);c.Opacity=.5f;r.Add(c);}
   panel.Add(r);rows.Add(item);ry+=rh;
  }
  void Rule(){panel.Add(new View{Position2D=new Position2D(24,ry+12),Size2D=new Size2D(CcW-48,1),BackgroundColor=new Color(1,1,1,.14f)});ry+=25;}
  Row(0,"sun","Brightness","brightness");Row(0,"contrast","Contrast","contrast");Row(0,"speaker","Volume","volume");Rule();
  Row(1,"picture","Picture Mode","cycle:picturemode");Row(1,"waveform","Sound Mode","cycle:external-sound-mode");Row(1,"soundbar","Sound Output","output");
  Row(2,"gamecontroller","Game Mode","game-mode");Row(1,"moon","Sleep Timer","sleeptimer");Rule();
  // which input Watch and Play open (Automatic = by the TV's input name); Left/Right or Select cycle
  Row(1,PortGlyph(watchPort,watchPort,playPort),"Watch","heroport:watch");Row(1,PortGlyph(playPort,watchPort,playPort),"Play","heroport:play");Rule();
  Row(3,"artframe","Ambient","ambient");Row(3,"controls","All Controls","full:2");Row(3,"gear","TV Settings","app:org.tizen.menu");
  oSub=0;UpdateControls();FocusRows();Fade(panel,0);MarkState("overlay_controls");
 }
 void UpdateControls()
 {
  foreach(var r in rows){
   switch(r.Action){
    case "volume":r.Value.Text=currentMute==1?"Muted":currentVolume<0?"–":currentVolume.ToString();break;
    case "game-mode":{bool on=SettingValue("game-mode")=="1";r.Switch.BackgroundColor=on?live:new Color(.47f,.47f,.5f,.5f);r.Knob.Position2D=new Position2D(on?31:3,3);break;}
    case "ambient":case "full:2":case "app:org.tizen.menu":break;
    default:r.Value.Text=r.Kind==0?(SettingValue(r.Action)!=""?SettingValue(r.Action):"–"):TileValue(r.Action);break;
   }
   if(r.Track!=null){float l=TileLevel(r.Action);r.Fill.Size2D=new Size2D(l<0?0:(int)(r.Track.Size2D.Width*Math.Clamp(l,0f,1f)),8);}
  }
 }
 float TileLevel(string a)
 {
  if(a=="volume")return currentVolume<0?-1:currentVolume/100f;
  if(a=="brightness"||a=="contrast"){var (mn,mx,_)=Range(a);if(mx>mn&&double.TryParse(SettingValue(a),NumberStyles.Float,CultureInfo.InvariantCulture,out var n))return (float)((n-mn)/(mx-mn));}
  return -1;
 }
 // Left/Right adjust or cycle in place; Select steps forward, toggles or opens
 void ControlsKey(string k)
 {
  var r=rows[oSub];int d=k=="Left"?-1:1;bool sel=k=="Return"||k=="KP_Enter";
  RefreshSettings(true);
  switch(r.Action){
   case "brightness":case "contrast":if(!sel)ChangeLevel(r.Action,d);break;
   case "volume":if(sel){if(sound_manager_get_master_mute(out int m)==0)sound_manager_set_master_mute(m==1?0:1);RefreshSettings(true);}else ChangeVolume(d);break;
   case "output":SetSetting("soundoutput",SettingValue("soundoutput")=="0"?"2":"0");break;
   case "game-mode":SetSetting("game-mode",SettingValue("game-mode")=="1"?"0":"1");break;
   case "heroport:watch":case "heroport:play":CycleHeroPort(r.Action=="heroport:watch",d);break;
   case "sleeptimer":if(int.TryParse(SettingValue("sleeptimer"),out int n))SetSetting("sleeptimer",(((n+d)%5+5)%5).ToString());break;
   case "ambient":if(sel){HideOverlay("overlay_ambient");Launch("org.tizen.art-app");}return;
   case "full:2":if(sel)OpenFull(2);return;
   case "app:org.tizen.menu":if(sel){HideOverlay("overlay_settings");Launch("org.tizen.menu");}return;
   default:if(r.Action.StartsWith("cycle:"))CycleSetting(r.Action.Substring(6),d);break;
  }
  UpdateControls();MarkState("overlay_control:"+r.Action);
 }

 // ---- keys (the overlay window has focus while shown) ----
 void OnOverlayKey(object sender,Window.KeyEventArgs e)
 {
  if(e.Key==null||e.Key.State!=Key.StateType.Down||!oShown)return;string k=e.Key.KeyPressedName;
  long now=Environment.TickCount64;bool arrow=k=="Up"||k=="Down"||k=="Left"||k=="Right";
  if(k==lastKey&&now-lastKeyAt<(arrow?90:300))return;
  lastKey=k;lastKeyAt=now;OverlayActivity();
  long t0=System.Diagnostics.Stopwatch.GetTimestamp();
  try{OverlayKey(k,arrow);}catch(Exception ex){oState="key-error:"+ex.GetType().Name;}
  RecordKeyTime(System.Diagnostics.Stopwatch.GetTimestamp()-t0);
 }
 void OverlayKey(string k,bool arrow)
 {
  bool back=k=="XF86Back"||k=="Back"||k=="Escape"||k=="XF86Exit",sel=k=="Return"||k=="KP_Enter";
  if(k=="XF86Home")return; // Home arrives as a launch request: Home() closes the overlay
  switch(oMode){
   case 0:
    if(back){HideOverlay("overlay_back");return;}
    if(k=="Left"||k=="Right"){int n=Math.Clamp(oSel+(k=="Left"?-1:1),0,bar.Count-1);if(n!=oSel){oSel=n;FocusBar(true);}}
    else if(sel||k=="Up"&&!bar[oSel].Action.StartsWith("hdmi:"))ActivateBar();
    return;
   case 1:
    if(back||k=="Down"&&oSub==rows.Count-1){OpenBar(false);return;}
    if(k=="Up"||k=="Down"){oSub=Math.Clamp(oSub+(k=="Up"?-1:1),0,rows.Count-1);FocusRows();}
    else if(sel){var s=menuInputs[oSub];HideOverlay("overlay_input:"+s.Type);if(s.Type!=lastSource)SelectSource(s.Type);}
    return;
   case 2:
    if(back){OpenBar(false);return;}
    if(arrow){
     int lines=(rows.Count+AppCols-1)/AppCols;
     if(k=="Down"&&oSub/AppCols==lines-1){OpenBar(false);return;}
     int n=oSub+(k=="Left"?-1:k=="Right"?1:k=="Up"?-AppCols:AppCols);
     if(n>=0&&n<rows.Count){oSub=n;FocusApps(true);}
    }
    else if(sel){var a=rows[oSub].Action.Substring(4);HideOverlay("overlay_app");Launch(a);}
    return;
   case 3:
    if(back){OpenBar(false);return;}
    if(k=="Up"||k=="Down"){oSub=Math.Clamp(oSub+(k=="Up"?-1:1),0,rows.Count-1);FocusRows();}
    else if(k=="Left"||k=="Right"||sel)ControlsKey(k);
    return;
  }
 }
 string OverlayState()=>(oShown?"on":"off")+":mode"+oMode+":"+oState+":main-"+(mainHidden?"hidden":"shown")+":fg="+lastOther;
}
