using System;
using System.IO;
using System.Text.Json;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Notifications on the TV (tvOS-style banner), on any input. The Home Assistant bridge (silentglass-ha) accepts the
// "Notifications for Android TV" protocol on port 7676, drops anything while the screen is off or Ambient is up, and
// hands the rest to SilentGlass as data/notice.json (+ the image next to it). SilentGlass picks the file up at once
// while it runs (file watcher) - or, if it is not running, the bridge starts it with quiet_notice=1 and it starts
// hidden. Taking the file is the acknowledgement (it is deleted).
// The banner is its own small notification-level window that never takes focus: the remote keeps controlling
// whatever is on screen, and the picture is never touched. A newer notification replaces the one showing.
// Layout numbers mirror tools/overlay_mockup.py (notice).
public sealed partial class QuietHome
{
 Window nw;View nCard;Tizen.NUI.Timer nTimer;Animation nAnimation;FileSystemWatcher nWatch;
 string noticeState="none";
 const int NoticeMargin=56,NoticeR=36,NoticePad=14,NoticeTextPad=26,NoticeMaxAgeMs=30000;

 sealed class Notice { public string Title="",Message="",Size="large",Position="bottom-right",Image=""; public int W,H,Duration=5; public long At; }

 static string NoticeFile=>DataFile("notice.json");
 // a notice waiting at start-up means the bridge started us for it: start hidden (no flash over the picture)
 static bool NoticePending(){try{var f=new FileInfo(NoticeFile);return f.Exists&&(DateTime.UtcNow-f.LastWriteTimeUtc).TotalSeconds<15;}catch{return false;}}
 void WatchNotices()
 {
  try{
   nWatch=new FileSystemWatcher(System.IO.Path.GetDirectoryName(NoticeFile),"notice.json"){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite};
   FileSystemEventHandler on=(s,e)=>ui?.Post(_=>TakeNotice("watch"),null);
   nWatch.Created+=on;nWatch.Changed+=on;nWatch.Renamed+=(s,e)=>ui?.Post(_=>TakeNotice("watch"),null);
   nWatch.EnableRaisingEvents=true;
  }catch(Exception ex){noticeState="watch-failed:"+ex.GetType().Name;}
 }
 void TakeNotice(string via)
 {
  string text;
  try{if(!File.Exists(NoticeFile))return;text=File.ReadAllText(NoticeFile);File.Delete(NoticeFile);}catch{return;}
  var n=ParseNotice(text);
  if(n==null){noticeState="bad";return;}
  if(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-n.At>NoticeMaxAgeMs){noticeState="stale";return;}
  ShowNotice(n);noticeState="shown:"+via;MarkState("notice:"+via);
 }
 static Notice ParseNotice(string json)
 {
  try{
   using var d=JsonDocument.Parse(json);var r=d.RootElement;
   string S(string k)=>r.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():"";
   int I(string k)=>r.TryGetProperty(k,out var v)&&v.TryGetInt32(out var i)?i:0;
   var n=new Notice{Title=S("title"),Message=S("message"),Size=S("size"),Position=S("position"),Image=S("image"),W=I("w"),H=I("h"),Duration=I("duration"),
    At=r.TryGetProperty("at",out var a)&&a.TryGetInt64(out var t)?t:0};
   // only a bare file name in our own data dir
   if(n.Image.Length>0&&(n.Image.Contains('/')||n.Image.Contains("..")||!File.Exists(DataFile(n.Image))))n.Image="";
   return n.Title.Length>0||n.Message.Length>0||n.Image.Length>0?n:null;
  }catch{return null;}
 }

 public static int NoticeWidth(string size)=>size switch{"small"=>420,"medium"=>520,"max"=>800,_=>640};
 // the image keeps its own shape between 16:9 and 3:2 (taller ones are cropped to 3:2)
 public static int NoticeImageHeight(int innerW,int w,int h)=>w<=0||h<=0?innerW*9/16:(int)(innerW*Math.Clamp((double)h/w,9/16.0,2/3.0));
 public static (int x,int y) NoticeOrigin(string position,int w,int h)=>position switch{
  "top-right"=>(1920-NoticeMargin-w,NoticeMargin),"top-left"=>(NoticeMargin,NoticeMargin),"bottom-left"=>(NoticeMargin,1080-NoticeMargin-h),
  "center"=>((1920-w)/2,(1080-h)/2),_=>(1920-NoticeMargin-w,1080-NoticeMargin-h)};

