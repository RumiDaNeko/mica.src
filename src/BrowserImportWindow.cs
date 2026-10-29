using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Mica {
 // Import from another browser on this PC: bookmarks, history, open tabs and extensions straight from its
 // profile files, and passwords from a file you export yourself. Sign-ins and cookies stay behind:
 // other browsers lock them to themselves, and reading them anyway is what password-stealing malware does.
 partial class BrowserApp {
  internal Window importWindow;
  internal Func<string> pickPasswordsFileForTest;

  void ShowImport() {
   if(privateMode){Status("Importing isn't available in private windows.");return;}
   if(importWindow!=null){importWindow.Activate();return;}
   StackPanel body,actions;var dialog=CreateMicaDialog(window,"Import from another browser",500,640,out body,out actions);importWindow=dialog;
   dialog.Closed+=delegate {if(importWindow==dialog)importWindow=null;};
   body.Children.Add(DialogText("Bring your bookmarks, history, open tabs and extensions over from another browser on this PC. Sign-ins can't come along: you'll sign in to sites again.",12,"Muted"));
   var sources=BrowserImporter.Find();ImportSource chosen=sources.FirstOrDefault();List<ImportExtension> extensions=new List<ImportExtension>();
   SectionLabel(body,"FROM");
   var list=new StackPanel {Tag="import-sources"};body.Children.Add(list);
   if(sources.Count==0)list.Children.Add(DialogText("No other browsers found on this PC.",12,"Ink"));
   SectionLabel(body,"WHAT TO BRING");
   Func<string,string,CheckBox> option=delegate(string text,string tag) {var box=new CheckBox {Content=text,IsChecked=true,Margin=new Thickness(0,4,0,4),Tag=tag};body.Children.Add(box);return box;};
   var bookmarks=option("Bookmarks","import-bookmarks");var history=option("History","import-history");var tabs=option("Open tabs","import-tabs");var addons=option("Extensions","import-extensions");
   var addonNames=DialogText("",11,"Muted");addonNames.TextWrapping=TextWrapping.Wrap;addonNames.Margin=new Thickness(28,0,0,4);body.Children.Add(addonNames);
   SectionLabel(body,"PASSWORDS");
   var passwordHint=DialogText("",12,"Muted");passwordHint.TextWrapping=TextWrapping.Wrap;body.Children.Add(passwordHint);
   var passwordRow=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0)};body.Children.Add(passwordRow);
   var pick=new Button {Content="Choose the exported file…",Padding=new Thickness(14,8,14,8),Tag="import-passwords"};passwordRow.Children.Add(pick);
   var erase=new Button {Content="Delete that file",Padding=new Thickness(14,8,14,8),Margin=new Thickness(8,0,0,0),Visibility=Visibility.Collapsed,Tag="import-erase"};passwordRow.Children.Add(erase);
   var passwordResult=DialogText("",12,"Ink");passwordResult.TextWrapping=TextWrapping.Wrap;passwordResult.Margin=new Thickness(0,8,0,0);body.Children.Add(passwordResult);
   var result=DialogText("",12,"Ink");result.TextWrapping=TextWrapping.Wrap;result.Margin=new Thickness(0,14,0,0);result.Tag="import-result";body.Children.Add(result);
   var go=new Button {Content="Import",Padding=new Thickness(18,9,18,9),IsDefault=true,IsEnabled=chosen!=null,Tag="import-go"};go.SetResourceReference(Button.BackgroundProperty,"Hover");actions.Children.Add(go);
   bool busy=false;
   Action refresh=null;
   Action<ImportSource> choose=delegate(ImportSource source) {
    chosen=source;
    foreach(var row in list.Children.OfType<Button>()){if(row.DataContext==source)row.SetResourceReference(Button.BackgroundProperty,"Hover");else row.ClearValue(Button.BackgroundProperty);}
    refresh();
   };
   foreach(var source in sources) {
    var item=source;var row=new Button {Content=item.Name,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,0,2),DataContext=item,Tag="import-source:"+item.Name};
    row.Click+=delegate {if(!busy)choose(item);};list.Children.Add(row);
   }
   refresh=delegate {
    if(chosen==null){passwordHint.Text=PasswordExportHint(null);return;}
    extensions=BrowserImporter.Extensions(chosen);
    addons.IsEnabled=extensions.Count>0;if(extensions.Count==0)addons.IsChecked=false;
    addons.Content=chosen.Firefox?"Extensions (Firefox add-ons can't run in Mica)":extensions.Count==0?"Extensions (none found)":"Extensions ("+extensions.Count+")";
    addonNames.Text=string.Join(", ",extensions.Take(6).Select(e=>e.Name))+(extensions.Count>6?" and "+(extensions.Count-6)+" more":"");
    passwordHint.Text=PasswordExportHint(chosen);
   };
   if(chosen!=null)choose(chosen);else refresh();
   go.Click+=async delegate {
    if(busy||chosen==null)return;
    if(go.Content as string=="Done"){dialog.Close();return;}
    busy=true;go.IsEnabled=false;result.Text="Importing…";
    try {result.Text=await ImportFrom(chosen,bookmarks.IsChecked==true,history.IsChecked==true,tabs.IsChecked==true,addons.IsChecked==true?extensions:new List<ImportExtension>());go.Content="Done";}
    catch(Exception e){result.Text="Couldn't import: "+e.Message;}
    finally {busy=false;go.IsEnabled=true;}
   };
   string exported=null;
   pick.Click+=async delegate {
    string file=pickPasswordsFileForTest!=null?pickPasswordsFileForTest():null;
    if(file==null){var picker=new OpenFileDialog {Title="Choose your exported passwords",Filter="Passwords export|*.csv|All files|*.*",CheckFileExists=true};if(picker.ShowDialog(dialog)!=true)return;file=picker.FileName;}
    pick.IsEnabled=false;passwordResult.Text="Adding your passwords…";
    try {passwordResult.Text=await ImportPasswords(file);exported=file;erase.Visibility=Visibility.Visible;}
    catch(Exception e){passwordResult.Text="Couldn't read that file: "+e.Message;}
    finally {pick.IsEnabled=true;}
   };
   // The export holds every password in plain text, so offer to get rid of it.
   erase.Click+=delegate {
    if(exported==null)return;
    try {EraseFile(exported);passwordResult.Text+=" The exported file is deleted.";erase.Visibility=Visibility.Collapsed;exported=null;}
    catch(Exception e){passwordResult.Text="Couldn't delete the file: "+e.Message;}
   };
   dialog.Show();
  }
  static string PasswordExportHint(ImportSource source) {
   string browser=source==null?null:source.Browser;
   string how=browser=="Firefox"?"In Firefox: Passwords (about:logins), the ••• menu, Export passwords."
    :browser=="Microsoft Edge"?"In Edge: Settings, Passwords, the ••• menu, Export passwords."
    :browser=="Google Chrome"?"In Chrome: Settings, Passwords and autofill, Google Password Manager, Settings, Export passwords."
    :"In your browser's password settings, choose Export passwords.";
   return "Mica doesn't open other browsers' saved passwords. Export them yourself, then choose the file. "+how;
  }
  static void EraseFile(string path) {
   long size=new FileInfo(path).Length;
   using(var stream=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.None)){var zeros=new byte[Math.Min(size,1<<16)];for(long done=0;done<size;done+=zeros.Length)stream.Write(zeros,0,(int)Math.Min(zeros.Length,size-done));}
   File.Delete(path);
  }

  internal async Task<string> ImportFrom(ImportSource source,bool bookmarks,bool history,bool tabs,List<ImportExtension> extensions) {
   string temp=Path.Combine(Path.GetTempPath(),"mica-import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
   var done=new List<string>();var problems=new List<string>();
   try {
    List<PageEntry> marks=null,visits=null,open=null;
    await Task.Run(delegate {
     if(bookmarks)try {marks=BrowserImporter.Bookmarks(source,temp);}catch(Exception e){problems.Add("bookmarks ("+e.Message+")");}
     if(history)try {visits=BrowserImporter.History(source,temp);}catch(Exception e){problems.Add("history ("+e.Message+")");}
     if(tabs)open=BrowserImporter.OpenTabs(source);
    });
    if(closing)return "";
    if(marks!=null) {
     var have=new HashSet<string>(state.Bookmarks.Select(b=>b.Url));int added=0;
     foreach(var mark in marks)if(have.Add(mark.Url)&&state.Bookmarks.Count<2000){state.Bookmarks.Add(new PageEntry {Title=mark.Title,Url=mark.Url,LastUsed=mark.LastUsed});added++;}
     done.Add(Localizer.Translate(Plural(added,"bookmark")));
    }
    if(visits!=null) {
     int added=0;
     foreach(var visit in visits) {
      if(visit.Url.Length>BrowserState.MaxHistoryUrl)continue;var known=state.History.FirstOrDefault(h=>h.Url==visit.Url);
      if(known==null){state.History.Add(new PageEntry {Title=visit.Title,Url=visit.Url,LastUsed=visit.LastUsed,VisitCount=visit.VisitCount});added++;}
      else {known.VisitCount+=visit.VisitCount;if(visit.LastUsed>known.LastUsed)known.LastUsed=visit.LastUsed;}
     }
     var kept=state.History.OrderByDescending(h=>h.LastUsed).Take(500).ToList();state.History.Clear();state.History.AddRange(kept);
     done.Add(Localizer.Translate(Plural(Math.Min(added,500),"page")+" of history"));
    }
    if(open!=null) {
     var have=new HashSet<string>(state.Tabs.Select(t=>t.Url));int added=0;
     foreach(var tab in open)if(have.Add(tab.Url)&&state.Tabs.Count<100){state.Tabs.Add(new PageEntry {Id=Guid.NewGuid().ToString("N"),Title=tab.Title,Url=tab.Url,Space=state.Space,LastUsed=DateTime.UtcNow.AddMinutes(-1)});added++;}
     done.Add(Localizer.Translate(Plural(added,"tab")));if(added>0)RenderTabs();
    }
    Save();
    if(extensions.Count>0) {
     int added=0;var failed=new List<string>();
     foreach(var extension in extensions) {
      if(closing)break;
      try {
       bool fromStore=false;
       if(extension.Store)try {await InstallStoreDirect(extension.Id);fromStore=true;}catch(OperationCanceledException){throw;}catch(Exception){}
       if(!fromStore){string copy=await Task.Run(()=>CopyExtension(extension));await InstallExtension(copy);}
       added++;
      }catch(Exception){failed.Add(extension.Name);}
     }
     done.Add(Localizer.Translate(Plural(added,"extension")));if(failed.Count>0)problems.Add(string.Join(", ",failed.Take(4))+(failed.Count>4?" +"+(failed.Count-4):"")+" "+Localizer.Translate("(extensions that didn't work in Mica)"));
    }
   }finally {try {Directory.Delete(temp,true);}catch(Exception){}}
   // Put together from translated parts: the counts vary too much for one phrase per combination.
   string summary=string.Format(Localizer.Translate("Brought over {0} from {1}."),done.Count==0?Localizer.Translate("nothing"):string.Join(", ",done),source.Name);
   if(problems.Count>0)summary+=" "+string.Format(Localizer.Translate("Left out: {0}."),string.Join("; ",problems));
   Status(summary);return summary;
  }
  static string Plural(int count,string word) {return count+" "+word+(count==1?"":"s");}
  // A copy Mica owns, so the other browser updating or removing the extension doesn't break it here.
  string CopyExtension(ImportExtension extension) {
   string target=Path.Combine(profile,"ImportedExtensions",extension.Id);
   if(Directory.Exists(target))Directory.Delete(target,true);
   foreach(var file in Directory.GetFiles(extension.Folder,"*",SearchOption.AllDirectories)) {
    string relative=file.Substring(extension.Folder.Length).TrimStart('\\');if(relative.StartsWith("_metadata",StringComparison.OrdinalIgnoreCase))continue; // the store's signature for the old browser
    string copy=Path.Combine(target,relative);Directory.CreateDirectory(Path.GetDirectoryName(copy));File.Copy(file,copy,true);
   }
   return target;
  }

  internal async Task<string> ImportPasswords(string file) {
   string text=await Task.Run(()=>File.ReadAllText(file));int skipped;var logins=BrowserImporter.PasswordsCsv(text,out skipped);text=null;
   if(logins.Count==0)return "No passwords to add"+(skipped>0?" ("+Plural(skipped,"entry")+" weren't for https sites)":"")+".";
   if(sessionVault==null)await OpenAutofillVault();
   if(sessionVault!=null) {
    foreach(var login in logins) {
     var entry=sessionVault.Entries.FirstOrDefault(e=>VaultCrypto.Matches(e.Origin,login.Origin)&&e.Username==login.Username);
     if(entry==null)sessionVault.Entries.Add(login);else entry.Password=login.Password;
    }
    if(!await SaveSessionVault())throw new IOException("the vault couldn't be saved");
   }else await Task.Run(()=>DeviceLogins.UpsertAll(profile,logins));
   string done="Added "+Plural(logins.Count,"password")+".";
   if(skipped>0)done+=" Skipped "+skipped+" that weren't for https sites or had no password.";
   return done;
  }
 }
}
