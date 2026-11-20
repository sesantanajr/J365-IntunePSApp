using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// The texts of the PSAppDeployToolkit dialogs, read from the string tables of the toolkit (Strings\strings.psd1 in
    /// English and Strings\&lt;language&gt;\strings.psd1 over it, like the toolkit does), so the preview of a package shows
    /// the sentences the package will show, in the language of the dialogs.
    /// </summary>
    public sealed class PsadtStrings
    {
        private readonly Dictionary<string, object> _root;

        private PsadtStrings(Dictionary<string, object> root, string culture)
        {
            _root = root;
            Culture = culture;
        }

        /// <summary>The language whose table was found ("pt-BR", "pt", or "" for the English one).</summary>
        public string Culture { get; }

        /// <summary>The text at a path ("CloseAppsPrompt", "Fluent", "DialogMessage", "Install"), or null.</summary>
        public string Get(params string[] path)
        {
            object node = _root;
            foreach (var key in path)
            {
                if (node is not Dictionary<string, object> table || !table.TryGetValue(key, out node)) return null;
            }
            return node as string;
        }

        /// <summary>
        /// The table of a language from the Strings folder of a toolkit: the English table, with the table of the language
        /// (or of its parent: pt-BR, then pt) over it. Null when the folder has no English table.
        /// </summary>
        public static PsadtStrings Load(string stringsFolder, string culture)
        {
            string basePath = Path.Combine(stringsFolder ?? "", "strings.psd1");
            if (!File.Exists(basePath)) return null;
            var root = Parse(File.ReadAllText(basePath, Encoding.UTF8));
            string found = "";
            for (var c = CultureOf(culture); c != null && c.Name.Length > 0; c = c.Parent)
            {
                string path = Path.Combine(stringsFolder, c.Name, "strings.psd1");
                if (!File.Exists(path)) continue;
                Overlay(root, Parse(File.ReadAllText(path, Encoding.UTF8)));
                found = c.Name;
                break;
            }
            return new PsadtStrings(root, found);
        }

        private static CultureInfo CultureOf(string name)
        {
            try { return string.IsNullOrWhiteSpace(name) ? null : CultureInfo.GetCultureInfo(name.Trim()); }
            catch (CultureNotFoundException) { return null; }
        }

        /// <summary>Values of the language replace the English ones, unless they are empty (like the toolkit).</summary>
        private static void Overlay(Dictionary<string, object> target, Dictionary<string, object> source)
        {
            foreach (var (key, value) in source)
            {
                if (value is Dictionary<string, object> table)
                {
                    if (!target.TryGetValue(key, out var existing) || existing is not Dictionary<string, object> sub)
                        target[key] = sub = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    Overlay(sub, table);
                }
                else if (!target.ContainsKey(key) || !string.IsNullOrWhiteSpace(value as string)) target[key] = value;
            }
        }

        // ------------------------------------------------------------------ PowerShell data files

        /// <summary>
        /// Reads a PowerShell data file made of nested hashtables of strings (what the string tables of the toolkit are):
        /// single- and double-quoted strings (PowerShell also takes typographic quotes for quotes), backtick escapes,
        /// comments. Other values ($true, numbers) are kept as their text. Keys are case-insensitive, like PowerShell.
        /// </summary>
        public static Dictionary<string, object> Parse(string text)
        {
            int i = 0;
            Skip(text, ref i);
            if (!text.AsSpan(i).StartsWith("@{")) throw new FormatException("A data file starts with @{.");
            return Table(text, ref i);
        }

        private static Dictionary<string, object> Table(string t, ref int i)
        {
            i += 2; // @{
            var table = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                Skip(t, ref i);
                if (i >= t.Length) throw new FormatException("A table is not closed.");
                if (t[i] == '}') { i++; return table; }
                string key = IsQuote(t[i]) ? Quoted(t, ref i) : Word(t, ref i);
                Skip(t, ref i);
                if (i >= t.Length || t[i] != '=') throw new FormatException($"'=' expected after {key}.");
                i++;
                Skip(t, ref i);
                table[key] = Value(t, ref i);
            }
        }

        private static object Value(string t, ref int i)
        {
            if (i >= t.Length) throw new FormatException("A value is missing at the end of the file.");
            if (t.AsSpan(i).StartsWith("@{")) return Table(t, ref i);
            if (IsQuote(t[i])) return Quoted(t, ref i);
            int start = i;
            while (i < t.Length && !char.IsWhiteSpace(t[i]) && t[i] != ';' && t[i] != '}') i++;
            return t.Substring(start, i - start);
        }

        private static string Word(string t, ref int i)
        {
            int start = i;
            while (i < t.Length && (char.IsLetterOrDigit(t[i]) || t[i] is '_' or '-' or '.')) i++;
            if (i == start) throw new FormatException(i < t.Length ? $"Unexpected '{t[i]}' at {i}." : "Unexpected end of the file.");
            return t.Substring(start, i - start);
        }

        private static bool IsSingle(char c) => c is '\'' or '‘' or '’' or '‚' or '‛';
        private static bool IsDouble(char c) => c is '"' or '“' or '”' or '„';
        private static bool IsQuote(char c) => IsSingle(c) || IsDouble(c);

        private static string Quoted(string t, ref int i)
        {
            bool single = IsSingle(t[i]);
            i++;
            var sb = new StringBuilder();
            while (i < t.Length)
            {
                char c = t[i];
                // A doubled quote is one quote: PowerShell keeps the second character of the pair.
                if (single && IsSingle(c))
                {
                    if (i + 1 < t.Length && IsSingle(t[i + 1])) { sb.Append(t[i + 1]); i += 2; continue; }
                    i++;
                    return sb.ToString();
                }
                if (!single && c == '`' && i + 1 < t.Length)
                {
                    char e = t[i + 1];
                    sb.Append(e switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '0' => '\0', 'a' => '\a', 'b' => '\b', 'f' => '\f', 'v' => '\v', _ => e });
                    i += 2;
                    continue;
                }
                if (!single && IsDouble(c))
                {
                    if (i + 1 < t.Length && IsDouble(t[i + 1])) { sb.Append(t[i + 1]); i += 2; continue; }
                    i++;
                    return sb.ToString();
                }
                sb.Append(c);
                i++;
            }
            throw new FormatException("A string is not closed.");
        }

        /// <summary>Spaces, line ends, ';' and comments (# to the end of the line, &lt;# ... #&gt;).</summary>
        private static void Skip(string t, ref int i)
        {
            while (i < t.Length)
            {
                char c = t[i];
                if (char.IsWhiteSpace(c) || c == ';' || c == '﻿') { i++; continue; }
                if (c == '<' && i + 1 < t.Length && t[i + 1] == '#')
                {
                    int end = t.IndexOf("#>", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? t.Length : end + 2;
                    continue;
                }
                if (c == '#')
                {
                    int end = t.IndexOf('\n', i);
                    i = end < 0 ? t.Length : end + 1;
                    continue;
                }
                return;
            }
        }
    }
}