 bool EnsureNoticeWindow()
 {
  if(nw!=null)return true;
  try{
   nw=new Window("SilentGlass Notice",new Rectangle(1920-NoticeMargin-640,600,640,400),true);
   nw.Hide();
   nw.Type=WindowType.Notification;
   nw.SetNotificationLevel(NotificationLevel.Top);
   nw.AddAuxiliaryHint("wm.policy.win.user.geometry","1");
   nw.AddAuxiliaryHint("wm.comp.win.effect.enable","0");
   nw.BackgroundColor=new Color(0,0,0,0);
   nw.SetAcceptFocus(false); // never takes the remote away from what is on screen
   return true;
  }catch(Exception ex){noticeState="window-failed:"+ex.GetType().Name;nw=null;return false;}
 }
 void ShowNotice(Notice n)
 {
  if(!EnsureNoticeWindow())return;
  StopAnimation(ref nAnimation);
  if(nCard!=null){nw.Remove(nCard);nCard.Dispose();nCard=null;}
  int w=NoticeWidth(n.Size),inner=w-2*NoticePad,tw=w-2*NoticeTextPad;
  bool img=n.Image.Length>0;int ih=img?NoticeImageHeight(inner,n.W,n.H):0;
  var title=Label(n.Title,NoticeTextPad,0,tw,10,30,white,"Semibold");title.MultiLine=true;title.Ellipsis=false;
  var msg=Label(n.Message,NoticeTextPad,0,tw,10,25,new Color(1,1,1,.72f));msg.MultiLine=true;msg.Ellipsis=false;
  int th=n.Title.Length>0?(int)Math.Ceiling(title.GetHeightForWidth(tw)):0,mh=n.Message.Length>0?(int)Math.Ceiling(msg.GetHeightForWidth(tw)):0;
  mh=Math.Min(mh,3*34); // three lines at most
  int y=img?NoticePad+ih+20:24;
  title.Position2D=new Position2D(NoticeTextPad,y);title.Size2D=new Size2D(tw,Math.Max(th,1));y+=th+(th>0&&mh>0?4:0);
  msg.Position2D=new Position2D(NoticeTextPad,y);msg.Size2D=new Size2D(tw,Math.Max(mh,1));y+=mh+24;
  int h=y;
  nCard=new View{Size2D=new Size2D(w,h),BackgroundColor=new Color(.10f,.10f,.12f,.90f),CornerRadius=new Vector4(NoticeR,NoticeR,NoticeR,NoticeR),
   BorderlineWidth=1.5f,BorderlineColor=new Color(1,1,1,.16f),BorderlineOffset=-1};
  if(img){
   var pic=new ImageView{ResourceUrl=DataFile(n.Image),Position2D=new Position2D(NoticePad,NoticePad),Size2D=new Size2D(inner,ih),
    CornerRadius=new Vector4(NoticeR-NoticePad,NoticeR-NoticePad,NoticeR-NoticePad,NoticeR-NoticePad)};
   // crop to fill (centre), from the size the bridge read from the image header
   if(n.W>0&&n.H>0){double want=(double)ih/inner,have=(double)n.H/n.W;
    if(have>want){float f=(float)(want/have);pic.PixelArea=new RelativeVector4(0,(1-f)/2,1,f);}
    else if(have<want){float f=(float)(have/want);pic.PixelArea=new RelativeVector4((1-f)/2,0,f,1);}}
   nCard.Add(pic);
  }
  if(n.Title.Length>0)nCard.Add(title);else title.Dispose();
  if(n.Message.Length>0)nCard.Add(msg);else msg.Dispose();
  var (x,oy)=NoticeOrigin(n.Position,w,h);
  try{nw.WindowPositionSize=new Rectangle(x,oy,w,h);}catch{}
  nw.Add(nCard);
  // in from the nearer screen edge
  int from=n.Position.StartsWith("top")?-16:16;
  nCard.Opacity=0;nCard.Position2D=new Position2D(0,from);
  nAnimation=new Animation(240){DefaultAlphaFunction=Ease()};
  nAnimation.AnimateTo(nCard,"Opacity",1f);nAnimation.AnimateTo(nCard,"PositionY",0f);
  nw.Show();nw.Raise();nAnimation.Play();
  int secs=Math.Clamp(n.Duration<=0?5:n.Duration,3,60);
  nTimer?.Stop();nTimer=new Tizen.NUI.Timer((uint)(secs*1000));
  nTimer.Tick+=(s,e)=>{HideNotice();return false;};nTimer.Start();
 }
 void HideNotice()
 {
  if(nCard==null||nw==null)return;
  StopAnimation(ref nAnimation);
  var card=nCard;
  nAnimation=new Animation(300){DefaultAlphaFunction=Ease()};
  nAnimation.AnimateTo(card,"Opacity",0f);
  nAnimation.Finished+=(s,e)=>{if(nCard!=card)return;try{nw.Hide();}catch{}nw.Remove(card);card.Dispose();nCard=null;noticeState="hidden";};
  nAnimation.Play();
 }
}
