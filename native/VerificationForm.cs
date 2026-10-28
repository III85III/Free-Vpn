using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace III85III.Vpn {
    internal sealed class VerificationForm : Form {
        readonly Engine engine; readonly string session;
        readonly TextBox code=new TextBox();
        readonly Label heading=new Label(),message=new Label(),feedback=new Label();
        readonly PictureBox challenge=new PictureBox{Visible=false,SizeMode=PictureBoxSizeMode.Zoom};
        readonly LuxuryButton verify=new LuxuryButton{Text="Verify",Variant=ButtonVariant.Primary};
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=1500};
        bool submitting,polling,closed; string step="";
        internal VerificationForm(Engine engine,string session,Dictionary<string,object> state){
            this.engine=engine;this.session=session;Text="Verify your Mozilla account";AutoScaleMode=AutoScaleMode.None;
            FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(460,400);BackColor=Theme.Bg;ForeColor=Theme.TextPrimary;
            try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
            Controls.Add(new LuxuryTitleBar(this,false));
            heading.SetBounds(28,58,404,35);heading.Font=Theme.Display(18);heading.ForeColor=Theme.TextPrimary;
            message.SetBounds(28,102,404,58);message.Font=Theme.Font(10);message.ForeColor=Theme.TextSecondary;
            challenge.SetBounds(28,155,404,55);
            code.SetBounds(28,222,404,36);code.Font=Theme.Font(16);code.BackColor=Theme.Surface;code.ForeColor=Theme.TextPrimary;code.BorderStyle=BorderStyle.FixedSingle;code.MaxLength=64;code.AccessibleName="Verification code";code.TextAlign=HorizontalAlignment.Center;code.RightToLeft=RightToLeft.No;
            feedback.SetBounds(28,268,404,58);feedback.Font=Theme.Font(9);feedback.ForeColor=Theme.Warning;
            verify.SetBounds(28,340,150,38);var cancel=new LuxuryButton{Text="Cancel",DialogResult=DialogResult.Cancel};cancel.SetBounds(190,340,120,38);
            Controls.AddRange(new Control[]{heading,message,challenge,code,feedback,verify,cancel});AcceptButton=verify;CancelButton=cancel;
            verify.Click+=async delegate{await Submit();};
            timer.Tick+=async delegate{if(submitting||polling||closed)return;polling=true;try{Apply(await engine.Api("GET","/api/signin/"+session));}catch(Exception e){if(!closed)feedback.Text=e.Message;}finally{polling=false;}};
            Shown+=delegate{Apply(state);code.Focus();if(!closed)timer.Start();};
            FormClosed+=delegate{closed=true;timer.Stop();timer.Dispose();code.Clear();if(challenge.Image!=null)challenge.Image.Dispose();};
        }
        internal static bool NeedsCode(string step){return step=="need_email_code"||step=="need_totp"||step=="need_unblock_code"||step=="need_captcha";}
        async Task Submit(){
            if(submitting||polling||closed)return;
            string value=code.Text.Trim();if(value.Length==0){feedback.Text="Enter your verification code.";code.Focus();return;}
            submitting=true;verify.Enabled=false;code.Enabled=false;
            try{var result=await engine.Api("POST","/api/signin/"+session,new{value=value});if(!closed){code.Clear();Apply(result);}}
            catch(Exception e){if(!closed){feedback.Text=e.Message;code.SelectAll();}}
            finally{submitting=false;if(!closed){verify.Enabled=NeedsCode(step);code.Enabled=NeedsCode(step);code.Focus();}}
        }
        void Apply(Dictionary<string,object> state){
            if(closed)return;string next=Json.Str(state,"step");
            if(next=="done"){timer.Stop();DialogResult=DialogResult.OK;Close();return;}
            if(next=="failed"||next=="cancelled"){timer.Stop();DialogResult=DialogResult.Cancel;Close();return;}
            if(step!=next){step=next;code.Clear();feedback.Text="";heading.Text=next=="need_totp"?"Enter your security code":"Check your email";message.Text=next=="need_totp"?"Enter the current code from your authenticator app.":"Enter the verification code Mozilla sent you. Your sign-in will continue here.";}
            verify.Enabled=code.Enabled=NeedsCode(next)&&!submitting;
            string encoded=Json.Str(state,"captcha");
            if(next=="need_captcha"&&encoded.Length<1500000&&encoded.StartsWith("data:image/",StringComparison.Ordinal)&&encoded.IndexOf(',')>0&&challenge.Image==null){
                try{using(var input=new MemoryStream(Convert.FromBase64String(encoded.Substring(encoded.IndexOf(',')+1))))using(var image=Image.FromStream(input))challenge.Image=new Bitmap(image);challenge.Visible=true;heading.Text="Confirm your sign-in";message.Text="Enter the characters shown below.";}catch{feedback.Text="Could not display the verification image. Cancel and try again.";}
            }
        }
    }
}
