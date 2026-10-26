using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.Applications;

// Quiet Home: SHIELD-first Home screen for the S90D.
// Scene = persistent chrome (wallpaper, focus glows, tab bar, clock, overlay) + a content layer rebuilt per page.
// Latency rules: no IPC or file I/O on the key path; a focus move restyles only the slots whose focus changed.
// Partial files: Home.Controls.cs (settings, launching), Home.Inputs.cs (live inputs, sheet, toast),
// Home.Info.cs (Info page), Home.Idle.cs (OLED dim), Home.State.cs (diagnostics file).
public sealed partial class QuietHome : NUIApplication
{
 const string Root="/opt/usr/apps/com.samsung.tv.bixby-promotion/res/quiet-home/";
 const int TB=9; // toolbar focus area; 1..3 are content rows
 // Kind: 0 plain (Info panels), 1 hero, 2 control tile
 sealed class Slot { public Renderer Glass,Rim; public View Card,Disc,Badge,BadgeDot; public TextLabel Label,Title; public ImageView Glyph; public string Name,Action; public int Row,X,Y,W,H,Kind; }
 sealed class CachedArtwork { public ImageView Image; public View Parent; }
 readonly List<Slot> slots=new List<Slot>();
 readonly Dictionary<string,CachedArtwork> artworkCache=new Dictionary<string,CachedArtwork>();
 View artworkPreloadRoot,content,overlay;
 ImageView glowWatch,glowPlay;
 int artworkViewsCreated,pageArtworkCold;
 List<LibraryApp> library=new List<LibraryApp>();
 int libraryPage;
 string libraryError="";
 TextLabel clock,notice,detail;
 TextLabel[] tabs;
 View[] tabBg;
 Animation focusAnimation,pageAnimation;
 bool fontLoaded,visible,styledAll;
 Tizen.NUI.Timer clockTimer;
 int page,selected,area=1,lastSource=15,styledSelected=-1,styledArea=-1,styledTabs=-1;
 string editing="";
 long buildMilliseconds;
 string lastKey="";
 long lastKeyAt;
 readonly Color ink=new Color(0.95f,0.95f,0.97f,1);
 readonly Color muted=new Color(0.62f,0.63f,0.68f,1);
 readonly Color chromeText=new Color(0.80f,0.81f,0.85f,1); // tabs and clock sit on the photo: a little brighter
 readonly Color white=new Color(1,1,1,1);
 readonly Color dark=new Color(0.07f,0.07f,0.09f,1);
 readonly Color clear=new Color(0,0,0,0);
 readonly Color discFill=new Color(1,1,1,.14f);
 readonly Color discFocus=new Color(0,0,0,.07f);
 readonly Color titleDim=new Color(1,1,1,.78f);
 readonly Color valueFocus=new Color(.32f,.33f,.37f,1);
 static AlphaFunction Ease()=>new AlphaFunction(AlphaFunction.BuiltinFunctions.EaseOutSine);

