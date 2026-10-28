using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace III85III.Vpn {
    internal sealed class SignInForm : Form {
        readonly Engine engine;
        readonly TextBox email = new TextBox(), password = new TextBox();
        readonly Label note = new Label(), feedback = new Label();
        readonly LuxuryButton submit = new LuxuryButton { Text = "Sign in", Variant = ButtonVariant.Primary }, cancel = new LuxuryButton { Text = "Cancel", Variant = ButtonVariant.Secondary };
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1200 };
        string session = ""; bool polling, closing, finished, submitting;

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
            FormClosed += delegate { closing = true; timer.Stop(); timer.Dispose(); password.Clear(); };
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
            if (submitting || closing || IsDisposed || session != "") return;
            submitting = true;
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
            finally { submitting = false; }
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

