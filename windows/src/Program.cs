// Точка входа. Требует только .NET Framework 4.x (встроен в Windows 10 и 11).
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("QR-мост")]
[assembly: AssemblyProduct("QR-мост")]
[assembly: AssemblyDescription("Передача текста между компьютером и телефоном через QR")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace QrBridge
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main()
        {
            try { SetProcessDPIAware(); } catch (Exception) { }   // чёткий QR на экранах 125–200 %
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            I18n i18n = new I18n(LoadResource("QrBridge.strings.ini"), Settings.LoadLang() ?? I18n.SystemLang());
            BridgeServer server = new BridgeServer(LoadResource("QrBridge.page.html"), i18n);
            try { server.Start(); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, i18n.T("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            try { Application.Run(new MainForm(server, i18n)); }
            finally { server.Stop(); }
            return 0;
        }

        /// <summary>Файлы из shared/ встроены в exe при сборке (см. build.bat).</summary>
        static string LoadResource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }
    }
}