 protected override void OnCreate()
 {
  base.OnCreate();
  ui=SynchronizationContext.Current;
  try{fontLoaded=FontClient.Instance.AddCustomFontDirectory(Root+"fonts");}catch{fontLoaded=false;}
  try{library=AppLibrary.Load(Root+"apps.json");}catch(Exception ex){libraryError=ex.Message;}
  // Samsung Home lives on the Apps page (not on Home); keep it before the "Get more apps" utility.
  if(!library.Exists(a=>a.Id=="com.samsung.tv.csfs")){int at=library.FindIndex(a=>a.Utility);var sh=new LibraryApp{Name="Samsung Home",Id="com.samsung.tv.csfs",Asset="tile-samsung-home.svg",Description="Samsung's home screen",Fit="cover",Background="#1C2638",Width=640,Height=360};if(at<0)library.Add(sh);else library.Insert(at,sh);}
  Window.Default.BackgroundColor=new Color(0,0,0,1);
  Window.Default.Title="SilentGlass Home";Window.Default.KeyEvent+=OnKey;
  style=ReadStyleConf();prefs=LoadPrefs();try{NUIApplication.SetRenderRefreshRate((uint)style.Vsync);}catch{}BuildChrome();
  PreloadArtwork();
  RefreshSource();RefreshInputs();ResolveHeroPorts();WatchInputs();WatchForeground();WatchSignal();
  selected=SourceIndex();BuildPage(false);
  clockTimer=new Tizen.NUI.Timer(30000);
  clockTimer.Tick+=(s,e)=>{clock.Text=Clock();CheckWallpaper();IdleTick();if(visible&&page==0&&inputsLayer==null)OnInputsChanged(false);return true;};clockTimer.Start();
  visible=true;MarkState("created");
 }
 protected override void OnResume(){base.OnResume();visible=true;styledAll=false;UpdateFocus(false); /* restyle everything after a pause */ BenchRender();CheckWallpaper();Wake(false);clockTimer?.Start();clock.Text=Clock();ReadSignal();OnInputsChanged(false);ScheduleLive();if(page==2){RefreshSettings();RefreshTileValues();}if(page==3)infoTimer?.Start();MarkState("resumed");}
 protected override void OnAppControlReceived(AppControlReceivedEventArgs e){base.OnAppControlReceived(e);Wake(false);string requested;
  // Home Assistant bridge: switch input the same way the cards do (only to an input the TV lists)
  if(e.ReceivedAppControl.ExtraData.TryGet("quiet_source",out string req)&&int.TryParse(req,out int src)&&inputs.Exists(i=>i.Type==src)){SelectSource(src);MarkState("ha_source:"+src);return;}
  if(e.ReceivedAppControl.ExtraData.TryGet("quiet_page",out requested)&&requested=="settings")OpenSettings("settings_request");else if(requested=="apps"){page=1;area=1;selected=0;BuildPage(false);}else if(requested=="info"){page=3;area=1;selected=0;BuildPage(false);}else Home("home_request");}
 protected override void OnPause(){base.OnPause();visible=false;editing="";CloseInputs(false);ReleaseLiveWindow();clockTimer?.Stop();infoTimer?.Stop();WriteState("paused");}
 void Home(string activity)
 {
  clockTimer?.Start();CloseInputs(false);OnInputsChanged(false);
  // already on Home: only move focus back. Rebuilding disposed every card and the live preview, so the
  // artwork re-decoded and the video re-attached - that was the flash after Inputs -> Home.
  if(page==0){
   area=1;editing="";selected=SourceIndex();
   if(!RelayoutHeroes()){UpdateHeroBadges(false);UpdateFocus();ScheduleLive();}
   MarkState(activity);return;
  }
  page=0;area=1;editing="";selected=SourceIndex();BuildPage(false);
  MarkState(activity);
 }
 void OpenSettings(string activity){clockTimer?.Start();page=2;area=1;selected=0;editing="";BuildPage(false);MarkState(activity);}
 static string Clock()=>DateTime.Now.ToString("h:mm tt");
 int SourceIndex(){return lastSource==playPort?1:0;}

