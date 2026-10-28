using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
namespace III85III.Vpn {
    internal static class LayoutTests {
        static int checks;
        static object Field(object owner,string name) { return owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner); }
        static void Check(bool value,string name) { checks++; if(!value) throw new Exception(name); }
        static void Arrange(MainForm form) { form.PerformLayout(); form.GetType().GetMethod("ArrangeResponsive",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null); Application.DoEvents(); }
        [STAThread] static int Main(string[] args) { try { return Run(args); } catch(Exception error) { Console.Error.WriteLine(error); return 1; } }
        static int Run(string[] args) {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using(var body=Theme.Font(10f)) { Check(body.FontFamily.Name=="Lato","bundled Lato body font must load"); Check(body.Size>=10.5f,"body font must be readable"); }
            using(var heading=Theme.Display(24f)) { Check(heading.FontFamily.Name.StartsWith("Lato"),"bundled heading font must load"); }
            string safe=SafeDiagnostics.Build(Json.Decode("{\"active_account\":{\"email\":\"private@example.invalid\",\"token\":\"never-export-this\"},\"config\":{\"password\":\"never-export-this\"},\"logs\":[\"private@example.invalid\"]}"));
            Check(!safe.Contains("private@example.invalid")&&!safe.Contains("never-export-this"),"safe report excludes identity, credentials and logs");
            using(var engine=new Engine(AppDomain.CurrentDomain.BaseDirectory,System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layout-data")))
            using(var form=new MainForm(engine,true)) {
                form.Show();
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
                            if(!control.Visible)continue;
                            Check(control.Left>=0 && control.Right<=panel.ClientSize.Width,"horizontal overflow: "+control.GetType().Name+" "+control.Text+" page "+page+" "+size);
                            Check(control.Bottom<=Math.Max(panel.ClientSize.Height,panel.AutoScrollMinSize.Height),"vertical overflow: "+control.Text+" page "+page+" "+size);
                        }
                        if(page==1 || page==2) {
                            var search=(Control)Field(form,page==1?"locationSearch":"splitSearch");
                            foreach(string name in page==1?new[]{"sortPingBtn","favoriteBtn","favoritesFilterBtn","pingBtn","applyLocationBtn"}:new[]{"splitBrowse","splitReload","splitSave"})
                                Check(!search.Bounds.IntersectsWith(((Control)Field(form,name)).Bounds),"toolbar overlap "+name);
                        }
                    }
                    Console.WriteLine("PASS six pages at "+size.Width+"x"+size.Height);
                }
                foreach(bool persian in new[]{false,true})foreach(bool light in new[]{false,true}){
                    form.GetType().GetField("desktopPersian",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,persian);
                    form.GetType().GetField("desktopLight",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,light);
                    form.GetType().GetMethod("ApplyDesktopAppearance",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
                    foreach(var size in new[]{new Size(680,520),new Size(1040,760)}){
                        form.ClientSize=size;
                        for(int index=0;index<6;index++){
                            form.GetType().GetMethod("SwitchPage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{index});Arrange(form);
                            var page=((Panel[])Field(form,"pages"))[index];
                            foreach(Control child in page.Controls)if(child.Visible)Check(child.Left>=0&&child.Right<=page.ClientSize.Width,"localized horizontal bounds "+child.Text+" "+size);
                        }
                    }
                }
                form.GetType().GetField("desktopPersian",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);
                form.GetType().GetField("desktopLight",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);
                form.GetType().GetMethod("ApplyDesktopAppearance",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
                var servers=(System.Collections.Generic.List<ServerChoice>)Field(form,"allLocations");
                servers.Add(new ServerChoice("Recommended", "") { PingMs=0 });
                servers.Add(new ServerChoice("United Kingdom", "gb") { PingMs=80 });
                servers.Add(new ServerChoice("Germany", "de") { PingMs=20 });
                servers.Add(new ServerChoice("Canada", "ca") { PingMs=20 });
                servers.Add(new ServerChoice("Failed", "xx") { PingMs=-1 });
                var filter=form.GetType().GetMethod("FilterLocations",BindingFlags.Instance|BindingFlags.NonPublic);
                filter.Invoke(form,null);
                var locations=(ListBox)Field(form,"locationList");
                Check(((ServerChoice)locations.Items[0]).Code=="ca" && ((ServerChoice)locations.Items[1]).Code=="de","lowest pings first with deterministic ties");
                Check(((ServerChoice)locations.Items[2]).Code=="gb","higher measured ping follows lower ping");
                Check(((ServerChoice)locations.Items[3]).PingMs<=0 && ((ServerChoice)locations.Items[4]).PingMs<=0,"unknown and failed pings stay below measured servers");
                locations.SelectedIndex=2;
                form.GetType().GetField("sortByPing",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);
                filter.Invoke(form,null);
                Check(((ServerChoice)locations.Items[0]).Code=="","disabling sort restores service order");
                Check(((ServerChoice)locations.SelectedItem).Code=="gb","sorting preserves selected server");
                form.GetType().GetField("sortByPing",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);
                ((LuxurySearchBox)Field(form,"locationSearch")).Input.Text="Germany";
                Check(locations.Items.Count==1 && ((ServerChoice)locations.Items[0]).Code=="de","search remains compatible with ping sort");
                ((LuxurySearchBox)Field(form,"locationSearch")).Input.Text="";
                servers.Clear();filter.Invoke(form,null);
                form.GetType().GetField("ready",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);
                form.GetType().GetField("lastStatus",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,Json.Decode("{\"state\":{\"phase\":\"off\"}}"));
                ((System.Threading.Tasks.Task)form.GetType().GetMethod("ConnectFromHome",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null)).GetAwaiter().GetResult();
                Check((int)Field(form,"activePageIndex")==3,"first-use action opens the account page without connecting");
                form.GetType().GetField("ready",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);
                if(args.Length>0) { form.ClientSize=new Size(1040,760); form.GetType().GetMethod("SwitchPage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{0});Arrange(form);Application.Run(form); }
            }
            Console.WriteLine("PASS "+checks+" layout checks"); return 0;
        }
    }
}
