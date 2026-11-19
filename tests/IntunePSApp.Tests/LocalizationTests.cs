using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;

namespace J365.IntunePSApp.Tests
{
    public class LocalizationTests
    {
        private static Dictionary<string, string> Table(string code)
        {
            var result = new Dictionary<string, string>();
            Lang.With(code, () => { foreach (var k in Loc.Instance.Keys(code)) result[k] = Loc.Instance.Get(k); });
            return result;
        }

        [Fact]
        public void Both_languages_define_the_same_keys()
        {
            var en = Loc.Instance.Keys(Loc.EnglishCode).ToHashSet();
            var pt = Loc.Instance.Keys(Loc.PortugueseCode).ToHashSet();
            Assert.NotEmpty(en);
            Assert.Empty(en.Except(pt));
            Assert.Empty(pt.Except(en));
        }

        [Fact]
        public void Translations_keep_the_same_placeholders()
        {
            var en = Table(Loc.EnglishCode);
            var pt = Table(Loc.PortugueseCode);
            static string Slots(string s) => string.Join(",", Regex.Matches(s, @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));
            var different = en.Keys.Where(k => Slots(en[k]) != Slots(pt[k])).ToList();
            Assert.True(different.Count == 0, "Placeholders differ: " + string.Join(", ", different));
        }

        [Fact]
        public void No_text_is_empty()
        {
            foreach (var code in new[] { Loc.EnglishCode, Loc.PortugueseCode })
                Assert.DoesNotContain(Table(code), kv => string.IsNullOrWhiteSpace(kv.Value));
        }

        [Fact]
        public void Every_key_used_by_the_source_code_exists()
        {
            string root = Repo.Root;
            Assert.True(root != null, "Repository root not found from " + AppContext.BaseDirectory);
            var keys = Loc.Instance.Keys(Loc.EnglishCode).ToHashSet();
            var missing = new SortedSet<string>();
            var files = Directory.GetFiles(Path.Combine(root, "src", "IntunePSApp"), "*.*", SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains(@"\bin\") && !f.Contains(@"\obj\") && !f.EndsWith(@"Localization\Loc.cs"));
            foreach (var file in files)
            {
                string text = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(text, @"\{l:T ([A-Za-z0-9_]+)\}")) if (!keys.Contains(m.Groups[1].Value)) missing.Add(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(text, @"\b(?:T|En)\(""([A-Za-z0-9_]+)""\s*[,)]")) if (!keys.Contains(m.Groups[1].Value)) missing.Add(m.Groups[1].Value);
            }
            Assert.True(missing.Count == 0, "Keys used but not defined: " + string.Join(", ", missing));
        }

        [Fact]
        public void Switching_the_language_changes_the_texts_and_notifies_bindings()
        {
            string before = Loc.Instance.Language;
            int notified = 0;
            void Handler() => notified++;
            Loc.Instance.LanguageChanged += Handler;
            try
            {
                Loc.Instance.SetLanguage(Loc.EnglishCode);
                Assert.Equal("Settings", Loc.T("Nav_Settings"));
                Loc.Instance.SetLanguage(Loc.PortugueseCode);
                Assert.Equal("Configurações", Loc.T("Nav_Settings"));
                Assert.True(Loc.Instance.IsPortuguese);
                Assert.Equal("Settings", Loc.En("Nav_Settings"));
                Assert.True(notified >= 1);
            }
            finally
            {
                Loc.Instance.LanguageChanged -= Handler;
                Loc.Instance.SetLanguage(before);
            }
        }

        [Fact]
        public void Unknown_keys_are_visible_instead_of_crashing()
        {
            Assert.Equal("[No_Such_Key]", Loc.T("No_Such_Key"));
            Assert.False(Loc.Instance.Has("No_Such_Key"));
        }

        [Theory]
        [InlineData("en", "en")]
        [InlineData("pt-BR", "pt-BR")]
        [InlineData("PT-br", "pt-BR")]
        public void Resolve_accepts_explicit_languages(string setting, string expected) => Assert.Equal(expected, Loc.Resolve(setting));

        [Theory]
        [InlineData("auto")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("xx")]
        public void Resolve_falls_back_to_a_supported_language(string setting)
            => Assert.Contains(Loc.Resolve(setting), new[] { Loc.EnglishCode, Loc.PortugueseCode });

        [Fact]
        public void Every_action_has_texts_in_both_languages()
        {
            foreach (var code in new[] { Loc.EnglishCode, Loc.PortugueseCode })
                Lang.With(code, () =>
                {
                    foreach (var def in ActionCatalog.All)
                    {
                        Assert.True(Loc.Instance.Has("Act_" + def.Type), $"Act_{def.Type}");
                        Assert.True(Loc.Instance.Has("ActD_" + def.Type), $"ActD_{def.Type}");
                        Assert.True(Loc.Instance.Has("ActCat_" + def.Category), $"ActCat_{def.Category}");
                        Assert.False(def.Label.StartsWith("["));
                        foreach (var f in def.Fields) Assert.True(Loc.Instance.Has($"ActF_{def.Type}_{f.Key}"), $"ActF_{def.Type}_{f.Key}");
                    }
                    foreach (var phase in ActionCatalog.Phases) Assert.False(ActionCatalog.PhaseLabel(phase).StartsWith("["));
                    foreach (var item in SetupService.CreateItems())
                    {
                        Assert.False(item.Title.StartsWith("["));
                        Assert.False(item.Description.StartsWith("["));
                        Assert.True(Loc.Instance.Has("Setup_Fallback_" + item.Key));
                    }
                    foreach (SetupState state in Enum.GetValues(typeof(SetupState))) Assert.True(Loc.Instance.Has("Setup_State_" + state));
                });
        }

        [Fact]
        public void Choice_options_that_are_not_technical_codes_have_labels()
        {
            foreach (var (type, field) in new[] { ("CreateShortcut", "Location"), ("StopService", "Mode"), ("RunProgram", "Source"), ("CopyToUserProfiles", "BasePath") })
            {
                var f = ActionCatalog.Get(type).Fields.Single(x => x.Key == field);
                foreach (var option in f.Options) Assert.True(Loc.Instance.Has($"ActO_{type}_{field}_{option}"), $"ActO_{type}_{field}_{option}");
            }
            // Registry types are shown as they are.
            Assert.Equal("DWord", ActionCatalog.OptionLabel("SetRegistry", "Type", "DWord"));
        }
    }
}
