// Тексты интерфейса на русском и английском (shared/strings.ini, встроен в exe).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace QrBridge
{
    public sealed class I18n
    {
        public static readonly string[] Langs = { "ru", "en" };
        const string Fallback = "en";

        readonly Dictionary<string, Dictionary<string, string>> table;
        volatile string lang = Fallback;   // читается и потоком сервера

        public I18n(string ini, string lang)
        {
            table = Parse(ini);
            SetLang(lang);
        }

        public string Lang { get { return lang; } }

        public void SetLang(string value)
        {
            if (value != null && table.ContainsKey(value)) lang = value;
        }

        /// <summary>T("count", "n", 5) — подставляет {n} = 5.</summary>
        public string T(string key, params object[] pairs)
        {
            string text = Lookup(lang, key) ?? Lookup(Fallback, key) ?? key;
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                text = text.Replace("{" + pairs[i] + "}", Convert.ToString(pairs[i + 1], CultureInfo.InvariantCulture));
            return text;
        }

        string Lookup(string language, string key)
        {
            Dictionary<string, string> d;
            string s;
            if (table.TryGetValue(language, out d) && d.TryGetValue(key, out s) && s.Length > 0) return s;
            return null;
        }

        public static string SystemLang()
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : Fallback;
        }

        /// <summary>[язык] + строки «ключ = значение».</summary>
        static Dictionary<string, Dictionary<string, string>> Parse(string ini)
        {
            Dictionary<string, Dictionary<string, string>> result = new Dictionary<string, Dictionary<string, string>>();
            Dictionary<string, string> section = null;
            using (StringReader r = new StringReader(ini))
            {
                string raw;
                while ((raw = r.ReadLine()) != null)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        string name = line.Substring(1, line.Length - 2).Trim();
                        if (!result.TryGetValue(name, out section))
                        {
                            section = new Dictionary<string, string>();
                            result[name] = section;
                        }
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq > 0 && section != null)
                        section[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            return result;
        }
    }
}
