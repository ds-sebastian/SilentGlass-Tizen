using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Live inputs: Samsung's source list (names, connected) and the current source, both pushed by the TV.
// Drives the hero badges, the Inputs tile, the Inputs sheet and a short "connected" toast.
// powerState is never used: on this TV the on-screen SHIELD reports Off (devices sit behind the soundbar);
// the only trusted power signal is the picture on the current input (memory/tv-viewer/mainscreen_signal_state).
public sealed partial class QuietHome
{
 List<InfoStats.InputSource> inputs=new List<InfoStats.InputSource>();
 SynchronizationContext ui;
 delegate void SourceListChangedCallback(int eventType,IntPtr userData);
 delegate void VconfChangedCallback(IntPtr keyNode,IntPtr userData);
 [DllImport("/usr/lib/libsource-api.so")] static extern void register_sourcelist_changed_callback(SourceListChangedCallback callback,IntPtr userData);
 [DllImport("libvconf.so.0")] static extern int vconf_notify_key_changed(string key,VconfChangedCallback callback,IntPtr userData);
 SourceListChangedCallback sourceListCallback; // referenced for the process lifetime (native code holds the pointer)
 VconfChangedCallback sourceTypeCallback;
 string inputsWatch="off";int sourceListEvents,sourceTypeEvents;long sourcesMicros;
 readonly Color onNowGreen=new Color(.2f,.78f,.36f,1);

 void WatchInputs()
 {
  try{sourceListCallback=(t,d)=>OnUi(()=>{sourceListEvents++;OnInputsChanged(true);});register_sourcelist_changed_callback(sourceListCallback,IntPtr.Zero);inputsWatch="list";}
  catch(Exception ex){inputsWatch="list-failed:"+ex.GetType().Name;}
  try{sourceTypeCallback=(n,d)=>OnUi(()=>{sourceTypeEvents++;OnSourceTypeChanged();});int r=vconf_notify_key_changed("memory/system/source_type",sourceTypeCallback,IntPtr.Zero);inputsWatch+=r==0?"+source":"+source-failed:"+r;}
  catch(Exception ex){inputsWatch+="+source-failed:"+ex.GetType().Name;}
 }
 // native callbacks may arrive off the UI thread: hop onto it before touching views
 void OnUi(Action a){Action safe=()=>{try{a();}catch{}};var c=ui;if(c!=null)c.Post(_=>safe(),null);else Post(safe);}
 void RefreshInputs(){long t0=Stopwatch.GetTimestamp();inputs=InfoStats.Sources();sourcesMicros=(Stopwatch.GetTimestamp()-t0)*1000000/Stopwatch.Frequency;}
 // TV, HDMI 1-4: anything else (USB, DLNA, apps) leaves lastSource on the last real input
 void RefreshSource(){try{if(vconf_get_int("memory/system/source_type",out int n)==0&&(n==0||(n>=13&&n<=16)))lastSource=n;}catch{}}
 void OnSourceTypeChanged()
 {
  int before=lastSource;RefreshSource();if(before==lastSource)return;
  ReadSignal();if(RelayoutHeroes())return;
  UpdateHeroBadges();RefreshTileValues();if(inputsLayer!=null)FillSheet();ScheduleLive();MarkState("source_changed");
 }
 int watchPort=15,playPort=13;
 // Watch / Play: the input chosen on the Controls page, or (Automatic) the input the TV calls "Shield" / "Switch",
 // falling back to HDMI 3 / HDMI 1
 bool ResolveHeroPorts()
 {
  int w=prefs.WatchPort!=0?prefs.WatchPort:PortNamed("shield",15),p=prefs.PlayPort!=0?prefs.PlayPort:PortNamed("switch",13);
  bool changed=w!=watchPort||p!=playPort;watchPort=w;playPort=p;return changed;
 }
 int PortNamed(string word,int fallback){var m=inputs.Find(i=>i.Known&&i.Type>=13&&i.Type<=16&&i.Name.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0);return m!=null?m.Type:fallback;}
 void OnInputsChanged(bool announce)
 {
  var before=inputs;RefreshInputs();RefreshSource();
  if(ResolveHeroPorts()&&page==0){BuildPage(false);MarkState("hero_ports_changed");return;}
  if(RelayoutHeroes())return;
  if(announce&&visible)
   foreach(var now in inputs){
    var was=before.Find(i=>i.Type==now.Type);
    if(was!=null&&was.Known&&now.Known&&was.Connected!=now.Connected){Toast(now.Name+(now.Connected?" connected":" unplugged"));break;}
   }
  UpdateHeroBadges();RefreshTileValues();if(inputsLayer!=null)FillSheet();MarkState("inputs_changed");
 }

