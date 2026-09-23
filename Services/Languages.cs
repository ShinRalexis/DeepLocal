using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepLocal.Services
{
    /// <summary>Una lingua traducibile: codice, nome inglese (usato nei prompt), nome italiano, direzione.</summary>
    public sealed record Language(string Code, string English, string Italian, bool IsRtl = false)
    {
        public const string AutoCode = "auto";

        public bool IsAuto => Code == AutoCode;

        public string DisplayName(string uiLang) => uiLang == "it" ? Italian : English;
    }

    public static class Languages
    {
        public static readonly Language Auto = new(Language.AutoCode, "Detect language", "Rileva lingua");

        // Sottoinsieme delle 55 lingue di TranslateGemma, le stesse che offre DeepL.
        public static readonly IReadOnlyList<Language> All = new List<Language>
        {
            new("ar", "Arabic", "Arabo", IsRtl: true),
            new("bg", "Bulgarian", "Bulgaro"),
            new("zh", "Chinese (Simplified)", "Cinese (semplificato)"),
            new("zh-TW", "Chinese (Traditional)", "Cinese (tradizionale)"),
            new("cs", "Czech", "Ceco"),
            new("da", "Danish", "Danese"),
            new("nl", "Dutch", "Olandese"),
            new("en", "English", "Inglese"),
            new("et", "Estonian", "Estone"),
            new("fi", "Finnish", "Finlandese"),
            new("fr", "French", "Francese"),
            new("de", "German", "Tedesco"),
            new("el", "Greek", "Greco"),
            new("he", "Hebrew", "Ebraico", IsRtl: true),
            new("hi", "Hindi", "Hindi"),
            new("hu", "Hungarian", "Ungherese"),
            new("id", "Indonesian", "Indonesiano"),
            new("it", "Italian", "Italiano"),
            new("ja", "Japanese", "Giapponese"),
            new("ko", "Korean", "Coreano"),
            new("lv", "Latvian", "Lettone"),
            new("lt", "Lithuanian", "Lituano"),
            new("nb", "Norwegian", "Norvegese"),
            new("fa", "Persian", "Persiano", IsRtl: true),
            new("pl", "Polish", "Polacco"),
            new("pt", "Portuguese", "Portoghese"),
            new("ro", "Romanian", "Rumeno"),
            new("ru", "Russian", "Russo"),
            new("sk", "Slovak", "Slovacco"),
            new("sl", "Slovenian", "Sloveno"),
            new("es", "Spanish", "Spagnolo"),
            new("sv", "Swedish", "Svedese"),
            new("th", "Thai", "Thailandese"),
            new("tr", "Turkish", "Turco"),
            new("uk", "Ukrainian", "Ucraino"),
            new("vi", "Vietnamese", "Vietnamita"),
        };

        public static Language? ByCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            if (code == Language.AutoCode) return Auto;
            return All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Riconosce un nome di lingua in inglese o italiano, tollerando punteggiatura e varianti.</summary>
        public static Language? ByName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var name = new string(raw.Where(c => char.IsLetter(c) || c == ' ' || c == '(' || c == ')').ToArray()).Trim();
            if (name.Length == 0) return null;

            var exact = All.FirstOrDefault(l =>
                string.Equals(l.English, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(l.Italian, name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // "Chinese", "Mandarin", "Norwegian Bokmal", "Farsi", "Brazilian Portuguese"...
            var lower = name.ToLowerInvariant();
            if (lower.Contains("traditional")) return ByCode("zh-TW");
            if (lower.Contains("chinese") || lower.Contains("mandarin")) return ByCode("zh");
            if (lower.Contains("norwegian") || lower.Contains("bokm")) return ByCode("nb");
            if (lower.Contains("farsi")) return ByCode("fa");
            if (lower.Contains("portuguese")) return ByCode("pt");
            return All.FirstOrDefault(l => lower.StartsWith(l.English.ToLowerInvariant()));
        }
    }
}
