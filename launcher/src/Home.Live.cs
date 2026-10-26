using System;
using System.Runtime.InteropServices;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Live picture of the current input inside its hero card - the same component Samsung's Channel List uses for its
// mini TV (FLUX VideoCanvasView + TVWindowControlAPI on the main video path). Hardware video plane: no decode cost.
// Only shown when the TV reports a signal on that input; created after the first frame so Home never waits for it.
// Any failure leaves the illustration in place (recorded as live=...).
public sealed partial class QuietHome
{
 const string SignalKey="memory/tv-viewer/mainscreen_signal_state";
 // Tunables, re-read whenever the preview is (re)built: /tmp/quiet-home-live.conf with key=0|1 lines.
 //  enabled - show the preview at all;  still - the live card does not grow/lift on focus (the video plane does not
 //  follow scale/position animations);  sync - Graphic-Video sync mode;  scrim - darken the bottom for the words.
 const string LiveConfPath="/tmp/quiet-home-live.conf";
 sealed class LiveConf{public bool Enabled=true,Still=true,Sync=false,Scrim=true,Cover=true;public string Source="default";}
 LiveConf liveConf=new LiveConf();
 static LiveConf ReadLiveConf()
 {
  var c=new LiveConf();
  try{
   if(!System.IO.File.Exists(LiveConfPath))return c;
   c.Source="file";
   foreach(var line in System.IO.File.ReadAllLines(LiveConfPath)){
    var kv=line.Split('=');if(kv.Length!=2)continue;bool v=kv[1].Trim()=="1";
    switch(kv[0].Trim()){case "enabled":c.Enabled=v;break;case "still":c.Still=v;break;case "sync":c.Sync=v;break;case "scrim":c.Scrim=v;break;case "cover":c.Cover=v;break;}
   }
  }catch(Exception ex){c.Source="error:"+ex.GetType().Name;}
  return c;
 }
 Slot liveSlot;bool liveWindow;Tizen.NUI.Timer liveGeometryTimer;
 VideoCanvasViewHolder liveView;
 Tizen.TV.Broadcast.TVWindowControl.TVWindowControlAPI tvWindow;
 uint tvsHint;bool tvsHintSet;
 string liveState="off";int signal=-1,signalEvents;
 VconfChangedCallback signalCallback;
 // thin wrapper so the FLUX type is only touched (and its assembly loaded) when a preview is really wanted
 sealed class VideoCanvasViewHolder { public View View; }

 void WatchSignal()
 {
  ReadSignal();
  // debounced: the flag can flap while the TV re-syncs a source; act only once it has held for 800 ms
  try{signalCallback=(n,d)=>OnUi(()=>{signalEvents++;if(signalTimer==null){signalTimer=new Tizen.NUI.Timer(800);signalTimer.Tick+=(s,e)=>{ApplySignal();return false;};}signalTimer.Stop();signalTimer.Start();});
   vconf_notify_key_changed(SignalKey,signalCallback,IntPtr.Zero);}catch{}
 }
 Tizen.NUI.Timer signalTimer;
 void ApplySignal(){int before=signal;ReadSignal();if(before==signal){if(liveView!=null)PushLiveGeometry(liveRect);return;}UpdateHeroBadges();if(inputsLayer!=null)FillSheet();ScheduleLive();MarkState("signal_changed");}
 void ReadSignal(){try{signal=vconf_get_int(SignalKey,out int v)==0?v:-1;}catch{signal=-1;}}
 bool CurrentHasSignal=>signal==1;