 // ---- status dots, no words: only what the TV can actually tell.
 // Current input: green = picture, grey = no signal. Unplugged: empty ring. Other inputs: no dot (power unknown here).
 enum Dot { None, On, Off, Unplugged }
 Dot DotFor(int type)
 {
  var src=inputs.Find(i=>i.Type==type);
  if(type==lastSource)return signal==1?Dot.On:signal==0?Dot.Off:Dot.None;
  return src!=null&&src.Known&&!src.Connected?Dot.Unplugged:Dot.None;
 }
 void PaintDot(View dot,Dot d,bool onLight)
 {
  dot.BackgroundColor=d==Dot.On?onNowGreen:d==Dot.Off?(onLight?new Color(.55f,.56f,.6f,1):muted):clear;
  dot.BorderlineWidth=d==Dot.Unplugged?3:0;dot.BorderlineColor=onLight?new Color(.45f,.46f,.5f,1):muted;
 }
 Animation badgeAnimation;
 void UpdateHeroBadges(bool animate=true)
 {
  if(page!=0)return;
  StopAnimation(ref badgeAnimation);
  if(animate)badgeAnimation=new Animation(220){DefaultAlphaFunction=Ease()};
  foreach(var s in slots){
   if(s.Kind!=1||!s.Action.StartsWith("hdmi:"))continue;
   var d=DotFor(int.Parse(s.Action.Substring(5)));
   if(d!=Dot.None)PaintDot(s.BadgeDot,d,false);
   float target=d!=Dot.None?1:0;
   if(animate)badgeAnimation.AnimateTo(s.Badge,"Opacity",target);else s.Badge.Opacity=target;
  }
  badgeAnimation?.Play();
 }

 // ---- toast: one short line in the free strip below the controls, fades out by itself ----
 View toast;Animation toastAnimation;
 void Toast(string text)
 {
  StopAnimation(ref toastAnimation);
  if(toast!=null){overlay.Remove(toast);toast.Dispose();toast=null;}
  var t=new View{Position2D=new Position2D(960-300,962),Size2D=new Size2D(600,68),BackgroundColor=new Color(.15f,.15f,.18f,.96f),CornerRadius=new Vector4(34,34,34,34),BorderlineWidth=1.5f,BorderlineColor=new Color(1,1,1,.12f),Opacity=0};
  Glass(t,34,600,68,true,new Color(.15f,.15f,.18f,.96f),dark:true);
  toastRim?.Dispose();toastRim=null;
  if(Liquid){t.BorderlineWidth=0;toastRim=RimOverlay(t,34,.18f,6,18,persistent:true,focusShadow:0);}
  t.Add(new ImageView{ResourceUrl=Root+"glyph-inputs.svg",Position2D=new Position2D(24,12),Size2D=new Size2D(44,44)});
  var l=Label(text,80,0,600-120,68,28,white,"Semibold");l.Ellipsis=true;t.Add(l);
  overlay.Add(t);toast=t;
  toastAnimation=new Animation(3400){DefaultAlphaFunction=Ease()};
  toastAnimation.AnimateTo(t,"Opacity",1f,0,220);toastAnimation.AnimateTo(t,"Opacity",0f,3000,3400);
  toastAnimation.Finished+=(s,e)=>{if(toast==t){overlay.Remove(t);t.Dispose();toast=null;}};
  toastAnimation.Play();MarkState("toast:"+text);
 }

