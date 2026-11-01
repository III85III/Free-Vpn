using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace III85III.Vpn {
    internal static class RequiredUpdateTests {
        [STAThread]static int Main(){string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"required-update-"+Guid.NewGuid().ToString("N"));try{
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            if(!AppUpdate.Required("2.2.3","2.2.4")||AppUpdate.Required("2.2.4","2.2.4")||AppUpdate.Required("2.2.5","2.2.4")||AppUpdate.Required("2.2.3","invalid"))throw new Exception("Required-version comparison failed");
            using(var engine=new Engine("unused",data))using(var form=new MainForm(engine,true)){
                form.Show();form.RequireUpdate("99.0.0").GetAwaiter().GetResult();
                if(AppUpdate.SavedRequirement(data)!="99.0.0")throw new Exception("Required version must survive restart");
                var gate=(Panel)form.GetType().GetField("updateGate",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                if(!gate.Visible)throw new Exception("Update gate must be visible");
                var connect=(Button)form.GetType().GetField("connectBtn",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);if(connect.Enabled)throw new Exception("Connect must be disabled while update gate is active");
                var task=(Task)form.GetType().GetMethod("TogglePower",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);bool denied=false;try{task.GetAwaiter().GetResult();}catch(IOException){denied=true;}if(!denied)throw new Exception("Power handler bypassed update gate");
                Button dismiss=null;foreach(Control control in gate.Controls)if(control is Button && control.Text.Contains("ادامه"))dismiss=(Button)control;
                if(dismiss==null)throw new Exception("Update gate must provide dismiss option to continue using current version");
                dismiss.PerformClick();
                if(gate.Visible)throw new Exception("Dismiss must hide update gate");
                if(!connect.Enabled)throw new Exception("Connect must be re-enabled after user dismisses update gate");
            }
            File.WriteAllText(Path.Combine(data,"required-update.txt"),"1.0.0");if(AppUpdate.SavedRequirement(data)!="")throw new Exception("Completed update must clear old version requirement");
            Console.WriteLine("PASS update comparison, persisted requirement, power guard, dismiss recovery to continue on current version, and upgrade cleanup");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{if(Directory.Exists(data))Directory.Delete(data,true);}}
    }
}
