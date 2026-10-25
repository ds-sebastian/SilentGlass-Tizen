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
 // Off until it can be tuned with someone watching: on the S90D the video plane lagged the card's focus scale/lift
 // (black cut-out while focused) and attaching it made the signal flag flap. Code kept; switch on to experiment.
 static readonly bool LivePreviewEnabled=false;
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
 void ApplySignal(){int before=signal;ReadSignal();if(before==signal)return;UpdateHeroBadges();if(inputsLayer!=null)FillSheet();ScheduleLive();MarkState("signal_changed");}
 void ReadSignal(){try{signal=vconf_get_int(SignalKey,out int v)==0?v:-1;}catch{signal=-1;}}
 bool CurrentHasSignal=>signal==1;

 // after the frame that shows the page: build or drop the preview for the current input's hero
 void ScheduleLive(){OnUi(UpdateLive);}
 void UpdateLive()
 {
  Slot hero=null;
  if(!LivePreviewEnabled){HideLive();return;}
  if(page==0&&visible&&!dimmed&&CurrentHasSignal)foreach(var s in slots)if(s.Kind==1&&s.Action=="hdmi:"+lastSource)hero=s;
  if(hero==null){HideLive();return;}
  if(liveView!=null&&liveView.View.GetParent()==hero.Card)return;
  HideLive();
  try{
   if(!tvsHintSet){tvsHint=Window.Default.AddAuxiliaryHint("wm.tvs.win.use.tvs","1");tvsHintSet=true;}
   else Window.Default.SetAuxiliaryHintValue(tvsHint,"1");
   Window.Default.SetTransparency(true); // the video plane shows through the canvas' cut-out; background art stays opaque
   tvWindow??=new Tizen.TV.Broadcast.TVWindowControl.TVWindowControlAPI();
   var canvas=new Tizen.TV.FLUX.VideoCanvasView{Size2D=new Size2D(hero.W,hero.H),Position2D=new Position2D(0,0)};
   canvas.CornerRadius=new Vector4(40,40,40,40);
   hero.Card.Add(canvas);canvas.LowerToBottom();hero.Card.GetChildAt(0).LowerToBottom(); // art below, video above it, text on top
   canvas.SetPlayer(tvWindow);
   // keep the word and device name readable over any picture
   var scrim=new View{Name="live-scrim",Position2D=new Position2D(0,hero.H-250),Size2D=new Size2D(hero.W,250),BackgroundColor=new Color(0,0,0,.45f),CornerRadius=new Vector4(0,0,40,40)};
   hero.Card.Add(scrim);scrim.RaiseAbove(canvas);
   liveView=new VideoCanvasViewHolder{View=canvas};liveState="on:"+hero.Name;
  }catch(Exception ex){liveState="failed:"+ex.GetType().Name+":"+ex.Message;HideLive();}
  MarkState("live");
 }
 void HideLive()
 {
  if(liveView==null)return;
  try{
   var card=liveView.View.GetParent() as View;
   if(card!=null){for(uint i=0;i<card.ChildCount;i++){var c=card.GetChildAt(i);if(c.Name=="live-scrim"){card.Remove(c);c.Dispose();break;}}card.Remove(liveView.View);}
   liveView.View.Dispose();
  }catch{}
  liveView=null;
  try{if(tvsHintSet)Window.Default.SetAuxiliaryHintValue(tvsHint,"0");Window.Default.SetTransparency(false);}catch{}
  if(!liveState.StartsWith("failed"))liveState="off";
 }
}
