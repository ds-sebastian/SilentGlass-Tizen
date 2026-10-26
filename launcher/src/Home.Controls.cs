using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Settings (Samsung settings API by short menuitem id), launching apps and switching inputs.
public sealed partial class QuietHome
{
 int currentVolume,currentMute;
 readonly Dictionary<string,string> settingValues=new Dictionary<string,string>();
 readonly Dictionary<string,string> settingNames=new Dictionary<string,string>();
 readonly Dictionary<string,bool> settingEnabled=new Dictionary<string,bool>();
 // picture levels the TV reports a range for (brightness, contrast): min, max, step
 readonly Dictionary<string,(double min,double max,double step)> ranges=new Dictionary<string,(double,double,double)>();
 (double min,double max,double step) Range(string id)=>ranges.TryGetValue(id,out var r)?r:(0,50,1);
 [return: MarshalAs(UnmanagedType.U1)][DllImport("/usr/lib/libsource-api.so")] static extern bool connect_source(int sourceType);
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

 // Each settings read is an IPC round trip: Home only reads the two values its tiles show.
 void RefreshSettings()
 {
  var ids=page==2?SettingIds:HomeSettingIds;
  if(page==2){
   currentVolume=sound_manager_get_master_volume(out var v)==0?v:-1;
   currentMute=sound_manager_get_master_mute(out var m)==0?m:-1;
   foreach(var id in new[]{"brightness","contrast"})
    try{if(CSharpGetItemMinMaxStepValue(id,out var mn,out var mx,out var st)&&st>0&&mx>mn)ranges[id]=(mn,mx,st);}catch{}
  }
  foreach(var id in ids){
   try{if(CSharpGetItemValue(id,out var name,out var value)&&!string.IsNullOrEmpty(value)){settingValues[id]=value;settingNames[id]=name;}else{settingValues.Remove(id);settingNames.Remove(id);}
    settingEnabled[id]=CSharpGetItemEnable(id,out var enabled)&&enabled;
   }catch{settingValues.Remove(id);settingNames.Remove(id);settingEnabled[id]=false;}
  }
 }
 void RefreshTileValues()
 {
  foreach(var s in slots){
   if((s.Kind!=2&&s.Kind!=5)||s.Label==null)continue;
   var v=TileValue(s.Action);if(over&&editing==s.Action)v="‹  "+v+"  ›"; // adjusting: Left / Right
   if(s.Label.Text!=v)s.Label.Text=v;
   if(s.Bar!=null){float l=TileLevel(s.Action);s.Bar.Size2D=new Size2D(l<0?0:(int)(s.BarW*Math.Clamp(l,0f,1f)),8);}
  }
 }
 static readonly string[] SettingIds={"brightness","contrast","game-mode","sleeptimer","external-sound-mode","soundoutput","picturemode","game-mode-auto","pixel-shift-menu"};
 static readonly string[] HomeSettingIds={"sleeptimer","soundoutput"};
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
   case "brightness":case "contrast":return SettingValue(action)!=""?SettingValue(action):"Unavailable";
   case "full:2":return "All controls";
   case "mute":return currentMute<0?"Unavailable":currentMute==1?"Muted":"Sound on";
   case "output":return SettingValue("soundoutput")=="2"?"Soundbar":SettingValue("soundoutput")=="0"?"TV Speaker":SettingName("soundoutput");
   case "network":return "Connections";
   case "controllers":return "Pair a device";
   case "app:org.tizen.menu":return "More settings";
   case "page:2":return "All controls";
   case "ambient":return "Art & photos";
   case "inputs":{var cur=inputs.Find(i=>i.Type==lastSource);return cur!=null?cur.Name:"Choose";}
   case "heroport:watch":return HeroPortValue(prefs.Watch,watchPort);
   case "heroport:play":return HeroPortValue(prefs.Play,playPort);
  }
  return SettingName(action.StartsWith("cycle:")?action.Substring(6):action);
 }
 string SettingValue(string id){return settingValues.TryGetValue(id,out var v)?v:"";}
 string SettingName(string id){return settingNames.TryGetValue(id,out var v)?v:"Unavailable";}
 bool CanEdit(string id){return settingEnabled.TryGetValue(id,out var enabled)&&enabled&&settingValues.ContainsKey(id);}
 void SetSetting(string id,string value){
  if(!CanEdit(id)){ShowError("This setting is unavailable for the current input.");return;}
  bool ok=false;try{ok=CSharpSetItemValue(id,value);}catch{}
  RefreshSettings();RefreshTileValues();
  bool verified=ok&&SettingValue(id)==value;
  if(!verified)ShowError("The TV could not apply this setting. Try Advanced settings.");
  MarkState("setting:"+id+":"+value+":"+verified);
 }
 void ChangeLevel(string id,int delta){var (mn,mx,st)=Range(id);if(double.TryParse(SettingValue(id),NumberStyles.Float,CultureInfo.InvariantCulture,out double n))SetSetting(id,Math.Max(mn,Math.Min(mx,n+delta*st)).ToString(CultureInfo.InvariantCulture));}
 void ChangeVolume(int delta){int n;int r=sound_manager_get_master_volume(out n);if(r==0)r=sound_manager_set_master_volume(Math.Max(0,Math.Min(100,n+delta)));if(r!=0)ShowError("Use the remote's volume buttons.");RefreshSettings();RefreshTileValues();MarkState("volume_changed");}
 // connect_source returns a C bool; the result is recorded, the viewer is opened either way (unchanged behaviour)
 void SelectSource(int source){bool ok=false;try{ok=connect_source(source);}catch{}lastSource=source;MarkState("source:"+source+":"+ok);Launch("org.tizen.tv-viewer");}
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
   if(r==0)r=app_control_send_launch_request(c,IntPtr.Zero,IntPtr.Zero);MarkState("launch:"+id+(menuItem==null?"":":"+menuItem)+":"+r);if(r!=0)ShowError("Could not open this screen. Try Advanced settings.");}finally{app_control_destroy(c);}
 }

}
