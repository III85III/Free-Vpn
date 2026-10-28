using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("III85III VPN")]
[assembly: System.Reflection.AssemblyProduct("III85III VPN")]
[assembly: System.Reflection.AssemblyDescription("Independent III85III selected-application VPN client")]
[assembly: System.Reflection.AssemblyCompany("III85III")]
[assembly: System.Reflection.AssemblyVersion("2.1.0.3")]
[assembly: System.Reflection.AssemblyFileVersion("2.1.0.3")]

namespace III85III.Vpn {
    internal sealed class AppBypass {
        public string Name, Path; public bool UdpOnly;
        public AppBypass(string name, string path, bool udpOnly = false) { Name = name; Path = path; UdpOnly = udpOnly; }
    }

    internal static class SplitRules {
        public static Dictionary<string, AppBypass> Read(Dictionary<string, object> config) {
            var result = new Dictionary<string, AppBypass>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Json.Rows(Json.Child(config, "tun"), "programs")) {
                string name = CleanName(Json.Str(row, "name"));
                if (name != "") result[name] = new AppBypass(name, Json.Str(row, "path"), Json.Bool(row, "udp_only"));
            }
            return result;
        }
        public static string CleanName(string value) {
            value = (value ?? "").Trim();
            int slash = value.LastIndexOfAny(new[] { '\\', '/' });
            return slash < 0 ? value : value.Substring(slash + 1);
        }
        public static void Apply(Dictionary<string, object> config, IEnumerable<AppBypass> rules) {
            var selected = new List<object>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules) {
                string name = CleanName(rule.Name);
                if (name == "" || !seen.Add(name)) continue;
                selected.Add(new { name = name, path = rule.Path ?? "", udp_only = rule.UdpOnly });
            }
            if (selected.Count > 100) throw new IOException("Select at most 100 applications.");
            var tun = Json.Child(config, "tun");
            tun["programs"] = selected;
            config["tun"] = tun;
        }
    }

    internal sealed class WarmupPolicy {
        string connection = ""; DateTime next = DateTime.MinValue; int delay = 5;
        public bool ShouldTest(Dictionary<string, object> status, DateTime now) {
            string key = Json.Str(Json.Child(status, "proxy"), "started_at") + "|" + Json.Str(status, "network_changed_at");
            if (key != connection) { connection = key; next = DateTime.MinValue; delay = 5; }
            if (Json.Str(Json.Child(status, "state"), "phase") != "running" || Json.Bool(status, "no_network") || Json.Bool(status, "auto_connecting") || ConnectionState.Verified(status)) return false;
            if (now < next) return false;
            next = now.AddSeconds(delay); delay = Math.Min(60, delay * 2); return true;
        }
        public void Finished(DateTime now) { next = now.AddSeconds(Math.Max(5, delay / 2)); }
    }

    internal static class ConnectionState {
        public static string QuickCountry(string chosen) { return String.Equals(chosen, "fastest", StringComparison.OrdinalIgnoreCase) ? "" : chosen ?? ""; }
        public static bool Verified(Dictionary<string, object> value) {
            var state = Json.Child(value, "state"); var proxy = Json.Child(value, "proxy"); var test = Json.Child(value, "last_test"); var tun = Json.Child(value, "tun");
            if (Json.Str(state, "phase") != "running" || Json.Str(proxy, "wake_error") != "" || Json.Str(value, "connect_failure") != "" || Json.Get(value, "system_proxy_taken") != null) return false;
            if (Json.Str(value, "system_proxy") == "tun" && Json.Str(tun, "state") != "on") return false;
            DateTimeOffset at, since, changed;
            if (!Json.Bool(test, "ok") || !DateTimeOffset.TryParse(Json.Str(test, "at"), out at)) return false;
            if (DateTimeOffset.TryParse(Json.Str(state, "since"), out since) && at < since) return false;
            if (DateTimeOffset.TryParse(Json.Str(value, "network_changed_at"), out changed) && at < changed) return false;
            return true;
        }
    }

    internal static class Json {
        public static string Encode(object value) { return new JavaScriptSerializer().Serialize(value); }
        public static Dictionary<string, object> Decode(string text) { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text); }
        public static Dictionary<string, object> Obj(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object Get(Dictionary<string, object> value, string key) { object found; return value.TryGetValue(key, out found) ? found : null; }
        public static Dictionary<string, object> Child(Dictionary<string, object> value, string key) { return Obj(Get(value, key)); }
        public static string Str(Dictionary<string, object> value, string key) { return Convert.ToString(Get(value, key)) ?? ""; }
        public static bool Bool(Dictionary<string, object> value, string key) { return Get(value, key) is bool && (bool)value[key]; }
        public static double Num(Dictionary<string, object> value, string key) { double n; return Double.TryParse(Str(value, key), out n) ? n : 0; }
        public static IEnumerable<Dictionary<string, object>> Rows(Dictionary<string, object> value, string key) {
            var rows = Get(value, key) as IEnumerable;
            if (rows != null) foreach (var row in rows) { var d = row as Dictionary<string, object>; if (d != null) yield return d; }
        }
    }

    internal sealed class Engine : IDisposable {
        public readonly string DataDir, Root;
        public string Endpoint { get; private set; }
        public string Token { get; private set; }
        public string LastError = "";
        Process process; readonly HttpClient http;
        public Engine(string root,string dataDir) {
            Root=root;DataDir=dataDir;
            http=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false});http.Timeout=TimeSpan.FromSeconds(160);
        }
        internal Engine(string root,string dataDir,string endpoint,string token):this(root,dataDir){Endpoint=endpoint;Token=token;}
        public static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        public static void PrivateDirectory(string path){
            Directory.CreateDirectory(path);var sid=WindowsIdentity.GetCurrent().User;var security=new DirectorySecurity();
            security.SetAccessRuleProtection(true,false);
            security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(security);
        }
        public void VerifyFiles(){
            var manifest=Json.Decode(File.ReadAllText(Path.Combine(Root,"component-hashes.json")));
            foreach(string relative in new[]{"runtime\\node.exe","runtime\\sing-box.exe","III85III Tools.exe","III85III Route Host.exe"}){
                string file=Path.Combine(Root,relative);
                if(!File.Exists(file))throw new IOException("Missing independent component: "+relative);
                using(var hash=SHA256.Create())using(var stream=File.OpenRead(file)){
                    string actual=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
                    if(!String.Equals(actual,Json.Str(manifest,relative),StringComparison.OrdinalIgnoreCase))throw new IOException("Component integrity check failed: "+relative);
                }
            }
        }
        public async Task Start(){
            VerifyFiles();PrivateDirectory(DataDir);
            process=new Process{StartInfo=new ProcessStartInfo(Path.Combine(Root,"runtime","node.exe"),"\""+Path.Combine(Root,"src","control.cjs")+"\" --data-dir \""+DataDir+"\""){
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Root}};
            process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs args){if(!String.IsNullOrWhiteSpace(args.Data))LastError="The independent engine reported a local startup error.";};
            process.Start();process.BeginErrorReadLine();var hello=process.StandardOutput.ReadLineAsync();
            if(await Task.WhenAny(hello,Task.Delay(12000))!=hello)throw new IOException("Independent engine startup timed out.");
            string line=await hello;if(String.IsNullOrWhiteSpace(line))throw new IOException("Independent engine startup failed.");
            var handshake=Json.Decode(line);int port=(int)Json.Num(handshake,"port");Token=Json.Str(handshake,"token");
            if(port<1||port>65535||Token.Length!=64)throw new IOException("Invalid local engine handshake.");
            Endpoint="http://127.0.0.1:"+port;
        }
        public async Task<Dictionary<string,object>> Api(string method,string path,object body=null,int timeoutMs=150000){
            if(Endpoint==null||String.IsNullOrEmpty(Token))throw new IOException("Local engine is not ready.");
            Dictionary<string,object> result;
            using(var request=new HttpRequestMessage(new HttpMethod(method),Endpoint+path)){
                request.Headers.Add("X-III85III-Token",Token);request.Headers.Add("X-Local-Token",Token);
                if(method!="GET")request.Content=new StringContent(Json.Encode(body??new{}),Encoding.UTF8,"application/json");
                using(var cancel=new CancellationTokenSource(timeoutMs))using(var reply=await http.SendAsync(request,cancel.Token)){
                    string text=await reply.Content.ReadAsStringAsync();result=String.IsNullOrWhiteSpace(text)?new Dictionary<string,object>():Json.Decode(text);
                    if(!reply.IsSuccessStatusCode)throw new IOException(Json.Str(result,"error")+" (HTTP "+(int)reply.StatusCode+")");
                }
            }
            bool denied=false;
            if(path=="/api/power"&&Json.Bool(result,"elevation_required")){
                try{Process.Start(new ProcessStartInfo(Path.Combine(Root,"III85III Route Host.exe"),"\""+Json.Str(result,"config")+"\" "+Json.Str(result,"parent")){UseShellExecute=true,Verb="runas"});}
                catch{denied=true;}
                if(denied){try{await Api("POST","/api/power",new{on=false});}catch{}throw new IOException("Windows routing approval was cancelled.");}
            }
            return result;
        }
        public Task InstallTun(){return Task.FromResult(0);}
        public async Task Stop(){
            if(process==null||process.HasExited)return;
            try{await Api("POST","/api/app/exit",null,3500);}catch{}
            try{process.StandardInput.Close();}catch{}
            await Task.Run(delegate{process.WaitForExit(12000);});
            if(!process.HasExited)throw new IOException("The independent engine is still shutting down.");
        }
        public void Dispose(){if(process!=null){try{process.StandardInput.Close();}catch{}process.Dispose();}http.Dispose();}
    }

    internal static class NativeMethods {
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] public static extern IntPtr SetCue(IntPtr hWnd, int msg, IntPtr wParam, string text);
    }

    internal static class Theme {
        // Luxury minimalist warm near-black & graphite palette
        public static Color Bg = Color.FromArgb(26, 33, 43);
        public static Color Sidebar = Color.FromArgb(30, 38, 49);
        public static Color Surface = Color.FromArgb(36, 46, 58);
        public static Color SurfaceHover = Color.FromArgb(45, 58, 70);
        public static Color SurfaceActive = Color.FromArgb(53, 66, 78);
        public static Color Border = Color.FromArgb(57, 69, 81);
        public static Color BorderHover = Color.FromArgb(92, 109, 122);
        public static Color TextPrimary = Color.FromArgb(247, 241, 231);
        public static Color TextSecondary = Color.FromArgb(193, 204, 210);
        public static Color TextSubtle = Color.FromArgb(165, 182, 191);

        // Refined muted steel blue accent (not neon)
        public static Color Accent = Color.FromArgb(226, 191, 142);
        public static Color AccentHover = Color.FromArgb(241, 210, 165);
        public static Color AccentActive = Color.FromArgb(202, 166, 117);
        public static Color AccentMuted = Color.FromArgb(59, 52, 44);

        // True state colors
        public static Color Success = Color.FromArgb(101, 180, 163);
        public static Color SuccessMuted = Color.FromArgb(24, 52, 42);
        public static Color Warning = Color.FromArgb(224, 164, 76); // Warm amber
        public static Color WarningMuted = Color.FromArgb(52, 42, 24);
        public static Color Danger = Color.FromArgb(224, 88, 88);   // Soft coral
        public static Color DangerHover = Color.FromArgb(240, 108, 108);

        static readonly PrivateFontCollection BundledFonts = new PrivateFontCollection();
        static FontFamily bodyFamily, displayFamily, persianFamily;
        public static bool Persian;
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int AddFontResourceEx(string path, uint flags, IntPtr reserved);
        static Theme() {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "fonts");
            if (!Directory.Exists(folder)) folder = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "native", "assets", "fonts"));
            if (Directory.Exists(folder)) foreach (string path in Directory.GetFiles(folder, "*.ttf")) {
                try { BundledFonts.AddFontFile(path); AddFontResourceEx(path, 0x10, IntPtr.Zero); } catch { }
            }
            foreach (var family in BundledFonts.Families) {
                if (family.Name == "Lato") bodyFamily = family;
                if (family.Name == "Lato Black") displayFamily = family;
                if (family.Name == "Vazirmatn") persianFamily = family;
            }
        }
        public static Font Font(float size, FontStyle style = FontStyle.Regular) {
            float readable = Math.Max(9.5f, size + .75f);
            if (Persian && persianFamily != null) return new Font(persianFamily, readable, style);
            if (bodyFamily != null) return new Font(bodyFamily, readable, style);
            return new Font("Candara", readable, style);
        }
        public static Font Display(float size) {
            return !Persian && displayFamily != null ? new Font(displayFamily, size, FontStyle.Regular) : Font(size, FontStyle.Bold);
        }
        public static Font Mono(float size, FontStyle style = FontStyle.Regular) {
            try { return new Font("Consolas", size, style); } catch { return new Font("Courier New", size, style); }
        }

        public static GraphicsPath RoundedRect(Rectangle r, int radius) {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DrawRounded(Graphics g, Pen pen, Rectangle r, int radius) {
            using (var path = RoundedRect(r, radius)) g.DrawPath(pen, path);
        }

        public static void FillRounded(Graphics g, Brush brush, Rectangle r, int radius) {
            using (var path = RoundedRect(r, radius)) g.FillPath(brush, path);
        }

        public static string Bytes(double value) {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (value >= 1000 && unit < 4) { value /= 1000; unit++; }
            return value.ToString(unit == 0 ? "0" : "0.0") + " " + units[unit];
        }
    }

    internal enum ButtonVariant { Primary, Secondary, Ghost, Danger, Success }

    internal sealed class LuxuryButton : Button {
        public ButtonVariant Variant = ButtonVariant.Secondary;
        public int CornerRadius = 12;
        private bool isHovered, isPressed;

        public LuxuryButton() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = Theme.Font(9.5f, FontStyle.Regular);
            Height = 36;
            Padding = new Padding(14, 6, 14, 6);
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { if (mevent.Button == MouseButtons.Left) isPressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { isPressed = false; Invalidate(); base.OnMouseUp(mevent); }

        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent == null ? Theme.Bg : Parent.BackColor); }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.Clear(Parent == null ? Theme.Bg : Parent.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg, border, fg;

            if (!Enabled) {
                bg = Theme.Surface; border = Theme.Border; fg = Theme.TextSubtle;
            } else {
                switch (Variant) {
                    case ButtonVariant.Primary:
                        bg = isPressed ? Theme.AccentActive : isHovered ? Theme.AccentHover : Theme.Accent;
                        border = bg;
                        fg = Theme.Bg;
                        break;
                    case ButtonVariant.Success:
                        bg = isPressed ? Color.FromArgb(36, 156, 114) : isHovered ? Color.FromArgb(56, 196, 144) : Theme.Success;
                        border = bg;
                        fg = Color.White;
                        break;
                    case ButtonVariant.Danger:
                        bg = isPressed ? Color.FromArgb(180, 60, 60) : isHovered ? Theme.DangerHover : Theme.Surface;
                        border = isHovered ? Theme.DangerHover : Theme.Border;
                        fg = isHovered ? Color.White : Theme.Danger;
                        break;
                    case ButtonVariant.Ghost:
                        bg = isPressed ? Theme.SurfaceActive : isHovered ? Theme.SurfaceHover : Color.Transparent;
                        border = Color.Transparent;
                        fg = isHovered ? Theme.TextPrimary : Theme.TextSecondary;
                        break;
                    case ButtonVariant.Secondary:
                    default:
                        bg = isPressed ? Theme.SurfaceActive : isHovered ? Theme.SurfaceHover : Theme.Surface;
                        border = isHovered ? Theme.BorderHover : Theme.Border;
                        fg = isHovered ? Theme.TextPrimary : Theme.TextSecondary;
                        break;
                }
            }

            using (var bBrush = new SolidBrush(bg)) Theme.FillRounded(g, bBrush, r, CornerRadius);
            if (border != Color.Transparent) {
                using (var pen = new Pen(border, 1f)) Theme.DrawRounded(g, pen, r, CornerRadius);
            }

            TextRenderer.DrawText(g, Text, Font, r, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal enum DialState { Disconnected, Connecting, Connected, Error }

    internal sealed class LuxuryDial : Control {
        public DialState State = DialState.Disconnected;
        private bool isHovered, isPressed;

        public LuxuryDial() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(116, 116);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) isPressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { isPressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int cx = Width / 2;
            int cy = Height / 2;
            int radius = Math.Min(Width, Height) / 2 - 4;
            var r = new Rectangle(cx - radius, cy - radius, radius * 2, radius * 2);

            Color surfaceColor = isPressed ? Theme.AccentActive : isHovered ? Theme.AccentHover : Theme.Accent;
            Color ringColor = Theme.Accent;
            Color glyphColor = Theme.Bg;

            switch (State) {
                case DialState.Connected:
                    surfaceColor = isPressed ? Color.FromArgb(20, 36, 30) : isHovered ? Color.FromArgb(28, 48, 40) : Color.FromArgb(22, 40, 34);
                    ringColor = Theme.Success;
                    glyphColor = Theme.Success;
                    break;
                case DialState.Connecting:
                    surfaceColor = isPressed ? Color.FromArgb(36, 30, 20) : isHovered ? Color.FromArgb(46, 38, 24) : Color.FromArgb(38, 32, 22);
                    ringColor = Theme.Warning;
                    glyphColor = Theme.Warning;
                    break;
                case DialState.Error:
                    surfaceColor = isPressed ? Color.FromArgb(40, 24, 24) : isHovered ? Color.FromArgb(48, 28, 28) : Color.FromArgb(36, 22, 22);
                    ringColor = Theme.Danger;
                    glyphColor = Theme.Danger;
                    break;
                case DialState.Disconnected:
                default:
                    if (isHovered) ringColor = Theme.AccentHover;
                    break;
            }

            using (var brush = new SolidBrush(surfaceColor)) g.FillEllipse(brush, r);
            using (var pen = new Pen(ringColor, State == DialState.Connected ? 2f : 1f)) g.DrawEllipse(pen, r);

            // Clean geometric power glyph
            int arcRadius = 23;
            using (var pen = new Pen(glyphColor, 3.2f)) {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, cx - arcRadius, cy - arcRadius + 2, arcRadius * 2, arcRadius * 2, -45, 270);
                g.DrawLine(pen, cx, cy - arcRadius - 4, cx, cy - 1);
            }
        }
    }

    internal sealed class StatusPill : Control {
        public DialState State = DialState.Disconnected;
        public string StatusText = "Not connected";

        public StatusPill() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(210, 34);
            Font = Theme.Font(9f, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var brush = new SolidBrush(Theme.Surface)) Theme.FillRounded(g, brush, r, Height / 2);
            using (var pen = new Pen(Theme.Border, 1f)) Theme.DrawRounded(g, pen, r, Height / 2);

            Color dotColor = Theme.TextSubtle;
            switch (State) {
                case DialState.Connected: dotColor = Theme.Success; break;
                case DialState.Connecting: dotColor = Theme.Warning; break;
                case DialState.Error: dotColor = Theme.Danger; break;
            }

            // Draw status indicator circle
            int dotY = Height / 2 - 4;
            using (var brush = new SolidBrush(dotColor)) g.FillEllipse(brush, 16, dotY, 8, 8);

            // Draw status label
            using (var brush = new SolidBrush(Theme.TextPrimary)) {
                var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                g.DrawString(StatusText, Font, brush, new Rectangle(32, 0, Width - 36, Height), sf);
            }
        }
    }

    internal sealed class LocationCard : Control {
        public string LocationName = "Recommended";
        public string Subtitle = "We'll choose a location for you";
        public string Latency = "— ms";
        public string CountryCode = "";
        private bool isHovered, isPressed;

        public LocationCard() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(380, 58);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) isPressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { isPressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = isPressed ? Theme.SurfaceActive : isHovered ? Theme.SurfaceHover : Theme.Surface;
            Color border = isHovered ? Theme.BorderHover : Theme.Border;

            using (var brush = new SolidBrush(bg)) Theme.FillRounded(g, brush, r, 10);
            using (var pen = new Pen(border, 1f)) Theme.DrawRounded(g, pen, r, 10);

            // Left icon badge
            var iconBox = new Rectangle(14, 11, 36, 36);
            using (var bBrush = new SolidBrush(Theme.Sidebar)) Theme.FillRounded(g, bBrush, iconBox, 8);
            using (var bPen = new Pen(Theme.Border, 1f)) Theme.DrawRounded(g, bPen, iconBox, 8);
            using (var font = Theme.Font(11f, FontStyle.Regular)) using (var brush = new SolidBrush(Theme.Accent)) {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawEllipse(Pens.Gray, 24, 21, 16, 16);
                g.DrawLine(Pens.Gray, 24, 29, 40, 29);
                g.DrawEllipse(Pens.Gray, 28, 21, 8, 16);
            }

            // Location text bounded box with ellipsis trimming so it NEVER collides with latency badge
            var nameRect = new RectangleF(58, 10, Math.Max(60, Width - 180), 20);
            var subRect = new RectangleF(58, 30, Math.Max(60, Width - 180), 18);
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center }) {
                using (var nameFont = Theme.Font(10.5f, FontStyle.Bold)) using (var brush = new SolidBrush(Theme.TextPrimary)) {
                    g.DrawString(LocationName, nameFont, brush, nameRect, sf);
                }
                using (var subFont = Theme.Font(8.5f, FontStyle.Regular)) using (var brush = new SolidBrush(Theme.TextSecondary)) {
                    g.DrawString(Subtitle, subFont, brush, subRect, sf);
                }
            }

            // Latency badge and chevron on right
            using (var latFont = Theme.Mono(9f, FontStyle.Regular)) using (var brush = new SolidBrush(Theme.TextSecondary)) {
                var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                g.DrawString(Latency + "  ›", latFont, brush, new Rectangle(Width - 110, 0, 96, Height), sf);
            }
        }
    }

    internal sealed class LuxuryToggle : Control {
        private bool isChecked;
        public bool Checked {
            get { return isChecked; }
            set { if (isChecked != value) { isChecked = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); } }
        }
        public event EventHandler CheckedChanged;

        public LuxuryToggle() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(42, 24);
            Cursor = Cursors.Hand;
        }

        protected override void OnClick(EventArgs e) {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color track = Checked ? Theme.Success : Theme.Border;
            using (var brush = new SolidBrush(track)) Theme.FillRounded(g, brush, r, Height / 2);

            int thumbSize = Height - 6;
            int thumbX = Checked ? Width - thumbSize - 3 : 3;
            var thumbRect = new Rectangle(thumbX, 3, thumbSize, thumbSize);
            using (var brush = new SolidBrush(Checked ? Color.White : Theme.TextSecondary)) g.FillEllipse(brush, thumbRect);
        }
    }

    internal sealed class LuxuryNavButton : Control {
        public bool Compact;
        public string Title = "";
        public string IconText = "";
        private bool isSelected, isHovered;

        public bool Selected {
            get { return isSelected; }
            set { if (isSelected != value) { isSelected = value; Invalidate(); } }
        }

        public LuxuryNavButton(string title, string iconText) {
            Title = title; IconText = iconText;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(175, 38);
            Cursor = Cursors.Hand;
            Font = Theme.Font(9.5f, FontStyle.Regular);
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (isSelected) {
                using (var brush = new SolidBrush(Theme.SurfaceActive)) Theme.FillRounded(g, brush, r, 7);
                // Active indicator pill on left edge
                using (var brush = new SolidBrush(Theme.Accent)) Theme.FillRounded(g, brush, new Rectangle(2, 8, 3, Height - 16), 2);
            } else if (isHovered) {
                using (var brush = new SolidBrush(Theme.SurfaceHover)) Theme.FillRounded(g, brush, r, 7);
            }

            Color fg = isSelected ? Theme.TextPrimary : Theme.TextSecondary;
            if (Compact) {
                using (var font = Theme.Font(8.5f, isSelected ? FontStyle.Bold : FontStyle.Regular)) {
                    TextRenderer.DrawText(g, Title, font, new Rectangle(4, 0, Width - 8, Height), fg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                return;
            }
            using (var iconFont = Theme.Font(10.5f, FontStyle.Regular)) using (var brush = new SolidBrush(isSelected ? Theme.Accent : fg)) {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.FillEllipse(brush, 20, Height / 2 - 2, 4, 4);
            }

            using (var brush = new SolidBrush(fg)) {
                var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                g.DrawString(Title, isSelected ? Theme.Font(9.5f, FontStyle.Bold) : Font, brush, new Rectangle(38, 0, Width - 42, Height), sf);
            }
        }
    }

    internal sealed class LuxuryTitleBar : Control {
        private readonly Form parent;
        private readonly Button minBtn = new Button(), maxBtn = new Button(), closeBtn = new Button();
        public readonly StatusPill StatusIndicator = new StatusPill { Visible = false };

        public LuxuryTitleBar(Form parent, bool showMinMax = true) {
            this.parent = parent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = 44;
            BackColor = Theme.Sidebar;

            if (showMinMax) {
                ConfigureButton(minBtn, "─", delegate { parent.WindowState = FormWindowState.Minimized; });
                ConfigureButton(maxBtn, "□", delegate {
                    parent.WindowState = (parent.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized);
                });
                minBtn.MouseEnter += delegate { minBtn.BackColor = Theme.SurfaceHover; minBtn.ForeColor = Theme.TextPrimary; };
                minBtn.MouseLeave += delegate { minBtn.BackColor = Color.Transparent; minBtn.ForeColor = Theme.TextSecondary; };
                maxBtn.MouseEnter += delegate { maxBtn.BackColor = Theme.SurfaceHover; maxBtn.ForeColor = Theme.TextPrimary; };
                maxBtn.MouseLeave += delegate { maxBtn.BackColor = Color.Transparent; maxBtn.ForeColor = Theme.TextSecondary; };
                Controls.Add(minBtn);
                Controls.Add(maxBtn);
                DoubleClick += delegate {
                    parent.WindowState = (parent.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized);
                };
            }

            ConfigureButton(closeBtn, "✕", delegate { parent.Close(); });
            closeBtn.MouseEnter += delegate { closeBtn.BackColor = Theme.Danger; closeBtn.ForeColor = Color.White; };
            closeBtn.MouseLeave += delegate { closeBtn.BackColor = Color.Transparent; closeBtn.ForeColor = Theme.TextSecondary; };
            Controls.Add(closeBtn);
        }

        private void ConfigureButton(Button btn, string text, EventHandler onClick) {
            btn.Text = text;
            btn.Font = Theme.Font(8.5f, FontStyle.Regular);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.BackColor = Color.Transparent;
            btn.ForeColor = Theme.TextSecondary;
            btn.Size = new Size(42, 44);
            btn.Cursor = Cursors.Hand;
            btn.Click += onClick;
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            closeBtn.Location = new Point(Width - 42, 0);
            if (maxBtn.Parent != null) maxBtn.Location = new Point(Width - 84, 0);
            if (minBtn.Parent != null) minBtn.Location = new Point(Width - 126, 0);
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button == MouseButtons.Left && e.Clicks == 1) {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(parent.Handle, 0xA1, 0x2, 0);
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Brand mark: III85III · VPN
            if (parent.Icon != null) g.DrawIcon(parent.Icon, new Rectangle(14, 9, 26, 26));
            using (var font = Theme.Font(9.5f, FontStyle.Bold)) using (var brush = new SolidBrush(Theme.TextPrimary)) {
                g.DrawString("III85III", font, brush, new PointF(48, 14));
            }
            using (var font = Theme.Font(8.5f, FontStyle.Regular)) using (var brush = new SolidBrush(Theme.TextSubtle)) {
                g.DrawString("·  VPN", font, brush, new PointF(108, 15));
            }

            // Subtle bottom hairline separator
            using (var pen = new Pen(Theme.Border, 1f)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }

    internal sealed class LuxurySearchBox : Panel {
        public readonly TextBox Input = new TextBox();
        private bool isFocused;

        public LuxurySearchBox(string placeholder = "Search…") {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 36;
            BackColor = Theme.Surface;
            Padding = new Padding(32, 8, 10, 8);

            Input.BorderStyle = BorderStyle.None;
            Input.BackColor = Theme.Surface;
            Input.ForeColor = Theme.TextPrimary;
            Input.Font = Theme.Font(9.5f);
            Input.Dock = DockStyle.Fill;
            Input.HandleCreated += delegate { NativeMethods.SetCue(Input.Handle, 0x1501, (IntPtr)1, placeholder); };
            Input.AccessibleName = placeholder;

            Input.GotFocus += delegate { isFocused = true; Invalidate(); };
            Input.LostFocus += delegate { isFocused = false; Invalidate(); };

            Controls.Add(Input);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var brush = new SolidBrush(Theme.Surface)) Theme.FillRounded(g, brush, r, 7);
            using (var pen = new Pen(isFocused ? Theme.Accent : Theme.Border, 1f)) Theme.DrawRounded(g, pen, r, 7);

            // Draw search magnifying glass glyph
            using (var pen = new Pen(Theme.TextSubtle, 1.4f)) {
                g.DrawEllipse(pen, 11, 11, 9, 9);
                g.DrawLine(pen, 19, 19, 24, 24);
            }
        }
    }

    internal sealed class LuxuryRadioCard : Control {
        public string Title = "";
        public string Description = "";
        public int ModeValue;
        private bool isChecked, isHovered;

        public bool Checked {
            get { return isChecked; }
            set { if (isChecked != value) { isChecked = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); } }
        }
        public event EventHandler CheckedChanged;

        public LuxuryRadioCard(string title, string description, int modeValue) {
            Title = title; Description = description; ModeValue = modeValue;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(680, 56);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) {
            if (!Checked) Checked = true;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = isChecked ? Theme.SurfaceActive : isHovered ? Theme.SurfaceHover : Theme.Surface;
            Color border = isChecked ? Theme.Accent : isHovered ? Theme.BorderHover : Theme.Border;

            using (var brush = new SolidBrush(bg)) Theme.FillRounded(g, brush, r, 8);
            using (var pen = new Pen(border, 1f)) Theme.DrawRounded(g, pen, r, 8);

            // Radio circle indicator
            int radioX = 16, radioY = Height / 2 - 8;
            var radioRect = new Rectangle(radioX, radioY, 16, 16);
            using (var pen = new Pen(isChecked ? Theme.Accent : Theme.TextSubtle, 1.5f)) g.DrawEllipse(pen, radioRect);
            if (isChecked) {
                using (var brush = new SolidBrush(Theme.Accent)) g.FillEllipse(brush, radioX + 4, radioY + 4, 8, 8);
            }

            // Title & Description with ellipsis trimming
            var titleRect = new RectangleF(44, 9, Math.Max(80, Width - 56), 18);
            var descRect = new RectangleF(44, 29, Math.Max(80, Width - 56), 18);
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center }) {
                using (var font = Theme.Font(9.5f, FontStyle.Bold)) using (var brush = new SolidBrush(Theme.TextPrimary)) {
                    g.DrawString(Title, font, brush, titleRect, sf);
                }
                using (var font = Theme.Font(8.5f, FontStyle.Regular)) using (var brush = new SolidBrush(Theme.TextSecondary)) {
                    g.DrawString(Description, font, brush, descRect, sf);
                }
            }
        }
    }

    internal sealed class SignInForm : Form {
        readonly Engine engine;
        readonly TextBox email = new TextBox(), password = new TextBox();
        readonly Label note = new Label(), feedback = new Label();
        readonly LuxuryButton submit = new LuxuryButton { Text = "Sign in", Variant = ButtonVariant.Primary }, cancel = new LuxuryButton { Text = "Cancel", Variant = ButtonVariant.Secondary };
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1200 };
        string session = ""; bool polling, closing, finished;

        public SignInForm(Engine engine) {
            this.engine = engine;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.TextPrimary;
            Size = new Size(480, Math.Min(460, Screen.PrimaryScreen.WorkingArea.Height - 32));
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var titleBar = new LuxuryTitleBar(this, false);
            Controls.Add(titleBar);

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 54, 28, 20), AutoScroll = true, AutoScrollMinSize = new Size(0, 414) };
            Controls.Add(panel);
            titleBar.BringToFront();

                var title = new Label { Text = "Welcome to III85III", Font = Theme.Display(19f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(28, 56) };
            panel.Controls.Add(title);

            note.Text = "Use your Mozilla email and password. If a code is needed, we'll ask for it next.";
            note.Font = Theme.Font(9f);
            note.ForeColor = Theme.TextSecondary;
            note.Size = new Size(420, 40);
            note.Location = new Point(28, 96);
            panel.Controls.Add(note);

            int y = 148;
            AddInputField(panel, "Email Address", email, ref y, false);
            AddInputField(panel, "Password", password, ref y, true);

            feedback.Font = Theme.Font(8.5f);
            feedback.ForeColor = Theme.Accent;
            feedback.Size = new Size(424, 38);
            feedback.Location = new Point(28, y);
            panel.Controls.Add(feedback);
            y += 42;

            submit.Size = new Size(130, 38); submit.Location = new Point(28, y);
            cancel.Size = new Size(110, 38); cancel.Location = new Point(168, y);
            panel.Controls.Add(submit); panel.Controls.Add(cancel);

            submit.Click += async delegate { await Submit(); };
            cancel.Click += delegate { Close(); };
            AcceptButton = submit; CancelButton = cancel;

            timer.Tick += async delegate {
                if (polling || closing || session == "") return;
                polling = true;
                try { Apply(await engine.Api("GET", "/api/signin/" + session)); }
                catch (Exception e) { feedback.Text = e.Message; }
                finally { polling = false; }
            };

            FormClosing += async delegate (object sender, FormClosingEventArgs e) {
                if (closing || finished || session == "") return;
                e.Cancel = true; closing = true; timer.Stop();
                try { await engine.Api("DELETE", "/api/signin/" + session, null, 3000); } catch { } Close();
            };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); password.Clear(); };
        }

        private void AddInputField(Panel p, string labelText, TextBox tb, ref int y, bool isPassword) {
            var lbl = new Label { Text = labelText, Font = Theme.Font(8.5f, FontStyle.Bold), ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(28, y) };
            p.Controls.Add(lbl);
            y += 20;

            var box = new Panel { Size = new Size(424, 36), Location = new Point(28, y), BackColor = Theme.Surface };
            box.Paint += delegate (object s, PaintEventArgs e) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, box.Width - 1, box.Height - 1);
                using (var pen = new Pen(tb.Focused ? Theme.Accent : Theme.Border, 1f)) Theme.DrawRounded(e.Graphics, pen, r, 6);
            };

            tb.BorderStyle = BorderStyle.None;
            tb.BackColor = Theme.Surface;
            tb.ForeColor = Theme.TextPrimary;
            tb.Font = Theme.Font(10f);
            tb.UseSystemPasswordChar = isPassword;
            tb.Location = new Point(10, 8);
            tb.Size = new Size(404, 20);
            tb.GotFocus += delegate { box.Invalidate(); };
            tb.LostFocus += delegate { box.Invalidate(); };

            box.Controls.Add(tb);
            p.Controls.Add(box);
            y += 46;
        }

        protected override CreateParams CreateParams {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; }
        }

        async Task Submit() {
            submit.Enabled = false;
            try {
                if (session == "") {
                    if (email.Text.Trim() == "" || password.Text == "") throw new IOException("Enter your Mozilla email and password.");
                    var reply = await engine.Api("POST", "/api/signin", new { email = email.Text.Trim(), password = password.Text }); password.Clear();
                    session = Json.Str(reply, "id"); if (session == "") throw new IOException("Sign-in did not return a session.");
                    email.Enabled = password.Enabled = false; timer.Start(); Apply(reply);
                }
            } catch (Exception e) { feedback.Text = e.Message; submit.Enabled = true; }
        }

        void Apply(Dictionary<string, object> state) {
            string next = Json.Str(state, "step"); feedback.Text = Json.Str(state, "message") + "\n" + Json.Str(state, "error");
            if (next == "done") { finished = true; timer.Stop(); DialogResult = DialogResult.OK; Close(); return; }
            if (next == "failed" || next == "cancelled") { timer.Stop(); session = ""; email.Enabled = password.Enabled = true; submit.Text = "Try again"; submit.Enabled = true; return; }
            bool input = next == "need_email_code" || next == "need_totp" || next == "need_unblock_code" || next == "need_captcha";
            submit.Enabled = false; submit.Text = "Waiting for Mozilla…";
            if (input) {
                timer.Stop();
                using (var verification = new VerificationForm(engine, session, state)) {
                    if (verification.ShowDialog(this) == DialogResult.OK) { finished = true; DialogResult = DialogResult.OK; }
                }
                Close();
            }
        }
    }

    internal sealed partial class MainForm : Form {
        readonly Engine engine;
        readonly LuxuryTitleBar titleBar;
        readonly Panel sidebar = new Panel();
        readonly Panel contentArea = new Panel();
        bool arranging;
        readonly Panel resizeGrip = new Panel { Size = new Size(14, 14), Cursor = Cursors.SizeNWSE };

        // Navigation buttons
        readonly List<LuxuryNavButton> navButtons = new List<LuxuryNavButton>();
        readonly Panel[] pages = new Panel[6];
        int activePageIndex = 0;

        // Connection View Controls
        readonly StatusPill statusPill = new StatusPill();
        readonly LuxuryDial dial = new LuxuryDial();
        readonly Label dialAction = new Label(), dialSubtext = new Label();
        readonly LuxuryButton quickBtn = new LuxuryButton { Text = "Quick connect", Variant = ButtonVariant.Secondary, Width = 135 };
        readonly LuxuryButton connectBtn = new LuxuryButton { Text = "Get started", Variant = ButtonVariant.Primary };
        readonly LuxuryButton antigravityBtn = new LuxuryButton { Text = "Antigravity (UK)", Variant = ButtonVariant.Secondary, Width = 150 };
        readonly LuxuryButton verifyBtn = new LuxuryButton { Text = "Check connection", Variant = ButtonVariant.Ghost, Width = 140 };
        readonly LocationCard selectedLocationCard = new LocationCard();
        readonly Label speedStats = new Label(), accountBadge = new Label(), noticeLabel = new Label();
        readonly LuxuryButton eyeBtn = new LuxuryButton { Text = "Hide", Variant = ButtonVariant.Ghost, Width = 64, Height = 24 };

        // Locations View Controls
        readonly LuxurySearchBox locationSearch = new LuxurySearchBox("Search locations…");
        readonly ListBox locationList = new ListBox();
        readonly LuxuryButton pingBtn = new LuxuryButton { Text = "Measure Latency", Variant = ButtonVariant.Secondary };
        readonly LuxuryButton sortPingBtn = new LuxuryButton { Text = "Ping: low first", Variant = ButtonVariant.Primary };
        bool sortByPing = true;
        readonly LuxuryButton applyLocationBtn = new LuxuryButton { Text = "Connect to Location", Variant = ButtonVariant.Primary };
        readonly LuxuryButton antigravityFilterBtn = new LuxuryButton { Text = "Antigravity Safe", Variant = ButtonVariant.Ghost, Width = 140 };
        readonly List<ServerChoice> allLocations = new List<ServerChoice>();
        bool antigravityFilterOnly;
        DateTime lastAutoPing = DateTime.MinValue;

        // Split Tunnel View Controls
        readonly LuxurySearchBox splitSearch = new LuxurySearchBox("Search applications…");
        readonly ListView splitList = new ListView();
        readonly LuxuryButton splitSave = new LuxuryButton { Text = "Save choices", Variant = ButtonVariant.Primary };
        readonly LuxuryButton splitReload = new LuxuryButton { Text = "Refresh apps", Variant = ButtonVariant.Secondary };
        readonly LuxuryButton splitBrowse = new LuxuryButton { Text = "Choose an app", Variant = ButtonVariant.Secondary };
        readonly Label splitCountLabel = new Label();
        readonly Dictionary<string, AppBypass> splitSelected = new Dictionary<string, AppBypass>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, AppBypass> splitAvailable = new Dictionary<string, AppBypass>(StringComparer.OrdinalIgnoreCase);
        bool splitRendering, splitLoaded;

        // Account View Controls
        readonly ListView accountsList = new ListView();
        readonly LuxuryButton loginBtn = new LuxuryButton { Text = "Add an account", Variant = ButtonVariant.Primary };
        readonly LuxuryButton refreshQuotaBtn = new LuxuryButton { Text = "Check balance", Variant = ButtonVariant.Secondary };
        readonly LuxuryButton removeAccountBtn = new LuxuryButton { Text = "Remove Account", Variant = ButtonVariant.Danger };
        readonly LuxuryButton accountEyeBtn = new LuxuryButton { Text = "Hide Email", Variant = ButtonVariant.Secondary, Width = 110 };
        bool hideEmail;

        // Settings View Controls
        readonly LuxuryRadioCard[] modeCards = new LuxuryRadioCard[3];
        readonly LuxuryButton applyModeBtn = new LuxuryButton { Text = "Apply Routing Mode", Variant = ButtonVariant.Primary };
        readonly LuxuryToggle minimizeToggle = new LuxuryToggle();
        int pendingModeIndex = 0;

        // Diagnostics View Controls
        readonly Label exitInfoLabel = new Label();
        readonly LuxuryButton diagTestBtn = new LuxuryButton { Text = "Test Connection", Variant = ButtonVariant.Secondary };
        readonly LuxuryButton diagPingBtn = new LuxuryButton { Text = "Measure Latency", Variant = ButtonVariant.Secondary };
        readonly LuxuryButton copyLogsBtn = new LuxuryButton { Text = "Copy Logs", Variant = ButtonVariant.Secondary };
        readonly TextBox logsBox = new TextBox();

        // Runtime state
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 2200 };
        readonly WarmupPolicy warmup = new WarmupPolicy();
        Dictionary<string, object> lastStatus = new Dictionary<string, object>();
        bool ready, polling, busy, closing, allowClose, testing;
        DateTime nextLogs = DateTime.MinValue;
        long lastLogId;

        public MainForm(Engine engine, bool layoutOnly = false) {
            this.engine = engine;
            Text = "III85III VPN";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Math.Min(1040, Screen.PrimaryScreen.WorkingArea.Width - 40), Math.Min(760, Screen.PrimaryScreen.WorkingArea.Height - 40));
            MinimumSize = new Size(680, 520);
            MaximizedBounds = Screen.PrimaryScreen.WorkingArea;
            BackColor = Theme.Bg;
            ForeColor = Theme.TextPrimary;
            DoubleBuffered = true;

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            titleBar = new LuxuryTitleBar(this, true);
            Controls.Add(titleBar);

            sidebar.Dock = DockStyle.Left;
            sidebar.Width = 195;
            sidebar.BackColor = Theme.Sidebar;
            sidebar.Padding = new Padding(10, 14, 10, 14);
            sidebar.Paint += delegate (object s, PaintEventArgs e) {
                using (var pen = new Pen(Theme.Border, 1f)) e.Graphics.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
            };
            Controls.Add(sidebar);

            contentArea.Dock = DockStyle.Fill;
            contentArea.BackColor = Theme.Bg;
            contentArea.Padding = new Padding(22, 16, 22, 16);
            Controls.Add(contentArea);

            InitializeSidebar();
            InitializePages();
            InitializeDesktopFeatures();
            resizeGrip.Paint += delegate(object sender, PaintEventArgs e) {
                using (var pen = new Pen(Theme.TextSubtle)) { e.Graphics.DrawLine(pen, 5, 12, 12, 5); e.Graphics.DrawLine(pen, 9, 12, 12, 9); }
            };
            resizeGrip.MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left) { NativeMethods.ReleaseCapture(); NativeMethods.SendMessage(Handle, 0xA1, 17, 0); }
            };
            Controls.Add(resizeGrip);
            titleBar.Dock = sidebar.Dock = contentArea.Dock = DockStyle.None;
            Resize += delegate { ArrangeResponsive(); };
            ArrangeResponsive();
            if (!layoutOnly) InitializeTray();

            // Hook actions
            dial.Click += async delegate { await ConnectFromHome(); };
            connectBtn.Click += async delegate { await ConnectFromHome(); };
            quickBtn.Click += async delegate { await Run(QuickConnect); };
            antigravityBtn.Click += async delegate { await Run(ConnectAntigravity); };
            verifyBtn.Click += async delegate { await TestConnection(true); };
            eyeBtn.Click += delegate { ToggleEmailPrivacy(); };
            accountEyeBtn.Click += delegate { ToggleEmailPrivacy(); };
            antigravityFilterBtn.Click += delegate { ToggleAntigravityFilter(); };
            selectedLocationCard.Click += delegate { SwitchPage(1); };

            pingBtn.Click += async delegate { await Run(async delegate { await AutoPingLocations(true); }); };
            applyLocationBtn.Click += async delegate { await Run(ApplySelectedLocation); };
            locationSearch.Input.TextChanged += delegate { FilterLocations(); };
            sortPingBtn.Click += delegate {
                sortByPing = !sortByPing;
                UpdatePingSort(); FilterLocations(); SavePreferences(); ArrangeResponsive();
            };
            locationList.DoubleClick += async delegate { await Run(ApplySelectedLocation); };

            splitReload.Click += async delegate { await Run(LoadSplitApps); };
            splitSave.Click += async delegate { await Run(SaveSplitApps); };
            splitBrowse.Click += delegate { BrowseApp(); };
            splitSearch.Input.TextChanged += delegate { RenderSplitApps(); };
            splitList.ItemChecked += delegate (object sender, ItemCheckedEventArgs e) {
                if (splitRendering) return;
                var rule = (AppBypass)e.Item.Tag;
                if (e.Item.Checked) splitSelected[rule.Name] = new AppBypass(rule.Name, rule.Path);
                else splitSelected.Remove(rule.Name);
                UpdateSplitSummary();
            };

            loginBtn.Click += async delegate {
                if (!ready) return;
                using (var dialog = new SignInForm(engine)) { dialog.ShowDialog(this); }
                await Run(RefreshAccounts);
            };
            refreshQuotaBtn.Click += async delegate {
                await Run(async delegate {
                    if (accountsList.SelectedItems.Count == 0) throw new IOException("Select an account first.");
                    string id = Convert.ToString(accountsList.SelectedItems[0].Tag);
                    await engine.Api("POST", "/api/accounts/" + Uri.EscapeDataString(id) + "/refresh");
                    await RefreshAccounts();
                });
            };
            removeAccountBtn.Click += async delegate {
                if (accountsList.SelectedItems.Count == 0) return;
                if (MessageBox.Show(this, "Remove this Mozilla account and its stored credentials?", "Remove account", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                await Run(async delegate {
                    string id = Convert.ToString(accountsList.SelectedItems[0].Tag);
                    await engine.Api("DELETE", "/api/accounts/" + Uri.EscapeDataString(id));
                    await RefreshAccounts();
                });
            };

            applyModeBtn.Click += async delegate {
                await Run(async delegate {
                    if (Json.Str(Json.Child(lastStatus, "state"), "phase") != "off") await engine.Api("POST", "/api/power", new { on = false });
                    string mode = pendingModeIndex == 0 ? "tun" : pendingModeIndex == 1 ? "set" : "clear";
                    await engine.Api("POST", "/api/system-proxy", new { mode = mode });
                });
            };

            diagTestBtn.Click += async delegate { await TestConnection(true); };
            diagPingBtn.Click += async delegate { await Run(async delegate { await engine.Api("POST", "/api/locations/ping", new { codes = new string[0] }); await LoadLocations(); }); };
            copyLogsBtn.Text = "Copy safe report";
            copyLogsBtn.Click += delegate { try { Clipboard.SetText(SafeDiagnostics.Build(lastStatus)); } catch { } };

            timer.Tick += async delegate { await Poll(); };

            Shown += async delegate {
                if (layoutOnly) return;
                try {
                    LoadPreferences();
                    ApplyDesktopAppearance();
                    UpdateEyeButtons();
                    await engine.Start();
                    ready = true;
                    timer.Start();
                    await Poll();
                    await RefreshAccounts();
                    try {
                        await LoadLocations();
                    } catch { }
                } catch (Exception e) {
                    dialSubtext.Text = "Engine error: " + e.Message;
                    logsBox.Text = e.Message;
                }
            };

            FormClosing += async delegate (object sender, FormClosingEventArgs e) {
                if (closing) return;
                if (minimizeToggle.Checked && !allowClose && e.CloseReason == CloseReason.UserClosing) {
                    e.Cancel = true; Hide(); return;
                }
                e.Cancel = true; closing = true; timer.Stop(); Enabled = false; tray.Visible = false;
                try { await engine.Stop(); } catch { } finally { engine.Dispose(); Close(); }
            };
        }

        protected override CreateParams CreateParams {
            get {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // Drop shadow
                return cp;
            }
        }

        protected override void WndProc(ref Message m) {
            if (m.Msg == 0x84) { // WM_NCHITTEST
                base.WndProc(ref m);
                if ((int)m.Result == 1) { // HTCLIENT
                    Point pt = PointToClient(new Point(m.LParam.ToInt32()));
                    int w = ClientSize.Width, h = ClientSize.Height;
                    const int b = 6;
                    if (pt.X <= b && pt.Y <= b) { m.Result = (IntPtr)13; return; }
                    if (pt.X >= w - b && pt.Y <= b) { m.Result = (IntPtr)14; return; }
                    if (pt.X <= b && pt.Y >= h - b) { m.Result = (IntPtr)16; return; }
                    if (pt.X >= w - b && pt.Y >= h - b) { m.Result = (IntPtr)17; return; }
                    if (pt.X <= b) { m.Result = (IntPtr)10; return; }
                    if (pt.X >= w - b) { m.Result = (IntPtr)11; return; }
                    if (pt.Y <= b) { m.Result = (IntPtr)12; return; }
                    if (pt.Y >= h - b) { m.Result = (IntPtr)15; return; }
                }
                return;
            }
            base.WndProc(ref m);
        }

        private void InitializeSidebar() {
            var items = new[] {
                new { Title = "Home", Icon = "⏻" },
                new { Title = "Locations", Icon = "🌐" },
                new { Title = "App rules", Icon = "⑂" },
                new { Title = "Account", Icon = "👤" },
                new { Title = "Settings", Icon = "⚙" },
                new { Title = "Help", Icon = "⚡" }
            };

            int y = 14;
            for (int i = 0; i < items.Length; i++) {
                int pageIdx = i;
                var btn = new LuxuryNavButton(items[i].Title, items[i].Icon) { Location = new Point(10, y) };
                if (i == 0) btn.Selected = true;
                btn.Click += delegate { SwitchPage(pageIdx); };
                sidebar.Controls.Add(btn);
                navButtons.Add(btn);
                y += 44;
            }
        }

        private void SwitchPage(int index) {
            activePageIndex = index;
            for (int i = 0; i < navButtons.Count; i++) navButtons[i].Selected = (i == index);
            for (int i = 0; i < pages.Length; i++) {
                if (pages[i] != null) pages[i].Visible = (i == index);
            }
            ArrangeResponsive();
            if (index == 2 && ready && !splitLoaded) {
                var _ = Run(LoadSplitApps);
            }
        }

        private void InitializePages() {
            for (int i = 0; i < pages.Length; i++) {
                pages[i] = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = (i == 0) };
                contentArea.Controls.Add(pages[i]);
            }

            BuildConnectionPage(pages[0]);
            BuildLocationsPage(pages[1]);
            BuildSplitPage(pages[2]);
            BuildAccountPage(pages[3]);
            BuildSettingsPage(pages[4]);
            BuildDiagnosticsPage(pages[5]);
            foreach (var page in pages) page.Resize += delegate { ArrangeResponsive(); };
        }

        private void BuildConnectionPage(Panel p) {
            p.Controls.Add(statusPill);
            p.Controls.Add(dial);

            dialAction.Text = "Let's get you connected.";
            dialAction.Font = Theme.Display(24f);
            dialAction.ForeColor = Theme.TextPrimary;
            dialAction.TextAlign = ContentAlignment.MiddleCenter;
            dialAction.Size = new Size(360, 26);
            p.Controls.Add(dialAction);

            dialSubtext.Text = "Add your Mozilla account to get started.";
            dialSubtext.Font = Theme.Font(11f);
            dialSubtext.ForeColor = Theme.TextSecondary;
            dialSubtext.TextAlign = ContentAlignment.MiddleCenter;
            dialSubtext.Size = new Size(460, 20);
            p.Controls.Add(dialSubtext);
            connectBtn.Font = Theme.Font(11f, FontStyle.Bold);
            p.Controls.Add(connectBtn);

            var actionRow = new FlowLayoutPanel {
                WrapContents = false, AutoSize = true, FlowDirection = FlowDirection.LeftToRight
            };
            actionRow.Name = "connectionActions";
            quickBtn.Width = 135;
            antigravityBtn.Width = 155;
            verifyBtn.Width = 95;
            actionRow.Controls.Add(quickBtn);
            actionRow.Controls.Add(antigravityBtn);
            actionRow.Controls.Add(verifyBtn);
            p.Controls.Add(actionRow);

            selectedLocationCard.Size = new Size(440, 60);
            p.Controls.Add(selectedLocationCard);

            // Real traffic and account metrics panel
            var metricsPanel = new Panel {
                Size = new Size(520, 72),
                BackColor = Theme.Surface, Name = "connectionMetrics"
            };
            metricsPanel.Paint += delegate (object s, PaintEventArgs e) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, metricsPanel.Width - 1, metricsPanel.Height - 1);
                using (var pen = new Pen(Theme.Border, 1f)) Theme.DrawRounded(e.Graphics, pen, r, 8);
            };

            speedStats.Text = "↓ 0 B/s    ↑ 0 B/s";
            speedStats.Font = Theme.Font(10f, FontStyle.Bold);
            speedStats.ForeColor = Theme.TextPrimary;
            speedStats.Location = new Point(18, 14);
            speedStats.AutoSize = true;
            metricsPanel.Controls.Add(speedStats);

            accountBadge.Text = "Your account will appear here";
            accountBadge.Font = Theme.Font(8.5f);
            accountBadge.ForeColor = Theme.TextSecondary;
            accountBadge.Location = new Point(18, 42);
            accountBadge.Size = new Size(metricsPanel.Width - 110, 22);
            accountBadge.AutoEllipsis = true;
            accountBadge.AutoSize = false;
            metricsPanel.Controls.Add(accountBadge);

            eyeBtn.Location = new Point(metricsPanel.Width - 78, 37);
            eyeBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            metricsPanel.Controls.Add(eyeBtn);

            p.Controls.Add(metricsPanel);

            noticeLabel.Text = "Your account stays on this computer.\nNeed a hand? Connection details are in Help.";
            noticeLabel.Font = Theme.Font(9f);
            noticeLabel.ForeColor = Theme.TextSubtle;
            noticeLabel.TextAlign = ContentAlignment.MiddleCenter;
            noticeLabel.Size = new Size(480, 36);
            p.Controls.Add(noticeLabel);

            Action layoutControls = delegate {
                int cx = p.ClientSize.Width / 2;
                int startY = Math.Max(16, (p.ClientSize.Height - 510) / 2);
                statusPill.Location = new Point(cx - statusPill.Width / 2, startY);
                dial.Location = new Point(cx - dial.Width / 2, startY + 50);
                dialAction.Location = new Point(cx - dialAction.Width / 2, startY + 180);
                dialSubtext.Location = new Point(cx - dialSubtext.Width / 2, startY + 208);
                actionRow.Location = new Point(cx - actionRow.Width / 2, startY + 242);
                selectedLocationCard.Location = new Point(cx - selectedLocationCard.Width / 2, startY + 296);
                metricsPanel.Location = new Point(cx - metricsPanel.Width / 2, startY + 372);
                noticeLabel.Location = new Point(cx - noticeLabel.Width / 2, startY + 460);
            };
            layoutControls();
        }

        private void BuildLocationsPage(Panel p) {
            var title = new Label { Text = "Pick your location", Font = Theme.Display(19f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 0) };
            p.Controls.Add(title);

            var desc = new Label { Text = "Choose somewhere to connect from. You can change it whenever you like.", Font = Theme.Font(10f), ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(0, 26) };
            p.Controls.Add(desc);

            p.Controls.Add(locationSearch);
            p.Controls.Add(antigravityFilterBtn);
            p.Controls.Add(pingBtn);
            p.Controls.Add(sortPingBtn);
            p.Controls.Add(applyLocationBtn);

            locationList.BackColor = Theme.Surface;
            locationList.ForeColor = Theme.TextPrimary;
            locationList.Font = Theme.Font(10f);
            locationList.BorderStyle = BorderStyle.None;
            locationList.DrawMode = DrawMode.OwnerDrawFixed;
            locationList.ItemHeight = 44;

            locationList.DrawItem += delegate (object sender, DrawItemEventArgs e) {
                if (e.Index < 0 || e.Index >= locationList.Items.Count) return;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                var item = (ServerChoice)locationList.Items[e.Index];
                var r = e.Bounds;

                using (var brush = new SolidBrush(selected ? Theme.SurfaceActive : Theme.Surface)) g.FillRectangle(brush, r);
                if (selected) {
                    using (var brush = new SolidBrush(Theme.Accent)) g.FillRectangle(brush, 0, r.Y + 8, 3, r.Height - 16);
                }

                var nameRect = new RectangleF(18, r.Y + 12, Math.Max(80, r.Width - 110), 22);
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center }) {
                    using (var brush = new SolidBrush(selected ? Theme.TextPrimary : Theme.TextSecondary)) {
                        g.DrawString(item.Name, Theme.Font(9.5f, selected ? FontStyle.Bold : FontStyle.Regular), brush, nameRect, sf);
                    }
                }

                if (item.PingMs > 0) {
                    Color dotColor = item.PingMs < 80 ? Theme.Success : item.PingMs < 180 ? Theme.Warning : Theme.TextSubtle;
                    using (var brush = new SolidBrush(dotColor)) g.FillEllipse(brush, r.Right - 84, r.Y + 18, 7, 7);
                    using (var brush = new SolidBrush(Theme.TextSecondary)) using (var font = Theme.Mono(9f)) {
                        g.DrawString(item.PingMs + " ms", font, brush, new PointF(r.Right - 70, r.Y + 14));
                    }
                } else if (item.Code != "") {
                    using (var brush = new SolidBrush(Theme.TextSubtle)) using (var font = Theme.Mono(9f)) {
                        g.DrawString("— ms", font, brush, new PointF(r.Right - 70, r.Y + 14));
                    }
                }
                using (var pen = new Pen(Theme.Border, 1f)) g.DrawLine(pen, 0, r.Bottom - 1, r.Right, r.Bottom - 1);
            };

            p.Controls.Add(locationList);

            Action layoutLocations = delegate {
                int rightMargin = 10;
                int btnY = 58;
                int gap = 8;

                applyLocationBtn.Size = new Size(145, 36);
                pingBtn.Size = new Size(125, 36);
                antigravityFilterBtn.Size = new Size(135, 36);

                int curRight = p.ClientSize.Width - rightMargin;

                curRight -= applyLocationBtn.Width;
                applyLocationBtn.Location = new Point(curRight, btnY);

                curRight -= (gap + pingBtn.Width);
                pingBtn.Location = new Point(curRight, btnY);

                curRight -= (gap + antigravityFilterBtn.Width);
                antigravityFilterBtn.Location = new Point(curRight, btnY);

                int searchWidth = curRight - gap;
                if (searchWidth < 160) searchWidth = 160;
                locationSearch.Location = new Point(0, btnY);
                locationSearch.Size = new Size(searchWidth, 36);

                locationList.Location = new Point(0, 106);
                locationList.Size = new Size(p.ClientSize.Width - rightMargin, Math.Max(100, p.ClientSize.Height - 116));
            };
            layoutLocations();
        }

        private void BuildSplitPage(Panel p) {
            var title = new Label { Text = "Your apps, your choice", Font = Theme.Display(19f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 0) };
            p.Controls.Add(title);

            var desc = new Label { Text = "Tick only the apps that should use VPN. All other apps stay direct.", Font = Theme.Font(10f), ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(0, 26) };
            p.Controls.Add(desc);

            p.Controls.Add(splitSearch);
            p.Controls.Add(splitBrowse);
            p.Controls.Add(splitReload);
            p.Controls.Add(splitSave);

            splitList.View = View.Details;
            splitList.CheckBoxes = true;
            splitList.FullRowSelect = true;
            splitList.BackColor = Theme.Surface;
            splitList.ForeColor = Theme.TextPrimary;
            splitList.BorderStyle = BorderStyle.None;
            splitList.Font = Theme.Font(9.5f);
            splitList.OwnerDraw = true;

            splitList.Columns.Add("Application", 220);
            splitList.Columns.Add("Location", 300);
            splitList.Columns.Add("Routing", 100);

            splitList.DrawColumnHeader += delegate (object sender, DrawListViewColumnHeaderEventArgs e) {
                using (var brush = new SolidBrush(Theme.Sidebar)) e.Graphics.FillRectangle(brush, e.Bounds);
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.Font(8.5f, FontStyle.Bold), e.Bounds, Theme.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            splitList.DrawItem += delegate (object s, DrawListViewItemEventArgs e) { e.DrawDefault = false; };
            splitList.DrawSubItem += delegate (object s, DrawListViewSubItemEventArgs e) {
                var g = e.Graphics;
                bool selected = (e.ItemState & ListViewItemStates.Selected) == ListViewItemStates.Selected;
                using (var brush = new SolidBrush(selected ? Theme.SurfaceActive : Theme.Surface)) g.FillRectangle(brush, e.Bounds);

                if (e.ColumnIndex == 0) {
                    // Checkbox
                    int cbX = e.Bounds.X + 8, cbY = e.Bounds.Y + e.Bounds.Height / 2 - 7;
                    var cbRect = new Rectangle(cbX, cbY, 14, 14);
                    using (var pen = new Pen(e.Item.Checked ? Theme.Accent : Theme.TextSubtle, 1.5f)) Theme.DrawRounded(g, pen, cbRect, 3);
                    if (e.Item.Checked) {
                        using (var brush = new SolidBrush(Theme.Accent)) Theme.FillRounded(g, brush, new Rectangle(cbX + 2, cbY + 2, 10, 10), 2);
                    }
                    TextRenderer.DrawText(g, e.Item.Text, Theme.Font(9.5f), new Rectangle(e.Bounds.X + 28, e.Bounds.Y, e.Bounds.Width - 28, e.Bounds.Height), Theme.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                } else {
                    Color fg = e.ColumnIndex == 2 && e.Item.Checked ? Theme.Accent : Theme.TextSecondary;
                    TextRenderer.DrawText(g, e.SubItem.Text, Theme.Font(9f), e.Bounds, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.PathEllipsis);
                }
                using (var pen = new Pen(Theme.Border, 1f)) g.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            };

            p.Controls.Add(splitList);

            splitCountLabel.Text = "0 apps selected to use VPN.";
            splitCountLabel.Font = Theme.Font(8.5f);
            splitCountLabel.ForeColor = Theme.TextSecondary;
            splitCountLabel.AutoSize = true;
            p.Controls.Add(splitCountLabel);

            Action layoutSplit = delegate {
                int rightMargin = 10;
                int btnY = 58;
                int gap = 8;

                splitSave.Size = new Size(135, 36);
                splitReload.Size = new Size(140, 36);
                splitBrowse.Size = new Size(95, 36);

                int curRight = p.ClientSize.Width - rightMargin;

                curRight -= splitSave.Width;
                splitSave.Location = new Point(curRight, btnY);

                curRight -= (gap + splitReload.Width);
                splitReload.Location = new Point(curRight, btnY);

                curRight -= (gap + splitBrowse.Width);
                splitBrowse.Location = new Point(curRight, btnY);

                int searchWidth = curRight - gap;
                if (searchWidth < 160) searchWidth = 160;
                splitSearch.Location = new Point(0, btnY);
                splitSearch.Size = new Size(searchWidth, 36);

                splitList.Location = new Point(0, 106);
                splitList.Size = new Size(p.ClientSize.Width - rightMargin, Math.Max(100, p.ClientSize.Height - 146));

                int totalW = splitList.ClientSize.Width;
                if (totalW > 320) {
                    splitList.Columns[0].Width = 200;
                    splitList.Columns[2].Width = 100;
                    splitList.Columns[1].Width = Math.Max(100, totalW - 200 - 100 - 4);
                }

                splitCountLabel.Location = new Point(0, p.ClientSize.Height - 28);
            };
            layoutSplit();
        }

        private void BuildAccountPage(Panel p) {
            var title = new Label { Text = "Your account", Font = Theme.Display(19f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 0) };
            p.Controls.Add(title);

            var desc = new Label { Text = "Sign in with Mozilla, then keep an eye on your monthly balance here.", Font = Theme.Font(10f), ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(0, 26) };
            p.Controls.Add(desc);

            loginBtn.Location = new Point(0, 58); loginBtn.Size = new Size(130, 36);
            refreshQuotaBtn.Location = new Point(138, 58); refreshQuotaBtn.Size = new Size(120, 36);
            removeAccountBtn.Location = new Point(266, 58); removeAccountBtn.Size = new Size(130, 36);
            accountEyeBtn.Location = new Point(404, 58); accountEyeBtn.Size = new Size(110, 36);
            p.Controls.Add(loginBtn); p.Controls.Add(refreshQuotaBtn); p.Controls.Add(removeAccountBtn); p.Controls.Add(accountEyeBtn);

            accountsList.View = View.Details;
            accountsList.FullRowSelect = true;
            accountsList.BackColor = Theme.Surface;
            accountsList.ForeColor = Theme.TextPrimary;
            accountsList.BorderStyle = BorderStyle.None;
            accountsList.Font = Theme.Font(9.5f);
            accountsList.OwnerDraw = true;

            accountsList.Columns.Add("Email", 220);
            accountsList.Columns.Add("Status", 130);
            accountsList.Columns.Add("Allowance Remaining", 160);
            accountsList.Columns.Add("Reset Date", 130);

            accountsList.DrawColumnHeader += delegate (object sender, DrawListViewColumnHeaderEventArgs e) {
                using (var brush = new SolidBrush(Theme.Sidebar)) e.Graphics.FillRectangle(brush, e.Bounds);
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.Font(8.5f, FontStyle.Bold), e.Bounds, Theme.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            accountsList.DrawItem += delegate (object s, DrawListViewItemEventArgs e) { e.DrawDefault = false; };
            accountsList.DrawSubItem += delegate (object s, DrawListViewSubItemEventArgs e) {
                var g = e.Graphics;
                bool selected = (e.ItemState & ListViewItemStates.Selected) == ListViewItemStates.Selected;
                using (var brush = new SolidBrush(selected ? Theme.SurfaceActive : Theme.Surface)) g.FillRectangle(brush, e.Bounds);
                Color fg = selected ? Theme.TextPrimary : Theme.TextSecondary;
                TextRenderer.DrawText(g, e.SubItem.Text, Theme.Font(9f), e.Bounds, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                using (var pen = new Pen(Theme.Border, 1f)) g.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            };

            p.Controls.Add(accountsList);

            // Privacy info box
            var infoBox = new Panel { BackColor = Theme.Surface, Name = "accountInfo" };
            infoBox.Paint += delegate (object s, PaintEventArgs e) {
                var r = new Rectangle(0, 0, infoBox.Width - 1, infoBox.Height - 1);
                using (var pen = new Pen(Theme.Border, 1f)) Theme.DrawRounded(e.Graphics, pen, r, 8);
            };
            var privTitle = new Label { Text = "At home on your computer", Font = Theme.Font(10f, FontStyle.Bold), ForeColor = Theme.TextPrimary, Location = new Point(14, 12), AutoSize = true };
            var privDesc = new Label {
                Text = "Your sign-in details are stored in your Windows account.\nIII85III doesn't send them to a separate III85III server.",
                Font = Theme.Font(8.5f), ForeColor = Theme.TextSecondary, Location = new Point(14, 32)
            };
            infoBox.Controls.Add(privTitle); infoBox.Controls.Add(privDesc);
            p.Controls.Add(infoBox);

            Action layoutAccounts = delegate {
                int rightMargin = 10;
                accountsList.Location = new Point(0, 106);
                accountsList.Size = new Size(p.ClientSize.Width - rightMargin, Math.Max(80, p.ClientSize.Height - 204));

                int totalW = accountsList.ClientSize.Width;
                if (totalW > 420) {
                    accountsList.Columns[0].Width = Math.Max(180, totalW - 130 - 160 - 130 - 4);
                    accountsList.Columns[1].Width = 130;
                    accountsList.Columns[2].Width = 160;
                    accountsList.Columns[3].Width = 130;
                }

                infoBox.Location = new Point(0, p.ClientSize.Height - 84);
                infoBox.Size = new Size(p.ClientSize.Width - rightMargin, 74);
                privDesc.Size = new Size(infoBox.Width - 28, 36);
            };
            layoutAccounts();
        }

        private void BuildSettingsPage(Panel p) {
            var title = new Label { Text = "Make it yours", Font = Theme.Display(19f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 0) };
            p.Controls.Add(title);

            var sec1 = new Label { Text = "Connection mode", Font = Theme.Font(9f, FontStyle.Bold), ForeColor = Theme.TextSubtle, AutoSize = true, Location = new Point(0, 40) };
            p.Controls.Add(sec1);

            modeCards[0] = new LuxuryRadioCard("Whole computer · Recommended", "Sends TCP traffic through the VPN. Your app exceptions still apply.", 0) { Location = new Point(0, 62), Checked = true };
            modeCards[1] = new LuxuryRadioCard("Windows system proxy", "Configures Windows Internet Options proxy settings for browsers and web apps.", 1) { Location = new Point(0, 124) };
            modeCards[2] = new LuxuryRadioCard("Local proxy only", "Runs loopback HTTP and SOCKS5 proxies without altering system routing.", 2) { Location = new Point(0, 186) };

            for (int i = 0; i < modeCards.Length; i++) {
                int idx = i;
                modeCards[i].CheckedChanged += delegate {
                    if (modeCards[idx].Checked) {
                        pendingModeIndex = idx;
                        for (int j = 0; j < modeCards.Length; j++) if (j != idx) modeCards[j].Checked = false;
                    }
                };
                p.Controls.Add(modeCards[i]);
            }

            applyModeBtn.Location = new Point(0, 248); applyModeBtn.Size = new Size(160, 36);
            p.Controls.Add(applyModeBtn);

            var sec2 = new Label { Text = "Window behavior", Name = "behaviorHeading", Font = Theme.Font(9f, FontStyle.Bold), ForeColor = Theme.TextSubtle, AutoSize = true, Location = new Point(0, 304) };
            p.Controls.Add(sec2);

            var trayRow = new Panel { Location = new Point(0, 326), Size = new Size(680, 44), BackColor = Theme.Surface, Name = "trayPreference" };
            trayRow.Paint += delegate (object s, PaintEventArgs e) {
                var r = new Rectangle(0, 0, trayRow.Width - 1, trayRow.Height - 1);
                using (var pen = new Pen(Theme.Border, 1f)) Theme.DrawRounded(e.Graphics, pen, r, 8);
            };
            minimizeToggle.Location = new Point(14, 10);
            var trayLabel = new Label { Text = "Keep running in notification tray when window is closed", Font = Theme.Font(9.5f), ForeColor = Theme.TextPrimary, Location = new Point(66, 12), AutoSize = true };
            trayRow.Controls.Add(minimizeToggle); trayRow.Controls.Add(trayLabel);
            p.Controls.Add(trayRow);

            var sec3 = new Label { Text = "Resources", Name = "resourcesHeading", Font = Theme.Font(9f, FontStyle.Bold), ForeColor = Theme.TextSubtle, AutoSize = true, Location = new Point(0, 388) };
            p.Controls.Add(sec3);

            var btnRow = new FlowLayoutPanel { Location = new Point(0, 410), Size = new Size(700, 44), WrapContents = true, Name = "resourceButtons" };
            var openFolderBtn = new LuxuryButton { Text = "Open Data Folder", Width = 140 };
            var docsBtn = new LuxuryButton { Text = "User Guide", Width = 110 };
            var dashBtn = new LuxuryButton { Text = "Engine Dashboard", Width = 150 };

            openFolderBtn.Click += delegate { Process.Start(new ProcessStartInfo(engine.DataDir) { UseShellExecute = true }); };
            docsBtn.Click += delegate { Process.Start(new ProcessStartInfo(Path.Combine(engine.Root, "START-HERE.html")) { UseShellExecute = true }); };
            dashBtn.Click += async delegate {
                await Run(async delegate {
                    var r = await engine.Api("POST", "/api/session/link");
                    string path = Json.Str(r, "path");
                    if (!path.StartsWith("/app/login/", StringComparison.Ordinal) || path.Contains("\\")) throw new IOException("Invalid dashboard link.");
                    Process.Start(engine.Endpoint + path);
                });
            };

            btnRow.Controls.Add(openFolderBtn); btnRow.Controls.Add(docsBtn); btnRow.Controls.Add(dashBtn);
            p.Controls.Add(btnRow);

            var legal = new Label {
                Text = "III85III VPN 2.1 Preview · Independent local client\nNode.js, sing-box and fonts retain their licenses.",
                Font = Theme.Font(8f), ForeColor = Theme.TextSubtle, Location = new Point(0, 470), AutoSize = false, Name = "sourceNotice"
            };
            p.Controls.Add(legal);

            Action layoutSettings = delegate {
                int cardW = Math.Max(200, p.ClientSize.Width - 10);
                for (int i = 0; i < modeCards.Length; i++) {
                    if (modeCards[i] != null) modeCards[i].Width = cardW;
                }
                trayRow.Width = cardW;
            };
            layoutSettings();
        }

        private void BuildDiagnosticsPage(Panel p) {
            var title = new Label { Text = "A little connection help", Font = Theme.Display(19f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 0) };
            p.Controls.Add(title);

            exitInfoLabel.Text = "Exit IP: not verified yet";
            exitInfoLabel.Font = Theme.Mono(9.5f);
            exitInfoLabel.ForeColor = Theme.TextPrimary;
            exitInfoLabel.Location = new Point(0, 32);
            exitInfoLabel.AutoSize = true;
            p.Controls.Add(exitInfoLabel);

            var btnRow = new FlowLayoutPanel { Location = new Point(0, 58), Size = new Size(600, 42), WrapContents = false };
            diagTestBtn.Size = new Size(130, 36);
            diagPingBtn.Size = new Size(130, 36);
            copyLogsBtn.Size = new Size(110, 36);
            btnRow.Controls.Add(diagTestBtn); btnRow.Controls.Add(diagPingBtn); btnRow.Controls.Add(copyLogsBtn);
            p.Controls.Add(btnRow);

            logsBox.Location = new Point(0, 106);
            logsBox.Multiline = true;
            logsBox.ReadOnly = true;
            logsBox.ScrollBars = ScrollBars.Vertical;
            logsBox.BackColor = Color.FromArgb(14, 15, 18);
            logsBox.ForeColor = Theme.TextSecondary;
            logsBox.BorderStyle = BorderStyle.None;
            logsBox.Font = Theme.Mono(9f);
            p.Controls.Add(logsBox);

            Action layoutDiag = delegate {
                logsBox.Size = new Size(p.ClientSize.Width - 10, Math.Max(80, p.ClientSize.Height - 116));
            };
            layoutDiag();
        }

        private static void LabelBox(Control control, int x, int y, int width, int height) {
            var label = control as Label;
            if (label != null) { label.AutoSize = false; label.AutoEllipsis = true; }
            control.SetBounds(x, y, Math.Max(1, width), Math.Max(1, height));
        }

        private static int PlaceButtons(Control[] buttons, int x, int y, int width) {
            int left = x, top = y, row = 0;
            foreach (var button in buttons) {
                int preferred = Math.Max(96, TextRenderer.MeasureText(button.Text, button.Font).Width + 28);
                int height = Math.Max(38, button.Font.Height + 18);
                preferred = Math.Min(preferred, width);
                if (left > x && left + preferred > x + width) { left = x; top += row + 8; row = 0; }
                button.SetBounds(left, top, preferred, height);
                left += preferred + 8; row = Math.Max(row, height);
            }
            return top + row;
        }

        private static int Header(Panel page, int width) {
            int bottom = 34;
            if (page.Controls.Count > 0) LabelBox(page.Controls[0], 0, 0, width, 30);
            if (page.Controls.Count > 1 && page.Controls[1] is Label) {
                var label = (Label)page.Controls[1];
                int height = Math.Max(36, TextRenderer.MeasureText(label.Text, label.Font, new Size(width, 300), TextFormatFlags.WordBreak).Height + 4);
                LabelBox(label, 0, 34, width, height); bottom = 34 + height;
            }
            return bottom + 14;
        }

        private void ArrangeResponsive() {
            if (arranging || pages[0] == null) return;
            arranging = true;
            try {
                bool compact = ClientSize.Width < 880;
                int header = Math.Max(44, Font.Height + 22);
                titleBar.SetBounds(0, 0, ClientSize.Width, header);
                resizeGrip.SetBounds(ClientSize.Width - 14, ClientSize.Height - 14, 14, 14);
                resizeGrip.Visible = WindowState != FormWindowState.Maximized;
                resizeGrip.BringToFront();
                int side = compact ? 0 : 184;
                int navHeight = compact ? 54 : 0;
                sidebar.SetBounds(0, header, compact ? ClientSize.Width : side, compact ? navHeight : Math.Max(1, ClientSize.Height - header));
                contentArea.Padding = compact ? new Padding(18, 14, 18, 14) : new Padding(26, 20, 26, 20);
                contentArea.SetBounds(side, header + navHeight, Math.Max(1, ClientSize.Width - side), Math.Max(1, ClientSize.Height - header - navHeight));
                for (int i = 0; i < navButtons.Count; i++) {
                    var nav = navButtons[i]; nav.Compact = compact;
                    nav.SetBounds(compact ? 8 + i * ((sidebar.Width - 16) / 6) : 10, compact ? 8 : 16 + i * 46,
                        compact ? (sidebar.Width - 16) / 6 - 2 : side - 20, compact ? 38 : 40);
                    nav.Invalidate();
                }
                foreach (var page in pages) { page.AutoScrollPosition = Point.Empty; page.AutoScrollMinSize = Size.Empty; page.AutoScroll = false; }

                // A quiet, centered connection page with bounded cards.
                var home = pages[0];
                home.AutoScroll = home.ClientSize.Height < 632;
                home.AutoScrollMinSize = home.AutoScroll ? new Size(0, 632) : Size.Empty;
                int w = Math.Max(100, home.ClientSize.Width - (home.AutoScroll ? 20 : 4));
                int cx = w / 2, y = Math.Max(8, (home.ClientSize.Height - 600) / 2);
                LabelBox(dialAction, 0, y, w, 42);
                LabelBox(dialSubtext, 0, y + 48, w, 42);
                statusPill.SetBounds(cx - 95, y + 104, 190, 32);
                dial.SetBounds(cx - 58, y + 148, 116, 116);
                connectBtn.SetBounds(cx - 92, y + 280, 184, 44);
                var actions = home.Controls["connectionActions"];
                actions.AutoSize = false; actions.SetBounds(Math.Max(0, (w - 490) / 2), y + 338, Math.Min(w, 490), 46);
                quickBtn.SetBounds(0, 0, 138, 38); antigravityBtn.SetBounds(0, 0, 166, 38); verifyBtn.SetBounds(0, 0, 164, 38);
                selectedLocationCard.SetBounds(Math.Max(0, (w - 520) / 2), y + 400, Math.Min(520, w), 72);
                var metrics = home.Controls["connectionMetrics"];
                metrics.SetBounds(Math.Max(0, (w - 520) / 2), y + 484, Math.Min(520, w), 80);
                LabelBox(accountBadge, 18, 43, metrics.Width - 110, 24); eyeBtn.SetBounds(metrics.Width - 76, 39, 64, 28);
                LabelBox(noticeLabel, Math.Max(0, (w - 520) / 2), y + 576, Math.Min(520, w), 40);

                // Search/action rows wrap before they can overlap one another.
                foreach (int pageIndex in new[] { 1, 2 }) {
                    var page = pages[pageIndex]; int width = Math.Max(100, page.ClientSize.Width - 10); int top = Header(page, width);
                    var searchBox = pageIndex == 1 ? locationSearch : splitSearch;
                    Control[] buttons = pageIndex == 1 ? new Control[] { sortPingBtn, favoriteBtn, favoritesFilterBtn, pingBtn, applyLocationBtn } : new Control[] { splitBrowse, splitReload, splitSave };
                    int bottom;
                    if (width < 740) {
                        searchBox.SetBounds(0, top, width, 38);
                        bottom = PlaceButtons(buttons, 0, top + 48, width);
                    } else {
                        int actionWidth = 0; foreach (var button in buttons) actionWidth += Math.Max(96, TextRenderer.MeasureText(button.Text, button.Font).Width + 28) + 8;
                        int searchWidth = Math.Max(180, width - actionWidth - 12);
                        searchBox.SetBounds(0, top, searchWidth, 38);
                        bottom = PlaceButtons(buttons, searchWidth + 12, top, width - searchWidth - 12);
                    }
                    int listTop = bottom + 14;
                    if (pageIndex == 1) locationList.SetBounds(0, listTop, width, Math.Max(100, page.ClientSize.Height - listTop - 6));
                    else {
                        splitList.SetBounds(0, listTop, width, Math.Max(100, page.ClientSize.Height - listTop - 34));
                        splitList.Columns[0].Width = Math.Min(200, width / 3); splitList.Columns[2].Width = 100;
                        splitList.Columns[1].Width = Math.Max(80, splitList.ClientSize.Width - splitList.Columns[0].Width - 104);
                        LabelBox(splitCountLabel, 0, splitList.Bottom + 8, width, 22);
                    }
                }

                var accountsPage = pages[3]; int aw = Math.Max(100, accountsPage.ClientSize.Width - 10);
                int accountTop = Header(accountsPage, aw);
                int accountButtonsBottom = PlaceButtons(new Control[] { loginBtn, selectAccountBtn, refreshQuotaBtn, removeAccountBtn, accountEyeBtn }, 0, accountTop, aw);
                int accountsTop = accountButtonsBottom + 14;
                accountsList.SetBounds(0, accountsTop, aw, Math.Max(100, accountsPage.ClientSize.Height - accountsTop - 112));
                accountsList.Columns[0].Width = Math.Max(140, accountsList.ClientSize.Width - 394);
                accountsList.Columns[1].Width = 100; accountsList.Columns[2].Width = 160; accountsList.Columns[3].Width = 130;
                var info = accountsPage.Controls["accountInfo"]; info.SetBounds(0, accountsList.Bottom + 12, aw, 88);
                LabelBox(info.Controls[0], 14, 10, aw - 28, 22); LabelBox(info.Controls[1], 14, 34, aw - 28, 46);
                if (info.Bottom > accountsPage.ClientSize.Height) { accountsPage.AutoScroll = true; accountsPage.AutoScrollMinSize = new Size(0, info.Bottom + 8); }

                var settings = pages[4];
                settings.AutoScroll = settings.ClientSize.Height < 592;
                settings.AutoScrollMinSize = settings.AutoScroll ? new Size(0, 592) : Size.Empty;
                int sw = Math.Max(100, settings.ClientSize.Width - 22);
                foreach (var card in modeCards) card.Width = sw;
                if (desktopPreferences != null) desktopPreferences.SetBounds(0, 126, sw, 160);
                var trayRow = settings.Controls["trayPreference"]; trayRow.Width = sw; trayRow.Height = 60;
                LabelBox(trayRow.Controls[1], 66, 9, Math.Max(1, sw - 80), 44);
                var resources = settings.Controls["resourceButtons"]; resources.SetBounds(0, 428, sw, 84);
                settings.Controls["resourcesHeading"].Top = 402;
                var legal = settings.Controls["sourceNotice"]; LabelBox(legal, 0, 528, sw, 52);

                var diagnostics = pages[5]; int dw = Math.Max(100, diagnostics.ClientSize.Width - 10);
                LabelBox(exitInfoLabel, 0, 34, dw, 28);
                var diagRow = diagTestBtn.Parent; diagRow.SetBounds(0, 76, dw, 46);
                logsBox.SetBounds(0, 136, dw, Math.Max(100, diagnostics.ClientSize.Height - 142));
            } finally { arranging = false; }
        }

        private void InitializeTray() {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open III85III VPN", null, delegate { Show(); WindowState = FormWindowState.Normal; Activate(); });
            menu.Items.Add("Disconnect", null, async delegate { await Run(async delegate { await engine.Api("POST", "/api/power", new { on = false }); }); });
            menu.Items.Add("Quit", null, delegate { allowClose = true; Close(); });
            tray.Icon = Icon;
            tray.Text = "III85III VPN";
            tray.ContextMenuStrip = menu;
            menu.Opening += delegate { RebuildDesktopTray(menu); };
            tray.Visible = true;
            tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
        }

        private void SetActions(bool enabled) {
            dial.Enabled = enabled;
            connectBtn.Enabled = enabled;
            quickBtn.Enabled = enabled && Json.Str(Json.Child(lastStatus, "state"), "phase") == "off";
            antigravityBtn.Enabled = enabled;
            verifyBtn.Enabled = enabled && !testing;
            loginBtn.Enabled = enabled;
            refreshQuotaBtn.Enabled = enabled;
            removeAccountBtn.Enabled = enabled;
            applyLocationBtn.Enabled = enabled;
            pingBtn.Enabled = enabled;
            splitSave.Enabled = enabled;
            splitReload.Enabled = enabled;
            splitBrowse.Enabled = enabled;
        }

        private async Task Run(Func<Task> action) {
            if (!ready || busy || closing) return;
            busy = true; SetActions(false);
            try { await action(); await Poll(); }
            catch (Exception e) { dialSubtext.Text = e.Message; }
            finally { busy = false; if (!closing) SetActions(ready); }
        }

        private async Task ConnectFromHome() {
            if (!ready || busy || closing) return;
            string phase = Json.Str(Json.Child(lastStatus, "state"), "phase");
            if ((phase == "off" || phase == "waiting") && Json.Child(lastStatus, "active_account").Count == 0) {
                SwitchPage(3);
                loginBtn.Focus();
                return;
            }
            await Run(TogglePower);
        }

        private async Task TogglePower() {
            if (Json.Str(Json.Child(lastStatus, "state"), "phase") != "off") {
                await engine.Api("POST", "/api/power", new { on = false }); return;
            }
            if (!Json.Rows(await engine.Api("GET", "/api/accounts"), "accounts").GetEnumerator().MoveNext())
                throw new IOException("Add your Mozilla account on the Account tab first.");
            string mode = pendingModeIndex == 0 ? "tun" : pendingModeIndex == 1 ? "set" : "clear";
            if (mode == "tun") {
                if (!Json.Bool(lastStatus, "tun_supported")) throw new IOException("Whole-system TUN is unavailable on this computer.");
                if (!Json.Bool(Json.Child(lastStatus, "tun"), "installed")) {
                    dialSubtext.Text = "Installing TUN driver… Please accept prompt.";
                    await engine.InstallTun();
                }
            }
            await engine.Api("POST", "/api/system-proxy", new { mode = mode });
            await engine.Api("POST", "/api/power", new { on = true });
        }

        private async Task QuickConnect() {
            if (Json.Str(Json.Child(lastStatus, "state"), "phase") != "off") return;
            string chosen = ConnectionState.QuickCountry(selectedLocationCard.CountryCode);
            await engine.Api("POST", "/api/location", new { country = chosen });
            await TogglePower();
        }

        private void LoadPreferences() {
            try {
                string path = Path.Combine(engine.DataDir, "ui-prefs.json");
                if (File.Exists(path)) {
                    var prefs = Json.Decode(File.ReadAllText(path));
                    hideEmail = Json.Bool(prefs, "hide_email");
                    if (Json.Get(prefs, "sort_ping") != null) sortByPing = Json.Bool(prefs, "sort_ping");
                }
            } catch { }
            UpdatePingSort();
        }

        private void SavePreferences() {
            try {
                string path = Path.Combine(engine.DataDir, "ui-prefs.json");
                File.WriteAllText(path, Json.Encode(new Dictionary<string, object> { { "hide_email", hideEmail }, { "sort_ping", sortByPing } }));
            } catch { }
        }

        private string MaskEmail(string email) {
            if (!hideEmail || String.IsNullOrEmpty(email)) return email ?? "";
            int at = email.IndexOf('@');
            if (at <= 1) return "••••••••";
            string user = email.Substring(0, 1) + "••••";
            int dot = email.IndexOf('.', at);
            string domain = (dot > at + 1) ? "••••" + email.Substring(dot) : "••••.com";
            return user + "@" + domain;
        }

        private void ToggleEmailPrivacy() {
            hideEmail = !hideEmail;
            SavePreferences();
            UpdateEyeButtons();
            UpdateAccountBadge();
            var _ = RefreshAccounts();
        }

        private void UpdateEyeButtons() {
            eyeBtn.Text = hideEmail ? "Show" : "Hide";
            accountEyeBtn.Text = hideEmail ? "Show Email" : "Hide Email";
        }

        private void UpdateAccountBadge() {
            var acct = Json.Child(lastStatus, "active_account");
            var usage = Json.Child(acct, "usage");
            string email = Json.Str(acct, "email");
            accountBadge.Text = acct.Count == 0 ? "Add an account to see your balance" :
                MaskEmail(email) + " · " + (Json.Bool(usage, "unlimited") ? "Unlimited" : Theme.Bytes(Json.Num(usage, "remaining_bytes")) + " remaining");
        }

        private async Task AutoPingLocations(bool force = false) {
            if (!ready || closing) return;
            if (!force && (DateTime.UtcNow - lastAutoPing).TotalSeconds < 45) return;
            lastAutoPing = DateTime.UtcNow;
            pingBtn.Text = "Pinging…";
            try {
                await engine.Api("POST", "/api/locations/ping", new { codes = new string[0] }, 60000);
                await LoadLocations();
            } catch { }
            finally { pingBtn.Text = "Measure Latency"; }
        }

        private async Task ConnectAntigravity() {
            string targetCode = "gb";
            ServerChoice targetChoice = null;
            foreach (var sc in allLocations) {
                if (sc.Code.Equals("gb", StringComparison.OrdinalIgnoreCase)) {
                    targetChoice = sc;
                    break;
                }
            }
            if (targetChoice == null) {
                string[] candidates = new [] { "de", "nl", "us", "fr" };
                foreach (var c in candidates) {
                    foreach (var sc in allLocations) {
                        if (sc.Code.Equals(c, StringComparison.OrdinalIgnoreCase)) {
                            targetChoice = sc;
                            targetCode = c;
                            break;
                        }
                    }
                    if (targetChoice != null) break;
                }
            }

            await engine.Api("POST", "/api/location", new { country = targetCode });
            selectedLocationCard.LocationName = targetChoice != null ? targetChoice.Name + " (Antigravity)" : "United Kingdom (Antigravity)";
            selectedLocationCard.CountryCode = targetCode;
            selectedLocationCard.Latency = (targetChoice != null && targetChoice.PingMs > 0) ? targetChoice.PingMs + " ms" : "— ms";
            selectedLocationCard.Invalidate();

            if (Json.Str(Json.Child(lastStatus, "state"), "phase") == "off") {
                await TogglePower();
            } else {
                dialSubtext.Text = "Reconnecting to Antigravity server (" + targetCode.ToUpperInvariant() + ")…";
                await engine.Api("POST", "/api/power", new { on = false });
                await Task.Delay(350);
                await engine.Api("POST", "/api/power", new { on = true });
            }
        }

        private void ToggleAntigravityFilter() {
            antigravityFilterOnly = !antigravityFilterOnly;
            antigravityFilterBtn.Variant = antigravityFilterOnly ? ButtonVariant.Primary : ButtonVariant.Ghost;
            antigravityFilterBtn.Text = antigravityFilterOnly ? "All Locations" : "Antigravity Safe";
            FilterLocations();
        }

        private async Task Poll() {
            if (!ready || polling || closing) return;
            polling = true;
            try {
                lastStatus = await engine.Api("GET", "/api/status", null, 5000);
                SynchronizeDesktopSettings();
                var state = Json.Child(lastStatus, "state");
                var tun = Json.Child(lastStatus, "tun");
                var proxy = Json.Child(lastStatus, "proxy");
                var acct = Json.Child(lastStatus, "active_account");
                var usage = Json.Child(acct, "usage");
                var egress = Json.Child(lastStatus, "exit");
                var traffic = Json.Child(lastStatus, "traffic");

                string phase = Json.Str(state, "phase");
                bool connected = ConnectionState.Verified(lastStatus);

                timer.Interval = phase == "running" && !connected ? 500 : 2000;

                dial.State = phase == "off" ? DialState.Disconnected :
                             connected ? DialState.Connected :
                             phase == "error" ? DialState.Error : DialState.Connecting;
                dial.Invalidate();

                statusPill.State = dial.State;
                statusPill.StatusText = phase == "off" ? "Not connected" :
                                        connected ? "Connected" :
                                        phase == "running" ? "Connecting" :
                                        phase == "waiting" ? "Add an account" :
                                        phase == "error" ? "Needs attention" : "Connecting";
                statusPill.Invalidate();

                bool needsAccount = acct.Count == 0 && (phase == "off" || phase == "waiting");
                dialAction.Text = needsAccount ? "Let's get you connected." : phase == "off" ? "Ready when you are." : connected ? "You're connected." : phase == "error" ? "Let's try that again." : "Getting you connected…";
                connectBtn.Text = needsAccount ? "Get started" : phase == "off" ? "Connect" : connected ? "Disconnect" : phase == "error" ? "Reset connection" : "Cancel connection";
                dialSubtext.Text = needsAccount ? "Add your Mozilla account to get started." :
                    phase == "off" ? "Choose a location. Connect whenever you're ready." :
                    connected ? "Your connection is ready. Make yourself at home." : Json.Str(state, "message");

                // Update metrics
                speedStats.Text = "↓ " + Theme.Bytes(Json.Num(traffic, "down_rate")) + "/s     ↑ " + Theme.Bytes(Json.Num(traffic, "up_rate")) + "/s";
                UpdateAccountBadge();

                if (egress.Count > 0) exitInfoLabel.Text = "Exit IPv4: " + Json.Str(egress, "ipv4") + "  ·  " + Json.Str(egress, "country_name");
                tray.Text = "III85III VPN · " + (connected ? "Connected" : "Disconnected");

                // Stream engine logs
                if (DateTime.UtcNow >= nextLogs) {
                    nextLogs = DateTime.UtcNow.AddSeconds(4);
                    var logEntries = await engine.Api("GET", "/api/logs?after=" + lastLogId, null, 4000);
                    foreach (var entry in Json.Rows(logEntries, "entries")) {
                        long id = (long)Json.Num(entry, "id");
                        if (id > lastLogId) lastLogId = id;
                        logsBox.AppendText(Json.Str(entry, "time") + "  " + Json.Str(entry, "level") + "  " + Json.Str(entry, "message") + Environment.NewLine);
                    }
                    if (logsBox.TextLength > 90000) logsBox.Text = logsBox.Text.Substring(logsBox.TextLength - 60000);
                }

                if (!busy) SetActions(true);
                TranslateDesktop();
                if (!testing && !busy && warmup.ShouldTest(lastStatus, DateTime.UtcNow)) {
                    var _ = TestConnection(false);
                }
            } catch {
                dial.State = DialState.Disconnected;
                statusPill.State = DialState.Disconnected;
            } finally { polling = false; }
        }

        private async Task TestConnection(bool show) {
            if (!ready || testing || closing) return;
            testing = true; verifyBtn.Enabled = false;
            try {
                var result = await engine.Api("POST", "/api/test");
                if (show && !closing) {
                    bool ok = Json.Bool(result, "ok");
                    MessageBox.Show(this, ok ? "Connection verified! Exit IP: " + Json.Str(result, "exit_ipv4") : Json.Str(result, "error"), "Connection test", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
            } catch (Exception e) { if (show && !closing) MessageBox.Show(this, e.Message, "Test error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { testing = false; warmup.Finished(DateTime.UtcNow); if (!closing) verifyBtn.Enabled = ready && !busy; }
            if (!closing) await Poll();
        }

        private async Task RefreshAccounts() {
            var reply = await engine.Api("GET", "/api/accounts");
            accountsList.Items.Clear();
            foreach (var a in Json.Rows(reply, "accounts")) {
                var usage = Json.Child(a, "usage");
                var item = new ListViewItem(MaskEmail(Json.Str(a, "email"))) { Tag = Json.Str(a, "id") };
                item.SubItems.Add(Json.Str(a, "status"));
                item.SubItems.Add(usage.Count == 0 ? "Unknown" : Json.Bool(usage, "unlimited") ? "Unlimited" : Theme.Bytes(Json.Num(usage, "remaining_bytes")));
                DateTime reset;
                item.SubItems.Add(DateTime.TryParse(Json.Str(usage, "reset_at"), out reset) ? reset.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—");
                accountsList.Items.Add(item);
            }
        }

        private async Task LoadLocations() {
            var reply = await engine.Api("GET", "/api/locations", null, 15000);
            string selected = Json.Str(lastStatus, "selection");
            allLocations.Clear();
            allLocations.Add(new ServerChoice("Recommended", "") { PingMs = 0 });

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "", "fastest", "REC" };
            foreach (var l in Json.Rows(reply, "locations")) {
                string code = Json.Str(l, "code");
                if (code == "") { allLocations[0].PingMs = (int)Json.Num(l, "ping_ms"); continue; }
                if (!seen.Add(code)) continue;
                string name = Json.Str(l, "name");
                if (name == "") name = code;
                allLocations.Add(new ServerChoice(name, code) { PingMs = (int)Json.Num(l, "ping_ms") });
            }
            FilterLocations();

            // Sync with selected card
            foreach (var sc in allLocations) {
                if (sc.Code.Equals(selected, StringComparison.OrdinalIgnoreCase)) {
                    selectedLocationCard.LocationName = sc.Name;
                    selectedLocationCard.CountryCode = sc.Code;
                    selectedLocationCard.Latency = sc.PingMs > 0 ? sc.PingMs + " ms" : "— ms";
                    selectedLocationCard.Invalidate();
                    break;
                }
            }
        }

        private void FilterLocations() {
            string q = locationSearch.Input.Text.Trim();
            var agSafe = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gb", "de", "nl", "us", "fr", "ca", "se", "ch", "at" };
            var selected = locationList.SelectedItem as ServerChoice;
            string selectedCode = selected == null ? null : selected.Code;
            var visible = new List<ServerChoice>();
            foreach (var sc in allLocations) {
                if (showFavoritesOnly && !favoriteCodes.Contains(LocationCode(sc.Code))) continue;
                if (antigravityFilterOnly && sc.Code != "" && !agSafe.Contains(sc.Code)) continue;
                if (q == "" || sc.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || sc.Code.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) visible.Add(sc);
            }
            if (sortByPing) visible.Sort(delegate(ServerChoice a, ServerChoice b) {
                int result = (a.PingMs > 0 ? a.PingMs : Int32.MaxValue).CompareTo(b.PingMs > 0 ? b.PingMs : Int32.MaxValue);
                if (result != 0) return result;
                result = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
                return result != 0 ? result : StringComparer.OrdinalIgnoreCase.Compare(a.Code, b.Code);
            });
            locationList.BeginUpdate();
            try {
                locationList.Items.Clear();
                foreach (var sc in visible) {
                    int index = locationList.Items.Add(sc);
                    if (selectedCode != null && sc.Code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase)) locationList.SelectedIndex = index;
                }
                if (locationList.Items.Count > 0 && locationList.SelectedIndex < 0) locationList.SelectedIndex = 0;
            } finally { locationList.EndUpdate(); }
        }

        private void UpdatePingSort() {
            sortPingBtn.Text = sortByPing ? "Ping: low first" : "Sort by ping";
            sortPingBtn.Variant = sortByPing ? ButtonVariant.Primary : ButtonVariant.Secondary;
            sortPingBtn.Invalidate();
        }

        private async Task ApplySelectedLocation() {
            if (locationList.SelectedItem == null) return;
            var chosen = (ServerChoice)locationList.SelectedItem;
            await engine.Api("POST", "/api/location", new { country = chosen.Code });
            selectedLocationCard.LocationName = chosen.Name;
            selectedLocationCard.CountryCode = chosen.Code;
            selectedLocationCard.Latency = chosen.PingMs > 0 ? chosen.PingMs + " ms" : "— ms";
            selectedLocationCard.Invalidate();
            SwitchPage(0);
        }

        private async Task LoadSplitApps() {
            if (!Json.Bool(Json.Child(lastStatus, "tun"), "programs_supported")) throw new IOException("Application routing is unavailable on this system.");
            if (!splitLoaded) {
                var config = Json.Child(await engine.Api("GET", "/api/settings"), "config");
                foreach (var pair in SplitRules.Read(config)) splitSelected[pair.Key] = pair.Value;
            }
            var running = await engine.Api("GET", "/api/tun/programs");
            splitAvailable.Clear();
            foreach (var row in Json.Rows(running, "programs")) {
                string name = SplitRules.CleanName(Json.Str(row, "name"));
                if (name != "") splitAvailable[name] = new AppBypass(name, Json.Str(row, "path"));
            }
            foreach (var pair in splitSelected) if (!splitAvailable.ContainsKey(pair.Key)) splitAvailable[pair.Key] = pair.Value;
            splitLoaded = true;
            RenderSplitApps();
            UpdateSplitSummary();
        }

        private void RenderSplitApps() {
            splitRendering = true;
            splitList.BeginUpdate();
            try {
                splitList.Items.Clear();
                var names = new List<string>(splitAvailable.Keys);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                string q = splitSearch.Input.Text.Trim();
                foreach (string name in names) {
                    var rule = splitAvailable[name];
                    if (q != "" && (name + " " + rule.Path).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    AppBypass selected;
                    bool check = splitSelected.TryGetValue(name, out selected);
                    var item = new ListViewItem(name) { Tag = rule, Checked = check };
                    item.SubItems.Add(rule.Path);
                    item.SubItems.Add(check ? (Theme.Persian ? "وی‌پی‌ان" : "VPN") : (Theme.Persian ? "مستقیم" : "Direct"));
                    splitList.Items.Add(item);
                }
            } finally { splitList.EndUpdate(); splitRendering = false; }
        }

        private void UpdateSplitSummary() {
            splitCountLabel.Text = splitSelected.Count + " applications selected to use VPN. All other apps stay direct.";
        }

        private async Task SaveSplitApps() {
            var config = Json.Child(await engine.Api("GET", "/api/settings"), "config");
            SplitRules.Apply(config, splitSelected.Values);
            await engine.Api("PUT", "/api/settings", new { config = config });
            var saved = SplitRules.Read(Json.Child(await engine.Api("GET", "/api/settings"), "config"));
            if (saved.Count != splitSelected.Count) throw new IOException("Engine did not retain all application rules.");
            splitCountLabel.Text = "Saved: " + saved.Count + " applications use VPN. All others stay direct.";
        }

        private void BrowseApp() {
            using (var picker = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true, Multiselect = true, Title = "Select applications to use VPN" }) {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                foreach (var path in picker.FileNames) {
                    string name = Path.GetFileName(path);
                    var rule = new AppBypass(name, path);
                    splitAvailable[name] = rule;
                    splitSelected[name] = rule;
                }
                RenderSplitApps();
                UpdateSplitSummary();
            }
        }
    }

    internal sealed class ServerChoice {
        public string Name, Code;
        public int PingMs;
        public ServerChoice(string name, string code) { Name = name; Code = code; }
        public override string ToString() { return Name; }
    }

    internal static class Program {
        [STAThread]
        static int Main(string[] args) {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string root = AppDomain.CurrentDomain.BaseDirectory;
            bool smoke = Array.IndexOf(args, "--smoke-test") >= 0;
            bool preview = Array.IndexOf(args, "--preview") >= 0;
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "III85III VPN Independent");
            bool isolated = false;
            foreach (string arg in args) {
                if (arg.StartsWith("--data-dir=", StringComparison.Ordinal)) {
                    data = Path.GetFullPath(arg.Substring(11));
                    isolated = true;
                }
            }
            string mutexName = "Local\\III85III-VPN-" + WindowsIdentity.GetCurrent().User.Value;
            if (isolated) using (var hash = SHA256.Create()) mutexName += "-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(data.ToLowerInvariant()))).Replace("-", "").Substring(0, 16);

            bool created;
            using (var mutex = new Mutex(true, mutexName, out created)) {
                if (!created) {
                    if (!smoke) MessageBox.Show("III85III VPN is already running. Access it from the notification tray.", "III85III VPN");
                    return 2;
                }
                try {
                    using (var engine = new Engine(root, data)) {
                        if (smoke) { Smoke(engine).GetAwaiter().GetResult(); return 0; }
                        var form = new MainForm(engine, preview);
                        if (preview) form.ConfigurePreview(Array.IndexOf(args,"--language=fa")>=0,Array.IndexOf(args,"--theme=light")>=0);
                        if (isolated) form.Text += " · Preview";
                        Application.Run(form);
                        return 0;
                    }
                } catch (Exception e) {
                    if (smoke) Console.Error.WriteLine(e.ToString());
                    else MessageBox.Show(e.Message, "III85III VPN", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
        }

        static async Task Smoke(Engine engine) {
            Exception failure = null;
            string resultPath = Path.Combine(engine.DataDir, "smoke-result.json");
            if (File.Exists(resultPath)) File.Delete(resultPath);
            try {
                await engine.Start();
                var state = await engine.Api("GET", "/api/status");
                if (Json.Str(Json.Child(state, "state"), "phase") != "off") throw new Exception("Engine unexpectedly started network routing.");
                if (!Json.Bool(state, "tun_supported")) throw new Exception("TUN support missing.");
                var accounts = await engine.Api("GET", "/api/accounts");
                foreach (var a in Json.Rows(accounts, "accounts")) throw new Exception("Smoke-test data must have no accounts.");
                var settings = await engine.Api("GET", "/api/settings");
                if (!Json.Str(Json.Child(Json.Child(settings, "config"), "dashboard"), "listen").StartsWith("127.0.0.1:", StringComparison.Ordinal)) throw new Exception("Dashboard is not loopback-only.");
                using (var bare = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }))
                using (var denied = await bare.GetAsync(engine.Endpoint + "/api/status")) {
                    if (denied.StatusCode != HttpStatusCode.Unauthorized) throw new Exception("Unauthenticated API access was not rejected.");
                }
                var programs = await engine.Api("GET", "/api/tun/programs");
                if (Json.Get(programs, "programs") == null) throw new Exception("Running app inventory is unavailable.");
                var original = Json.Child(settings, "config");
                string transport = Json.Str(Json.Child(original, "proxy"), "transport");
                var originalRules = SplitRules.Read(original);
                SplitRules.Apply(original, new[] { new AppBypass("iii85iii-smoke-app.exe", "") });
                await engine.Api("PUT", "/api/settings", new { config = original });
                var applied = Json.Child(await engine.Api("GET", "/api/settings"), "config");
                if (!SplitRules.Read(applied).ContainsKey("iii85iii-smoke-app.exe")) throw new Exception("Split rule did not persist in real engine.");
                if (Json.Str(Json.Child(applied, "proxy"), "transport") != transport) throw new Exception("Split save changed the transport.");
                SplitRules.Apply(applied, originalRules.Values);
                await engine.Api("PUT", "/api/settings", new { config = applied });
                var report = new { passed = true, engine_version = Json.Str(state, "version"), state = "off", tun_supported = true, isolated_empty_accounts = true, loopback_only = true, unauthenticated_api_rejected = true, split_rule_roundtrip = true, app_inventory = true, network_connection_tested = false };
                File.WriteAllText(Path.Combine(engine.DataDir, "smoke-result.json"), Json.Encode(report));
            } catch (Exception e) { failure = e; }
            await engine.Stop();
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
