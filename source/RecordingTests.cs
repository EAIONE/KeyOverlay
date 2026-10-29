using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
class RecordingTests {
 static BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static object Get(object target,string name){return target.GetType().GetField(name,flags).GetValue(target);}
 static void Set(object target,string name,object value){target.GetType().GetField(name,flags).SetValue(target,value);}
 static object Call(object target,string name,params object[] args){return target.GetType().GetMethod(name,flags).Invoke(target,args);}
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 [STAThread] static int Main(){try{
  var overlay=(KeyOverlay)FormatterServices.GetUninitializedObject(typeof(KeyOverlay));
  Set(overlay,"held",new HashSet<int>());Set(overlay,"options",new OverlayOptions());
  var panel=new StackPanel();var box=(TextBox)Call(overlay,"AddHotkey",panel,"test","Ctrl + Alt + K");
  box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,0,null,box){RoutedEvent=Keyboard.GotKeyboardFocusEvent});
  bool automatic=(bool)Get(overlay,"recording");Call(overlay,"EndRecording");
  Check(!automatic,"automatic focus must not suspend keyboard display");
  box.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseDownEvent});
  Check((bool)Get(overlay,"recording"),"explicit mouse selection starts recording");
  Call(overlay,"SettingsDeactivated",null,EventArgs.Empty);
  Check(!(bool)Get(overlay,"recording")&&Get(overlay,"recordingBox")==null,"deactivation ends recording even when logical focus remains");
  Call(overlay,"EndRecording");
  Console.WriteLine("All recording tests passed");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
