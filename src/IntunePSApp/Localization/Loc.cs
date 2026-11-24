using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows.Data;

namespace J365.IntunePSApp.Localization
{
    /// <summary>
    /// Runtime localization. Strings live in embedded JSON files (Resources/Strings.&lt;code&gt;.json);
    /// adding a language means adding one JSON file and one entry in <see cref="Languages"/>.
    /// XAML binds through the <c>{l:T Key}</c> extension and refreshes live when the language changes.
    /// </summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        public const string EnglishCode = "en";
        public const string PortugueseCode = "pt-BR";

        public static readonly (string Code, string Name, string Culture)[] Languages =
        {
            (EnglishCode, "English", "en-US"),
            (PortugueseCode, "Português (Brasil)", "pt-BR"),
        };

        // Captured before the app overrides the thread culture, so "auto" always means the Windows display language.
        private static readonly CultureInfo OsUiCulture = CultureInfo.CurrentUICulture;

        public static Loc Instance { get; } = new Loc();

        private readonly Dictionary<string, string> _english;
        private Dictionary<string, string> _strings;

        public string Language { get; private set; } = EnglishCode;
        public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");
        public bool IsPortuguese => Language == PortugueseCode;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action LanguageChanged;

        private Loc()
        {
            _english = Load(EnglishCode);
            _strings = _english;
        }

        /// <summary>Indexer used by XAML bindings.</summary>
        public string this[string key] => Get(key);

        public bool Has(string key) => _strings.ContainsKey(key) || _english.ContainsKey(key);

        public string Get(string key)
        {
            if (key == null) return "";
            if (_strings.TryGetValue(key, out var v)) return v;
            if (_english.TryGetValue(key, out v)) return v;
            return "[" + key + "]";
        }

        /// <summary>Text in the current language.</summary>
        /// <summary>
        /// A text with a count: the key followed by "_One" when the count is 1 and the language has it ("Gerar 1 pacote"),
        /// otherwise the key itself ("Gerar 3 pacotes"). The count is the first value of the text.
        /// </summary>
        public static string Count(string key, int count, params object[] more)
        {
            var values = new object[] { count }.Concat(more ?? System.Array.Empty<object>()).ToArray();
            return T(count == 1 && Instance.Has(key + "_One") ? key + "_One" : key, values);
        }

        public static string T(string key) => Instance.Get(key);

        /// <summary>Formatted text in the current language (numbers and dates follow the language culture).</summary>
        public static string T(string key, params object[] args)
        {
            string format = Instance.Get(key);
            try { return string.Format(Instance.Culture, format, args); }
            catch (FormatException) { return format; }
        }

        /// <summary>English text regardless of the current language (used inside generated scripts).</summary>
        public static string En(string key) => Instance._english.TryGetValue(key ?? "", out var v) ? v : key;

        /// <summary>"auto" follows the Windows display language; anything else is used as-is when supported.</summary>
        public static string Resolve(string setting)
        {
            foreach (var l in Languages)
                if (string.Equals(l.Code, setting, StringComparison.OrdinalIgnoreCase)) return l.Code;
            return OsUiCulture.Name.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? PortugueseCode : EnglishCode;
        }

        public void SetLanguage(string code)
        {
            code = Resolve(code);
            string cultureName = "en-US";
            foreach (var l in Languages) if (l.Code == code) cultureName = l.Culture;
            _strings = code == EnglishCode ? _english : Load(code);
            Language = code;
            Culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.DefaultThreadCurrentCulture = Culture;
            CultureInfo.DefaultThreadCurrentUICulture = Culture;
            Thread.CurrentThread.CurrentCulture = Culture;
            Thread.CurrentThread.CurrentUICulture = Culture;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
            LanguageChanged?.Invoke();
        }

        public IReadOnlyCollection<string> Keys(string code) => code == EnglishCode ? _english.Keys : Load(code).Keys;

        private static Dictionary<string, string> Load(string code)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Strings.{code}.json")
                ?? throw new InvalidOperationException($"Missing language resource: Strings.{code}.json");
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// XAML markup: <c>Text="{l:T Nav_Home}"</c>. It is a real Binding, so it works in templates, setters and
    /// any dependency property, and updates automatically when the language changes.
    /// </summary>
    public sealed class TExtension : Binding
    {
        public TExtension(string key) : base("[" + key + "]")
        {
            Source = Loc.Instance;
            Mode = BindingMode.OneWay;
        }
    }
}
