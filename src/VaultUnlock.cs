using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Threading.Tasks;

namespace Mica {
 partial class BrowserApp {
  IHelloVerifier helloVerifier=new WindowsHello();
  Window vaultUnlockWindow;
  StackPanel vaultUnlockBody,vaultUnlockActions;
  CheckBox vaultHelloOptIn;
  PasswordBox vaultMaster;
  Button vaultUnlockButton,vaultHelloButton;
  bool vaultUnlockComplete,helloBusy;
  void SetVaultUnlockBusy(bool busy) {
   if(vaultUnlockWindow==null)return;
   vaultUnlockBody.IsEnabled=!busy;vaultUnlockActions.IsEnabled=!busy;
  }
  void CloseVaultUnlock() {
   vaultUnlockComplete=true;
   if(vaultUnlockWindow!=null)vaultUnlockWindow.Close();
   vaultMaster=null;vaultUnlockButton=null;vaultHelloOptIn=null;
  }
  Action vaultOpenUnlock;
  void FinishVaultUnlock() {vaultUnlockComplete=true;if(vaultUnlockWindow!=null)vaultUnlockWindow.Close();}
  void PrepareVaultUnlock(StackPanel root,PasswordBox master,Button unlock,TextBlock status) {
   vaultMaster=master;vaultUnlockButton=unlock;vaultUnlockComplete=false;helloBusy=false;
   var owner=vaultWindow;
   // The vault opens without asking; the lock button calls this when passwords are needed.
   vaultOpenUnlock=delegate {
    if(vaultWindow!=owner||vaultUnlockWindow!=null)return;
    vaultUnlockComplete=false;helloBusy=false;foreach(FrameworkElement reused in new FrameworkElement[]{master,unlock}){var parent=reused.Parent as Panel;if(parent!=null)parent.Children.Remove(reused);}
    bool existing=File.Exists(Path.Combine(profile,"vault.enc.json"));bool choosing=existing&&File.Exists(PassphrasePendingPath);
    var popup=CreateMicaDialog(owner,choosing?"Choose your master password":existing?"Unlock your vault":"Create your vault",500,510,out vaultUnlockBody,out vaultUnlockActions);vaultUnlockWindow=popup;
    vaultUnlockBody.Resources=root.Resources;
    var icon=DialogText("\uE72E",30,"Accent");icon.FontFamily=new System.Windows.Media.FontFamily("Segoe MDL2 Assets");icon.Margin=new Thickness(0,0,0,16);vaultUnlockBody.Children.Add(icon);
    var intro=DialogText(choosing?"Mica already keeps your logins in your vault. Choose a master password (12+ characters) to view them; it works like a passphrase and cannot be recovered.":existing?"Enter your master password to access saved logins.":"Choose a master password with at least 12 characters. Keep it safe — it cannot be recovered.",13,"Muted");intro.LineHeight=21;intro.Margin=new Thickness(0,0,0,16);vaultUnlockBody.Children.Add(intro);
    vaultUnlockBody.Children.Add(DialogText("Master password",11,"Muted"));master.Margin=new Thickness(0,8,0,14);System.Windows.Automation.AutomationProperties.SetName(master,"Master password");vaultUnlockBody.Children.Add(master);
    bool enrolled=File.Exists(HelloUnlockStore.PathFor(profile));
    vaultHelloOptIn=new CheckBox {Content="Enable Windows Hello on this device",IsChecked=false,IsEnabled=false,Margin=new Thickness(0,0,0,8),Visibility=enrolled?Visibility.Collapsed:Visibility.Visible};vaultHelloOptIn.SetResourceReference(Control.ForegroundProperty,"Ink");vaultUnlockBody.Children.Add(vaultHelloOptIn);
    var helloNote=DialogText("Checking Windows Hello…",11,"Muted");helloNote.Margin=new Thickness(0,0,0,12);vaultUnlockBody.Children.Add(helloNote);
    var feedback=DialogText("",11,"Muted");feedback.SetBinding(TextBlock.TextProperty,new Binding("Text"){Source=status});vaultUnlockBody.Children.Add(feedback);status.Text="";
    if(enrolled){var forget=new Button {Content="Forget Windows Hello on this device",Padding=new Thickness(0,8,0,8),HorizontalAlignment=HorizontalAlignment.Left};forget.Click+=delegate {try{HelloUnlockStore.Forget(profile);vaultHelloButton.Visibility=Visibility.Collapsed;forget.Visibility=Visibility.Collapsed;vaultHelloOptIn.Visibility=Visibility.Visible;status.Text="Windows Hello removed. Use your master password.";}catch(Exception){status.Text="Could not remove Windows Hello enrollment.";}};vaultUnlockBody.Children.Add(forget);}
    vaultHelloButton=new Button {Content="Windows Hello",Padding=new Thickness(14,10,14,10),Margin=new Thickness(0,0,8,0),Visibility=enrolled?Visibility.Visible:Visibility.Collapsed,IsEnabled=false};vaultHelloButton.Click+=async delegate {await UnlockVaultWithHello(owner,popup,master,unlock,status);};vaultUnlockActions.Children.Add(vaultHelloButton);
    unlock.Tag="Unlock / create local vault";unlock.Content=choosing?"Set password":existing?"Unlock vault":"Create vault";unlock.Margin=new Thickness(0);unlock.Padding=new Thickness(18,10,18,10);unlock.IsDefault=true;unlock.SetResourceReference(Button.BackgroundProperty,"Hover");vaultUnlockActions.Children.Add(unlock);
    popup.Closed+=delegate {bool finished=vaultUnlockComplete;vaultUnlockWindow=null;owner.IsEnabled=true;if(owner.IsVisible)owner.Activate();};
    owner.IsEnabled=false;popup.Show();master.Focus();
    CheckVaultHello(popup,helloNote);
   };
  }
  async void CheckVaultHello(Window popup,TextBlock note) {
   bool available=await helloVerifier.Available(()=>vaultUnlockWindow==popup);
   if(vaultUnlockWindow!=popup)return;
   vaultHelloOptIn.IsEnabled=available;vaultHelloButton.IsEnabled=available;
   note.Text=available?"PIN, fingerprint or face. Optional device-only unlock, protected by Windows.":"Windows Hello isn't available here. Use your master password.";
  }
  async Task UnlockVaultWithHello(Window owner,Window popup,PasswordBox master,Button unlock,TextBlock status) {
   if(helloBusy||vaultUnlockWindow!=popup)return;helloBusy=true;SetVaultUnlockBusy(true);
   bool verified=false;
   try {
    verified=await helloVerifier.Verify(new WindowInteropHelper(popup).Handle,()=>vaultUnlockWindow==popup&&vaultWindow==owner);
    if(vaultUnlockWindow!=popup||vaultWindow!=owner)return;
    if(!verified){status.Text="Windows Hello was cancelled or unavailable. Use your master password.";return;}
    // No DPAPI read before successful native verification and a still-live popup.
    master.Password=HelloUnlockStore.Load(profile);vaultHelloOptIn.IsChecked=false;
   }catch(Exception){verified=false;if(vaultUnlockWindow==popup)status.Text="Windows Hello unlock could not be read. Use your master password or forget this device enrollment.";}
   finally {helloBusy=false;if(vaultUnlockWindow==popup)SetVaultUnlockBusy(false);}
   if(verified&&vaultUnlockWindow==popup)unlock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  }
 }
}
