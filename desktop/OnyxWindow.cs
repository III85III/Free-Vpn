using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Onyx.Vpn {
    internal sealed partial class MainForm {
        const string OnyxOrigin = "https://onyx.local/index.html";
        WebView2 onyxView;
        bool onyxPreview;
        bool onyxLoaded;
        bool onyxActionRunning;
        string lastOnyxState;
        object[] onyxApps = new object[0];
        object[] onyxAccounts = new object[0];

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        internal void UseOnyx(bool preview) {
            onyxPreview = preview;
            Text = "ONYX // VPN";
            var iconPath = Path.Combine(engine.Root, "assets", "brand", "onyx.ico");
            if (File.Exists(iconPath)) Icon = new Icon(iconPath);

            // Configure Onyx window dimensions and hide legacy WinForms UI immediately to avoid startup flicker
            MinimumSize = new Size(380, 520);
            Size = new Size(460, 650);
            CenterToScreen();
            BackColor = Color.FromArgb(6, 7, 10);
            if (titleBar != null) titleBar.Visible = false;
            if (sidebar != null) sidebar.Visible = false;
            if (contentArea != null) contentArea.Visible = false;

            onyxView = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(6, 7, 10), Visible = true };
            Controls.Add(onyxView);
            onyxView.BringToFront();

            var stateTimer = new Timer { Interval = 600 };
            stateTimer.Tick += delegate { PublishOnyxState(); };
            FormClosed += delegate { stateTimer.Dispose(); };
            Shown += async delegate {
                try {
                    var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ONYX", "WebView");
                    var environment = await CoreWebView2Environment.CreateAsync(null, cache);
                    if (IsDisposed) return;
                    await onyxView.EnsureCoreWebView2Async(environment);
                    var web = onyxView.CoreWebView2;
                    web.Settings.AreDevToolsEnabled = false;
                    web.Settings.AreDefaultContextMenusEnabled = false;
                    web.Settings.IsStatusBarEnabled = false;
                    web.SetVirtualHostNameToFolderMapping("onyx.local", Path.Combine(engine.Root, "interface"), CoreWebView2HostResourceAccessKind.Deny);
                    web.NavigationStarting += (sender, args) => { args.Cancel = args.Uri != OnyxOrigin; };
                    web.NewWindowRequested += (sender, args) => { args.Handled = true; };
                    web.WebMessageReceived += HandleOnyxAction;
                    web.NavigationCompleted += async (sender, args) => {
                        if (!args.IsSuccess || IsDisposed) return;
                        onyxLoaded = true;
                        ShowOnyx();
                        PublishOnyxState();
                        stateTimer.Start();
                        if (preview) {
                            var rendered = await web.ExecuteScriptAsync("(() => { let ok = Boolean(window.OnyxVPN && !document.getElementById('desktop-tools') && Array.from(document.styleSheets).some(sheet => sheet.href && sheet.href.endsWith('/tailwind.css'))); for (const button of document.querySelectorAll('[data-drawer]')) { button.click(); ok = ok && document.getElementById(button.dataset.drawer).classList.contains('open'); button.click(); } return ok; })()");
                            File.WriteAllText(Path.Combine(Path.GetTempPath(), "ONYX preview result.txt"), rendered);
                            using (var image = File.Create(Path.Combine(Path.GetTempPath(), "ONYX preview.png"))) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                        }
                    };
                    web.Navigate(OnyxOrigin);
                } catch (Exception) {
                    onyxView.Visible = false;
                    if (titleBar != null) titleBar.Visible = true;
                    if (sidebar != null) sidebar.Visible = true;
                    if (contentArea != null) contentArea.Visible = true;
                    MinimumSize = new Size(740, 540);
                    Size = new Size(840, 670);
                    CenterToScreen();
                }
            };
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs args) {
                if (!args.Control || args.KeyCode != Keys.O) return;
                ShowOnyx();
                args.Handled = true;
            };
        }

        void ShowOnyx() {
            if (!onyxLoaded) return;
            MinimumSize = new Size(380, 520);
            Size = new Size(460, 650);
            onyxView.Visible = true;
            onyxView.BringToFront();
            if (titleBar != null) titleBar.Visible = false;
            if (sidebar != null) sidebar.Visible = false;
            if (contentArea != null) contentArea.Visible = false;
        }

        void OpenDesktopControls(int page) {
            if (!onyxLoaded) return;
            var drawer = page == 3 ? "drawer-account" : page == 2 ? "drawer-apps" : "drawer-shield";
            onyxView.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-drawer=\"" + drawer + "\"]').click()");
        }

        void PublishOnyxState() {
            if (!onyxLoaded || !onyxView.Visible || IsDisposed) return;
            var traffic = Json.Child(lastStatus, "traffic");
            var snapshot = Json.Child(lastStatus, "state");
            var egress = Json.Child(lastStatus, "exit");
            var failure = lastActionError != "" ? lastActionError : ConnectionState.Failure(lastStatus);
            var phase = Json.Str(snapshot, "phase");
            var mode = Json.Str(lastStatus, "system_proxy");
            var state = ConnectionState.Verified(lastStatus) ? "connected" : failure != "" ? "error" : phase == "off" || phase == "" ? "disconnected" : "connecting";
            var payload = Json.Encode(new {
                state = state, preview = onyxPreview, ready = ready, failure = state == "error" ? "Could not connect. Open Settings for details." : "",
                mode = mode == "tun" ? "whole" : mode == "off" ? "local" : mode == "" ? "" : "proxy",
                server = selectedLocationCard.LocationName, ping = selectedLocationCard.Latency,
                download = Theme.Bytes(Json.Num(traffic, "down_rate")) + "/s", upload = Theme.Bytes(Json.Num(traffic, "up_rate")) + "/s",
                totalDl = Theme.Bytes(Json.Num(traffic, "down_total")), totalUl = Theme.Bytes(Json.Num(traffic, "up_total")),
                exitIp = Json.Str(egress, "ip"), account = accountCard.QuotaText, apps = onyxApps, accounts = onyxAccounts,
                servers = allLocations.Select(location => new { name = location.Name, code = location.Code, ping = location.PingMs }).ToArray()
            });
            if (payload == lastOnyxState) return;
            lastOnyxState = payload;
            onyxView.CoreWebView2.PostWebMessageAsJson(payload);
        }

        async void HandleOnyxAction(object sender, CoreWebView2WebMessageReceivedEventArgs args) {
            if (args.Source != OnyxOrigin || !onyxView.Visible || args.WebMessageAsJson.Length > 16384) return;
            try {
                var message = Json.Decode(args.WebMessageAsJson);
                var action = Json.Str(message, "action");
                if (action == "MINIMIZE") { WindowState = FormWindowState.Minimized; return; }
                if (action == "CLOSE") { Close(); return; }
                if (action == "DRAG_WINDOW") { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); return; }
                if (action == "SETTINGS") { OpenDesktopControls(4); return; }
                if (action == "ACCOUNT") { OpenDesktopControls(3); return; }
                if (action == "APP_RULES") { OpenDesktopControls(2); return; }
                if (!ready || onyxPreview || onyxActionRunning) return;
                onyxActionRunning = true;
                try {
                    switch (action) {
                        case "REPAIR_VPN":
                            if (ConfirmRepair()) await Run(async delegate {
                                await engine.Api("POST", "/api/power", new { on = false });
                                await engine.InstallTun();
                                onyxView.CoreWebView2.PostWebMessageAsJson(Json.Encode(new { notice = "VPN is ready. Try connecting again." }));
                            });
                            break;
                        case "REFRESH_CATALOGS":
                            await RefreshOnyxCatalogs();
                            break;
                        case "ADD_ACCOUNT":
                            using (var signIn = new SignInForm(engine)) { signIn.ShowDialog(this); }
                            await RefreshAccounts();
                            await RefreshOnyxCatalogs();
                            break;
                        case "SWITCH_ACCOUNT":
                            var available = await engine.Api("GET", "/api/accounts");
                            var chosenAccount = Json.Rows(available, "accounts").FirstOrDefault(account => Json.Str(account, "email") == Json.Str(message, "email"));
                            if (chosenAccount != null) await engine.Api("POST", "/api/accounts/" + Uri.EscapeDataString(Json.Str(chosenAccount, "id")) + "/activate");
                            await RefreshOnyxCatalogs();
                            break;
                        case "ADD_APP_DIALOG":
                            BrowseApp();
                            onyxApps = splitAvailable.Values.Concat(splitSelected.Values).GroupBy(app => app.Name, StringComparer.OrdinalIgnoreCase).Select(group => new { name = group.First().Name, running = splitAvailable.ContainsKey(group.First().Name), bypass = splitSelected.ContainsKey(group.First().Name) }).Cast<object>().ToArray();
                            break;
                        case "SAVE_APP_RULES":
                            var rules = Json.Rows(message, "rules").ToArray();
                            await Run(async delegate {
                                var config = Json.Child(await engine.Api("GET", "/api/settings"), "config");
                                var saved = SplitRules.Read(config);
                                foreach (var rule in rules) {
                                    var ruleName = SplitRules.CleanName(Json.Str(rule, "name"));
                                    if (ruleName == "") continue;
                                    if (!Json.Bool(rule, "bypass")) saved.Remove(ruleName);
                                    else if (!saved.ContainsKey(ruleName)) saved[ruleName] = new AppBypass(ruleName, "");
                                }
                                SplitRules.Apply(config, saved.Values);
                                await engine.Api("PUT", "/api/settings", new { config = config });
                                await RefreshOnyxCatalogs();
                                onyxView.CoreWebView2.PostWebMessageAsJson(Json.Encode(new { notice = "App rules saved" }));
                            });
                            break;
                        case "CONNECT": await ConnectFromHome(); break;
                        case "DISCONNECT": if (Json.Str(Json.Child(lastStatus, "state"), "phase") != "off") await TogglePower(); break;
                        case "TEST_PING": await MeasureLocationLatency(); break;
                        case "TELEGRAM_PROXY": await ShowTelegramProxyDialog(); break;
                        case "SET_CONNECTION_MODE":
                            var mode = Json.Str(message, "mode");
                            if (mode == "whole" || mode == "proxy" || mode == "local") await ChangeCoverage(mode == "whole" ? 0 : mode == "proxy" ? 1 : 2);
                            break;
                        case "SELECT_SERVER":
                            var name = Json.Str(message, "server");
                            var server = allLocations.FirstOrDefault(location => location.Name == name);
                            if (server == null) break;
                            await Run(async delegate {
                                await engine.Api("POST", "/api/location", new { country = server.Code });
                                selectedLocationCard.LocationName = server.Name;
                                selectedLocationCard.CountryCode = server.Code;
                                selectedLocationCard.Latency = server.PingMs > 0 ? server.PingMs + " ms" : "-- ms";
                                selectedLocationCard.Invalidate();
                            });
                            break;
                    }
                } finally {
                    onyxActionRunning = false;
                    PublishOnyxState();
                }
            } catch (Exception ex) { ShowFailure(ex.Message); }
        }

        async System.Threading.Tasks.Task RefreshOnyxCatalogs() {
            var accounts = await engine.Api("GET", "/api/accounts");
            onyxAccounts = Json.Rows(accounts, "accounts").Select(account => {
                var usage = Json.Child(account, "usage");
                return (object)new { email = Json.Str(account, "email"), plan = usage.Count == 0 ? "Quota unavailable" : Json.Bool(usage, "unlimited") ? "Unlimited" : Theme.Bytes(Json.Num(usage, "remaining_bytes")) + " remaining" };
            }).ToArray();
            var config = Json.Child(await engine.Api("GET", "/api/settings"), "config");
            var rules = SplitRules.Read(config);
            var programs = await engine.Api("GET", "/api/tun/programs");
            var running = Json.Rows(programs, "programs").Select(program => Json.Str(program, "name")).Where(name => name != "").ToArray();
            onyxApps = running.Concat(rules.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Select(name => (object)new { name = name, running = running.Contains(name, StringComparer.OrdinalIgnoreCase), bypass = rules.ContainsKey(name) }).ToArray();
        }
    }
}
