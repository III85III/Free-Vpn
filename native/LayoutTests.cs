using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
namespace III85III.Vpn {
    internal static class LayoutTests {
        static int checks;
        static object Field(object owner,string name) { return owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner); }
        static void Check(bool value,string name) { checks++; if(!value) throw new Exception(name); }
        static int TextBoxes(Control root){int count=root is TextBox?1:0;foreach(Control child in root.Controls)count+=TextBoxes(child);return count;}
        static void Arrange(MainForm form) { form.PerformLayout(); form.GetType().GetMethod("ArrangeResponsive",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null); Application.DoEvents(); }
        [STAThread] static int Main(string[] args) {try{return Run(args);}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        static int Run(string[] args) {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Check(AccountSetup.ShouldShow(false,false,false),"fresh empty profile gets account setup instructions");
            Check(!AccountSetup.ShouldShow(true,false,false)&&!AccountSetup.ShouldShow(false,true,false)&&!AccountSetup.ShouldShow(false,false,true),"account setup skips existing accounts, acknowledged intros and required updates");
            Check(AccountSetup.Message.Contains("Mozilla password"),"setup identifies Mozilla credentials without changing sign-in");
            using(var intro=new AccountIntroForm()){Check(TextBoxes(intro)==0,"setup introduction does not collect credentials before sign-in");}
            using(var body=Theme.Font(10f)) { Check(body.FontFamily.Name=="Lato","bundled Lato body font must load"); Check(body.Size>=10.5f,"body font must be readable"); }
            using(var heading=Theme.Display(24f)) { Check(heading.FontFamily.Name.StartsWith("Lato"),"bundled heading font must load"); }
            using(var engine=new Engine(AppDomain.CurrentDomain.BaseDirectory,System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layout-data")))
            using(var form=new MainForm(engine,true)) {
                using(var login=new SignInForm(engine)){Check(TextBoxes(login)==2,"credentials window has only email and password");}
                using(var verify=new VerificationForm(engine,"test",Json.Decode("{\"step\":\"need_email_code\"}"))){
                    Check(TextBoxes(verify)==1,"verification window has a single code input");
                    var apply=verify.GetType().GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic);
                    apply.Invoke(verify,new object[]{Json.Decode("{\"step\":\"need_email_code\"}")});Check(((Label)Field(verify,"heading")).Text=="Check your email","email code prompt");
                    apply.Invoke(verify,new object[]{Json.Decode("{\"step\":\"need_totp\"}")});Check(((Label)Field(verify,"heading")).Text=="Enter your security code","authenticator prompt follows service response");
                    Check(!VerificationForm.NeedsCode("done"),"finished login cannot request another code");
                }
                form.Show();
                Check(!((Control)Field(form,"quickBtn")).Visible&&!((Control)Field(form,"antigravityBtn")).Visible,"home avoids duplicate connection and country shortcuts");
                var settingsPanel=((Panel[])Field(form,"pages"))[4];
                Check(settingsPanel.Controls["resourceButtons"].Controls.Count==2,"settings keeps updates and About without technical shortcuts");
                var shortcut=form.GetType().GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic);
                for(int page=0;page<6;page++){
                    object[] keyArgs={new Message(),Keys.Control|(Keys)((int)Keys.D1+page)};
                    Check((bool)shortcut.Invoke(form,keyArgs)&&(int)Field(form,"activePageIndex")==page,"keyboard navigation selects page "+page);
                }
                Check((bool)shortcut.Invoke(form,new object[]{new Message(),Keys.Control|Keys.F})&&(int)Field(form,"activePageIndex")==1,"find shortcut opens locations from other pages");
                var locationInput=((LuxurySearchBox)Field(form,"locationSearch")).Input;
                locationInput.Text="Germany";
                shortcut.Invoke(form,new object[]{new Message(),Keys.Control|Keys.F});
                Check(locationInput.Focused&&locationInput.SelectionLength==locationInput.TextLength,"find selects existing search text");
                shortcut.Invoke(form,new object[]{new Message(),Keys.Escape});Check(locationInput.TextLength==0,"escape clears location filter");
                shortcut.Invoke(form,new object[]{new Message(),Keys.Control|Keys.D3});
                var appInput=((LuxurySearchBox)Field(form,"splitSearch")).Input;
                appInput.Text="app";shortcut.Invoke(form,new object[]{new Message(),Keys.Control|Keys.F});
                Check((int)Field(form,"activePageIndex")==2&&appInput.Focused,"find keeps app search on the app rules page");
                shortcut.Invoke(form,new object[]{new Message(),Keys.Escape});Check(appInput.TextLength==0,"escape clears app filter");
                shortcut.Invoke(form,new object[]{new Message(),Keys.F1});Check((int)Field(form,"activePageIndex")==5,"F1 opens connection help");
                foreach(var size in new[]{new Size(680,520),new Size(800,600),new Size(1040,760),new Size(1280,900)}) {
                    form.ClientSize=size;
                    for(int page=0;page<6;page++) {
                        form.GetType().GetMethod("SwitchPage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{page}); Arrange(form);
                        var content=(Control)Field(form,"contentArea"); var sidebar=(Control)Field(form,"sidebar");var title=(Control)Field(form,"titleBar");
                        Check(!content.Bounds.IntersectsWith(sidebar.Bounds),"navigation overlaps content "+size);
                        Check(!content.Bounds.IntersectsWith(title.Bounds),"title overlaps content "+size);
                        var panel=((Panel[])Field(form,"pages"))[page];
                        if(page==0) {
                            Check(((Control)Field(form,"dialSubtext")).Bottom<=panel.Controls["connectionActions"].Top,"home description overlaps actions");
                            Check(panel.Controls["connectionActions"].Bottom<=((Control)Field(form,"selectedLocationCard")).Top,"actions overlap location card");
                            var cta=(Control)Field(form,"connectBtn");
                            Check(((Control)Field(form,"dial")).Bottom<=cta.Top,"dial overlaps main action");
                            Check(cta.Bottom<=panel.Controls["connectionActions"].Top,"main action overlaps secondary actions");
                            foreach(string name in new[]{"connectBtn","quickBtn","antigravityBtn","verifyBtn"}) {
                                var button=(Control)Field(form,name);
                                Check(TextRenderer.MeasureText(button.Text,button.Font).Width<=button.Width-12,"home action text clipped: "+name);
                            }
                        }
                        foreach(Control control in panel.Controls) {
                            Check(control.Left>=0 && control.Right<=panel.ClientSize.Width,"horizontal overflow: "+control.GetType().Name+" "+control.Text+" page "+page+" "+size);
                            Check(control.Bottom<=Math.Max(panel.ClientSize.Height,panel.AutoScrollMinSize.Height),"vertical overflow: "+control.Text+" page "+page+" "+size);
                        }
                        if(page==1 || page==2) {
                            var search=(Control)Field(form,page==1?"locationSearch":"splitSearch");
                            foreach(string name in page==1?new[]{"sortPingBtn","pingBtn","applyLocationBtn","antigravityFilterBtn"}:new[]{"splitBrowse","splitReload","splitSave"})
                                Check(!search.Bounds.IntersectsWith(((Control)Field(form,name)).Bounds),"toolbar overlap "+name);
                        }
                    }
                    Console.WriteLine("PASS six pages at "+size.Width+"x"+size.Height);
                }
                var servers=(System.Collections.Generic.List<ServerChoice>)Field(form,"allLocations");
                servers.Add(new ServerChoice("Recommended", "") { PingMs=0 });
                servers.Add(new ServerChoice("Lowest latency", "fastest") { PingMs=0 });
                servers.Add(new ServerChoice("Automatic", "REC") { PingMs=0 });
                servers.Add(new ServerChoice("United Kingdom", "gb") { PingMs=80 });
                servers.Add(new ServerChoice("Germany", "de") { PingMs=20 });
                servers.Add(new ServerChoice("Canada", "ca") { PingMs=20 });
                servers.Add(new ServerChoice("Failed", "xx") { PingMs=-1 });
                var filter=form.GetType().GetMethod("FilterLocations",BindingFlags.Instance|BindingFlags.NonPublic);
                filter.Invoke(form,null);
                var locations=(ListBox)Field(form,"locationList");
                Check(((ServerChoice)locations.Items[0]).Code=="" && ((ServerChoice)locations.Items[1]).Code=="fastest" && ((ServerChoice)locations.Items[2]).Code=="REC","automatic options stay pinned above measured countries");
                Check(((ServerChoice)locations.Items[3]).Code=="ca" && ((ServerChoice)locations.Items[4]).Code=="de","lowest country pings first with deterministic ties");
                Check(((ServerChoice)locations.Items[5]).Code=="gb","higher measured ping follows lower ping");
                Check(((ServerChoice)locations.Items[6]).PingMs<=0,"unknown and failed country pings stay below measured servers");
                locations.SelectedIndex=5;
                form.GetType().GetField("sortByPing",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);
                filter.Invoke(form,null);
                Check(((ServerChoice)locations.Items[0]).Code=="","disabling sort restores service order");
                Check(((ServerChoice)locations.SelectedItem).Code=="gb","sorting preserves selected server");
                form.GetType().GetField("sortByPing",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);
                ((LuxurySearchBox)Field(form,"locationSearch")).Input.Text="Germany";
                Check(locations.Items.Count==4 && ((ServerChoice)locations.Items[3]).Code=="de","search preserves pinned choices while filtering countries");
                ((LuxurySearchBox)Field(form,"locationSearch")).Input.Text="";
                servers.Clear();filter.Invoke(form,null);
                Check(!ConnectionState.HasAccounts(Json.Decode("{\"accounts\":{\"total\":0}}")),"first-use display recognizes a known empty account store");
                Check(ConnectionState.HasAccounts(Json.Decode("{\"accounts\":{\"total\":1}}")),"saved accounts do not require an active connection for the home display");
                if(args.Length>0) { form.ClientSize=new Size(1040,760); form.GetType().GetMethod("SwitchPage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{0});Arrange(form);Application.Run(form); }
            }
            Console.WriteLine("PASS "+checks+" layout checks"); return 0;
        }
    }
}