 // ---- scene helpers: page views go into the content layer and are disposed with the page ----
 View Add(View v){content.Add(v);return v;}
 View Panel(int x,int y,int w,int h,Color color,int radius=18){var v=Add(new View{Position2D=new Position2D(x,y),Size2D=new Size2D(w,h),BackgroundColor=color,CornerRadius=new Vector4(radius,radius,radius,radius)});Corners(v,radius,w,h);return v;}
 // a focusable card: positioned by its centre so it can scale in place
 View Card(int x,int y,int w,int h,Color color,int radius){var c=Panel(x+w/2,y+h/2,w,h,color,radius);c.PivotPoint=new Position(.5f,.5f,.5f);c.PositionUsesPivotPoint=true;return c;}
 TextLabel Text(string text,int x,int y,int w,int h,float size,Color color,string face="Medium")
 {return (TextLabel)Add(Label(text,x,y,w,h,size,color,face));}
 TextLabel Label(string text,int x,int y,int w,int h,float size,Color color,string face="Medium")
 {return new TextLabel{Text=text,Position2D=new Position2D(x,y),Size2D=new Size2D(w,h),PixelSize=size,FontFamily="Quiet Inter "+face,TextColor=color,VerticalAlignment=VerticalAlignment.Center};}
 // StopFinal: an interrupted animation lands on its end values. Plain Stop() froze it mid-way, so a tile that was
 // fading out of focus (fast key presses, or Menu pausing the app) could stay highlighted.
 static void StopAnimation(ref Animation a){if(a==null)return;a.EndAction=Animation.EndActions.StopFinal;a.Stop();a.Dispose();a=null;}

