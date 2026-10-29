using System;
class BehaviorTests {
 static void Check(bool yes,string name){if(!yes)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static int Main(){try{
  var key=HotkeySpec.Parse("Ctrl + Alt + L");
  Check(key.Modifiers==3&&key.Key==76,"parse combination into Windows hotkey values");
  Check(HotkeySpec.Parse("Shift+F8").ToString()=="Shift + F8","function key with single modifier");
  Check(HotkeySpec.Parse("Ctrl+Alt+K").ToString()=="Ctrl + Alt + K","format editable hotkey");
  Check(HotkeySpec.Parse("Ctrl+Shift+OemPlus").Key==187,"punctuation key binding");
  Check(!HotkeySpec.TryParse("Ctrl",out key),"reject modifier-only binding");
  Check(!HotkeySpec.TryParse("A",out key),"reject unmodified typing key");
  Check(!HotkeySpec.TryParse("Ctrl+A+B",out key),"reject two main keys");
  Check(!HotkeySpec.TryParse("Ctrl+Alt+999",out key),"reject out-of-range virtual key");
  Check(!HotkeySpec.TryParse("Ctrl+Ctrl+A",out key),"reject duplicate modifier");
  Check(!HotkeySpec.TryParse("Win+L",out key),"reject Windows lock shortcut");
  var clicks=new TripleClickTracker(500,6);
  Check(!clicks.Add(0,100,100,true)&&!clicks.Add(150,101,100,true)&&clicks.Add(300,100,101,true),"three nearby clicks open settings");
  Check(!clicks.Add(310,100,100,true),"fourth click does not reopen settings");
  clicks=new TripleClickTracker(500,6);
  Check(!clicks.Add(0,100,100,true)&&!clicks.Add(600,100,100,true)&&!clicks.Add(700,100,100,true),"slow clicks do not trigger triple click");
  clicks=new TripleClickTracker(500,6);
  clicks.Add(0,100,100,true);clicks.Add(50,100,100,false);
  Check(!clicks.Add(100,100,100,true)&&!clicks.Add(150,100,100,true),"click outside card resets sequence");
  clicks=new TripleClickTracker(500,6);
  clicks.Add(0,100,100,true);clicks.Add(50,130,100,true);
  Check(!clicks.Add(100,100,100,true),"moved clicks do not trigger settings");
  var old=OverlayOptions.Load(new[]{"32","0.88","True","100","200","1.4"});
  Check(old.Size==32&&old.Locked&&old.LockKey=="Ctrl + Alt + L"&&!old.Hidden,"load old six-line settings with new defaults");
  Check(old.HotkeysEnabled,"old settings retain enabled global hotkeys");
  old.LockKey="Ctrl + Shift + F9";old.Hidden=true;old.HotkeysEnabled=false;
  var again=OverlayOptions.Load(old.Serialize());
  Check(again.LockKey=="Ctrl + Shift + F9"&&again.Hidden,"custom hotkeys and visibility persist");
  Check(!again.HotkeysEnabled,"disabled global hotkeys persist across restart");
  var corrupt=OverlayOptions.Load(new[]{"NaN","Infinity","True","NaN","-Infinity","-3","bad","bad","bad","bad","bad"});
  Check(corrupt.Size==32&&corrupt.Alpha==.88&&corrupt.Left==100&&corrupt.Top==100&&corrupt.Duration==1.4,"corrupt numeric settings recover safely");
  Check(StartupEntry.Command(@"D:\path with space\KeyOverlay.exe")=="\"D:\\path with space\\KeyOverlay.exe\" --startup","quote autostart path and suppress settings at logon");
  Console.WriteLine("All behavior tests passed");return 0;
 }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}}
}
