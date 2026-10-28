using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace III85III.Vpn {
    internal sealed partial class MainForm {
        readonly LuxuryButton favoriteBtn=new LuxuryButton{Text="Favorite / unstar"};
        readonly LuxuryButton favoritesFilterBtn=new LuxuryButton{Text="Favorites only"};
        readonly LuxuryButton selectAccountBtn=new LuxuryButton{Text="Use account",Variant=ButtonVariant.Primary};
        readonly HashSet<string> favoriteCodes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> recentCodes=new List<string>();
        Panel desktopPreferences;ComboBox desktopLanguage,desktopTheme;CheckBox desktopReconnect;
        bool showFavoritesOnly,desktopUpdating,desktopPersian,desktopLight;
        static readonly Dictionary<string,Color> DarkColors=CaptureColors();
        static Dictionary<string,Color> CaptureColors(){var result=new Dictionary<string,Color>();foreach(var f in typeof(Theme).GetFields(BindingFlags.Public|BindingFlags.Static))if(f.FieldType==typeof(Color))result[f.Name]=(Color)f.GetValue(null);return result;}
        string Tr(string en,string fa){return desktopPersian?fa:en;}
        internal void ConfigurePreview(bool persian,bool light){desktopPersian=persian;desktopLight=light;ApplyDesktopAppearance();}
        static string LocationCode(string value){return String.IsNullOrWhiteSpace(value)?"REC":value.ToUpperInvariant();}
        static List<string> StringRows(object value){var result=new List<string>();var rows=value as IEnumerable;if(rows!=null&&!(value is string))foreach(var row in rows)result.Add(Convert.ToString(row));return result;}
        void InitializeDesktopFeatures(){
            pages[1].Controls.Add(favoriteBtn);pages[1].Controls.Add(favoritesFilterBtn);pages[3].Controls.Add(selectAccountBtn);antigravityFilterBtn.Visible=false;
            favoriteBtn.Click+=async delegate{await Run(async delegate{
                if(locationList.SelectedItem==null)return;string code=LocationCode(((ServerChoice)locationList.SelectedItem).Code);
                var updated=new HashSet<string>(favoriteCodes,StringComparer.OrdinalIgnoreCase);if(!updated.Remove(code))updated.Add(code);
                await engine.Api("PUT","/preferences",new{favorites=new List<string>(updated)});favoriteCodes.Clear();foreach(string id in updated)favoriteCodes.Add(id);FilterLocations();
            });};
            favoritesFilterBtn.Click+=delegate{showFavoritesOnly=!showFavoritesOnly;favoritesFilterBtn.Variant=showFavoritesOnly?ButtonVariant.Primary:ButtonVariant.Secondary;FilterLocations();favoritesFilterBtn.Invalidate();};
            selectAccountBtn.Click+=async delegate{await Run(SelectDesktopAccount);};accountsList.DoubleClick+=async delegate{await Run(SelectDesktopAccount);};
            modeCards[0].Title="Selected apps only · Recommended";modeCards[0].Description="Only checked apps use VPN. All other apps connect directly.";
            modeCards[1].Visible=modeCards[2].Visible=false;applyModeBtn.Visible=false;pendingModeIndex=0;
            desktopPreferences=new Panel{Name="desktopPreferences",BackColor=Theme.Bg};pages[4].Controls.Add(desktopPreferences);
            desktopLanguage=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(180,6),Size=new Size(220,32),Font=Theme.Font(10)};
            desktopLanguage.Items.AddRange(new object[]{"English","فارسی"});desktopLanguage.SelectedIndex=0;
            desktopTheme=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(180,50),Size=new Size(220,32),Font=Theme.Font(10)};
            desktopTheme.Items.AddRange(new object[]{"Dark","Light"});desktopTheme.SelectedIndex=0;
            desktopReconnect=new CheckBox{Text="Reconnect automatically",Location=new Point(0,98),Size=new Size(450,42),Font=Theme.Font(10),ForeColor=Theme.TextPrimary,Checked=true};
            desktopPreferences.Controls.Add(new Label{Text="Language",AutoSize=false,Location=new Point(0,8),Size=new Size(170,28),Font=Theme.Font(10),ForeColor=Theme.TextPrimary});
            desktopPreferences.Controls.Add(new Label{Text="Appearance",AutoSize=false,Location=new Point(0,52),Size=new Size(170,28),Font=Theme.Font(10),ForeColor=Theme.TextPrimary});
            desktopPreferences.Controls.Add(desktopLanguage);desktopPreferences.Controls.Add(desktopTheme);desktopPreferences.Controls.Add(desktopReconnect);
            desktopPreferences.Resize+=delegate{desktopLanguage.Width=desktopTheme.Width=Math.Max(120,Math.Min(300,desktopPreferences.Width-184));desktopReconnect.Width=desktopPreferences.Width;};
            desktopLanguage.SelectedIndexChanged+=async delegate{if(!desktopUpdating)await Run(async delegate{await engine.Api("PUT","/preferences",new{language=desktopLanguage.SelectedIndex==1?"fa":"en"});});};
            desktopTheme.SelectedIndexChanged+=async delegate{if(!desktopUpdating)await Run(async delegate{await engine.Api("PUT","/preferences",new{theme=desktopTheme.SelectedIndex==1?"light":"dark"});});};
            desktopReconnect.CheckedChanged+=async delegate{if(!desktopUpdating)await Run(async delegate{await engine.Api("PUT","/preferences",new{autoReconnect=desktopReconnect.Checked});});};
            minimizeToggle.CheckedChanged+=async delegate{if(!desktopUpdating)await Run(async delegate{await engine.Api("PUT","/preferences",new{closeToTray=minimizeToggle.Checked});});};
            pages[4].Controls["sourceNotice"].Text="III85III VPN 2.1 Preview · Independent local client\nNode.js, sing-box and fonts retain their licenses.";
            var resources=(FlowLayoutPanel)pages[4].Controls["resourceButtons"];if(resources.Controls.Count>2)resources.Controls[2].Visible=false;
            KeyPreview=true;KeyDown+=async delegate(object sender,KeyEventArgs e){if(e.Control&&e.KeyCode==Keys.D){e.SuppressKeyPress=true;await Run(async delegate{await engine.Api("POST","/api/power",new{on=false});});}else if(e.Control&&e.KeyCode==Keys.L){e.SuppressKeyPress=true;SwitchPage(1);locationSearch.Input.Focus();}else if(e.Control&&e.KeyCode==Keys.K){e.SuppressKeyPress=true;SwitchPage(2);splitSearch.Input.Focus();}};
            foreach(Control c in new Control[]{connectBtn,quickBtn,favoriteBtn,favoritesFilterBtn,selectAccountBtn,pingBtn,splitSave})c.AccessibleName=c.Text;
        }
        async Task SelectDesktopAccount(){
            if(accountsList.SelectedItems.Count==0)return;if(Json.Str(Json.Child(lastStatus,"state"),"phase")!="off")await engine.Api("POST","/api/power",new{on=false});
            await engine.Api("POST","/api/accounts/"+Convert.ToString(accountsList.SelectedItems[0].Tag)+"/select");await RefreshAccounts();
        }
        void SynchronizeDesktopSettings(){
            var value=Json.Child(lastStatus,"preferences");if(value.Count==0)return;bool persian=Json.Str(value,"language")=="fa",light=Json.Str(value,"theme")=="light";
            desktopUpdating=true;try{desktopLanguage.SelectedIndex=persian?1:0;desktopTheme.SelectedIndex=light?1:0;desktopReconnect.Checked=Json.Bool(value,"autoReconnect");minimizeToggle.Checked=Json.Bool(value,"closeToTray");}finally{desktopUpdating=false;}
            favoriteCodes.Clear();foreach(string id in StringRows(Json.Get(value,"favorites")))favoriteCodes.Add(id);recentCodes.Clear();recentCodes.AddRange(StringRows(Json.Get(value,"recent")));
            if(persian!=desktopPersian||light!=desktopLight){desktopPersian=persian;desktopLight=light;ApplyDesktopAppearance();}
            var usage=Json.Child(Json.Child(lastStatus,"active_account"),"usage");DateTimeOffset reset;
            if(DateTimeOffset.TryParse(Json.Str(usage,"reset_at"),out reset))noticeLabel.Text=Tr("Quota resets: ","تمدید سهمیه: ")+reset.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
            if(recentCodes.Count>0)selectedLocationCard.Subtitle=Tr("Last used: ","آخرین اتصال: ")+DesktopLocationName(recentCodes[0]);
        }
        void ApplyDesktopAppearance(){
            var old=CaptureColors();Theme.Persian=desktopPersian;foreach(var pair in DarkColors)typeof(Theme).GetField(pair.Key).SetValue(null,pair.Value);
            if(desktopLight){Theme.Bg=Color.FromArgb(247,244,237);Theme.Sidebar=Color.FromArgb(239,236,228);Theme.Surface=Color.White;Theme.SurfaceHover=Color.FromArgb(240,237,229);Theme.SurfaceActive=Color.FromArgb(232,227,215);Theme.TextPrimary=Color.FromArgb(30,39,51);Theme.TextSecondary=Color.FromArgb(81,98,109);Theme.TextSubtle=Color.FromArgb(94,108,117);Theme.Border=Color.FromArgb(215,218,216);Theme.BorderHover=Color.FromArgb(157,165,166);}
            ApplyControlAppearance(this,old,CaptureColors());RightToLeft=desktopPersian?RightToLeft.Yes:RightToLeft.No;RightToLeftLayout=desktopPersian;TranslateDesktop();ArrangeResponsive();Invalidate(true);
        }
        void ApplyControlAppearance(Control c,Dictionary<string,Color> old,Dictionary<string,Color> next){
            foreach(var pair in old)if(c.BackColor.ToArgb()==pair.Value.ToArgb()){c.BackColor=next[pair.Key];break;}
            foreach(var pair in old)if(c.ForeColor.ToArgb()==pair.Value.ToArgb()){c.ForeColor=next[pair.Key];break;}
            c.RightToLeft=desktopPersian&&!(c is TextBox)?RightToLeft.Yes:RightToLeft.No;
            if(!(c is TextBox))c.Font=Theme.Font(Math.Max(8.75f,c.Font.Size-.75f),c.Font.Style);
            foreach(Control child in c.Controls)ApplyControlAppearance(child,old,next);
        }
        void TranslateDesktop(){TranslateControl(this);foreach(var nav in navButtons)nav.Title=DesktopWords.Translate(nav.Title,desktopPersian);statusPill.StatusText=DesktopWords.Translate(statusPill.StatusText,desktopPersian);}
        void TranslateControl(Control c){if(!(c is TextBox)&&!(c is ComboBox))c.Text=DesktopWords.Translate(c.Text,desktopPersian);var list=c as ListView;if(list!=null)foreach(ColumnHeader column in list.Columns)column.Text=DesktopWords.Translate(column.Text,desktopPersian);var card=c as LuxuryRadioCard;if(card!=null){card.Title=DesktopWords.Translate(card.Title,desktopPersian);card.Description=DesktopWords.Translate(card.Description,desktopPersian);}foreach(Control child in c.Controls)TranslateControl(child);}
        string DesktopLocationName(string code){foreach(var item in allLocations)if(LocationCode(item.Code)==code)return item.Name;return code=="REC"?Tr("Recommended","پیشنهادی"):code;}
        void RebuildDesktopTray(ContextMenuStrip menu){
            menu.Items.Clear();menu.Items.Add(Tr("Open III85III VPN","باز کردن III85III VPN"),null,delegate{Show();WindowState=FormWindowState.Normal;Activate();});bool off=Json.Str(Json.Child(lastStatus,"state"),"phase")=="off";
            var c=menu.Items.Add(Tr("Connect","اتصال"),null,async delegate{await ConnectFromHome();});c.Enabled=ready&&!busy&&off;
            var d=menu.Items.Add(Tr("Disconnect","قطع اتصال"),null,async delegate{await Run(async delegate{await engine.Api("POST","/api/power",new{on=false});});});d.Enabled=ready&&!busy&&!off;
            foreach(var pair in new[]{new KeyValuePair<string,IEnumerable<string>>(Tr("Favorites","محبوب‌ها"),favoriteCodes),new KeyValuePair<string,IEnumerable<string>>(Tr("Recently used","آخرین اتصال‌ها"),recentCodes)}){
                var parent=new ToolStripMenuItem(pair.Key);menu.Items.Add(parent);int count=0;foreach(string value in pair.Value){string code=value;var item=parent.DropDownItems.Add(DesktopLocationName(code),null,async delegate{await Run(async delegate{await engine.Api("POST","/api/location",new{country=code=="REC"?"":code.ToLowerInvariant()});await TogglePower();});});item.Enabled=ready&&!busy&&off;count++;}parent.Enabled=count>0&&off;
            }
            menu.Items.Add(new ToolStripSeparator());menu.Items.Add(Tr("Quit","خروج کامل"),null,delegate{allowClose=true;Close();});
        }
    }
    internal static class DesktopWords {
        static readonly Dictionary<string,string> Words=new Dictionary<string,string>{
            {"Home","خانه"},{"Locations","سرورها"},{"App rules","برنامه‌ها"},{"Account","حساب کاربری"},{"Settings","تنظیمات"},{"Help","راهنما"},
            {"Connect","اتصال"},{"Disconnect","قطع اتصال"},{"Get started","شروع"},{"Quick connect","اتصال سریع"},{"Check connection","بررسی اتصال"},
            {"Let's get you connected.","شروع کنیم به وصل شدن."},{"Ready when you are.","هر وقت آماده‌ای، وصل شو."},{"You're connected.","متصل شدی."},{"Getting you connected…","در حال اتصال…"},
            {"Add your Mozilla account to get started.","برای شروع، حساب موزیلا را اضافه کن."},{"Choose a location. Connect whenever you're ready.","سرور را انتخاب کن و هر وقت آماده‌ای وصل شو."},
            {"Not connected","متصل نیست"},{"Connected","متصل است"},{"Connecting","در حال اتصال"},{"Needs attention","نیاز به بررسی"},
            {"Pick your location","سرورت را انتخاب کن"},{"Your apps, your choice","برنامه‌ها، به انتخاب تو"},{"Your account","حساب تو"},{"Make it yours","مطابق سلیقهٔ تو"},{"A little connection help","راهنمای اتصال"},
            {"Choose somewhere to connect from. You can change it whenever you like.","سروری را برای اتصال انتخاب کن. هر زمان بخواهی قابل تغییر است."},
            {"Tick only the apps that should use VPN. All other apps stay direct.","فقط برنامه‌هایی را تیک بزن که باید با VPN وصل شوند؛ بقیه مستقیم‌اند."},
            {"Sign in with Mozilla, then keep an eye on your monthly balance here.","با حساب موزیلا وارد شو و سهمیهٔ ماهانه را اینجا ببین."},
            {"Favorite / unstar","تغییر وضعیت محبوب"},{"Favorites only","فقط محبوب‌ها"},{"Ping: low first","پینگ: کمتر اول"},{"Sort by ping","مرتب‌سازی پینگ"},{"Measure Latency","اندازه‌گیری پینگ"},{"Connect to Location","انتخاب این سرور"},
            {"Save choices","ذخیره انتخاب‌ها"},{"Refresh apps","به‌روزرسانی فهرست"},{"Choose an app","افزودن برنامه"},{"Add an account","افزودن حساب"},{"Use account","انتخاب حساب"},{"Check balance","بررسی سهمیه"},{"Remove Account","حذف حساب"},
            {"Hide Email","پنهان کردن ایمیل"},{"Show Email","نمایش ایمیل"},{"Hide","پنهان"},{"Show","نمایش"},
            {"Application","برنامه"},{"Location","مکان"},{"Routing","مسیر اتصال"},{"Allowance Remaining","سهمیهٔ باقی‌مانده"},{"Reset Date","تاریخ تمدید"},{"Executable path","مسیر فایل اجرایی"},{"Email","ایمیل"},{"Status","وضعیت"},{"Remaining","باقی‌مانده"},{"Reset","تمدید"},
            {"Connection mode","حالت اتصال"},{"Selected apps only · Recommended","فقط برنامه‌های انتخابی · پیشنهادی"},{"Only checked apps use VPN. All other apps connect directly.","فقط برنامه‌های تیک‌خورده با VPN وصل می‌شوند؛ بقیه مستقیم‌اند."},
            {"Language","زبان"},{"Appearance","ظاهر"},{"Reconnect automatically","اتصال مجدد خودکار"},{"Window behavior","رفتار پنجره"},{"Keep running in notification tray when window is closed","با بستن پنجره کنار ساعت فعال بماند"},
            {"Resources","منابع"},{"Open Data Folder","پوشهٔ داده‌ها"},{"User Guide","راهنمای برنامه"},{"Test Connection","بررسی اتصال"},{"Copy Logs","کپی گزارش"},{"Copy safe report","کپی گزارش امن"},
            {"No account active","حسابی فعال نیست"},{"Add an account to see your balance","برای دیدن سهمیه، حساب اضافه کن"},{"At home on your computer","محلی روی کامپیوتر تو"}
        };
        public static string Translate(string text,bool fa){if(text==null)return "";string result;if(fa&&Words.TryGetValue(text,out result))return result;if(!fa)foreach(var pair in Words)if(pair.Value==text)return pair.Key;return text;}
    }
    internal static class SafeDiagnostics {
        public static string Build(Dictionary<string,object> status){
            return Json.Encode(new{application="III85III VPN",version=Json.Str(status,"version"),independent=Json.Bool(status,"independent"),phase=Json.Str(Json.Child(status,"state"),"phase"),adapter=Json.Str(Json.Child(status,"tun"),"state"),verified=Json.Bool(Json.Child(status,"last_test"),"ok"),recovery_attempt=(int)Json.Num(Json.Child(status,"recovery"),"attempt")});
        }
    }
}
