using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.Applications;

public sealed class QuietHome : NUIApplication
{
 const string Root="/opt/usr/apps/com.samsung.tv.bixby-promotion/res/quiet-home/";
 const string StateFile="/tmp/quiet-home-state.txt";
 sealed class Slot { public View Card,FocusRing,Disc; public TextLabel Label,Title; public ImageView Glyph; public string Name,Action,Description; public int Row,X,Y,W,H,Kind; } // Kind: 0 plain, 1 hero, 2 control tile
 sealed class CachedArtwork { public ImageView Image; public View Parent; }
 readonly List<Slot> slots=new List<Slot>();
 readonly List<View> scene=new List<View>();
 readonly Dictionary<string,CachedArtwork> artworkCache=new Dictionary<string,CachedArtwork>();
 View artworkPreloadRoot;
 int artworkViewsCreated,pageArtworkCold;
 List<LibraryApp> library=new List<LibraryApp>();
 int libraryPage;
 string libraryError="";
 TextLabel clock, notice, detail;
 TextLabel[] tabs;
 View[] tabBg;
 Animation focusAnimation;
 bool fontLoaded;
 Tizen.NUI.Timer clockTimer;
 const int TB=9; // toolbar focus area; 1..3 are content rows
 int page, selected, area=1, lastSource=15;
 string editing="";
 long buildMilliseconds;
 int currentVolume, currentMute;
 readonly Dictionary<string,string> settingValues=new Dictionary<string,string>();
 readonly Dictionary<string,string> settingNames=new Dictionary<string,string>();
 readonly Dictionary<string,bool> settingEnabled=new Dictionary<string,bool>();
 double brightnessMin,brightnessMax=50,brightnessStep=1;
 string lastKey="";
 long lastKeyAt;
 readonly Color ink=new Color(0.95f,0.95f,0.97f,1);
 readonly Color muted=new Color(0.62f,0.63f,0.68f,1);
 readonly Color white=new Color(1,1,1,1);
 readonly Color accent=new Color(0.47f,0.6f,1f,1);
 readonly Color dark=new Color(0.07f,0.07f,0.09f,1);
 [DllImport("libsource-api.so")] static extern int connect_source(int sourceType);
 [DllImport("libvconf.so.0")] static extern int vconf_get_int(string key,out int value);
 [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_create(out IntPtr app);
 [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_set_app_id(IntPtr app,string id);
 [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_set_operation(IntPtr app,string operation);
 [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_add_extra_data(IntPtr app,string key,string value);
 [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_send_launch_request(IntPtr app,IntPtr cb,IntPtr data);
 [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_destroy(IntPtr app);
 [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_get_master_volume(out int value);
 [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_set_master_volume(int value);
 [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_get_master_mute(out int value);
 [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_set_master_mute(int value);
 [DllImport("libsettings-api.so")] static extern bool CSharpGetItemValue(string id,out string name,out string value);
 [DllImport("libsettings-api.so")] static extern bool CSharpSetItemValue(string id,string value);
 [DllImport("libsettings-api.so")] static extern bool CSharpGetItemEnable(string id,out bool enabled);
 [DllImport("libsettings-api.so")] static extern bool CSharpGetItemMinMaxStepValue(string id,out double min,out double max,out double step);



 protected override void OnCreate()
 {
  base.OnCreate();
  try{fontLoaded=FontClient.Instance.AddCustomFontDirectory(Root+"fonts");}catch{fontLoaded=false;}
  try{library=AppLibrary.Load(Root+"apps.json");}catch(Exception ex){libraryError=ex.Message;}
  // Samsung Home lives on the Apps page now (no longer on Home); keep it before the "Get more apps" utility.
  if(!library.Exists(a=>a.Id=="com.samsung.tv.csfs")){int at=library.FindIndex(a=>a.Utility);var sh=new LibraryApp{Name="Samsung Home",Id="com.samsung.tv.csfs",Asset="tile-samsung-home.svg",Description="Samsung's home screen",Fit="cover",Background="#1C2638",Width=640,Height=360};if(at<0)library.Add(sh);else library.Insert(at,sh);}
  Window.Default.BackgroundColor=new Color(0,0,0,1);
  Window.Default.Title="Quiet Home";Window.Default.KeyEvent+=OnKey;
  PreloadArtwork();
  RefreshSource();selected=SourceIndex();BuildPage();
  clockTimer=new Tizen.NUI.Timer(30000);
  clockTimer.Tick+=(s,e)=>{if(clock!=null)clock.Text=DateTime.Now.ToString("h:mm tt");IdleTick();return true;};clockTimer.Start();WriteState("created");
 }
 protected override void OnResume(){base.OnResume();Wake(false);clockTimer?.Start();RefreshSource();if(tabs!=null&&page==2){RefreshSettings();UpdateFocus(false);}if(tabs!=null&&page==3)infoTimer?.Start();WriteState("resumed");}
 protected override void OnAppControlReceived(AppControlReceivedEventArgs e){base.OnAppControlReceived(e);Wake(false);string requested; if(e.ReceivedAppControl.ExtraData.TryGet("quiet_page",out requested)&&requested=="settings")OpenSettings("settings_request");else if(requested=="apps"){page=1;area=1;selected=0;BuildPage();}else if(requested=="info"){page=3;area=1;selected=0;BuildPage();}else Home("home_request");}
 protected override void OnPause(){base.OnPause();editing="";CloseInputs(false);clockTimer?.Stop();infoTimer?.Stop();WriteState("paused");}
 void Home(string activity){clockTimer?.Start();RefreshSource();if(tabs!=null){bool unchanged=page==0&&area==1&&editing==""&&selected==SourceIndex();page=0;area=1;editing="";selected=SourceIndex();if(!unchanged)BuildPage();WriteState(activity);}}
 void OpenSettings(string activity){clockTimer?.Start();page=2;area=1;selected=0;editing="";BuildPage();WriteState(activity);}
 void RefreshSource(){try{int n;if(vconf_get_int("memory/system/source_type",out n)==0&&(n==13||n==15))lastSource=n;}catch{}}
 int SourceIndex(){return lastSource==13?1:0;}
 View Add(View v){scene.Add(v);Window.Default.Add(v);return v;}
 View Panel(int x,int y,int w,int h,Color color,int radius=18){return Add(new View{Position2D=new Position2D(x,y),Size2D=new Size2D(w,h),BackgroundColor=color,CornerRadius=new Vector4(radius,radius,radius,radius)});}
 TextLabel Text(string text,int x,int y,int w,int h,float size,Color color,string face="Medium")
 {return (TextLabel)Add(Label(text,x,y,w,h,size,color,face));}
 TextLabel Label(string text,int x,int y,int w,int h,float size,Color color,string face="Medium")
 {return new TextLabel{Text=text,Position2D=new Position2D(x,y),Size2D=new Size2D(w,h),PixelSize=size,FontFamily="Quiet Inter "+face,TextColor=color,VerticalAlignment=VerticalAlignment.Center};}
 void BuildPage()
 {
  var stopwatch=Stopwatch.StartNew();
  if(focusAnimation!=null){focusAnimation.Stop();focusAnimation.Dispose();focusAnimation=null;}
  ParkArtwork();pageArtworkCold=0;
  infoTimer?.Stop();CloseInputs(false);glowWatch=glowPlay=null;
  foreach(var v in scene){Window.Default.Remove(v);v.Dispose();}scene.Clear();slots.Clear();
  Add(new ImageView{ResourceUrl=Root+"free-background.svg",Size2D=new Size2D(1920,1080)});
  // tvOS-style centred tab bar; the selected tab stays lit, and turns white while it has focus
  string[] nav={"Home","Apps","Controls","Info"};int[] tw={132,122,178,110};int total=12;foreach(var w in tw)total+=w;
  int px=960-total/2;Panel(px,36,total,68,new Color(1,1,1,.10f),34);
  tabs=new TextLabel[4];tabBg=new View[4];int tabX=px+6;
  for(int i=0;i<tabs.Length;i++){tabBg[i]=Panel(tabX,42,tw[i],56,new Color(0,0,0,0),28);tabs[i]=Text(nav[i],tabX,42,tw[i],56,28,muted);tabs[i].HorizontalAlignment=HorizontalAlignment.Center;tabX+=tw[i];}
  clock=Text(DateTime.Now.ToString("h:mm tt"),1520,40,300,60,30,muted);clock.HorizontalAlignment=HorizontalAlignment.End;
  if(page==0){
   RefreshSettings();
   // SHIELD is the main device: the wide Watch card carries the apps that live on it.
   inputList=InfoStats.Sources();
   glowWatch=(ImageView)Add(new ImageView{ResourceUrl=Root+"glow-watch.svg",Size2D=new Size2D(1920,1080),DesiredWidth=480,DesiredHeight=270,Opacity=0});
   glowPlay=(ImageView)Add(new ImageView{ResourceUrl=Root+"glow-play.svg",Size2D=new Size2D(1920,1080),DesiredWidth=480,DesiredHeight=270,Opacity=0});
   var watch=Hero("Watch","hdmi:15","SHIELD","feature-watch.svg",100,170,1100,560,new Color(.76f,.94f,.56f,1));
   HeroApps(watch,"rTn6ClEurC.Jellyfin","com.samsung.tv.cobalt-yt");
   Hero("Play","hdmi:13","Nintendo Switch","feature-play.svg",1240,170,580,560,new Color(1f,.74f,.78f,1));
   Control("Inputs","inputs","inputs",0);
   Control("Ambient","ambient","ambient",1);
   Control("Sleep Timer","sleeptimer","sleep",2);
   Control("Sound","output","output",3);
   Control("More","page:2","more",4);
  }else if(page==1){
   BuildLibrary();
  }else if(page==3){
   BuildInfo();
  }else{
   RefreshSettings();
   SettingsTile("Volume","volume","Left / Right to adjust","volume",0,0);
   SettingsTile("Sound","mute","","mute",1,0);
   SettingsTile("Soundbar Mode","cycle:external-sound-mode","","soundmode",2,0);
   SettingsTile("Output","output","","output",3,0);
   SettingsTile("Picture Mode","cycle:picturemode","","picture",0,1);
   SettingsTile("Brightness","brightness","Left / Right to adjust","brightness",1,1);
   SettingsTile("Game Mode","game-mode","","game",2,1);
   SettingsTile("Auto Game Mode","cycle:game-mode-auto","","autogame",3,1);
   SettingsTile("Sleep Timer","sleeptimer","","sleep",0,2);
   SettingsTile("Network","network","","network",1,2);
   SettingsTile("Bluetooth","controllers","","bluetooth",2,2);
   SettingsTile("Advanced","app:org.tizen.menu","","settings",3,2);
  }
  detail=Text("",100,952,1720,40,24,muted);detail.HorizontalAlignment=HorizontalAlignment.Center;
  notice=Text("",100,1000,1720,40,24,new Color(1f,.76f,.46f,1));notice.HorizontalAlignment=HorizontalAlignment.Center;
  selected=Math.Max(0,Math.Min(selected,slots.Count-1));UpdateFocus(false);
  buildMilliseconds=stopwatch.ElapsedMilliseconds;WriteState("page_ready");
 }
 void BuildLibrary()
 {
  if(library.Count==0){Text("Apps could not be loaded.",100,300,1500,70,32,ink);return;}
  libraryPage=Math.Clamp(libraryPage,0,AppLibrary.PageCount(library.Count)-1);
  int start=libraryPage*AppLibrary.PageSize,count=AppLibrary.VisibleCount(library.Count,libraryPage);
  for(int i=0;i<count;i++)
  {
   var app=library[start+i];int x=100+(i%5)*346,y=180+(i/5)*262,w=316,h=178;
   var card=Panel(x+w/2,y+h/2,w,h,HexColor(app.Background),18);
   card.PivotPoint=new Position(.5f,.5f,.5f);card.PositionUsesPivotPoint=true;
   AddAppArtwork(card,app,w,h);
   var ring=new View{Size2D=new Size2D(w,h),BackgroundColor=new Color(0,0,0,0),CornerRadius=new Vector4(18,18,18,18),BorderlineWidth=4,BorderlineColor=white,Opacity=0};
   card.Add(ring);
   var label=Text(app.Name,x,y+h+16,w,38,26,muted,"Semibold");label.HorizontalAlignment=HorizontalAlignment.Center;
   slots.Add(new Slot{Card=card,FocusRing=ring,Label=label,Name=app.Name,Action="app:"+app.Id,Description=app.Description,Row=i/5,X=x,Y=y,W=w,H=h});
  }
  if(AppLibrary.PageCount(library.Count)>1){
   var pages=Text((libraryPage+1)+" / "+AppLibrary.PageCount(library.Count),1620,120,200,30,22,muted);
   pages.HorizontalAlignment=HorizontalAlignment.End;
  }
 }
 Color HexColor(string hex)
 {
  try{int n=Convert.ToInt32(hex.TrimStart('#'),16);return new Color(((n>>16)&255)/255f,((n>>8)&255)/255f,(n&255)/255f,1);}
  catch{return white;}
 }
 void AddAppArtwork(View card,LibraryApp app,int w,int h)
 {
  if(!app.Utility){
   var cached=GetArtwork(app,w,h);
   if(cached.Image.LoadingStatus!=ImageView.LoadingStatusType.Ready)pageArtworkCold++;
   cached.Parent.Remove(cached.Image);card.Add(cached.Image);cached.Parent=card;
  }
  if(app.Utility){
   card.Add(new View{Position2D=new Position2D(w/2-28,h/2-2),Size2D=new Size2D(56,4),BackgroundColor=ink});
   card.Add(new View{Position2D=new Position2D(w/2-2,h/2-28),Size2D=new Size2D(4,56),BackgroundColor=ink});
  }
 }
 void PreloadArtwork()
 {
  // Keep the real image views alive on the scene; moving between pages never
  // destroys their decoded textures. The zero-opacity parent is never drawn.
  artworkPreloadRoot=new View{Size2D=new Size2D(1920,1080),Opacity=0};
  Window.Default.Add(artworkPreloadRoot);
  foreach(var app in library)if(!app.Utility)GetArtwork(app,316,178);
  foreach(var id in new[]{"com.samsung.tv.cobalt-yt","rTn6ClEurC.Jellyfin"}){
   var app=library.Find(a=>a.Id==id);if(app!=null)GetArtwork(app,210,118);
  }
 }
 CachedArtwork GetArtwork(LibraryApp app,int w,int h)
 {
  string key=app.Id+"@"+w+"x"+h;
  if(artworkCache.TryGetValue(key,out var cached))return cached;
  bool cover=app.Fit=="cover",full=cover||app.Fit=="contain-full";
  int iw=full?w:w-54,ih=full?h:h-40;var crop=app.Crop;
  float cropWidth=crop==null?1:crop[2],cropHeight=crop==null?1:crop[3];
  var image=new ImageView{
   Position2D=new Position2D((w-iw)/2,(h-ih)/2),Size2D=new Size2D(iw,ih),
   FittingMode=cover?FittingModeType.ScaleToFill:FittingModeType.ShrinkToFit,
   SamplingMode=SamplingModeType.BoxThenLanczos,
   DesiredWidth=(int)Math.Ceiling(iw*2/cropWidth),DesiredHeight=(int)Math.Ceiling(ih*2/cropHeight),
   ReleasePolicy=ReleasePolicyType.Destroyed,CornerRadius=new Vector4(18,18,18,18)};
  if(crop!=null)image.PixelArea=new RelativeVector4(crop[0],crop[1],crop[2],crop[3]);
  cached=new CachedArtwork{Image=image,Parent=artworkPreloadRoot};
  artworkCache.Add(key,cached);artworkViewsCreated++;
  image.ResourceReady+=(s,e)=>WriteState("artwork_ready");
  artworkPreloadRoot.Add(image);image.ResourceUrl=Root+app.Asset;
  return cached;
 }
 void ParkArtwork()
 {
  foreach(var cached in artworkCache.Values)if(cached.Parent!=artworkPreloadRoot){
   cached.Parent.Remove(cached.Image);artworkPreloadRoot.Add(cached.Image);cached.Parent=artworkPreloadRoot;
  }
 }
 int ArtworkReadyCount()
 {
  int count=0;foreach(var cached in artworkCache.Values)
   if(cached.Image.LoadingStatus==ImageView.LoadingStatusType.Ready)count++;
  return count;
 }
 string ArtworkPending()
 {
  var pending=new List<string>();
  foreach(var entry in artworkCache)
   if(entry.Value.Image.LoadingStatus!=ImageView.LoadingStatusType.Ready)
    pending.Add(entry.Key+":"+entry.Value.Image.LoadingStatus);
  return string.Join(";",pending);
 }
 void SettingsTile(string name,string action,string description,string icon,int column,int row)
 {
  int w=409,h=236,x=100+column*(w+28),y=150+row*262;
  var card=Panel(x+w/2,y+h/2,w,h,new Color(1,1,1,.10f),30);card.PivotPoint=new Position(.5f,.5f,.5f);card.PositionUsesPivotPoint=true;
  var disc=new View{Position2D=new Position2D(26,26),Size2D=new Size2D(84,84),BackgroundColor=new Color(1,1,1,.14f),CornerRadius=new Vector4(42,42,42,42)};
  var glyph=new ImageView{ResourceUrl=Root+"glyph-"+icon+".svg",Position2D=new Position2D(16,16),Size2D=new Size2D(52,52)};disc.Add(glyph);card.Add(disc);
  var title=Label(name,28,124,w-56,38,26,new Color(1,1,1,.78f),"Semibold");title.Ellipsis=true;card.Add(title);
  var value=Label("",28,164,w-56,50,36,white,"Display");value.Ellipsis=true;card.Add(value);
  slots.Add(new Slot{Card=card,Label=value,Title=title,Glyph=glyph,Disc=disc,Name=name,Action=action,Description=description,Row=row,X=x,Y=y,W=w,H=h,Kind=2});
 }
 Slot Hero(string word,string action,string sub,string asset,int x,int y,int w,int h,Color subColor)
 {
  var card=Panel(x+w/2,y+h/2,w,h,new Color(0,0,0,0),40);card.PivotPoint=new Position(.5f,.5f,.5f);card.PositionUsesPivotPoint=true;
  card.Add(new ImageView{ResourceUrl=Root+asset,Size2D=new Size2D(w,h)});
  card.Add(Label(word,50,h-206,w-100,116,100,white,"Display"));
  card.Add(Label(sub,56,h-96,w-112,46,30,subColor,"Semibold"));
  var src=inputList.Find(i=>"hdmi:"+i.Type==action);
  // Power state is not shown: on this TV powerState reads "Off" even for the input on screen (devices sit behind the soundbar).
  if(action!="hdmi:"+lastSource&&src!=null&&!src.Connected){
   var off=new View{Position2D=new Position2D(40,36),Size2D=new Size2D(210,50),BackgroundColor=new Color(0,0,0,.55f),CornerRadius=new Vector4(25,25,25,25)};
   off.Add(new View{Position2D=new Position2D(20,18),Size2D=new Size2D(14,14),BackgroundColor=new Color(.62f,.63f,.68f,1),CornerRadius=new Vector4(7,7,7,7)});
   off.Add(Label("Unplugged",44,0,160,50,24,new Color(1,1,1,.85f),"Semibold"));card.Add(off);
  }
  if(action=="hdmi:"+lastSource){
   var badge=new View{Position2D=new Position2D(40,36),Size2D=new Size2D(160,50),BackgroundColor=new Color(1,1,1,.94f),CornerRadius=new Vector4(25,25,25,25)};
   badge.Add(new View{Position2D=new Position2D(20,18),Size2D=new Size2D(14,14),BackgroundColor=new Color(.2f,.78f,.36f,1),CornerRadius=new Vector4(7,7,7,7)});
   badge.Add(Label("On now",44,0,108,50,24,dark,"Semibold"));card.Add(badge);
  }
  var slot=new Slot{Card=card,Name=word,Action=action,Description=sub,Row=0,X=x,Y=y,W=w,H=h,Kind=1};slots.Add(slot);return slot;
 }
 // App artwork chips inside a hero card (right to left) - shows which apps live on that device.
 void HeroApps(Slot hero,params string[] ids)
 {
  int n=0;
  foreach(var id in ids){
   var app=library.Find(a=>a.Id==id);if(app==null||app.Utility)continue;
   var chip=new View{Position2D=new Position2D(hero.W-52-210-n*230,hero.H-52-118),Size2D=new Size2D(210,118),BackgroundColor=HexColor(app.Background),CornerRadius=new Vector4(18,18,18,18)};
   var cached=GetArtwork(app,210,118);
   if(cached.Image.LoadingStatus!=ImageView.LoadingStatusType.Ready)pageArtworkCold++;
   cached.Parent.Remove(cached.Image);chip.Add(cached.Image);cached.Parent=chip;hero.Card.Add(chip);n++;
  }
 }
 // Control Centre style tile: glyph disc, name, current value.
 void Control(string name,string action,string glyph,int n)
 {
  const int Count=5,Gap=25;int w=(1720-(Count-1)*Gap)/Count,h=150,x=100+n*(w+Gap),y=780;
  var card=Panel(x+w/2,y+h/2,w,h,new Color(1,1,1,.10f),30);card.PivotPoint=new Position(.5f,.5f,.5f);card.PositionUsesPivotPoint=true;
  var disc=new View{Position2D=new Position2D(22,35),Size2D=new Size2D(80,80),BackgroundColor=new Color(1,1,1,.14f),CornerRadius=new Vector4(40,40,40,40)};
  var g=new ImageView{ResourceUrl=Root+"glyph-"+glyph+".svg",Position2D=new Position2D(15,15),Size2D=new Size2D(50,50)};disc.Add(g);card.Add(disc);
  var title=Label(name,120,32,w-136,42,28,white,"Semibold");title.Ellipsis=true;card.Add(title);
  var value=Label("",120,76,w-136,40,24,muted);value.Ellipsis=true;card.Add(value);
  slots.Add(new Slot{Card=card,Label=value,Title=title,Glyph=g,Disc=disc,Name=name,Action=action,Description=name,Row=1,X=x,Y=y,W=w,H=h,Kind=2});
 }
 List<int> Row(int row){var r=new List<int>();for(int i=0;i<slots.Count;i++)if(slots[i].Row==row)r.Add(i);return r;}
 int Nearest(List<int> row){var c=slots[selected];float cx=c.X+c.W/2f;int best=row[0];float bd=float.MaxValue;foreach(var i in row){float d=Math.Abs(slots[i].X+slots[i].W/2f-cx);if(d<bd){bd=d;best=i;}}return best;}
 void UpdateFocus(bool animate=true)
 {
  if(focusAnimation!=null){focusAnimation.Stop();focusAnimation.Dispose();focusAnimation=null;}
  if(animate)focusAnimation=new Animation(200);
  for(int i=0;i<tabs.Length;i++){
   bool cur=i==page;
   tabs[i].TextColor=cur&&area==0?dark:cur?white:muted;tabs[i].FontFamily="Quiet Inter "+(cur?"Semibold":"Medium");
   tabBg[i].BackgroundColor=cur?(area==0?white:new Color(1,1,1,.20f)):new Color(0,0,0,0);
  }
  if(page==0||page==2)foreach(var s in slots)if(s.Kind==2&&s.Label!=null)s.Label.Text=TileValue(s.Action);
  for(int i=0;i<slots.Count;i++){
   var s=slots[i];bool f=area!=0&&selected==i;
   float scale=1,lift=0,opacity=1;
   if(s.Kind==1){scale=f?1.045f:1;lift=f?-6:0;opacity=f||area==0?1:.7f;s.Card.BorderlineWidth=f?4:0;s.Card.BorderlineColor=new Color(1,1,1,.95f);}
   else if(s.Kind==2){
    scale=f?1.06f:1;
    s.Card.BackgroundColor=f?white:new Color(1,1,1,.10f);
    s.Disc.BackgroundColor=f?new Color(0,0,0,.07f):new Color(1,1,1,.14f);
    s.Glyph.Color=f?dark:white;
    s.Title.TextColor=f?dark:page==2?new Color(1,1,1,.78f):white;
    s.Label.TextColor=f?(page==2?dark:new Color(.32f,.33f,.37f,1)):page==2?white:muted;
   }
   else if(page==1){scale=f?1.08f:1;lift=f?-6:0;if(s.Label!=null)s.Label.TextColor=f?white:muted;}
   else{scale=f?1.015f:1;s.Card.BorderlineWidth=f?3:0;s.Card.BorderlineColor=new Color(1,1,1,.7f);}
   var pos=new Position(s.X+s.W/2f,s.Y+s.H/2f+lift,0);
   if(animate){focusAnimation.AnimateTo(s.Card,"Scale",new Vector3(scale,scale,1));focusAnimation.AnimateTo(s.Card,"Position",pos);focusAnimation.AnimateTo(s.Card,"Opacity",opacity);}
   else{s.Card.Scale=new Vector3(scale,scale,1);s.Card.Position=pos;s.Card.Opacity=opacity;}
   if(s.FocusRing!=null)s.FocusRing.Opacity=f?1:0;
   if(f)s.Card.RaiseToTop();
  }
  if(page==0&&glowWatch!=null){
   string fa=area==1&&selected<slots.Count?slots[selected].Action:"";
   float gw=fa=="hdmi:15"?1:0,gp=fa=="hdmi:13"?1:0;
   if(animate){focusAnimation.AnimateTo(glowWatch,"Opacity",gw);focusAnimation.AnimateTo(glowPlay,"Opacity",gp);}else{glowWatch.Opacity=gw;glowPlay.Opacity=gp;}
  }
  if(focusAnimation!=null)focusAnimation.Play();
  detail.Text=editing!=""?"Left / Right to adjust · Select to finish":"";
  WriteState("focus");
 }
 void OnKey(object sender,Window.KeyEventArgs e)
 {
  if(e.Key==null||e.Key.State!=Key.StateType.Down)return;string k=e.Key.KeyPressedName;
  if(dimmed){Wake(true);lastKey=k;lastKeyAt=Environment.TickCount64;return;} // first press only wakes
  lastActivity=Environment.TickCount64;
  long now=Environment.TickCount64;if(k==lastKey&&now-lastKeyAt<180)return;lastKey=k;lastKeyAt=now;
  try{
   if(inputsLayer!=null&&HandleInputsKey(k))return;
   if(k=="XF86Home"){Home("home_key");return;}
   if(k=="XF86SysMenu"||k=="XF86More"){OpenSettings("settings_key");return;}
   if(page==1&&HandleLibraryKey(k))return;
   if(editing!=""){if(k=="Left"||k=="Right"){int d=k=="Left"?-1:1;if(editing=="volume")ChangeVolume(d);else ChangeBrightness(d);}else if(k=="Return"||k=="KP_Enter"||k=="XF86Back"||k=="Back"||k=="Escape"){editing="";UpdateFocus();}return;}
   if(k=="Up"){if(area>=2&&area!=TB){area--;selected=Nearest(Row(area-1));}else if(area==1)area=0;UpdateFocus();}
   else if(k=="Down"){if(area==0||area==TB){area=1;selected=page==0?SourceIndex():Row(0)[0];}else if(Row(area).Count>0){area++;selected=Nearest(Row(area-1));}UpdateFocus();}
   else if(k=="Left"||k=="Right"){
    int d=k=="Left"?-1:1;
    if(area==0){int next=Math.Max(0,Math.Min(3,page+d));if(next!=page){page=next;selected=page==0?SourceIndex():0;BuildPage();}}
    else{var r=Row(area-1);int n=r.IndexOf(selected);selected=r[(n+d+r.Count)%r.Count];UpdateFocus();}
   }
   else if(k=="Return"||k=="KP_Enter"){if(area==0){area=1;selected=0;UpdateFocus();}else Activate();}
   else if(k=="XF86Back"||k=="Back"||k=="Escape"||k=="XF86Exit")SelectSource(lastSource);
  }catch(Exception ex){ShowError(ex.Message);}
 }
 bool HandleLibraryKey(string key)
 {
  if(key=="XF86Back"||key=="Back"||key=="Escape"||key=="XF86Exit"){Home("apps_back");return true;}
  if(library.Count==0)return key=="Up"||key=="Down";
  if((area==0||area==TB)&&key=="Down"){area=1;selected=0;UpdateFocus();return true;}
  if(area!=1)return false;
  if(key=="Left"||key=="Right"||key=="Up"||key=="Down"){
   int next=AppLibrary.Move(library.Count,libraryPage*AppLibrary.PageSize+selected,key);
   if(next<0){area=0;UpdateFocus();return true;}
   int nextPage=next/AppLibrary.PageSize;selected=next%AppLibrary.PageSize;
   if(nextPage!=libraryPage){libraryPage=nextPage;BuildPage();}else UpdateFocus();
   return true;
  }
  return false;
 }
 void Activate()
 {
  string a=slots[selected].Action;notice.Text="";
  if(a=="info"){BuildPage();return;}
  if(a=="inputs"){OpenInputs();return;}
  if(a=="ambient"){Launch("org.tizen.art-app");return;}
  if(a.StartsWith("hdmi:")){SelectSource(int.Parse(a.Substring(5)));return;}
  if(a.StartsWith("page:")){page=int.Parse(a.Substring(5));selected=page==0?SourceIndex():0;area=1;BuildPage();return;}
  if(a.StartsWith("app:")){Launch(a.Substring(4));return;}
  if(a=="controllers"){Launch("org.tizen.menu","add-bluetooth-keyboard");return;}
  if(a=="network"){Launch("org.tizen.menu","networksettings");return;}
  if(a=="volume"||a=="brightness"){RefreshSettings();if(a=="brightness"&&!CanEdit(a)){ShowError("Brightness is unavailable for the current input.");return;}editing=a;UpdateFocus();return;}
  if(a=="game-mode"){RefreshSettings();SetSetting(a,SettingValue(a)=="1"?"0":"1");return;}
  if(a.StartsWith("cycle:")){CycleSetting(a.Substring(6));return;}
  if(a=="output"){RefreshSettings();SetSetting("soundoutput",SettingValue("soundoutput")=="0"?"2":"0");return;}
  if(a=="sleeptimer"){RefreshSettings();int n;if(int.TryParse(SettingValue(a),out n))SetSetting(a,((n+1)%5).ToString());return;}
  if(a=="mute"){int n;int r=sound_manager_get_master_mute(out n);if(r==0)r=sound_manager_set_master_mute(n==1?0:1);if(r!=0)ShowError("Use the remote's mute button.");RefreshSettings();UpdateFocus();WriteState("mute_changed");}
 }
 void RefreshSettings(){
  currentVolume=sound_manager_get_master_volume(out var v)==0?v:-1;
  currentMute=sound_manager_get_master_mute(out var m)==0?m:-1;
  foreach(var id in SettingIds){
   try{if(CSharpGetItemValue(id,out var name,out var value)&&!string.IsNullOrEmpty(value)){settingValues[id]=value;settingNames[id]=name;}else{settingValues.Remove(id);settingNames.Remove(id);}
    settingEnabled[id]=CSharpGetItemEnable(id,out var enabled)&&enabled;
   }catch{settingValues.Remove(id);settingNames.Remove(id);settingEnabled[id]=false;}
  }
  try{if(!CSharpGetItemMinMaxStepValue("brightness",out brightnessMin,out brightnessMax,out brightnessStep)||brightnessStep<=0){brightnessMin=0;brightnessMax=50;brightnessStep=1;}}catch{}
 }

 static readonly string[] SettingIds={"brightness","game-mode","sleeptimer","external-sound-mode","soundoutput","picturemode","game-mode-auto"};
 [DllImport("libsettings-api.so")] static extern bool CSharpGetItemDataList(string id,System.Text.StringBuilder list);
 [DllImport("libsettings-api.so")] static extern bool CSharpGetItemDataListEnable(string id,string value,out bool enabled);
 [DllImport("libsettings-api.so")] static extern bool CSharpGetItemDataListVisible(string id,string value,out bool visible);
 // Options the TV reports as enabled AND visible for the current input; numeric values only
 // (the native list can end with garbage bytes).
 List<string> AvailableOptions(string id){
  var result=new List<string>();var sb=new System.Text.StringBuilder(32768);
  try{if(!CSharpGetItemDataList(id,sb))return result;}catch{return result;}
  foreach(var entry in sb.ToString().Split(';')){
   int c=entry.IndexOf(',');string v=c<0?entry:entry.Substring(0,c);
   if(v.Length==0||v.Length>9||!int.TryParse(v,NumberStyles.None,CultureInfo.InvariantCulture,out _))continue;
   try{if(CSharpGetItemDataListEnable(id,v,out var en)&&en&&CSharpGetItemDataListVisible(id,v,out var vis)&&vis&&!result.Contains(v))result.Add(v);}catch{}
  }
  return result;
 }
 void CycleSetting(string id){
  RefreshSettings();var options=AvailableOptions(id);
  if(options.Count<2){ShowError("No other options are available for the current input.");return;}
  int i=options.IndexOf(SettingValue(id));SetSetting(id,options[(i+1)%options.Count]);
 }
 string TileValue(string action){
  switch(action){
   case "volume":return currentVolume<0?"Unavailable":currentVolume.ToString();
   case "mute":return currentMute<0?"Unavailable":currentMute==1?"Muted":"Sound on";
   case "output":return SettingValue("soundoutput")=="2"?"Soundbar":SettingValue("soundoutput")=="0"?"TV Speaker":SettingName("soundoutput");
   case "network":return "Connections";
   case "controllers":return "Pair a device";
   case "app:org.tizen.menu":return "More settings";
   case "page:2":return "All controls";
   case "ambient":return "Art & photos";
   case "inputs":{var cur=inputList.Find(i=>i.Type==lastSource);return cur!=null?cur.Name:"Choose";}
  }
  return SettingName(action.StartsWith("cycle:")?action.Substring(6):action);
 }
 string SettingValue(string id){return settingValues.TryGetValue(id,out var v)?v:"";}
 string SettingName(string id){return settingNames.TryGetValue(id,out var v)?v:"Unavailable";}
 bool CanEdit(string id){return settingEnabled.TryGetValue(id,out var enabled)&&enabled&&settingValues.ContainsKey(id);}
 void SetSetting(string id,string value){
  if(!CanEdit(id)){ShowError("This setting is unavailable for the current input.");return;}
  bool ok=false;try{ok=CSharpSetItemValue(id,value);}catch{}
  RefreshSettings();UpdateFocus();
  bool verified=ok&&SettingValue(id)==value;
  if(!verified)ShowError("The TV could not apply this setting. Try Advanced settings.");
  WriteState("setting:"+id+":"+value+":"+verified);
 }
 void ChangeBrightness(int delta){double n;if(double.TryParse(SettingValue("brightness"),NumberStyles.Float,CultureInfo.InvariantCulture,out n))SetSetting("brightness",Math.Max(brightnessMin,Math.Min(brightnessMax,n+delta*brightnessStep)).ToString(CultureInfo.InvariantCulture));}
 void ChangeVolume(int delta){int n;int r=sound_manager_get_master_volume(out n);if(r==0)r=sound_manager_set_master_volume(Math.Max(0,Math.Min(100,n+delta)));if(r!=0)ShowError("Use the remote's volume buttons.");RefreshSettings();UpdateFocus();WriteState("volume_changed");}
 void SelectSource(int source){int r=connect_source(source);if(r<0){ShowError("Input could not be switched. Open Advanced settings to check the connection.");WriteState("source_failed:"+r);return;}lastSource=source;Launch("org.tizen.tv-viewer");}
 void Launch(string id,string menuItem=null)
 {
  IntPtr c;int r=app_control_create(out c);if(r!=0){ShowError("Could not open the app.");return;}
  try{r=app_control_set_app_id(c,id);if(r==0)r=app_control_set_operation(c,"http://tizen.org/appcontrol/operation/default");
   if(r==0&&id=="org.tizen.menu"){
    if(menuItem!=null){
     r=app_control_add_extra_data(c,"menuitem",menuItem);
     if(r==0)r=app_control_add_extra_data(c,"launchoption","pressed");
    }
    if(r==0)r=app_control_add_extra_data(c,"__K_EX_FORCE_BOOST","enable");
    if(r==0)r=app_control_add_extra_data(c,"__K_SPLASH_SCREEN","LA_UI_VR_TRANSPARENT");
   }
   if(r==0)r=app_control_send_launch_request(c,IntPtr.Zero,IntPtr.Zero);WriteState("launch:"+id+(menuItem==null?"":":"+menuItem)+":"+r);if(r!=0)ShowError("Could not open this screen. Try Advanced settings.");}finally{app_control_destroy(c);}
 }

 // ---- TV Info page: read-only, refreshed every 2 s only while visible ----
 Tizen.NUI.Timer infoTimer;
 TextLabel liveRate,liveCpu,liveMem,liveTotal;
 readonly List<View> spark=new List<View>();
 readonly double[] sparkValues=new double[36];
 (long rx,long tx)? lastNet;(long idle,long total)? lastCpu;long lastNetAt=-100000;string infoIface="eth0";
 string lastRateText="Measuring…",lastCpuText;int infoMarkers=-2,infoDisabled=-2;
 string TotalsText(Dictionary<string,(long rx,long tx)> all)=>all.TryGetValue(infoIface,out var c)?"↓ "+InfoStats.Bytes(c.rx)+"  ↑ "+InfoStats.Bytes(c.tx):null;
 void DrawSpark()
 {
  double peak=50000;foreach(var v in sparkValues)peak=Math.Max(peak,v);
  for(int i=0;i<spark.Count;i++){int h=Math.Max(3,(int)(180*sparkValues[i]/peak));spark[i].Size2D=new Size2D(14,h);spark[i].Position2D=new Position2D(30+i*22,330-h);}
 }
 View InfoPanel(int x,int y,int w,int h,string title,Color dot,int row)
 {
  var card=Panel(x+w/2,y+h/2,w,h,new Color(1,1,1,.08f),30);card.PivotPoint=new Position(.5f,.5f,.5f);card.PositionUsesPivotPoint=true;
  card.Add(new View{Position2D=new Position2D(30,39),Size2D=new Size2D(14,14),BackgroundColor=dot,CornerRadius=new Vector4(7,7,7,7)});
  card.Add(Label(title,56,24,w-86,44,29,white,"Semibold"));
  slots.Add(new Slot{Card=card,Name=title,Action="info",Description=title,Row=row,X=x,Y=y,W=w,H=h});
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
  infoIface=InfoStats.DefaultInterface()??"eth0";
  var tv=InfoPanel(100,140,540,390,"This TV",new Color(.56f,.66f,1f,1),0);
  string ver=InfoStats.SystemInfo("http://tizen.org/feature/platform.version");
  string build=InfoStats.ParseBuildDate(InfoStats.SystemInfo("http://tizen.org/system/build.string"));
  InfoRow(tv,540,0,"Model",InfoStats.SystemInfo("http://tizen.org/system/model_name"));
  InfoRow(tv,540,1,"Software",(ver!=null?"Tizen "+ver:"Tizen")+(build!=null?" · "+build:""));
  var up=InfoStats.Uptime();InfoRow(tv,540,2,"Up for",up.HasValue?InfoStats.Duration(up.Value):null);
  liveMem=InfoRow(tv,540,3,"Memory",MemText(InfoStats.Memory()));
  int cores=InfoStats.Cores();liveCpu=InfoRow(tv,540,4,"Processor","Measuring…"+(cores>0?" · "+cores+" cores":""));

  var net=InfoPanel(690,140,540,390,"Network",new Color(.33f,.82f,.75f,1),0);
  bool wifi=infoIface.StartsWith("wlan");int? level=wifi?InfoStats.WifiLevel(infoIface):null;
  string link=wifi?"Wi-Fi"+(level.HasValue?" · "+level+" dBm":""):"Wired"+(InfoStats.LinkSpeed(infoIface) is string spd?" · "+spd:"");
  InfoRow(net,540,0,"Connection",InfoStats.DefaultInterface()==null?"Not connected":link);
  InfoRow(net,540,1,"IP address",InfoStats.Ipv4(infoIface));
  var dns=InfoStats.Nameservers();InfoRow(net,540,2,"DNS",dns.Count>0?string.Join(" · ",dns.ConvertAll(d=>d=="8.8.8.8"?"Google backup":d)):null);
  liveTotal=InfoRow(net,540,3,"Since start",TotalsText(InfoStats.NetDev()));

  var pv=InfoPanel(1280,140,540,390,"Privacy",new Color(.72f,.56f,1f,1),0);
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
  var tr=InfoPanel(100,570,830,390,"Thread border router",routing?good:th.present?warn:muted,1);
  InfoRow(tr,830,0,"Status",status,routing?good:th.present?warn:muted);
  InfoRow(tr,830,1,"Home network",home,home=="Connected"?good:home==null?null:warn);
  var all=InfoStats.NetDev();
  InfoRow(tr,830,2,"Thread traffic",all.TryGetValue("wpan0",out var w0)?"↓ "+InfoStats.Bytes(w0.rx)+"  ↑ "+InfoStats.Bytes(w0.tx):null);
  InfoRow(tr,830,3,"Interface",th.present?"wpan0 · "+th.addresses+" addresses":"Not running");
  infoThread=status+" | "+(home??"-")+" | fwd="+fwd;

  var live=InfoPanel(970,570,850,390,"Live · TV network",new Color(.4f,.78f,1f,1),1);
  bool fresh=Environment.TickCount64-lastNetAt>10000;
  if(fresh){Array.Clear(sparkValues,0,sparkValues.Length);lastNet=null;lastCpu=null;lastRateText="Measuring…";}
  if(lastCpuText!=null&&!fresh)liveCpu.Text=lastCpuText;
  liveRate=Label(lastRateText,30,86,790,52,36,white,"Display");live.Add(liveRate);
  spark.Clear();
  for(int i=0;i<sparkValues.Length;i++){var b=new View{Position2D=new Position2D(30+i*22,330),Size2D=new Size2D(14,3),BackgroundColor=new Color(.4f,.78f,1f,.85f),CornerRadius=new Vector4(4,4,4,4)};spark.Add(b);live.Add(b);}
  DrawSpark();
  if(fresh)InfoTick();
  if(infoTimer==null){infoTimer=new Tizen.NUI.Timer(2000);infoTimer.Tick+=(s,e)=>{if(page!=3)return false;InfoTick();return true;};}
  infoTimer.Start();
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
  Window.Default.Add(dimLayer);dimLayer.RaiseToTop();
  dimAnimation=new Animation(2000);dimAnimation.AnimateTo(dimLayer,"Opacity",1f);dimAnimation.Play();
  WriteState("dimmed");
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
  if(dimLayer!=null){Window.Default.Remove(dimLayer);dimLayer.Dispose();dimLayer=null;dimClock=null;}
  if(page==3&&infoTimer!=null)infoTimer.Start();
  WriteState(fromKey?"woke_key":"woke");
 }
 // ---- Inputs sheet: every HDMI port with its name, plus connection and power state ----
 List<InfoStats.InputSource> inputList=new List<InfoStats.InputSource>();
 ImageView glowWatch,glowPlay;
 View inputsLayer;readonly List<View> inputRows=new List<View>();int inputSel;
 void OpenInputs()
 {
  CloseInputs(false);inputList=InfoStats.Sources();
  inputsLayer=new View{Size2D=new Size2D(1920,1080),BackgroundColor=new Color(0,0,0,.6f)};
  int rowH=96,w=640,h=96+inputList.Count*rowH+20,x=100,y=Math.Max(60,760-h);
  var sheet=new View{Position2D=new Position2D(x,y),Size2D=new Size2D(w,h),BackgroundColor=new Color(.13f,.13f,.15f,.97f),CornerRadius=new Vector4(34,34,34,34),BorderlineWidth=1.5f,BorderlineColor=new Color(1,1,1,.12f)};
  sheet.Add(Label("Inputs",36,22,w-72,56,36,white,"Display"));
  inputRows.Clear();inputSel=Math.Max(0,inputList.FindIndex(i=>i.Type==lastSource));
  for(int i=0;i<inputList.Count;i++){
   var s=inputList[i];bool cur=s.Type==lastSource;
   var row=new View{Position2D=new Position2D(16,96+i*rowH),Size2D=new Size2D(w-32,rowH-8),CornerRadius=new Vector4(22,22,22,22),Opacity=s.Connected||cur?1:.45f};
   var g=new ImageView{ResourceUrl=Root+"glyph-inputs.svg",Position2D=new Position2D(20,22),Size2D=new Size2D(44,44)};row.Add(g);
   bool sub=s.Name!=s.Port;
   var name=Label(s.Name,84,sub?10:0,330,sub?44:rowH-8,30,white,"Semibold");name.Ellipsis=true;row.Add(name);
   if(sub)row.Add(Label(s.Port,84,50,300,32,22,muted));
   string st=cur?"On now":!s.Connected?"Not connected":"";
   if(st!=""){
    var pill=new View{Position2D=new Position2D(w-32-196,(rowH-8-44)/2),Size2D=new Size2D(180,44),CornerRadius=new Vector4(22,22,22,22),BackgroundColor=cur?new Color(.2f,.78f,.36f,.22f):new Color(1,1,1,.08f)};
    var pl=Label(st,0,0,180,44,21,cur?new Color(.5f,.95f,.6f,1):muted,"Semibold");pl.HorizontalAlignment=HorizontalAlignment.Center;pill.Add(pl);row.Add(pill);
   }
   sheet.Add(row);inputRows.Add(row);
  }
  inputsLayer.Add(sheet);Window.Default.Add(inputsLayer);inputsLayer.RaiseToTop();
  FocusInputs();WriteState("inputs_open");
 }
 void FocusInputs()
 {
  for(int i=0;i<inputRows.Count;i++){
   bool f=i==inputSel;var row=inputRows[i];row.BackgroundColor=f?white:new Color(0,0,0,0);
   for(uint c=0;c<row.ChildCount;c++){var v=row.GetChildAt(c);
    if(v is TextLabel tl)tl.TextColor=f?dark:(c==2?muted:white);
    else if(v is ImageView iv)iv.Color=f?dark:white;
    else if(v.ChildCount>0&&v.GetChildAt(0) is TextLabel pl){ // status pill: stays readable on the white focused row
     bool cur=inputList[i].Type==lastSource;
     v.BackgroundColor=cur?new Color(.2f,.78f,.36f,f?1:.22f):new Color(f?0:1,f?0:1,f?0:1,f?.08f:.08f);
     pl.TextColor=cur?(f?white:new Color(.5f,.95f,.6f,1)):f?new Color(.32f,.33f,.37f,1):muted;}}
  }
 }
 bool HandleInputsKey(string k)
 {
  if(k=="Up"||k=="Down"){inputSel=Math.Clamp(inputSel+(k=="Up"?-1:1),0,inputRows.Count-1);FocusInputs();return true;}
  if(k=="Return"||k=="KP_Enter"){var s=inputList[inputSel];CloseInputs(false);SelectSource(s.Type);return true;}
  if(k=="XF86Back"||k=="Back"||k=="Escape"||k=="XF86Exit"){CloseInputs(true);return true;}
  if(k=="XF86Home"){CloseInputs(false);return false;}
  return true; // Left/Right and other keys stay inside the sheet
 }
 void CloseInputs(bool refocus)
 {
  if(inputsLayer==null)return;
  Window.Default.Remove(inputsLayer);inputsLayer.Dispose();inputsLayer=null;inputRows.Clear();
  if(refocus){UpdateFocus(false);WriteState("inputs_closed");}
 }
 string InputsState()=>string.Join(";",inputList.ConvertAll(i=>i.Type+","+i.Name+","+(i.Connected?1:0)+","+i.Power));
 void ShowError(string text){notice.Text=text;}
 void WriteState(string activity){try{File.WriteAllText(StateFile,DateTime.UtcNow.ToString("o")+"\npage="+page+"\nselected="+selected+"\narea="+area+"\nactivity="+activity+"\nkey="+lastKey+"\nfont_loaded="+fontLoaded+"\nstyle=tvos-dark-v10b\nbuild_ms="+buildMilliseconds+"\nlibrary_page="+libraryPage+"\nlibrary_count="+library.Count+"\nlibrary_error="+libraryError+"\nselected_name="+(selected>=0&&selected<slots.Count?slots[selected].Name:"")+"\nartwork_cached="+artworkCache.Count+"\nartwork_ready="+ArtworkReadyCount()+"\nartwork_pending="+ArtworkPending()+"\nartwork_views_created="+artworkViewsCreated+"\npage_artwork_cold="+pageArtworkCold+"\nprocess_id="+Process.GetCurrentProcess().Id+"\nediting="+editing+"\nvolume="+currentVolume+"\nmute="+currentMute+"\nbrightness="+SettingValue("brightness")+"\ngame-mode="+SettingValue("game-mode")+"\nsleeptimer="+SettingValue("sleeptimer")+"\nsound-mode="+SettingValue("external-sound-mode")+"\nsoundoutput="+SettingValue("soundoutput")+"\npicturemode="+SettingValue("picturemode")+"\ngame-mode-auto="+SettingValue("game-mode-auto")+"\ninfo_markers="+infoMarkers+"\ninfo_disabled_apps="+infoDisabled+"\ninfo_live="+lastRateText+" | "+lastCpuText+"\ninfo_thread="+infoThread+"\ninputs="+InputsState()+"\ninputs_open="+(inputsLayer!=null)+"\ndimmed="+dimmed+"\nidle_s="+(Environment.TickCount64-lastActivity)/1000+"\nstyle_theme=dark\n");}catch{}}
 public static void Main(string[] args){using(var app=new QuietHome())app.Run(args);}
}