 // after the frame that shows the page: build or drop the preview for the current input's hero
 void ScheduleLive(){OnUi(UpdateLive);}
 void UpdateLive()
 {
  liveConf=ReadLiveConf();
  Slot hero=null;
  if(liveConf.Enabled&&page==0&&visible&&!dimmed&&CurrentHasSignal)foreach(var s in slots)if(s.Kind==1&&s.Action=="hdmi:"+lastSource)hero=s;
  if(hero==null){HideLive();MarkState("live");return;}
  // already live: re-assert the plane rectangle (the viewer can reset it when the source re-syncs or changes mode)
  if(liveView!=null&&liveSlot==hero){PushLiveGeometry(liveRect);MarkState("live");return;}
  HideLive();
  try{
   // the window is told once that it shows TV video and stays so until Home is left (toggling it per preview made
   // the TV re-sync the input, which flapped the signal flag, which rebuilt the preview...)
   if(!liveWindow){
    if(!tvsHintSet){tvsHint=Window.Default.AddAuxiliaryHint("wm.tvs.win.use.tvs","1");tvsHintSet=true;}
    else Window.Default.SetAuxiliaryHintValue(tvsHint,"1");
    Window.Default.SetTransparency(true);liveWindow=true;
   }
   tvWindow??=new Tizen.TV.Broadcast.TVWindowControl.TVWindowControlAPI();
   var art=hero.Card.GetChildAt(0); // the card artwork; the video goes directly above it, words and dot above that
   var canvas=new Tizen.TV.FLUX.VideoCanvasView{Size2D=new Size2D(hero.W,hero.H),Position2D=new Position2D(0,0)};
   canvas.CornerRadius=new Vector4(40,40,40,40);
   hero.Card.Add(canvas);canvas.LowerToBottom();art.LowerToBottom();
   View scrim=null;
   if(liveConf.Scrim){
    // bottom band that eases from clear to 82% black behind the words (Apple TV app style). The video plane is
    // separate hardware, so it cannot be blurred - darkening is the only option over a live picture.
    const int Band=330;
    scrim=new View{Name="live-scrim",Position2D=new Position2D(0,hero.H-Band),Size2D=new Size2D(hero.W,Band),CornerRadius=new Vector4(0,0,40,40)};
    var stops=new PropertyArray();var offsets=new PropertyArray();
    foreach(var (o,al) in new[]{(0f,0f),(.25f,.18f),(.5f,.48f),(.75f,.70f),(1f,.82f)}){offsets.PushBack(new PropertyValue(o));stops.PushBack(new PropertyValue(new Vector4(0,0,0,al)));}
    var fade=new PropertyMap();
    fade.Add(Visual.Property.Type,new PropertyValue((int)Visual.Type.Gradient));
    fade.Add(GradientVisualProperty.StartPosition,new PropertyValue(new Vector2(0,-.5f)));
    fade.Add(GradientVisualProperty.EndPosition,new PropertyValue(new Vector2(0,.5f)));
    fade.Add(GradientVisualProperty.StopOffset,new PropertyValue(offsets));
    fade.Add(GradientVisualProperty.StopColor,new PropertyValue(stops));
    fade.Add(Visual.Property.CornerRadius,new PropertyValue(new Vector4(0,0,40,40)));
    scrim.Background=fade;
    hero.Card.Add(scrim);scrim.LowerToBottom();art.LowerToBottom();canvas.RaiseAbove(art);scrim.RaiseAbove(canvas);
   }
   // fill, don't squash: the canvas (the see-through hole) is exactly the card, but the video plane gets a 16:9
   // rectangle covering it; the overflow sits behind the opaque launcher and is never seen
   canvas.DisableAutoUpdateVideoGeometry=liveConf.Cover;
   canvas.SetPlayer(tvWindow,liveConf.Sync?Tizen.TV.FLUX.VideoCanvasView.VideoSyncMode.GraphicVideoSync:Tizen.TV.FLUX.VideoCanvasView.VideoSyncMode.None);
   liveView=new VideoCanvasViewHolder{View=canvas};liveSlot=hero;
   int idx=slots.IndexOf(hero);StyleSlot(hero,idx==selected&&area==1,false); // settle any focus transform first
   // once layout has settled, push the final rectangle to the video plane explicitly
   liveGeometryTimer?.Stop();liveGeometryTimer=new Tizen.NUI.Timer(250);
   var c=liveConf.Cover?Cover(hero):(hero.X,hero.Y,hero.W,hero.H);
   // 4 px bleed on every side: the see-through hole is exactly the card, so a plane even 1 px short showed as a thin
   // dark line along the card edge; the bleed sits behind the card's own artwork
   var rect=(c.Item1-Bleed,c.Item2-Bleed,c.Item3+2*Bleed,c.Item4+2*Bleed);liveRect=rect;
   PushLiveGeometry(rect);UnmuteLive();
   liveGeometryTimer.Tick+=(s,e)=>{PushLiveGeometry(rect);UnmuteLive();return false;};liveGeometryTimer.Start();
   liveState="on:"+hero.Name+(liveConf.Still?":still":"")+(liveConf.Sync?":sync":"")+":conf="+liveConf.Source;
  }catch(Exception ex){liveState="failed:"+ex.GetType().Name+":"+ex.Message;HideLive();}
  MarkState("live");
 }
 static (int,int,int,int) Cover(Slot s)
 {
  // smallest 16:9 rectangle that covers the card, centred on it
  if(s.W*9>=s.H*16){int h=s.W*9/16;return (s.X,s.Y-(h-s.H)/2,s.W,h);}
  int w=s.H*16/9;return (s.X-(w-s.W)/2,s.Y,w,s.H);
 }
 void PushLiveGeometry((int x,int y,int w,int h) r)
 {
  try{(liveView?.View as Tizen.TV.FLUX.VideoCanvasView)?.ForceUpdateVideoGeometry(r.x,r.y,r.w,r.h);liveGeometry=$"{r.w}x{r.h}+{r.x}+{r.y}";}
  catch(Exception ex){liveGeometry="error:"+ex.GetType().Name;}
 }
 // The TV can leave the main video muted while its viewer is in the background (seen after Quiet Home restarted
 // mid-preview: plane in place, picture black). Samsung's Channel List unmutes on the same condition.
 void UnmuteLive()
 {
  try{using var av=Tizen.TV.Broadcast.TVService.TVServiceAPI.Instance().CreateAVControl((Tizen.TV.Broadcast.TVService.Types.Profile)1,0);liveUnmute=av.MuteVideo(0);}
  catch(Exception ex){liveUnmute=-1;liveGeometry+=" unmute:"+ex.GetType().Name;}
 }
 string liveGeometry="";int liveUnmute=-99;
 const int Bleed=4;(int,int,int,int) liveRect;
 bool IsLiveStill(Slot s)=>s==liveSlot&&liveView!=null&&liveConf.Still;
 void HideLive()
 {
  liveGeometryTimer?.Stop();
  if(liveView==null)return;
  var was=liveSlot;liveSlot=null;
  try{
   var card=liveView.View.GetParent() as View;
   if(card!=null){for(uint i=0;i<card.ChildCount;i++){var c=card.GetChildAt(i);if(c.Name=="live-scrim"){card.Remove(c);c.Dispose();break;}}card.Remove(liveView.View);}
   liveView.View.Dispose();
  }catch{}
  liveView=null;
  if(was!=null&&slots.Contains(was))StyleSlot(was,slots.IndexOf(was)==selected&&area==1,false);
  if(!liveState.StartsWith("failed"))liveState="off";
 }
 // leaving Home: hand the screen back to the TV the normal way
 void ReleaseLiveWindow()
 {
  HideLive();
  if(!liveWindow)return;
  try{if(tvsHintSet)Window.Default.SetAuxiliaryHintValue(tvsHint,"0");Window.Default.SetTransparency(false);}catch{}
  liveWindow=false;
 }
}
