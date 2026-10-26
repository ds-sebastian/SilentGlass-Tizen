using System;
using System.Collections.Generic;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Info page: four status cards that answer "is it fine?" at a glance (a coloured glyph disc + one big word, details
// below), then Quiet Home's own settings as a grid of tiles. Only the settings take focus. Live values refresh every
// 2 s and only while the page is visible.
public sealed partial class QuietHome
{
 Tizen.NUI.Timer infoTimer;
 readonly Color good=new Color(.45f,.88f,.58f,1),warn=new Color(1f,.74f,.36f,1),neutral=new Color(.56f,.58f,.64f,1);
 string infoIface="eth0",lastRateText="Measuring…",lastCpuText,infoThread="";int infoMarkers=-2,infoDisabled=-2;
 (long rx,long tx)? lastNet;(long idle,long total)? lastCpu;long lastNetAt=-100000;

 // ---- status cards (not focusable) ----
 const int CardY=150,CardW=415,CardH=380,CardGap=20;
 sealed class Status { public View Card,Disc;public TextLabel Head;public readonly List<TextLabel> Rows=new List<TextLabel>(); }
 Status StatusCard(int n,string title,string glyph,params string[] keys)
 {
  int x=100+n*(CardW+CardGap);
  var s=new Status{Card=Card(x,CardY,CardW,CardH,clear,30)};
  AddGlass(s.Card,0,x,CardY,30,tint:.06f,shadow:.08f,shadowY:5,blur:16,focusShadow:0);
  s.Disc=new View{Position2D=new Position2D(28,28),Size2D=new Size2D(64,64),CornerRadius=new Vector4(32,32,32,32),BackgroundColor=neutral};
  s.Disc.Add(new ImageView{ResourceUrl=Root+"glyph-"+glyph+".svg",Position2D=new Position2D(12,12),Size2D=new Size2D(40,40)});
  s.Card.Add(s.Disc);
  s.Card.Add(Label(title,108,26,CardW-136,30,22,muted,"Semibold"));
  s.Head=Label("",108,52,CardW-136,50,38,white,"Display");s.Card.Add(s.Head);
  s.Card.Add(new View{Position2D=new Position2D(28,124),Size2D=new Size2D(CardW-56,1),BackgroundColor=new Color(1,1,1,.10f)});
  for(int i=0;i<keys.Length;i++){
   int y=144+i*52;
   s.Card.Add(Label(keys[i],28,y,118,40,21,muted));
   var v=Label("",146,y,CardW-174,40,22,ink,"Semibold");v.HorizontalAlignment=HorizontalAlignment.End;s.Card.Add(v);s.Rows.Add(v);
  }
  return s;
 }
 static void Show(Status s,string head,Color c,params string[] rows)
 {
  s.Head.Text=head;s.Disc.BackgroundColor=c;
  for(int i=0;i<rows.Length&&i<s.Rows.Count;i++)s.Rows[i].Text=rows[i]??"—";
 }
 Status netCard,threadCard,tvCard;
 readonly List<View> spark=new List<View>();
 readonly double[] sparkValues=new double[24];

 // ---- settings tiles: glyph disc, name, one line of explanation; a switch (green = on) or the current choice ----
 const int SetY=610,SetW=560,SetH=120,SetGap=20;
 (int x,int y) SetCell(int n)=>(100+n%3*(SetW+SetGap),SetY+n/3*(SetH+SetGap));
 void SettingTile(int n,string name,string sub,string glyph,string action,bool? on,string choice=null)
 {
  var (x,y)=SetCell(n);
  var card=Card(x,y,SetW,SetH,clear,30);var glass=AddGlass(card,0,x,y,30);
  var disc=new View{Position2D=new Position2D(24,24),Size2D=new Size2D(72,72),BackgroundColor=discFill,CornerRadius=new Vector4(36,36,36,36)};
  var g=new ImageView{ResourceUrl=Root+"glyph-"+glyph+".svg",Position2D=new Position2D(14,14),Size2D=new Size2D(44,44)};disc.Add(g);card.Add(disc);
  var title=Label(name,116,22,SetW-250,42,28,white,"Semibold");card.Add(title);
  var value=Label(sub,116,64,SetW-250,34,22,muted);card.Add(value);
  if(on.HasValue){
   // the off track stays visible on the bright focused glass too
   var track=new View{Position2D=new Position2D(SetW-28-80,41),Size2D=new Size2D(80,38),CornerRadius=new Vector4(19,19,19,19),BackgroundColor=on.Value?onNowGreen:new Color(.5f,.5f,.55f,.55f)};
   track.Add(new View{Position2D=new Position2D(on.Value?47:5,5),Size2D=new Size2D(28,28),CornerRadius=new Vector4(14,14,14,14),BackgroundColor=white});
   card.Add(track);
  }
  else{var c=Label(choice,SetW-28-110,40,110,40,26,ink,"Semibold");c.HorizontalAlignment=HorizontalAlignment.End;card.Add(c);}
  slots.Add(new Slot{Card=card,Glass=glass,Label=value,Title=title,Glyph=g,Disc=disc,Name=name,Action=action,Row=n/3,X=x,Y=y,W=SetW,H=SetH,Kind=2});
 }
 // fills the last grid cell: what is running (not focusable)
 void AboutTile(int n)
 {
  var (x,y)=SetCell(n);
  var card=Card(x,y,SetW,SetH,clear,30);AddGlass(card,0,x,y,30,tint:.03f,shadow:0,shadowY:0,blur:8,focusShadow:0);
  card.Add(Label("Quiet Home "+StyleVersion.Replace("liquid-",""),28,22,SetW-56,42,26,muted,"Semibold"));
  card.Add(Label("Liquid Glass · "+60/style.Vsync+" fps · nothing sent anywhere",28,64,SetW-56,34,22,neutral));
 }

 void BuildInfo()
 {
  string route=InfoStats.DefaultInterface();infoIface=route??"eth0";
  var netdev=InfoStats.NetDev();
  bool fresh=Environment.TickCount64-lastNetAt>10000;
  if(fresh){Array.Clear(sparkValues,0,sparkValues.Length);lastNet=null;lastCpu=null;lastRateText="Measuring…";}

  // Privacy: everything this mod switched off is still off
  var pv=StatusCard(0,"Privacy","shield","Blocks","Apps off","Developer");
  infoMarkers=InfoStats.MarkersInPlace();infoDisabled=InfoStats.DisabledAppCount("com.samsung.tv.bixby-promotion");
  int marks=InfoStats.VendorMarkers.Length,apps=InfoStats.DisabledApps.Length;
  bool known=infoMarkers>=0&&infoDisabled>=0,ok=infoMarkers==marks&&infoDisabled==apps;
  Show(pv,!known?"Unknown":ok?"Protected":"Check",!known?neutral:ok?good:warn,
   infoMarkers<0?null:infoMarkers+" of "+marks,infoDisabled<0?null:infoDisabled+" of "+apps,InfoStats.SdbListening()?"SDB open":"Closed");

  // Network: how the TV is connected; the live rate with a small sparkline along the bottom
  netCard=StatusCard(1,"Network","network","Address","Speed","Now");
  bool wifi=infoIface.StartsWith("wlan");
  string link=route==null?"Offline":wifi?"Wi-Fi":"Wired";
  string detail=route==null?null:wifi?(InfoStats.WifiLevel(infoIface) is int dbm?dbm+" dBm":null):InfoStats.LinkSpeed(infoIface);
  // one short word up top; details in rows (rates as one combined number so nothing is ever cut off)
  Show(netCard,link,route==null?warn:good,InfoStats.Ipv4(infoIface),detail,lastRateText);
  spark.Clear();
  for(int i=0;i<sparkValues.Length;i++){
   var b=new View{Position2D=new Position2D(28+i*15,CardH-27),Size2D=new Size2D(9,3),BackgroundColor=new Color(.4f,.78f,1f,.85f),CornerRadius=new Vector4(3,3,3,3)};
   spark.Add(b);netCard.Card.Add(b);
  }
  DrawSpark();

  threadCard=StatusCard(2,"Thread","thread","Home","Traffic","Mesh");
  UpdateThread(netdev);

  tvCard=StatusCard(3,"This TV","tv","Model","Software","Memory","Processor");
  string ver=InfoStats.SystemInfo("http://tizen.org/feature/platform.version");
  string build=InfoStats.ParseBuildDate(InfoStats.SystemInfo("http://tizen.org/system/build.string"));
  var up=InfoStats.Uptime();int cores=InfoStats.Cores();
  Show(tvCard,up.HasValue?"Up "+InfoStats.Duration(up.Value):"Running",good,
   InfoStats.SystemInfo("http://tizen.org/system/model_name"),(ver!=null?"Tizen "+ver:"Tizen")+(build!=null?" · "+build.Substring(0,7):""),
   MemText(InfoStats.Memory()),lastCpuText!=null&&!fresh?lastCpuText:"Measuring…");

  // Quiet Home's own settings (saved in the app's data directory: they survive reboots)
  Text("Quiet Home",108,SetY-50,600,40,26,muted,"Semibold");
  int n=0;
  SettingTile(n++,"Apps tab","Samsung apps and Home","apps","toggle:apps",!prefs.HideApps);
  SettingTile(n++,"Live preview","Video in the input card","picture","toggle:live",prefs.Live);
  SettingTile(n++,"Dim when idle","Black after 5 minutes","sleep","toggle:dim",prefs.Dim);
  if(wallpaper!=null)SettingTile(n++,"Wallpaper",prefs.Wall=="auto"?"Follows sunset":prefs.Wall=="day"?"Always day":"Always night","wallpaper","toggle:wall",null,
   prefs.Wall=="auto"?"Auto":prefs.Wall=="day"?"Day":"Night");
  if(ThreadSwitchable())SettingTile(n++,"Thread border router","Smart-home mesh","thread","toggle:thread",ThreadWanted());
  if(n<6)AboutTile(n);

  if(fresh)InfoTick();
  if(infoTimer==null){infoTimer=new Tizen.NUI.Timer(2000);infoTimer.Tick+=(s,e)=>{if(page!=3)return false;InfoTick();return true;};}
  infoTimer.Start();
 }
 void DrawSpark()
 {
  double peak=50000;foreach(var v in sparkValues)peak=Math.Max(peak,v);
  for(int i=0;i<spark.Count;i++){int h=Math.Max(3,(int)(34*sparkValues[i]/peak));spark[i].Size2D=new Size2D(9,h);spark[i].Position2D=new Position2D(28+i*15,CardH-24-h);}
 }
 static string MemText((long totalKb,long availableKb)? m)=>m.HasValue?InfoStats.Bytes((m.Value.totalKb-m.Value.availableKb)*1024.0)+" of "+InfoStats.Bytes(m.Value.totalKb*1024.0):null;

 // role from wpan0's RLOC/ALOC addresses, home reach from forwarding + an off-mesh address; also called from InfoTick,
 // so the card follows the border router starting or stopping after a flip of its switch
 void UpdateThread(Dictionary<string,(long rx,long tx)> netdev)
 {
  if(threadCard==null)return;
  var th=InfoStats.Thread();bool fwd=InfoStats.Ipv6Forwarding(),wanted=ThreadWanted();
  bool routing=th.present&&th.rloc16>=0&&(th.rloc16&0x1ff)==0;
  string head=!th.present?(!ThreadSwitchable()?"Not set up":wanted?"Not running":"Paused"):!wanted?"Stopping…":th.rloc16<0?"Starting…":routing?(th.leader?"Leader":"Router"):"Child";
  string home=!th.present?null:!fwd?"No forwarding":th.homeRoute?"Connected":"Waiting";
  Show(threadCard,head,routing&&wanted?good:th.present?warn:neutral,home,
   netdev.TryGetValue("wpan0",out var w0)?"↓ "+InfoStats.Bytes(w0.rx)+"  ↑ "+InfoStats.Bytes(w0.tx):null,
   th.present&&th.rloc16>=0?"Node "+(th.rloc16>>10)+" · "+th.addresses+" addr":null);
  infoThread=head+" | "+(home??"-")+" | fwd="+fwd;
 }
 void InfoTick()
 {
  try{
   long now=Environment.TickCount64;
   var all=InfoStats.NetDev();
   UpdateThread(all);
   if(netCard!=null&&all.TryGetValue(infoIface,out var cur)){
    if(lastNet.HasValue&&now>lastNetAt){
     double dt=(now-lastNetAt)/1000.0,rx=Math.Max(0,(cur.rx-lastNet.Value.rx)/dt),tx=Math.Max(0,(cur.tx-lastNet.Value.tx)/dt);
     Array.Copy(sparkValues,1,sparkValues,0,sparkValues.Length-1);sparkValues[sparkValues.Length-1]=rx+tx;
     lastRateText=netCard.Rows[2].Text=InfoStats.Bytes(rx+tx)+"/s";
     DrawSpark();
    }
    lastNet=cur;lastNetAt=now;
   }
   var cpu=InfoStats.Cpu();
   if(tvCard!=null&&cpu.HasValue&&lastCpu.HasValue&&cpu.Value.total>lastCpu.Value.total){
    double busy=100.0*(1-(double)(cpu.Value.idle-lastCpu.Value.idle)/(cpu.Value.total-lastCpu.Value.total));
    lastCpuText=tvCard.Rows[3].Text=Math.Round(Math.Clamp(busy,0,100))+"% · "+InfoStats.Cores()+" cores";
   }
   lastCpu=cpu;
   if(tvCard!=null)tvCard.Rows[2].Text=MemText(InfoStats.Memory())??"—";
  }catch{}
 }
}
