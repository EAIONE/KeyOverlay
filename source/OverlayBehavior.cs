using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Forms=System.Windows.Forms;

public sealed class HotkeySpec {
 public uint Modifiers {get;private set;}
 public uint Key {get;private set;}
 HotkeySpec(uint mod,uint key){Modifiers=mod;Key=key;}
 public static HotkeySpec Parse(string text){HotkeySpec result;if(!TryParse(text,out result))throw new FormatException("请使用 Ctrl/Alt/Shift + 一个按键，例如 Ctrl+Alt+L。");return result;}
 public static bool TryParse(string text,out HotkeySpec result){
  result=null;if(String.IsNullOrWhiteSpace(text))return false;
  uint mod=0,key=0;var tokens=text.Split('+');
  foreach(string token in tokens){string t=token.Trim();uint bit=0;
   switch(t.ToLowerInvariant()){case "ctrl":case "control":bit=2;break;case "alt":bit=1;break;case "shift":bit=4;break;case "win":bit=8;break;}
   if(bit!=0){if((mod&bit)!=0)return false;mod|=bit;continue;}
   Forms.Keys parsed;if(key!=0||t.Length==0||!Enum.TryParse<Forms.Keys>(t,true,out parsed))return false;
   int k=(int)parsed;if(k<8||k>254||k==16||k==17||k==18||k==91||k==92||(k>=160&&k<=165))return false;
   key=(uint)k;
  }
  if(mod==0||key==0||(mod==8&&(key==76||key==85)))return false;
  result=new HotkeySpec(mod,key);return true;
 }
 public override string ToString(){var v=new List<string>();if((Modifiers&2)!=0)v.Add("Ctrl");if((Modifiers&1)!=0)v.Add("Alt");if((Modifiers&4)!=0)v.Add("Shift");if((Modifiers&8)!=0)v.Add("Win");v.Add(((Forms.Keys)Key).ToString());return String.Join(" + ",v);}
}
public sealed class TripleClickTracker {
 readonly long interval;readonly int tolerance;int count,x,y;long last;
 public TripleClickTracker(long intervalMilliseconds,int tolerancePixels){interval=intervalMilliseconds;tolerance=tolerancePixels;}
 public bool Add(long time,int px,int py,bool inside){
  if(!inside){count=0;return false;}
  if(count==0||time<last||time-last>interval||Math.Abs(px-x)>tolerance||Math.Abs(py-y)>tolerance){count=1;x=px;y=py;last=time;return false;}
  last=time;count++;if(count<3)return false;count=0;return true;
 }
}
public sealed class OverlayOptions {
 public double Size=32,Alpha=.88,Duration=1.4,Left=100,Top=100;
 public bool Locked=true,Hidden=false,HotkeysEnabled=true;
 public string LockKey="Ctrl + Alt + L",SettingsKey="Ctrl + Alt + K",PauseKey="Ctrl + Alt + P",VisibilityKey="Ctrl + Alt + H";
 static double Number(string s,double min,double max,double fallback){double d;if(!double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out d)||double.IsNaN(d)||double.IsInfinity(d)||d<min||d>max)return fallback;return d;}
 public static OverlayOptions Load(string[] v){var o=new OverlayOptions();
  if(v.Length>0)o.Size=Number(v[0],16,80,32);if(v.Length>1)o.Alpha=Number(v[1],.15,1,.88);
  bool b;if(v.Length>2&&bool.TryParse(v[2],out b))o.Locked=b;
  if(v.Length>3)o.Left=Number(v[3],-100000,100000,100);if(v.Length>4)o.Top=Number(v[4],-100000,100000,100);
  if(v.Length>5)o.Duration=Number(v[5],.3,5,1.4);
  HotkeySpec k;if(v.Length>6&&HotkeySpec.TryParse(v[6],out k))o.LockKey=k.ToString();
  if(v.Length>7&&HotkeySpec.TryParse(v[7],out k))o.SettingsKey=k.ToString();
  if(v.Length>8&&HotkeySpec.TryParse(v[8],out k))o.PauseKey=k.ToString();
  if(v.Length>9&&HotkeySpec.TryParse(v[9],out k))o.VisibilityKey=k.ToString();
  if(v.Length>10&&bool.TryParse(v[10],out b))o.Hidden=b;
  if(v.Length>11&&bool.TryParse(v[11],out b))o.HotkeysEnabled=b;
  var keys=new[]{o.LockKey,o.SettingsKey,o.PauseKey,o.VisibilityKey};
  if(keys.Distinct().Count()!=4){o.LockKey="Ctrl + Alt + L";o.SettingsKey="Ctrl + Alt + K";o.PauseKey="Ctrl + Alt + P";o.VisibilityKey="Ctrl + Alt + H";}
  return o;
 }
 public string[] Serialize(){return new[]{Size.ToString(CultureInfo.InvariantCulture),Alpha.ToString(CultureInfo.InvariantCulture),Locked.ToString(),Left.ToString(CultureInfo.InvariantCulture),Top.ToString(CultureInfo.InvariantCulture),Duration.ToString(CultureInfo.InvariantCulture),LockKey,SettingsKey,PauseKey,VisibilityKey,Hidden.ToString(),HotkeysEnabled.ToString()};}
}
public static class StartupEntry {
 public const string ValueName="KeyOverlayRounded";
 public static string Command(string path){if(path.IndexOf('"')>=0)throw new ArgumentException("Invalid executable path");return "\""+path+"\" --startup";}
}
