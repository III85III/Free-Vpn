using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace Onyx.Vpn {
    internal sealed class VerificationForm : Form {
        readonly Engine engine; readonly string session;
        readonly TextBox code=new TextBox();
        readonly Label heading=new Label(),message=new Label(),feedback=new Label();
        readonly PictureBox challenge=new PictureBox{Visible=false,SizeMode=PictureBoxSizeMode.Zoom};
        readonly LuxuryButton verify=new LuxuryButton{Text="Verify",Variant=ButtonVariant.Primary};
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=1500};
        bool submitting,polling,closed; string step="",promptStamp="",answeredStamp="";
        internal VerificationForm(Engine engine,string session,Dictionary<string,object> state){
            this.engine=engine;this.session=session;Text="Verify your Mozilla account";AutoScaleMode=AutoScaleMode.None;
            promptStamp=Json.Str(state,"updated");
            FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(560,570);BackColor=Theme.Bg;ForeColor=Theme.TextPrimary;
            try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
            Controls.Add(new LuxuryTitleBar(this,false));
            heading.SetBounds(28,58,404,35);heading.Font=Theme.Display(18);heading.ForeColor=Theme.TextPrimary;
            message.SetBounds(28,102,404,58);message.Font=Theme.Font(10);message.ForeColor=Theme.TextSecondary;
            challenge.SetBounds(28,166,504,210);
            challenge.BackColor=Color.White;
            challenge.Cursor=Cursors.Hand;
            challenge.AccessibleName="Verification image. Click to enlarge.";
            challenge.Click+=delegate{
                if(challenge.Image==null)return;
                using(var zoom=new Form{Text="Verification image",StartPosition=FormStartPosition.CenterParent,
                    Size=new Size(Math.Min(960,Screen.FromControl(this).WorkingArea.Width-40),
                        Math.Min(640,Screen.FromControl(this).WorkingArea.Height-40)),BackColor=Color.White}){
                    zoom.Controls.Add(new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Image=challenge.Image});
                    zoom.ShowDialog(this);
                }
            };
            code.SetBounds(28,222,404,36);code.Font=Theme.Font(16);code.BackColor=Theme.Surface;code.ForeColor=Theme.TextPrimary;code.BorderStyle=BorderStyle.FixedSingle;code.MaxLength=64;code.AccessibleName="Verification code";code.TextAlign=HorizontalAlignment.Center;code.RightToLeft=RightToLeft.No;
            feedback.SetBounds(28,268,404,58);feedback.Font=Theme.Font(9);feedback.ForeColor=Theme.Warning;
            verify.SetBounds(28,340,150,38);var cancel=new LuxuryButton{Text="Cancel",DialogResult=DialogResult.Cancel};cancel.SetBounds(190,340,120,38);
            Controls.AddRange(new Control[]{heading,message,challenge,code,feedback,verify,cancel});AcceptButton=verify;CancelButton=cancel;
            message.SetBounds(28,102,504,58);
            code.SetBounds(28,398,504,40);
            feedback.SetBounds(28,450,504,52);
            verify.SetBounds(28,514,150,38);
            cancel.SetBounds(190,514,120,38);
            verify.Click+=async delegate{await Submit();};
            timer.Tick+=async delegate{if(submitting||polling||closed)return;polling=true;try{Apply(await engine.Api("GET","/api/signin/"+session));}catch(Exception e){if(!closed)feedback.Text=e.Message;}finally{polling=false;}};
            Shown+=delegate{Apply(state);code.Focus();if(!closed)timer.Start();};
            FormClosed+=delegate{closed=true;timer.Stop();timer.Dispose();code.Clear();if(challenge.Image!=null)challenge.Image.Dispose();};
        }
        internal static bool NeedsCode(string step){return step=="need_email_code"||step=="need_totp"||step=="need_unblock_code"||step=="need_captcha";}
        async Task Submit(){
            if(submitting||polling||closed)return;
            string value=code.Text.Trim();if(value.Length==0){feedback.Text="Enter your verification code.";code.Focus();return;}
            submitting=true;answeredStamp=promptStamp;verify.Enabled=false;code.Enabled=false;
            try{Dictionary<string,object> result=null;bool changed=false;try{result=await engine.Api("POST","/api/signin/"+session,new{value=value});}catch(ApiFailure failure){if(failure.Status!=409)throw;changed=true;}if(changed)result=await engine.Api("GET","/api/signin/"+session);if(!closed){code.Clear();Apply(result);}}
            catch(Exception e){if(!closed){feedback.Text=e.Message;code.SelectAll();}}
            finally{submitting=false;if(!closed){verify.Enabled=code.Enabled=NeedsCode(step)&&(answeredStamp==""||promptStamp!=answeredStamp);if(code.Enabled)code.Focus();}}
        }
        string shownCaptcha = "";
        void Apply(Dictionary<string,object> state){
            if(closed)return;string next=Json.Str(state,"step");
            if(next=="done"){timer.Stop();DialogResult=DialogResult.OK;Close();return;}
            if(next=="failed"){timer.Stop();step=next;verify.Enabled=code.Enabled=false;feedback.Text=Json.Str(state,"error");message.Text=Json.Str(state,"message");if(feedback.Text.Length==0)feedback.Text="Sign-in did not complete. Cancel and try again.";return;}
            if(next=="cancelled"){timer.Stop();DialogResult=DialogResult.Cancel;Close();return;}
            promptStamp=Json.Str(state,"updated");
            if(NeedsCode(next)&&answeredStamp!=""&&promptStamp==answeredStamp){verify.Enabled=code.Enabled=false;message.Text="Checking the code you submitted…";return;}
            if(step!=next){step=next;code.Clear();feedback.Text="";heading.Text=next=="need_totp"?"Enter your security code":"Check your email";message.Text=next=="need_totp"?"Enter the current code from your authenticator app.":"Enter the verification code Mozilla sent you. Your sign-in will continue here.";}
            verify.Enabled=code.Enabled=NeedsCode(next)&&!submitting;
            string problem=Json.Str(state,"error");if(problem!="")feedback.Text=problem;
            string encoded=Json.Str(state,"captcha");
            if(next!="need_captcha"){challenge.Visible=false;}
            else if(encoded!=shownCaptcha){
                shownCaptcha=encoded;
                var previous=challenge.Image;challenge.Image=null;if(previous!=null)previous.Dispose();
                heading.Text="Confirm your sign-in";message.Text="Enter the characters shown below.";
                try{
                    if(encoded.Length>=1500000||!encoded.StartsWith("data:image/",StringComparison.Ordinal)||encoded.IndexOf(',')<0)throw new InvalidDataException();
                    using(var input=new MemoryStream(Convert.FromBase64String(encoded.Substring(encoded.IndexOf(',')+1))))
                    using(var image=Image.FromStream(input))challenge.Image=new Bitmap(image);
                    challenge.Visible=true;
                }catch{challenge.Visible=false;verify.Enabled=code.Enabled=false;feedback.Text="Could not display the verification image. Cancel and try again.";}
            }
        }
    }
}
