using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace III85III.Vpn {
    internal sealed class SignInForm : Form {
        readonly Engine engine;
        readonly LuxuryButton tabPassword = new LuxuryButton { Text = "🔑 Email & Password", Variant = ButtonVariant.Primary };
        readonly LuxuryButton tabBrowser = new LuxuryButton { Text = "🌐 Browser & Google", Variant = ButtonVariant.Secondary };
        readonly Panel panelPassword = new Panel { BackColor = Theme.Bg };
        readonly Panel panelBrowser = new Panel { BackColor = Theme.Bg, Visible = false };
        readonly TextBox email = new TextBox(), password = new TextBox();
        readonly LuxuryButton passwordVisibility = new LuxuryButton { Text = "Show", Variant = ButtonVariant.Ghost, AccessibleName = "Show password" };
        readonly Label note = new Label(), feedback = new Label();
        readonly LuxuryButton submit = new LuxuryButton { Text = "Sign in", Variant = ButtonVariant.Primary }, cancel = new LuxuryButton { Text = "Cancel", Variant = ButtonVariant.Secondary };
        readonly TextBox browserEmail = new TextBox(), browserToken = new TextBox();
        readonly LuxuryButton openBrowserBtn = new LuxuryButton { Text = "🌐 Open Firefox Accounts in Browser", Variant = ButtonVariant.Primary };
        readonly LuxuryButton linkAccountBtn = new LuxuryButton { Text = "Link Account", Variant = ButtonVariant.Primary };
        readonly LuxuryButton browserCancel = new LuxuryButton { Text = "Cancel", Variant = ButtonVariant.Secondary };
        readonly Label browserNote = new Label(), browserFeedback = new Label();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1200 };
        string session = ""; bool polling, closing, finished, submitting, browserSubmitting;

        public SignInForm(Engine engine) {
            this.engine = engine;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.TextPrimary;
            Size = new Size(500, Math.Min(540, Screen.PrimaryScreen.WorkingArea.Height - 32));
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var titleBar = new LuxuryTitleBar(this, false);
            Controls.Add(titleBar);

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 54, 28, 20), AutoScroll = true };
            Controls.Add(panel);
            titleBar.BringToFront();

            var title = new Label { Text = "Add Your Mozilla Account", Font = Theme.Display(18f), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(28, 54) };
            panel.Controls.Add(title);

            tabPassword.SetBounds(28, 90, 212, 34);
            tabPassword.Font = Theme.Font(9f, FontStyle.Bold);
            tabBrowser.SetBounds(244, 90, 212, 34);
            tabBrowser.Font = Theme.Font(9f, FontStyle.Bold);
            panel.Controls.Add(tabPassword);
            panel.Controls.Add(tabBrowser);

            tabPassword.Click += delegate { SwitchTab(true); };
            tabBrowser.Click += delegate { SwitchTab(false); };

            panelPassword.SetBounds(28, 132, 436, 370);
            panel.Controls.Add(panelPassword);

            panelBrowser.SetBounds(28, 132, 436, 370);
            panel.Controls.Add(panelBrowser);

            // --- Password Panel Layout ---
            note.Text = "Use your Mozilla email and password. If a code is needed, we'll ask for it next.";
            note.Font = Theme.Font(9f);
            note.ForeColor = Theme.TextSecondary;
            note.Size = new Size(424, 36);
            note.Location = new Point(0, 4);
            panelPassword.Controls.Add(note);

            int yPass = 48;
            AddInputField(panelPassword, "Email Address", email, ref yPass, false);
            AddInputField(panelPassword, "Password", password, ref yPass, true);

            feedback.Font = Theme.Font(8.5f);
            feedback.ForeColor = Theme.Accent;
            feedback.Size = new Size(424, 38);
            feedback.Location = new Point(0, yPass);
            panelPassword.Controls.Add(feedback);
            yPass += 42;

            submit.Size = new Size(130, 38); submit.Location = new Point(0, yPass);
            cancel.Size = new Size(110, 38); cancel.Location = new Point(140, yPass);
            panelPassword.Controls.Add(submit); panelPassword.Controls.Add(cancel);

            submit.Click += async delegate { await Submit(); };
            cancel.Click += delegate { Close(); };
            passwordVisibility.Click += delegate { SetPasswordVisible(password.UseSystemPasswordChar); };

            AcceptButton = submit; CancelButton = cancel;
            Shown += delegate { email.Focus(); };

            timer.Tick += async delegate {
                if (polling || closing || session == "") return;
                polling = true;
                try { Apply(await engine.Api("GET", "/api/signin/" + session)); }
                catch (Exception e) { if (!closing && !IsDisposed) feedback.Text = e.Message; }
                finally { polling = false; }
            };

            FormClosing += async delegate (object sender, FormClosingEventArgs e) {
                if (closing || finished) return;
                closing = true; timer.Stop();
                if (session == "") return;
                e.Cancel = true;
                string cancelledSession = session; session = "";
                await CancelSession(cancelledSession);
                if (!IsDisposed) Close();
            };
            FormClosed += delegate { closing = true; timer.Stop(); timer.Dispose(); SetPasswordVisible(false); password.Clear(); };
        }

        bool browserFieldsAdded;
        void EnsureBrowserFields() {
            if (browserFieldsAdded) return;
            browserFieldsAdded = true;

            browserNote.Text = "Sign in to Firefox Accounts in your regular browser. You can use 'Continue with Google', Apple, or saved logins with no unusual sign-in security blocks.";
            browserNote.Font = Theme.Font(9f);
            browserNote.ForeColor = Theme.TextSecondary;
            browserNote.Size = new Size(424, 48);
            browserNote.Location = new Point(0, 4);
            panelBrowser.Controls.Add(browserNote);

            openBrowserBtn.SetBounds(0, 56, 424, 38);
            openBrowserBtn.Click += delegate {
                try {
                    Process.Start(new ProcessStartInfo("https://accounts.firefox.com/signin?context=fx_desktop_v3&entrypoint=vpn") { UseShellExecute = true });
                    browserFeedback.Text = "Browser opened. Sign in with Google or Firefox, then enter your details below.";
                } catch (Exception ex) {
                    browserFeedback.Text = "Could not open browser: " + ex.Message;
                }
            };
            panelBrowser.Controls.Add(openBrowserBtn);

            int yBrow = 104;
            AddInputField(panelBrowser, "Account Email / Gmail", browserEmail, ref yBrow, false);
            AddInputField(panelBrowser, "Account Token or Quick Link", browserToken, ref yBrow, false);

            browserFeedback.Font = Theme.Font(8.5f);
            browserFeedback.ForeColor = Theme.Accent;
            browserFeedback.Size = new Size(424, 34);
            browserFeedback.Location = new Point(0, yBrow);
            panelBrowser.Controls.Add(browserFeedback);
            yBrow += 38;

            linkAccountBtn.Size = new Size(140, 38); linkAccountBtn.Location = new Point(0, yBrow);
            browserCancel.Size = new Size(110, 38); browserCancel.Location = new Point(150, yBrow);
            panelBrowser.Controls.Add(linkAccountBtn); panelBrowser.Controls.Add(browserCancel);

            linkAccountBtn.Click += async delegate { await SubmitBrowserLink(); };
            browserCancel.Click += delegate { Close(); };
        }

        void SwitchTab(bool passwordTab) {
            if (!passwordTab) EnsureBrowserFields();
            panelPassword.Visible = passwordTab;
            panelBrowser.Visible = !passwordTab;
            tabPassword.Variant = passwordTab ? ButtonVariant.Primary : ButtonVariant.Secondary;
            tabBrowser.Variant = !passwordTab ? ButtonVariant.Primary : ButtonVariant.Secondary;
            tabPassword.Invalidate();
            tabBrowser.Invalidate();
            AcceptButton = passwordTab ? submit : linkAccountBtn;
            if (passwordTab) email.Focus(); else browserEmail.Focus();
        }

        async Task SubmitBrowserLink() {
            if (browserSubmitting || closing || IsDisposed) return;
            browserSubmitting = true;
            linkAccountBtn.Enabled = false;
            browserFeedback.Text = "Linking account\u2026";
            try {
                string mail = browserEmail.Text.Trim(), token = browserToken.Text.Trim();
                if (mail == "") throw new IOException("Enter your Mozilla account email.");
                if (token == "") throw new IOException("Paste your account token or refresh token from your browser.");
                var reply = await engine.Api("POST", "/api/accounts/import", new { email = mail, refresh_token = token });
                finished = true;
                DialogResult = DialogResult.OK;
                Close();
            } catch (Exception ex) {
                if (!closing && !IsDisposed) {
                    browserFeedback.Text = ex.Message;
                    linkAccountBtn.Enabled = true;
                }
            } finally {
                browserSubmitting = false;
            }
        }

        private void AddInputField(Panel p, string labelText, TextBox tb, ref int y, bool isPassword) {
            var lbl = new Label { Text = labelText, Font = Theme.Font(8.5f, FontStyle.Bold), ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(0, y) };
            p.Controls.Add(lbl);
            y += 20;

            var box = new Panel { Size = new Size(424, 36), Location = new Point(0, y), BackColor = Theme.Surface };
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
            tb.AccessibleName = isPassword ? "Mozilla password" : "Email address";
            tb.AccessibleDescription = isPassword ? "The password for your Mozilla account." : "Your Mozilla account email address.";
            tb.TabIndex = 0;
            tb.Location = new Point(10, 8);
            tb.Size = new Size(404, 20);
            tb.GotFocus += delegate { box.Invalidate(); };
            tb.LostFocus += delegate { box.Invalidate(); };

            box.Controls.Add(tb);
            if (isPassword) {
                tb.Width = box.Width - 92;
                passwordVisibility.SetBounds(box.Width - 72, 3, 64, 30);
                passwordVisibility.Font = Theme.Font(8.5f);
                passwordVisibility.TabIndex = 1;
                box.Controls.Add(passwordVisibility);
            }
            p.Controls.Add(box);
            y += 46;
        }

        protected override CreateParams CreateParams {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; }
        }

        void SetPasswordVisible(bool visible) {
            if (IsDisposed || password.IsDisposed || passwordVisibility.IsDisposed) return;
            int start = password.SelectionStart, length = password.SelectionLength;
            password.UseSystemPasswordChar = !visible;
            password.Select(start, length);
            passwordVisibility.Text = visible ? "Hide" : "Show";
            passwordVisibility.AccessibleName = visible ? "Hide password" : "Show password";
        }

        async Task Submit() {
            if (submitting || closing || IsDisposed || session != "") return;
            submitting = true;
            SetPasswordVisible(false);
            passwordVisibility.Enabled = false;
            submit.Enabled = false;
            try {
                if (email.Text.Trim() == "" || password.Text == "") throw new IOException("Enter your Mozilla email and password.");
                var reply = await engine.Api("POST", "/api/signin", new { email = email.Text.Trim(), password = password.Text });
                string returnedSession = Json.Str(reply, "id");
                if (closing || IsDisposed) {
                    await CancelSession(returnedSession);
                    return;
                }
                password.Clear();
                session = returnedSession; if (session == "") throw new IOException("Sign-in did not return a session.");
                email.Enabled = password.Enabled = false; timer.Start(); Apply(reply);
            } catch (Exception e) { if (!closing && !IsDisposed) { feedback.Text = e.Message; submit.Enabled = true; } }
            finally {
                submitting = false;
                if (!closing && !IsDisposed) passwordVisibility.Enabled = session == "" && password.Enabled;
            }
        }

        async Task CancelSession(string id) {
            if (String.IsNullOrEmpty(id)) return;
            try { await engine.Api("DELETE", "/api/signin/" + id, null, 3000); } catch { }
        }

        void Apply(Dictionary<string, object> state) {
            if (closing || IsDisposed) return;
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
}

