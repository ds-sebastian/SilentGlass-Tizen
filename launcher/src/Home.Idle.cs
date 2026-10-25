using System;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

public sealed partial class QuietHome
{
 // ---- OLED care: fade to black with a drifting dim clock after 5 idle minutes on screen ----
 const long IdleMs=5*60*1000;
 long lastActivity=Environment.TickCount64;
 bool dimmed;
 View dimLayer;TextLabel dimClock;Animation dimAnimation;
 readonly Random drift=new Random();
 void IdleTick()
 {
  if(dimmed){MoveDimClock();return;}
  if(editing==""&&Environment.TickCount64-lastActivity>=IdleMs)Dim();
 }
 void Dim()
 {
  dimmed=true;infoTimer?.Stop();
  dimLayer=new View{Size2D=new Size2D(1920,1080),BackgroundColor=new Color(0,0,0,1),Opacity=0};
  dimClock=new TextLabel{Size2D=new Size2D(360,90),PixelSize=64,FontFamily="Quiet Inter Display",TextColor=new Color(.32f,.33f,.36f,1),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
  dimLayer.Add(dimClock);MoveDimClock();
  overlay.Add(dimLayer);dimLayer.RaiseToTop();
  dimAnimation=new Animation(2000);dimAnimation.AnimateTo(dimLayer,"Opacity",1f);dimAnimation.Play();
  MarkState("dimmed");
 }
 void MoveDimClock()
 {
  if(dimClock==null)return;
  dimClock.Text=DateTime.Now.ToString("h:mm");
  dimClock.Position2D=new Position2D(120+drift.Next(1920-240-360),120+drift.Next(1080-240-90));
 }
 void Wake(bool fromKey)
 {
  lastActivity=Environment.TickCount64;
  if(!dimmed)return;
  dimmed=false;
  if(dimAnimation!=null){dimAnimation.Stop();dimAnimation.Dispose();dimAnimation=null;}
  if(dimLayer!=null){overlay.Remove(dimLayer);dimLayer.Dispose();dimLayer=null;dimClock=null;}
  if(page==3&&infoTimer!=null)infoTimer.Start();
  MarkState(fromKey?"woke_key":"woke");
 }
}