 // ---- Inputs sheet: every HDMI port by the TV's name for it; Up/Down, Select switches, Back closes ----
 sealed class SheetRow { public View Row,Dot; public TextLabel Name,Port; public ImageView Glyph,Check; public bool Current; public Dot State; }
 View inputsLayer,sheet;Animation sheetAnimation;Renderer sheetRim,toastRim;
 readonly List<SheetRow> sheetRows=new List<SheetRow>();
 List<InfoStats.InputSource> sheetInputs=new List<InfoStats.InputSource>();
 int inputSel;
 const int SheetX=100,SheetW=640,RowH=96,SheetBottom=760;
 void OpenInputs()
 {
  CloseInputs(false);
  inputsLayer=new View{Size2D=new Size2D(1920,1080),BackgroundColor=new Color(0,0,0,.42f),Opacity=0};
  sheet=new View{Position2D=new Position2D(SheetX,0),BackgroundColor=new Color(.13f,.13f,.15f,.97f),CornerRadius=new Vector4(34,34,34,34),BorderlineWidth=1.5f,BorderlineColor=new Color(1,1,1,.12f)};
  Glass(sheet,34,SheetW,400,true,new Color(.13f,.13f,.15f,.97f),dark:true);
  if(Liquid)sheet.BorderlineWidth=0;
  inputsLayer.Add(sheet);overlay.Add(inputsLayer);
  inputSel=-1;FillSheet();
  // slide up 40 px while fading in
  int y=sheet.Position2D.Y;sheet.Position2D=new Position2D(SheetX,y+40);
  sheetAnimation=new Animation(180){DefaultAlphaFunction=Ease()};
  sheetAnimation.AnimateTo(inputsLayer,"Opacity",1f);sheetAnimation.AnimateTo(sheet,"PositionY",(float)y);sheetAnimation.Play();
  MarkState("inputs_open");
 }
 // (re)builds the rows from the current input list; keeps the selection on the same input
 void FillSheet()
 {
  int keepType=inputSel>=0&&inputSel<sheetInputs.Count?sheetInputs[inputSel].Type:lastSource;
  while(sheet.ChildCount>0){var v=sheet.GetChildAt(0);sheet.Remove(v);v.Dispose();}
  sheetRim?.Dispose();sheetRim=null;
  if(Liquid)sheetRim=RimOverlay(sheet,34,.22f,10,30,persistent:true,focusShadow:0); // edge light + shadow; the DALi blur stays the body
  sheetRows.Clear();sheetInputs=inputs;
  int h=96+sheetInputs.Count*RowH+20,y=Math.Max(60,SheetBottom-h);
  sheet.Size2D=new Size2D(SheetW,h);if(sheetAnimation==null||sheetAnimation.State!=Animation.States.Playing)sheet.Position2D=new Position2D(SheetX,y);
  sheet.Add(Label("Inputs",36,22,SheetW-72,56,36,white,"Display"));
  for(int i=0;i<sheetInputs.Count;i++){
   var s=sheetInputs[i];bool cur=s.Type==lastSource,off=s.Known&&!s.Connected&&!cur;
   var r=new SheetRow{Current=cur};
   r.Row=new View{Position2D=new Position2D(16,96+i*RowH),Size2D=new Size2D(SheetW-32,RowH-8),CornerRadius=new Vector4(22,22,22,22),Opacity=off?.45f:1};
   r.Glyph=new ImageView{ResourceUrl=Root+"glyph-inputs.svg",Position2D=new Position2D(20,22),Size2D=new Size2D(44,44)};r.Row.Add(r.Glyph);
   bool sub=s.Name!=s.Port;
   r.Name=Label(s.Name,84,sub?10:0,SheetW-32-84-130,sub?44:RowH-8,30,white,"Semibold");r.Name.Ellipsis=true;r.Row.Add(r.Name);
   if(sub){r.Port=Label(s.Port,84,50,300,32,22,muted);r.Row.Add(r.Port);}
   // right side: status dot (is it on?) and, separately, a checkmark on the selected input
   r.State=DotFor(s.Type);
   if(r.State!=Dot.None){r.Dot=new View{Position2D=new Position2D(SheetW-32-118,(RowH-8-22)/2),Size2D=new Size2D(22,22),CornerRadius=new Vector4(11,11,11,11)};r.Row.Add(r.Dot);}
   if(cur){r.Check=new ImageView{ResourceUrl=Root+"glyph-check.svg",Position2D=new Position2D(SheetW-32-74,(RowH-8-44)/2),Size2D=new Size2D(44,44)};r.Row.Add(r.Check);}
   sheet.Add(r.Row);sheetRows.Add(r);
  }
  inputSel=Math.Max(0,sheetInputs.FindIndex(i=>i.Type==keepType));
  FocusInputs();
 }
 void FocusInputs()
 {
  for(int i=0;i<sheetRows.Count;i++){
   var r=sheetRows[i];bool f=i==inputSel;
   r.Row.BackgroundColor=f?white:clear;
   r.Name.TextColor=f?dark:white;r.Glyph.Color=f?dark:white;
   if(r.Port!=null)r.Port.TextColor=f?valueFocus:muted;
   if(r.Dot!=null)PaintDot(r.Dot,r.State,f);
   if(r.Check!=null)r.Check.Color=f?dark:white;
  }
 }
 bool HandleInputsKey(string k)
 {
  if(k=="Up"||k=="Down"){inputSel=Math.Clamp(inputSel+(k=="Up"?-1:1),0,sheetRows.Count-1);FocusInputs();return true;}
  if(k=="Return"||k=="KP_Enter"){if(inputSel<0||inputSel>=sheetInputs.Count)return true;var s=sheetInputs[inputSel];CloseInputs(false);SelectSource(s.Type);return true;}
  if(k=="XF86Back"||k=="Back"||k=="Escape"||k=="XF86Exit"){CloseInputs(true);return true;}
  if(k=="XF86Home"){CloseInputs(false);return false;}
  return true; // Left/Right and other keys stay inside the sheet
 }
 void CloseInputs(bool refocus)
 {
  if(inputsLayer==null)return;
  StopAnimation(ref sheetAnimation);
  overlay.Remove(inputsLayer);inputsLayer.Dispose();inputsLayer=null;sheet=null;sheetRows.Clear();sheetRim?.Dispose();sheetRim=null;
  if(refocus)MarkState("inputs_closed");
 }
 // ---- Ambient's Home key: the Art app always opens Samsung Home (hard-coded); come back to Quiet Home instead ----
 const string ForegroundKey="memory/appfw/current_fullscreen_appid";
 [DllImport("libvconf.so.0")] static extern string vconf_get_str(string key); // malloc'd; the marshaller frees it
 VconfChangedCallback foregroundCallback;
 string lastForeground="",foregroundWatch="off";int ambientReturns;
 void WatchForeground()
 {
  try{lastForeground=vconf_get_str(ForegroundKey)??"";foregroundCallback=(n,d)=>OnUi(OnForegroundChanged);int r=vconf_notify_key_changed(ForegroundKey,foregroundCallback,IntPtr.Zero);foregroundWatch=r==0?"on":"failed:"+r;}
  catch(Exception ex){foregroundWatch="failed:"+ex.GetType().Name;}
 }
 static bool IsAmbient(string id)=>id=="org.tizen.art-app"||id=="com.samsung.tv.home.art"||id=="com.samsung.tv.ambientfte";
 void OnForegroundChanged()
 {
  string now=null;try{now=vconf_get_str(ForegroundKey);}catch{}
  if(string.IsNullOrEmpty(now))return;
  string before=lastForeground;lastForeground=now;
  if(now=="com.samsung.tv.csfs"&&IsAmbient(before)){ambientReturns++;Launch("com.samsung.tv.bixby-promotion");MarkState("ambient_home_redirect");}
 }
 string InputsState()=>string.Join(";",inputs.ConvertAll(i=>i.Type+","+i.Name+","+(i.Known?(i.Connected?"1":"0"):"?")+","+i.Power));
}