 // Created once: wallpaper, glows, tab bar and clock never re-decode or re-layout on page changes.
 void BuildChrome()
 {
  var win=Window.Default;
  win.Add(BuildWallpaper());
  // colour glows belong to the gradient wallpaper; over a photo they would just wash it out (and cost two
  // full-screen blended layers on every Home frame)
  if(wallpaper==null){glowWatch=Glow("watch");glowPlay=Glow("play");}
  content=new View{Size2D=new Size2D(1920,1080)};win.Add(content);
  // tvOS-style centred tab bar; the selected tab stays lit, and turns white while it has focus
  tabBar=new View{Size2D=new Size2D(1920,120)};win.Add(tabBar);BuildTabs();
  clock=Label(Clock(),1520,40,300,60,30,chromeText);clock.HorizontalAlignment=HorizontalAlignment.End;win.Add(clock);
  detail=Label("",100,952,1720,40,24,muted);detail.HorizontalAlignment=HorizontalAlignment.Center;win.Add(detail);
  notice=Label("",100,1000,1720,40,24,new Color(1f,.76f,.46f,1));notice.HorizontalAlignment=HorizontalAlignment.Center;win.Add(notice);
  overlay=new View{Size2D=new Size2D(1920,1080)};win.Add(overlay); // inputs sheet, toast and OLED dim: always on top
 }
 // rebuilt in place (same layer) when the Apps tab is shown or hidden
 View tabBar;Renderer tabGlass;
 static readonly string[] Nav={"Home","Apps","Controls","Info"};static readonly int[] TabW={132,122,178,110};
 void BuildTabs()
 {
  while(tabBar.ChildCount>0){var v=tabBar.GetChildAt(0);tabBar.Remove(v);v.Dispose();}
  tabGlass?.Dispose();
  int total=12;for(int i=0;i<Nav.Length;i++)if(TabShown(i))total+=TabW[i];
  int px=960-total/2,x=px+6;
  var capsule=new View{Position2D=new Position2D(px,36),Size2D=new Size2D(total,68)};tabBar.Add(capsule);
  tabGlass=AddGlass(capsule,0,px,36,34,persistent:true,tint:.07f,shadow:.1f,shadowY:4,blur:14,focusShadow:0,bevel:16,refract:18);
  tabs=new TextLabel[Nav.Length];tabBg=new View[Nav.Length];
  for(int i=0;i<Nav.Length;i++){
   if(!TabShown(i))continue;
   tabBg[i]=new View{Position2D=new Position2D(x,42),Size2D=new Size2D(TabW[i],56),BackgroundColor=clear,CornerRadius=new Vector4(28,28,28,28)};tabBar.Add(tabBg[i]);
   tabs[i]=Label(Nav[i],x,42,TabW[i],56,28,muted);tabs[i].HorizontalAlignment=HorizontalAlignment.Center;tabBar.Add(tabs[i]);x+=TabW[i];
  }
  styledTabs=-1;
 }
 ImageView Glow(string name)
 {
  // tiny gradient art scaled to full screen; follows focus on Home (green = Watch, red/blue = Play)
  var g=new ImageView{ResourceUrl=Root+"glow-"+name+".svg",Size2D=new Size2D(1920,1080),DesiredWidth=480,DesiredHeight=270,Opacity=0};
  Window.Default.Add(g);return g;
 }
 void BuildPage(bool fade=true)
 {
  var stopwatch=Stopwatch.StartNew();
  StopAnimation(ref focusAnimation);
  ParkArtwork();pageArtworkCold=0;ReleasePageRenderers();
  infoTimer?.Stop();CloseInputs(false);HideLive();
  while(content.ChildCount>0){var v=content.GetChildAt(0);content.Remove(v);v.Dispose();}
  slots.Clear();styledAll=false;styledSelected=-1;notice.Text="";
  if(page==0)BuildHome();
  else if(page==1)BuildLibrary();
  else if(page==3)BuildInfo();
  else BuildControls();
  if(page!=0){if(glowWatch!=null){glowWatch.Opacity=0;glowPlay.Opacity=0;}GlowTo(0,0,false);}
  selected=Math.Max(0,Math.Min(selected,slots.Count-1));UpdateFocus(false);
  StopAnimation(ref pageAnimation);
  if(fade){content.Opacity=0;pageAnimation=new Animation(140){DefaultAlphaFunction=Ease()};pageAnimation.AnimateTo(content,"Opacity",1f);pageAnimation.Play();}
  else content.Opacity=1;
  buildMilliseconds=stopwatch.ElapsedMilliseconds;buildMsByPage[page]=Math.Max(buildMsByPage.TryGetValue(page,out long was)?was:0,buildMilliseconds);MarkState("page_ready");
  if(page==0)ScheduleLive();
 }
 const int HeroWide=996,HeroNarrow=684,HeroGap=40; // 996x560 = 16:9; 996+40+684 = the 1720 content width
 bool playWide;
 bool PlayIsWide()=>lastSource==playPort;
 // the wide card follows the input on screen; rebuilt (no fade) when that changes while Home is the page
 bool RelayoutHeroes(){if(page!=0||PlayIsWide()==playWide)return false;BuildPage(false);MarkState("hero_layout");return true;}
 void BuildHome()
 {
  RefreshSettings();
  // Watch (SHIELD) left, Play (Switch) right - the order never changes. The card of the input on screen is the
  // wide one: exactly 16:9, so its live preview fills it uncropped. SHIELD is wide when neither is on screen.
  // ports follow the TV's input names (Shield / Switch), so re-cabling needs no code change
  playWide=PlayIsWide();
  int ww=playWide?HeroNarrow:HeroWide,pw=playWide?HeroWide:HeroNarrow;
  Hero("Watch","hdmi:"+watchPort,InputName(watchPort),"feature-watch-"+(playWide?"narrow":"wide")+".svg",100,170,ww,560,new Color(.76f,.94f,.56f,1));
  Hero("Play","hdmi:"+playPort,InputName(playPort),"feature-play-"+(playWide?"wide":"narrow")+".svg",100+ww+HeroGap,170,pw,560,new Color(1f,.74f,.78f,1));
  Control("Inputs","inputs","inputs",0);
  Control("Ambient","ambient","ambient",1);
  Control("Sleep Timer","sleeptimer","sleep",2);
  Control("Sound","output","output",3);
  Control("More","page:2","more",4);
  UpdateHeroBadges(false);RefreshTileValues();
 }
 void BuildControls()
 {
  RefreshSettings();
  SettingsTile("Volume","volume","volume",0,0);
  SettingsTile("Sound","mute","mute",1,0);
  SettingsTile("Soundbar Mode","cycle:external-sound-mode","soundmode",2,0);
  SettingsTile("Output","output","output",3,0);
  SettingsTile("Sleep Timer","sleeptimer","sleep",4,0);
  SettingsTile("Picture Mode","cycle:picturemode","picture",0,1);
  SettingsTile("Brightness","brightness","brightness",1,1);
  SettingsTile("Game Mode","game-mode","game",2,1);
  SettingsTile("Auto Game Mode","cycle:game-mode-auto","autogame",3,1);
  SettingsTile("Pixel Shift","cycle:pixel-shift-menu","picture",4,1); // OLED Panel Care; Pixel Refresh runs by itself
  SettingsTile("Watch Input","heroport:watch","inputs",0,2);
  SettingsTile("Play Input","heroport:play","inputs",1,2);
  SettingsTile("Network","network","network",2,2);
  SettingsTile("Bluetooth","controllers","bluetooth",3,2);
  SettingsTile("Advanced","app:org.tizen.menu","settings",4,2);
  RefreshTileValues();
 }
 void BuildLibrary()
 {
  if(library.Count==0){Text("Apps could not be loaded.",100,300,1500,70,32,ink);return;}
  libraryPage=Math.Clamp(libraryPage,0,AppLibrary.PageCount(library.Count)-1);
  int start=libraryPage*AppLibrary.PageSize,count=AppLibrary.VisibleCount(library.Count,libraryPage);
  for(int i=0;i<count;i++)
  {
   var app=library[start+i];int x=100+(i%5)*346,y=180+(i/5)*262,w=316,h=178;
   var card=Card(x,y,w,h,HexColor(app.Background),18);
   AddAppArtwork(card,app,w,h);
   var rim=RimOverlay(card,18,.1f,6,16,focusShadow:.2f);
   var label=Text(app.Name,x,y+h+16,w,38,26,muted,"Semibold");label.HorizontalAlignment=HorizontalAlignment.Center;
   slots.Add(new Slot{Card=card,Rim=rim,Label=label,Name=app.Name,Action="app:"+app.Id,Row=i/5,X=x,Y=y,W=w,H=h});
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
  else{
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
  image.ResourceReady+=(s,e)=>MarkState("artwork_ready");
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
 void SettingsTile(string name,string action,string icon,int column,int row)
 {
  int w=322,h=236,x=100+column*(w+27),y=150+row*262; // five across
  var card=Card(x,y,w,h,clear,30);var glass=AddGlass(card,0,x,y,30);
  var disc=new View{Position2D=new Position2D(26,26),Size2D=new Size2D(84,84),BackgroundColor=new Color(1,1,1,.14f),CornerRadius=new Vector4(42,42,42,42)};
  var glyph=new ImageView{ResourceUrl=Root+"glyph-"+icon+".svg",Position2D=new Position2D(16,16),Size2D=new Size2D(52,52)};disc.Add(glyph);card.Add(disc);
  var title=Label(name,28,124,w-56,38,26,new Color(1,1,1,.78f),"Semibold");title.Ellipsis=true;card.Add(title);
  var value=Label("",28,166,w-56,48,32,white,"Display");value.Ellipsis=true;card.Add(value);
  slots.Add(new Slot{Card=card,Glass=glass,Label=value,Title=title,Glyph=glyph,Disc=disc,Name=name,Action=action,Row=row,X=x,Y=y,W=w,H=h,Kind=2});
 }
 Slot Hero(string word,string action,string sub,string asset,int x,int y,int w,int h,Color subColor)
 {
  var card=Card(x,y,w,h,clear,40);
  card.Add(new ImageView{ResourceUrl=Root+asset,Size2D=new Size2D(w,h)});
  // 150 tall around the same centre line: at 116 the 100px word did not fit its line height and the TV drew "..."
  var big=Label(word,50,h-223,w-100,150,100,white,"Display");big.Ellipsis=false;
  var subLabel=Label(sub,56,h-96,w-112,46,30,subColor,"Semibold");
  card.Add(big);card.Add(subLabel);
  // status dot, no words: set by UpdateHeroBadges whenever the input or signal changes
  var badge=new View{Position2D=new Position2D(36,36),Size2D=new Size2D(44,44),BackgroundColor=new Color(0,0,0,.45f),CornerRadius=new Vector4(22,22,22,22),Opacity=0};
  var dot=new View{Position2D=new Position2D(11,11),Size2D=new Size2D(22,22),CornerRadius=new Vector4(11,11,11,11)};badge.Add(dot);card.Add(badge);
  // lit edge; shadow only when focused (tvOS lift)
  var slot=new Slot{Card=card,Rim=RimOverlay(card,40,0,14,30,focusShadow:.26f),Name=word,Action=action,Row=0,X=x,Y=y,W=w,H=h,Kind=1,Badge=badge,BadgeDot=dot};
  slots.Add(slot);return slot;
 }
 // Control Centre style tile: glyph disc, name, current value. Five across.
 void Control(string name,string action,string glyph,int n)
 {
  const int Count=5,Gap=25;int w=(1720-(Count-1)*Gap)/Count,h=150,x=100+n*(w+Gap),y=780;
  var card=Card(x,y,w,h,clear,30);var glass=AddGlass(card,0,x,y,30);
  var disc=new View{Position2D=new Position2D(22,35),Size2D=new Size2D(80,80),BackgroundColor=discFill,CornerRadius=new Vector4(40,40,40,40)};
  var g=new ImageView{ResourceUrl=Root+"glyph-"+glyph+".svg",Position2D=new Position2D(15,15),Size2D=new Size2D(50,50)};disc.Add(g);card.Add(disc);
  var title=Label(name,120,32,w-136,42,28,white,"Semibold");title.Ellipsis=true;card.Add(title);
  var value=Label("",120,76,w-136,40,24,muted);value.Ellipsis=true;card.Add(value);
  slots.Add(new Slot{Card=card,Glass=glass,Label=value,Title=title,Glyph=g,Disc=disc,Name=name,Action=action,Row=1,X=x,Y=y,W=w,H=h,Kind=2});
 }

 // ---- focus ----
 List<int> Row(int row){var r=new List<int>();for(int i=0;i<slots.Count;i++)if(slots[i].Row==row)r.Add(i);return r;}
 int Nearest(List<int> row){var c=slots[selected];float cx=c.X+c.W/2f;int best=row[0];float bd=float.MaxValue;foreach(var i in row){float d=Math.Abs(slots[i].X+slots[i].W/2f-cx);if(d<bd){bd=d;best=i;}}return best;}
 Animation FocusAnimation()=>focusAnimation??=new Animation(170){DefaultAlphaFunction=Ease()};
 void UpdateFocus(bool animate=true)
 {
  StopAnimation(ref focusAnimation);
  StyleTabs();
  int focus=area!=0&&selected<slots.Count?selected:-1;
  bool areaChanged=styledArea!=area;
  // only the slots whose state changed are touched (plus the heroes when focus enters/leaves the tab bar)
  for(int i=0;i<slots.Count;i++)
   if(!styledAll||i==styledSelected||i==focus||(areaChanged&&slots[i].Kind==1))StyleSlot(slots[i],i==focus,animate&&styledAll);
  styledAll=true;styledSelected=focus;styledArea=area;
  if(page==0){
   string fn=focus>=0&&slots[focus].Kind==1?slots[focus].Name:"";
   if(glowWatch!=null){FadeTo(glowWatch,fn=="Watch"?1:0,animate);FadeTo(glowPlay,fn=="Play"?1:0,animate);}
   GlowTo(fn=="Watch"?1:0,fn=="Play"?1:0,animate);
  }
  focusAnimation?.Play();
  detail.Text=editing!=""?"Left / Right to adjust · Select to finish":"";
  MarkState("focus");
 }
 void FadeTo(View v,float target,bool animate){if(Math.Abs(v.Opacity-target)<.01f)return;if(animate)FocusAnimation().AnimateTo(v,"Opacity",target);else v.Opacity=target;}
 void StyleTabs()
 {
  int key=page*2+(area==0?1:0);if(key==styledTabs)return;styledTabs=key;
  for(int i=0;i<tabs.Length;i++){
   if(tabs[i]==null)continue; // hidden tab
   bool cur=i==page;
   tabs[i].TextColor=cur&&area==0?dark:cur?white:chromeText;
   string face="Quiet Inter "+(cur?"Semibold":"Medium");if(tabs[i].FontFamily!=face)tabs[i].FontFamily=face;
   tabBg[i].BackgroundColor=cur?(area==0?white:new Color(1,1,1,.20f)):clear;
  }
 }
 void StyleSlot(Slot s,bool f,bool animate)
 {
  float scale=1,lift=0,opacity=1;
  // the live card never grows or lifts: the video plane does not follow view animations
  if(s.Kind==1){bool live=IsLive(s);scale=f&&!live?1.02f:1;lift=f&&!live?-3:0;opacity=f||area==0||live?1:.7f;GlassFocus(s.Rim,f?1:0,0,animate);}
  else if(s.Kind==2){
   scale=f?page==3?1.03f:1.06f:1; // Info tiles are wide: 6% would almost touch the neighbours
   GlassFocus(s.Glass,f?1:0,0,animate);
   s.Disc.BackgroundColor=f?discFocus:discFill;
   s.Glyph.Color=f?dark:white;
   s.Title.TextColor=f?dark:page==2?titleDim:white;
   s.Label.TextColor=f?(page==2?dark:valueFocus):page==2?white:muted;
  }
  else if(page==1){scale=f?1.08f:1;lift=f?-6:0;s.Label.TextColor=f?white:muted;GlassFocus(s.Rim,f?1:0,f?1:0,animate);}
  else{scale=f?1.015f:1;GlassFocus(s.Glass,0,f?1:0,animate);} // Info panels: a bright ring, the panel stays dark
  var pos=new Position(s.X+s.W/2f,s.Y+s.H/2f+lift,0);var sc=new Vector3(scale,scale,1);
  // heroes dim by darkening, not fading: a faded card would show the photo wallpaper through its artwork
  bool hero=s.Kind==1&&!style.HeroFade;var tint=new Color(opacity,opacity,opacity,1);
  if(animate){var a=FocusAnimation();a.AnimateTo(s.Card,"Scale",sc);a.AnimateTo(s.Card,"Position",pos);if(hero)a.AnimateTo(s.Card,"Color",tint);else a.AnimateTo(s.Card,"Opacity",opacity);}
  else{s.Card.Scale=sc;s.Card.Position=pos;if(hero)s.Card.Color=tint;else s.Card.Opacity=opacity;}
  if(f)s.Card.RaiseToTop();
 }

 // ---- keys ----
 void OnKey(object sender,Window.KeyEventArgs e)
 {
  if(e.Key==null||e.Key.State!=Key.StateType.Down)return;string k=e.Key.KeyPressedName;
  long now=Environment.TickCount64;
  if(dimmed){Wake(true);lastKey=k;lastKeyAt=now;return;} // first press only wakes
  lastActivity=now;
  // drop auto-repeat floods only: arrows may repeat every 90 ms; Select/Back never fire twice within 300 ms
  bool arrow=k=="Up"||k=="Down"||k=="Left"||k=="Right";
  if(k==lastKey&&now-lastKeyAt<(arrow?90:300))return;
  lastKey=k;lastKeyAt=now;
  long t0=Stopwatch.GetTimestamp();
  try{HandleKey(k);}catch(Exception ex){ShowError(ex.Message);}
  RecordKeyTime(Stopwatch.GetTimestamp()-t0);
 }
 void HandleKey(string k)
 {
  if(inputsLayer!=null&&HandleInputsKey(k))return;
  if(k=="XF86Home"){Home("home_key");return;}
  if(k=="XF86SysMenu"||k=="XF86More"){OpenSettings("settings_key");return;}
  if(page==1&&HandleLibraryKey(k))return;
  if(editing!=""){if(k=="Left"||k=="Right"){int d=k=="Left"?-1:1;if(editing=="volume")ChangeVolume(d);else ChangeBrightness(d);}else if(k=="Return"||k=="KP_Enter"||k=="XF86Back"||k=="Back"||k=="Escape"){editing="";UpdateFocus();}return;}
  if(k=="Up"){if(area>=2&&area!=TB){area--;selected=Nearest(Row(area-1));}else if(area==1)area=0;UpdateFocus();}
  else if(k=="Down"){if(area==0||area==TB){area=1;selected=page==0?SourceIndex():Row(0)[0];}else if(Row(area).Count>0){area++;selected=Nearest(Row(area-1));}UpdateFocus();}
  else if(k=="Left"||k=="Right"){
   int d=k=="Left"?-1:1;
   if(area==0){int next=HomeLogic.NextTab(page,d,prefs.HideApps);if(next!=page){page=next;selected=page==0?SourceIndex():0;BuildPage();}}
   else{var r=Row(area-1);int n=r.IndexOf(selected);selected=r[(n+d+r.Count)%r.Count];UpdateFocus();}
  }
  else if(k=="Return"||k=="KP_Enter"){if(area==0||area==TB){area=1;selected=page==0?SourceIndex():0;UpdateFocus();}else Activate();}
  else if(k=="XF86Back"||k=="Back"||k=="Escape"||k=="XF86Exit")SelectSource(lastSource);
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
  if(selected<0||selected>=slots.Count)return;
  string a=slots[selected].Action;notice.Text="";
  if(a=="info"){BuildPage(false);return;} // refresh in place: no fade (it read as a flash)
  if(a=="inputs"){OpenInputs();return;}
  if(a=="ambient"){Launch("org.tizen.art-app");return;}
  if(a.StartsWith("hdmi:")){SelectSource(int.Parse(a.Substring(5)));return;}
  if(a.StartsWith("page:")){page=int.Parse(a.Substring(5));selected=page==0?SourceIndex():0;area=1;BuildPage();return;}
  if(a.StartsWith("app:")){Launch(a.Substring(4));return;}
  if(a=="controllers"){Launch("org.tizen.menu","add-bluetooth-keyboard");return;}
  if(a=="network"){Launch("org.tizen.menu","networksettings");return;}
  if(a.StartsWith("heroport:")){CycleHeroPort(a.Substring(9)=="watch");return;}
  if(a.StartsWith("toggle:")){ToggleSetting(a.Substring(7));return;}
  if(a=="volume"||a=="brightness"){RefreshSettings();RefreshTileValues();if(a=="brightness"&&!CanEdit(a)){ShowError("Brightness is unavailable for the current input.");return;}editing=a;UpdateFocus();return;}
  if(a=="game-mode"){RefreshSettings();SetSetting(a,SettingValue(a)=="1"?"0":"1");return;}
  if(a.StartsWith("cycle:")){CycleSetting(a.Substring(6));return;}
  if(a=="output"){RefreshSettings();SetSetting("soundoutput",SettingValue("soundoutput")=="0"?"2":"0");return;}
  if(a=="sleeptimer"){RefreshSettings();int n;if(int.TryParse(SettingValue(a),out n))SetSetting(a,((n+1)%5).ToString());return;}
  if(a=="mute"){int n;int r=sound_manager_get_master_mute(out n);if(r==0)r=sound_manager_set_master_mute(n==1?0:1);if(r!=0)ShowError("Use the remote's mute button.");RefreshSettings();RefreshTileValues();MarkState("mute_changed");}
 }
 void ShowError(string text){notice.Text=text;}
 public static void Main(string[] args){using(var app=new QuietHome())app.Run(args);}
}
