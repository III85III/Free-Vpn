using System;
using System.Reflection;
using System.Windows.Forms;

namespace III85III.Vpn {
    internal static class KeyboardTests {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
        static object Invoke(Control control,string method,object argument){return typeof(KeyboardActionControl).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(control,new[]{argument});}
        static void Down(Control control,Keys key){var args=new KeyEventArgs(key);Invoke(control,"OnKeyDown",args);Check(!args.SuppressKeyPress,"key release remains deliverable");}
        static void Up(Control control,Keys key){Invoke(control,"OnKeyUp",new KeyEventArgs(key));}
        [STAThread] static int Main(){
            try{
                Application.EnableVisualStyles();
                using(var form=new Form())using(var nav=new LuxuryNavButton("Locations",""))using(var toggle=new LuxuryToggle())using(var radio=new LuxuryRadioCard("Local proxy","Description",2))using(var location=new LocationCard())using(var other=new TextBox()){
                    int y=0;
                    foreach(Control control in new Control[]{nav,toggle,radio,location,other}){control.Top=y;y+=65;form.Controls.Add(control);}
                    form.Height=420;form.Show();form.Activate();Application.DoEvents();
                    int clicks=0;nav.Click+=delegate{clicks++;};nav.Focus();
                    Check(nav.Focused&&nav.TabStop,"navigation accepts keyboard focus");
                    Check((bool)Invoke(nav,"IsInputKey",Keys.Enter)&&(bool)Invoke(nav,"IsInputKey",Keys.Space),"activation keys remain control input");
                    Down(nav,Keys.Space);Down(nav,Keys.Space);Check(clicks==0,"held key does not repeatedly activate");Up(nav,Keys.Space);Check(clicks==1,"space release activates once");
                    Up(nav,Keys.Space);Check(clicks==1,"unarmed release is ignored");
                    Down(nav,Keys.Enter);Up(nav,Keys.Enter);Check(clicks==2,"enter release activates navigation");
                    Down(nav,Keys.Control|Keys.Space);Up(nav,Keys.Control|Keys.Space);Check(clicks==2,"modified shortcuts do not activate controls");
                    Down(nav,Keys.Space);other.Focus();nav.Focus();Up(nav,Keys.Space);Check(clicks==2,"focus loss cancels a pending activation");
                    Down(nav,Keys.Space);nav.Enabled=false;nav.Enabled=true;nav.Focus();Up(nav,Keys.Space);Check(clicks==2,"disabling cancels a pending activation");
                    var press=new KeyPressEventArgs(' ');Invoke(nav,"OnKeyPress",press);Check(press.Handled,"activation character is consumed");
                    Check(nav.AccessibilityObject.Name=="Locations"&&nav.AccessibilityObject.Role==AccessibleRole.PageTab,"navigation has a meaningful accessible label and role");
                    nav.Selected=true;Check((nav.AccessibilityObject.State&AccessibleStates.Selected)!=0,"selected page is exposed to accessibility");
                    toggle.Focus();Down(toggle,Keys.Space);Up(toggle,Keys.Space);Check(toggle.Checked,"space toggles the tray preference");
                    Check((toggle.AccessibilityObject.State&AccessibleStates.Checked)!=0,"toggle checked state is exposed");
                    toggle.AccessibilityObject.DoDefaultAction();Check(!toggle.Checked,"accessible activation toggles the control");
                    toggle.Enabled=false;toggle.AccessibilityObject.DoDefaultAction();Check(!toggle.Checked,"disabled accessible action is ignored");
                    radio.Focus();Down(radio,Keys.Enter);Up(radio,Keys.Enter);Check(radio.Checked,"enter selects a routing card");
                    Check(radio.AccessibilityObject.Name=="Local proxy"&&radio.AccessibilityObject.Role==AccessibleRole.RadioButton&&(radio.AccessibilityObject.State&AccessibleStates.Checked)!=0,"routing choice exposes name role and checked state");
                    int selections=0;location.Click+=delegate{selections++;};location.Focus();Down(location,Keys.Enter);Up(location,Keys.Enter);Check(selections==1,"location picker activates from the keyboard");
                    Check(location.AccessibilityObject.Name=="Choose a VPN location","location picker has an accessible name");
                    form.Close();
                }
                Console.WriteLine("All "+checks+" keyboard checks passed.");return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
