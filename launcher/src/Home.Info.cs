using System;
using System.Collections.Generic;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

public sealed partial class QuietHome
{
 // ---- TV Info page: read-only, refreshed every 2 s only while visible ----
 Tizen.NUI.Timer infoTimer;
 TextLabel liveRate,liveCpu,liveMem,liveTotal;
 readonly List<View> spark=new List<View>();
 readonly double[] sparkValues=new double[36];
 (long rx,long tx)? lastNet;(long idle,long total)? lastCpu;long lastNetAt=-100000;string infoIface="eth0";
 string lastRateText="Measuring…",lastCpuText;int infoMarkers=-2,infoDisabled=-2;
 string TotalsText(Dictionary<string,(long rx,long tx)> all)=>all.TryGetValue(infoIface,out var c)?"↓ "+InfoStats.Bytes(c.rx)+"  ↑ "+InfoStats.Bytes(c.tx):null;
 const int SparkBase=296; // bottom of the bars inside the 330-tall Live panel
 void DrawSpark()
 {
  double peak=50000;foreach(var v in sparkValues)peak=Math.Max(peak,v);
  for(int i=0;i<spark.Count;i++){int h=Math.Max(3,(int)(150*sparkValues[i]/peak));spark[i].Size2D=new Size2D(14,h);spark[i].Position2D=new Position2D(30+i*22,SparkBase-h);}
 }
 View InfoPanel(int x,int y,int w,int h,string title,Color dot,int row)
 {
  var card=Card(x,y,w,h,clear,30);var glass=AddGlass(card,0,x,y,30,tint:.06f,shadow:.08f,shadowY:5,blur:16,focusShadow:.08f);
  card.Add(new View{Position2D=new Position2D(30,39),Size2D=new Size2D(14,14),BackgroundColor=dot,CornerRadius=new Vector4(7,7,7,7)});
  card.Add(Label(title,56,24,w-86,44,29,white,"Semibold"));
  slots.Add(new Slot{Card=card,Glass=glass,Name=title,Action="info",Row=row,X=x,Y=y,W=w,H=h});
  return card;
 }
 TextLabel InfoRow(View card,int w,int n,string key,string value,Color color=null)
 {
  int y=90+n*54;
  card.Add(Label(key,30,y,200,42,23,muted));
  var v=Label(value??"Unavailable",196,y,w-226,42,24,color??ink,"Semibold");v.HorizontalAlignment=HorizontalAlignment.End;v.Ellipsis=true;card.Add(v);
  return v;
 }
 static string MemText((long totalKb,long availableKb)? m)=>m.HasValue?InfoStats.Bytes((m.Value.totalKb-m.Value.availableKb)*1024.0)+" of "+InfoStats.Bytes(m.Value.totalKb*1024.0)+" used":"Unavailable";
 string infoThread="";
 void BuildInfo()
 {
  var good=new Color(.45f,.88f,.58f,1);var warn=new Color(1f,.74f,.36f,1);
  string route=InfoStats.DefaultInterface();infoIface=route??"eth0";
  var netdev=InfoStats.NetDev();
  var tv=InfoPanel(100,140,540,370,"This TV",new Color(.56f,.66f,1f,1),0);
  string ver=InfoStats.SystemInfo("http://tizen.org/feature/platform.version");
  string build=InfoStats.ParseBuildDate(InfoStats.SystemInfo("http://tizen.org/system/build.string"));
  InfoRow(tv,540,0,"Model",InfoStats.SystemInfo("http://tizen.org/system/model_name"));
  InfoRow(tv,540,1,"Software",(ver!=null?"Tizen "+ver:"Tizen")+(build!=null?" · "+build:""));
  var up=InfoStats.Uptime();InfoRow(tv,540,2,"Up for",up.HasValue?InfoStats.Duration(up.Value):null);
  liveMem=InfoRow(tv,540,3,"Memory",MemText(InfoStats.Memory()));
  int cores=InfoStats.Cores();liveCpu=InfoRow(tv,540,4,"Processor","Measuring…"+(cores>0?" · "+cores+" cores":""));

  var net=InfoPanel(690,140,540,370,"Network",new Color(.33f,.82f,.75f,1),0);
  bool wifi=infoIface.StartsWith("wlan");int? level=wifi?InfoStats.WifiLevel(infoIface):null;
  string link=wifi?"Wi-Fi"+(level.HasValue?" · "+level+" dBm":""):"Wired"+(InfoStats.LinkSpeed(infoIface) is string spd?" · "+spd:"");
  InfoRow(net,540,0,"Connection",route==null?"Not connected":link);
  InfoRow(net,540,1,"IP address",InfoStats.Ipv4(infoIface));
  var dns=InfoStats.Nameservers();InfoRow(net,540,2,"DNS",dns.Count>0?string.Join(" · ",dns.ConvertAll(d=>d=="8.8.8.8"?"Google backup":d)):null);
  liveTotal=InfoRow(net,540,3,"Since start",TotalsText(netdev));

  var pv=InfoPanel(1280,140,540,370,"Privacy",new Color(.72f,.56f,1f,1),0);
  infoMarkers=InfoStats.MarkersInPlace();int total=InfoStats.VendorMarkers.Length;
  InfoRow(pv,540,0,"Vendor blocks",infoMarkers<0?"Unavailable":infoMarkers+" of "+total+" in place",infoMarkers<0?null:infoMarkers==total?good:warn);
  infoDisabled=InfoStats.DisabledAppCount("com.samsung.tv.bixby-promotion");int apps=InfoStats.DisabledApps.Length;
  InfoRow(pv,540,1,"Disabled apps",infoDisabled<0?"Unavailable":infoDisabled+" of "+apps+" off",infoDisabled<0?null:infoDisabled==apps?good:warn);
  bool sdb=InfoStats.SdbListening();
  InfoRow(pv,540,2,"Developer access",sdb?"SDB on":"SDB off",sdb?good:muted);

  // Thread border router: role from wpan0's RLOC/ALOC addresses, home reach from forwarding + an off-mesh address.
  var th=InfoStats.Thread();bool fwd=InfoStats.Ipv6Forwarding();
  bool routing=th.present&&th.rloc16>=0&&(th.rloc16&0x1ff)==0;
  string status=!th.present?"Off":th.rloc16<0?"Starting…":routing?"Online · Router "+(th.rloc16>>10)+(th.leader?" · Leader":""):"Online · Child";
  string home=!th.present?null:!fwd?"Forwarding off":th.homeRoute?"Connected":"Waiting for route";
  var tr=InfoPanel(100,540,830,330,"Thread border router",routing?good:th.present?warn:muted,1);
  InfoRow(tr,830,0,"Status",status,routing?good:th.present?warn:muted);
  InfoRow(tr,830,1,"Home network",home,home=="Connected"?good:home==null?null:warn);
  InfoRow(tr,830,2,"Thread traffic",netdev.TryGetValue("wpan0",out var w0)?"↓ "+InfoStats.Bytes(w0.rx)+"  ↑ "+InfoStats.Bytes(w0.tx):null);
  InfoRow(tr,830,3,"Interface",th.present?"wpan0 · "+th.addresses+" addresses":"Not running");
  infoThread=status+" | "+(home??"-")+" | fwd="+fwd;

  var live=InfoPanel(970,540,850,330,"Live · TV network",new Color(.4f,.78f,1f,1),1);
  bool fresh=Environment.TickCount64-lastNetAt>10000;
  if(fresh){Array.Clear(sparkValues,0,sparkValues.Length);lastNet=null;lastCpu=null;lastRateText="Measuring…";}
  if(lastCpuText!=null&&!fresh)liveCpu.Text=lastCpuText;
  liveRate=Label(lastRateText,30,86,790,52,36,white,"Display");live.Add(liveRate);
  spark.Clear();
  for(int i=0;i<sparkValues.Length;i++){var b=new View{Position2D=new Position2D(30+i*22,SparkBase),Size2D=new Size2D(14,3),BackgroundColor=new Color(.4f,.78f,1f,.85f),CornerRadius=new Vector4(4,4,4,4)};spark.Add(b);live.Add(b);}
  DrawSpark();
  AppsTabSwitch(100,900);
  if(fresh)InfoTick();
  if(infoTimer==null){infoTimer=new Tizen.NUI.Timer(2000);infoTimer.Tick+=(s,e)=>{if(page!=3)return false;InfoTick();return true;};}
  infoTimer.Start();
 }
 // an Apple-style switch row (one word and a switch: green = Apps tab shown)
 void AppsTabSwitch(int x,int y)
 {
  const int W=540,H=72;bool on=!prefs.HideApps;
  var card=Card(x,y,W,H,clear,H/2);var glass=AddGlass(card,0,x,y,H/2,tint:.06f,shadow:.08f,shadowY:5,blur:16,focusShadow:.08f);
  card.Add(Label("Show Apps tab",32,0,W-160,H,28,white,"Semibold"));
  var track=new View{Position2D=new Position2D(W-32-76,18),Size2D=new Size2D(76,36),CornerRadius=new Vector4(18,18,18,18),BackgroundColor=on?onNowGreen:new Color(1,1,1,.22f)};
  track.Add(new View{Position2D=new Position2D(on?44:4,4),Size2D=new Size2D(28,28),CornerRadius=new Vector4(14,14,14,14),BackgroundColor=white});
  card.Add(track);
  slots.Add(new Slot{Card=card,Glass=glass,Name="Show Apps tab",Action="toggle:apps",Row=2,X=x,Y=y,W=W,H=H});
 }
 void InfoTick()
 {
  try{
   long now=Environment.TickCount64;
   var all=InfoStats.NetDev();
   if(all.TryGetValue(infoIface,out var cur)){
    if(liveTotal!=null)liveTotal.Text=TotalsText(all);
    if(lastNet.HasValue&&now>lastNetAt){
     double dt=(now-lastNetAt)/1000.0,rx=Math.Max(0,(cur.rx-lastNet.Value.rx)/dt),tx=Math.Max(0,(cur.tx-lastNet.Value.tx)/dt);
     Array.Copy(sparkValues,1,sparkValues,0,sparkValues.Length-1);sparkValues[sparkValues.Length-1]=rx+tx;
     lastRateText=liveRate.Text="↓ "+InfoStats.Bytes(rx)+"/s   ↑ "+InfoStats.Bytes(tx)+"/s";
     DrawSpark();
    }
    lastNet=cur;lastNetAt=now;
   }else if(liveRate!=null)liveRate.Text="Unavailable";
   var cpu=InfoStats.Cpu();
   if(cpu.HasValue&&lastCpu.HasValue&&cpu.Value.total>lastCpu.Value.total){
    double busy=100.0*(1-(double)(cpu.Value.idle-lastCpu.Value.idle)/(cpu.Value.total-lastCpu.Value.total));
    int cores=InfoStats.Cores();lastCpuText=liveCpu.Text=Math.Round(Math.Max(0,Math.Min(100,busy)))+"% busy"+(cores>0?" · "+cores+" cores":"");
   }
   lastCpu=cpu;
   if(liveMem!=null)liveMem.Text=MemText(InfoStats.Memory());
  }catch{}
 }
}
