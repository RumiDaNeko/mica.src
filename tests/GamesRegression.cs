using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  async Task RunTetrisTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"tetris");Directory.CreateDirectory(dir);
   try {
    RegressionCheck("mica://tetris is an internal address",delegate {
     Require(Navigation.Resolve("mica://tetris")=="mica://tetris"&&Navigation.Resolve("MICA://Tetris/")=="mica://tetris","resolve");
     Require(Navigation.FromEngine(Navigation.EngineAddress("mica://tetris"))=="mica://tetris","round trip");
     Require(Navigation.FromEngine("https://evil.test/?tetris.mica.invalid")=="https://evil.test/?tetris.mica.invalid","other hosts stay");
    });
    Navigate("mica://tetris",false);await WaitForPage();
    var core=CurrentView().CoreWebView2;
    for(int i=0;i<40&&await core.ExecuteScriptAsync("!!document.getElementById('field')")!="true";i++)await Task.Delay(250); // the first game served can take a moment
    string page=await core.ExecuteScriptAsync("JSON.stringify({title:document.title,canvas:!!document.getElementById('field'),w:document.getElementById('field').width})");
    log.Add("tetris page: "+page+" address "+Find<TextBlock>("AddressText").Text+" tab "+Active.Url);
    RegressionCheck("mica://tetris opens the game",delegate {Require(page.Contains("Tetris")&&page.Contains("\\\"canvas\\\":true"),page);Require(Active.Url=="mica://tetris"&&Find<TextBlock>("AddressText").Text=="mica://tetris",Active.Url);});
    // Play a little: start, hard drop a few pieces, and see the stack grow.
    await core.ExecuteScriptAsync("document.getElementById('start').click()");await Task.Delay(200);
    foreach(var k in new[]{"Space","ArrowLeft","Space","ArrowRight","ArrowRight","Space","ArrowUp","Space"}) {
     foreach(var type in new[]{"rawKeyDown","keyUp"})await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\""+type+"\",\"code\":\""+k+"\",\"key\":\""+(k=="Space"?" ":k)+"\",\"windowsVirtualKeyCode\":"+(k=="Space"?32:k=="ArrowLeft"?37:k=="ArrowUp"?38:39)+"}");
     await Task.Delay(260);
    }
    string played=await core.ExecuteScriptAsync("JSON.stringify({score:+document.getElementById('score').textContent,filled:board.flat().filter(Boolean).length,state})");log.Add("after playing: "+played);
    RegressionCheck("the game responds to the keyboard",delegate {Require(played.Contains("play")&&!played.Contains("\\\"filled\\\":0,")&&!played.Contains("\\\"score\\\":0,"),played);});
    var shot=await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot","{\"format\":\"png\"}");
    File.WriteAllBytes(Path.Combine(dir,"tetris.png"),Convert.FromBase64String((string)new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(shot)["data"]));
    // Nothing reaches the network: the page is served by Mica even with a host that never resolves.
    RegressionCheck("Tetris is served offline",delegate {Require(Navigation.TetrisHost.EndsWith(".invalid"),Navigation.TetrisHost);});
    // An address that cannot resolve shows the error page with the Tetris link.
    Navigate("https://mica-offline-check.invalid/",false);await WaitForPage();
    // The lookup can take a moment to fail; wait for the error page instead of guessing.
    await WaitFor(()=>Find<Button>("GamesButton").Visibility==Visibility.Visible,10000).ContinueWith(t=>{});await Task.Delay(200);
    string title=Find<TextBlock>("ErrorTitle").Text;bool button=Find<Button>("GamesButton").Visibility==Visibility.Visible;
    log.Add("unresolvable host: "+title+" / "+Find<TextBlock>("ErrorText").Text+" / tetris button "+button);
    RegressionCheck("a connection failure offers Tetris",delegate {Require(Find<Border>("ErrorPanel").Visibility==Visibility.Visible&&button,title);});
    var saved=NetworkAvailable;NetworkAvailable=()=>false;
    try {ShowLoadError(CoreWebView2WebErrorStatus.Disconnected);}finally {NetworkAvailable=saved;}
    RegressionCheck("no internet says so",delegate {Require(Find<TextBlock>("ErrorTitle").Text=="Oh no, no internet."&&Find<Button>("GamesButton").Visibility==Visibility.Visible,Find<TextBlock>("ErrorTitle").Text);});
    ShowError("plain");RegressionCheck("other errors hide the games link",delegate {Require(Find<Button>("GamesButton").Visibility==Visibility.Collapsed,"visible");});
    ShowLoadError(CoreWebView2WebErrorStatus.HostNameNotResolved);
    int before=state.Tabs.Count;Find<Button>("GamesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitForPage();await Task.Delay(400);
    RegressionCheck("the link opens the Games page in a new tab",delegate {Require(state.Tabs.Count==before+1&&Active.Url=="mica://games"&&Active.Title=="Games",Active.Url+" "+Active.Title);});
    // The Games page lists every game; each one opens at its own mica:// address and plays.
    Func<string,string,Task> key=async delegate(string code,string name) {
     var c=CurrentView().CoreWebView2;int vk=code=="ArrowLeft"?37:code=="ArrowUp"?38:code=="ArrowRight"?39:code=="ArrowDown"?40:code=="Space"?32:0;
     foreach(var type in new[]{"rawKeyDown","keyUp"})await c.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\""+type+"\",\"code\":\""+code+"\",\"key\":\""+name+"\",\"windowsVirtualKeyCode\":"+vk+"}");
    };
    Navigate("mica://games",false);await WaitForPage();await Task.Delay(400);
    string hub=await CurrentView().CoreWebView2.ExecuteScriptAsync("JSON.stringify([...document.querySelectorAll('a.game')].map(a=>a.href))");log.Add("games page: "+hub);
    RegressionCheck("the Games page lists every game",delegate {Require(Active.Url=="mica://games"&&Active.Title=="Games"&&hub.Contains("tetris.mica.invalid")&&Navigation.Games.Skip(1).All(g=>hub.Contains("games.mica.invalid/"+g[2]+"\\\"")),Active.Url+" "+hub);});
    await CurrentView().CoreWebView2.ExecuteScriptAsync("document.querySelector('a[href$=\"/snake\"]').click()");await WaitForPage();await Task.Delay(400);
    RegressionCheck("a game opens from the Games page at its own address",delegate {Require(Active.Url=="mica://snake"&&Find<TextBlock>("AddressText").Text=="mica://snake",Active.Url);});
    await CurrentView().CoreWebView2.ExecuteScriptAsync("document.getElementById('start').click()");await Task.Delay(150);
    await key("ArrowDown","ArrowDown");await Task.Delay(700);
    string snake=await CurrentView().CoreWebView2.ExecuteScriptAsync("JSON.stringify({state,head:snake[0],dir})");log.Add("snake: "+snake);
    RegressionCheck("Snake moves and turns",delegate {Require(snake.Contains("\\\"state\\\":\\\"play\\\"")&&snake.Contains("\\\"dir\\\":{\\\"x\\\":0,\\\"y\\\":1}"),snake);});
    Navigate("mica://2048",false);await WaitForPage();await Task.Delay(400);
    string before2048=await CurrentView().CoreWebView2.ExecuteScriptAsync("JSON.stringify(tiles.map(t=>[t.x,t.y,t.v]))");
    foreach(var k in new[]{"ArrowLeft","ArrowUp","ArrowRight","ArrowDown","ArrowLeft"}){await key(k,k);await Task.Delay(220);}
    string after2048=await CurrentView().CoreWebView2.ExecuteScriptAsync("JSON.stringify({n:tiles.length,score,shown:document.querySelectorAll('.tile').length})");log.Add("2048: "+before2048+" -> "+after2048);
    RegressionCheck("2048 slides and adds tiles",delegate {Require(Active.Title=="2048"&&!after2048.Contains("\\\"n\\\":2,")&&after2048.Contains("\\\"shown\\\":"),after2048);});
    Navigate("mica://minesweeper",false);await WaitForPage();await Task.Delay(400);
    string mines=await CurrentView().CoreWebView2.ExecuteScriptAsync("cells[40].click();JSON.stringify({opened,over,mines:mines.reduce((a,b)=>a+b,0),first:mines[40]})");log.Add("minesweeper: "+mines);
    RegressionCheck("Minesweeper's first click is safe and opens a patch",delegate {Require(Active.Title=="Minesweeper"&&mines.Contains("\\\"over\\\":false")&&mines.Contains("\\\"mines\\\":10")&&mines.Contains("\\\"first\\\":0")&&!mines.Contains("\\\"opened\\\":0,")&&!mines.Contains("\\\"opened\\\":1,"),mines);});
    Navigate("mica://breakout",false);await WaitForPage();await Task.Delay(300);
    await CurrentView().CoreWebView2.ExecuteScriptAsync("document.getElementById('start').click()");await Task.Delay(150);await key("Space"," ");await Task.Delay(700);
    string breakout=await CurrentView().CoreWebView2.ExecuteScriptAsync("JSON.stringify({state,stuck,y:Math.round(ball.y),bricks:bricks.filter(b=>b.hits>0).length})");log.Add("breakout: "+breakout);
    RegressionCheck("Breakout launches the ball",delegate {Require(Active.Title=="Breakout"&&breakout.Contains("\\\"stuck\\\":false")&&breakout.Contains("\\\"state\\\":\\\"play\\\""),breakout);});
    await key("KeyP","p");await Task.Delay(200);
    string paused=await CurrentView().CoreWebView2.ExecuteScriptAsync("state");
    RegressionCheck("a paused game stops drawing",delegate {Require(paused=="\"paused\"",paused);});
    Navigate("mica://sudoku",false);await WaitForPage();await Task.Delay(300);
    string sudoku=await CurrentView().CoreWebView2.ExecuteScriptAsync("const blank=board.indexOf(0);sel=blank;put(solution[blank]);JSON.stringify({clues:puzzle.filter(Boolean).length,unique:count(puzzle.slice(),2),valid:puzzle.every((v,i)=>!v||v===solution[i]),placed:board[blank]===solution[blank]})");log.Add("sudoku: "+sudoku);
    RegressionCheck("Sudoku makes a puzzle with exactly one answer",delegate {Require(Active.Title=="Sudoku"&&sudoku.Contains("\\\"unique\\\":1")&&sudoku.Contains("\\\"valid\\\":true")&&sudoku.Contains("\\\"placed\\\":true"),sudoku);});
    Navigate("mica://memory",false);await WaitForPage();await Task.Delay(300);
    string memory=await CurrentView().CoreWebView2.ExecuteScriptAsync("const a=0,b=deck.indexOf(deck[0],1);flip(a);flip(b);JSON.stringify({pairs,moves,cards:deck.length})");log.Add("memory: "+memory);
    RegressionCheck("Memory matches a pair",delegate {Require(Active.Title=="Memory"&&memory.Contains("\\\"pairs\\\":1")&&memory.Contains("\\\"cards\\\":16"),memory);});
    // Power-ups: Breakout's multi-ball and wide paddle, Snake's ghost through its own tail.
    Navigate("mica://breakout",false);await WaitForPage();await Task.Delay(300);
    string powered=await CurrentView().CoreWebView2.ExecuteScriptAsync("start();grant('multi');grant('wide');JSON.stringify({extra:extra.length,width:width(),powers:document.getElementById('powers').textContent})");log.Add("breakout power-ups: "+powered);
    RegressionCheck("Breakout power-ups split the ball and widen the paddle",delegate {Require(powered.Contains("\\\"extra\\\":2")&&powered.Contains("\\\"width\\\":128")&&powered.Contains("Wide"),powered);});
    Navigate("mica://snake",false);await WaitForPage();await Task.Delay(300);
    string ghost=await CurrentView().CoreWebView2.ExecuteScriptAsync("start();clearTimeout(timer);spawnBonus('ghost');bonus.x=snake[0].x+1;bonus.y=snake[0].y;step();clearTimeout(timer);const g=active('ghost');snake=[{x:19,y:5},{x:18,y:5},{x:17,y:5}];dir={x:1,y:0};queued=[];step();clearTimeout(timer);JSON.stringify({g,state,head:snake[0],power:document.getElementById('power').textContent})");log.Add("snake ghost: "+ghost);
    RegressionCheck("Snake's ghost bonus wraps through the wall",delegate {Require(ghost.Contains("\\\"g\\\":true")&&ghost.Contains("\\\"state\\\":\\\"play\\\"")&&ghost.Contains("\\\"head\\\":{\\\"x\\\":0,\\\"y\\\":5}"),ghost);});
    // The new games: each opens from its address and plays.
    Navigate("mica://flappy",false);await WaitForPage();await Task.Delay(300);
    await CurrentView().CoreWebView2.ExecuteScriptAsync("start();window.y0=bird.y");await Task.Delay(400);string flappy=await CurrentView().CoreWebView2.ExecuteScriptAsync("JSON.stringify({state,moved:bird.y!==y0,pipes:pipes.length})");
    await Task.Delay(3500);string flapOver=await CurrentView().CoreWebView2.ExecuteScriptAsync("state");log.Add("flappy: "+flappy+" then "+flapOver);
    RegressionCheck("Flappy flies and ends when it falls",delegate {Require(Active.Title=="Flappy"&&flappy.Contains("\\\"moved\\\":true")&&flapOver=="\"over\"",flappy+" "+flapOver);});
    Navigate("mica://pong",false);await WaitForPage();await Task.Delay(300);
    await CurrentView().CoreWebView2.ExecuteScriptAsync("start()");await Task.Delay(1200);string pong=await CurrentView().CoreWebView2.ExecuteScriptAsync("JSON.stringify({state,moving:ball.vx!==0,x:Math.round(ball.x)})");log.Add("pong: "+pong);
    RegressionCheck("Pong serves and the ball moves",delegate {Require(Active.Title=="Pong"&&pong.Contains("\\\"moving\\\":true"),pong);});
    Navigate("mica://invaders",false);await WaitForPage();await Task.Delay(300);
    string invaders=await CurrentView().CoreWebView2.ExecuteScriptAsync("start();grant('triple');fire();JSON.stringify({state,shots:shots.length,aliens:aliens.filter(a=>a.alive).length,powers:document.getElementById('powers').textContent})");log.Add("invaders: "+invaders);
    RegressionCheck("Invaders fires, and triple shot fires three",delegate {Require(Active.Title=="Invaders"&&invaders.Contains("\\\"shots\\\":3")&&invaders.Contains("\\\"aliens\\\":45")&&invaders.Contains("Triple"),invaders);});
    Navigate("mica://connect4",false);await WaitForPage();await Task.Delay(300);
    await CurrentView().CoreWebView2.ExecuteScriptAsync("play(3)");await Task.Delay(1200);string connect=await CurrentView().CoreWebView2.ExecuteScriptAsync("JSON.stringify({you:board.flat().filter(v=>v===1).length,cpu:board.flat().filter(v=>v===2).length,turn})");
    string blocks=await CurrentView().CoreWebView2.ExecuteScriptAsync("board=Array.from({length:6},()=>Array(7).fill(0));board[5][0]=board[5][1]=board[5][2]=1;board[4][0]=2;board[4][1]=2;choose()");log.Add("connect four: "+connect+", blocks at "+blocks);
    RegressionCheck("Connect Four answers your move and blocks three in a row",delegate {Require(Active.Title=="Connect Four"&&connect.Contains("\\\"you\\\":1")&&connect.Contains("\\\"cpu\\\":1")&&blocks=="3",connect+" "+blocks);});
    foreach(var name in new[]{"flappy","pong","invaders","connect4","breakout"}) {
     Navigate("mica://"+name,false);await WaitForPage();await Task.Delay(400);
     await CurrentView().CoreWebView2.ExecuteScriptAsync(name=="connect4"?"play(3)":name=="breakout"?"start();grant('wide');launch()":"start()");await Task.Delay(name=="connect4"?900:700);
     if(name!="connect4")await CurrentView().CoreWebView2.ExecuteScriptAsync("if(typeof pause==='function')pause();document.getElementById('overlay').hidden=true");
     var picture=await CurrentView().CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot","{\"format\":\"png\"}");
     File.WriteAllBytes(Path.Combine(dir,name+".png"),Convert.FromBase64String((string)new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(picture)["data"]));
    }
    // Games follow Mica's theme, not Windows': they open in it, and switch while open.
    bool wasDark=state.Dark;state.Dark=true;ApplyTheme();Navigate("mica://snake",false);await WaitForPage();await Task.Delay(300);
    Func<Task<string>> look=()=>CurrentView().CoreWebView2.ExecuteScriptAsync("JSON.stringify({dark:document.documentElement.classList.contains('dark'),bg:getComputedStyle(document.body).backgroundColor})");
    string darkLook=await look();state.Dark=false;ApplyTheme();await Task.Delay(300);string lightLook=await look();state.Dark=wasDark;ApplyTheme();log.Add("theme: "+darkLook+" / "+lightLook);
    RegressionCheck("games open in Mica's dark theme and switch to light in place",delegate {Require(darkLook.Contains("\\\"dark\\\":true")&&lightLook.Contains("\\\"dark\\\":false")&&darkLook!=lightLook,darkLook+" / "+lightLook);});
    RegressionCheck("unknown game paths are not served",delegate {Require(Navigation.FromEngine("https://games.mica.invalid/nope")=="https://games.mica.invalid/nope"&&Navigation.EngineAddress("mica://snake")=="https://games.mica.invalid/snake"&&Navigation.Resolve("MICA://2048/")=="mica://2048","mapping");});
    var gshot=await CurrentView().CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot","{\"format\":\"png\"}");
    File.WriteAllBytes(Path.Combine(dir,"minesweeper.png"),Convert.FromBase64String((string)new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(gshot)["data"]));
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
