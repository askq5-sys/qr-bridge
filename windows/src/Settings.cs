// Настройки пользователя: %APPDATA%\QR-bridge\settings.ini (сейчас только язык).
using System;
using System.IO;
using System.Text;

namespace QrBridge
{
    public static class Settings
    {
        static string FilePath
        {
            get
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(Path.Combine(dir, "QR-bridge"), "settings.ini");
            }
        }

        /// <summary>Сохранённый язык или null.</summary>
        public static string LoadLang()
        {
            try
            {
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0 && line.Substring(0, eq).Trim() == "lang") return line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception) { }
            return null;
        }

        public static void SaveLang(string lang)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, "lang=" + lang + "\r\n", Encoding.UTF8);
            }
            catch (Exception) { }   // не смогли сохранить — язык просто не запомнится
        }
    }
}
