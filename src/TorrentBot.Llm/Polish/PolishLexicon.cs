using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TorrentBot.Llm.Polish;

/// <summary>
/// Basic Polish word with inflections for better NL command handling (supplements LLM).
/// Used for keyword matching, normalization, and to help small models with Polish multi-turn.
/// </summary>
public sealed record PolishWord(
    string BaseForm,
    string[] AllForms,
    string PartOfSpeech, // verb, noun, adj, prep, num, modal, etc.
    string[]? Tags = null // e.g. "imperative", "1sg", "accusative", "infinitive"
)
{
    /// <summary>
    /// Simple GetForm - returns matching form or BaseForm.
    /// Extend with real morphology if needed (e.g. via IInflections).
    /// </summary>
    public string GetForm(string? desiredForm = null, int? personOrNumber = null, string? grammaticalCase = null)
    {
        if (string.IsNullOrWhiteSpace(desiredForm))
            return BaseForm;

        var norm = desiredForm.ToLowerInvariant();

        // Try direct contains match (for simplicity)
        var match = AllForms.FirstOrDefault(f => 
            f.Contains(norm, StringComparison.OrdinalIgnoreCase) ||
            norm.Contains(f, StringComparison.OrdinalIgnoreCase));

        return match ?? BaseForm;
    }

    public override string ToString() => $"{BaseForm} ({PartOfSpeech})";
}

/// <summary>
/// Static lexicon of ~100 common Polish words used in bot commands.
/// Populated statically (fast in-memory). Can be extended with reflection scan for IInflections etc.
/// </summary>
public static class PolishLexicon
{
    private static readonly Lazy<IReadOnlyDictionary<string, PolishWord>> _byForm = new(() =>
    {
        var dict = new Dictionary<string, PolishWord>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in AllWords)
        {
            if (string.IsNullOrWhiteSpace(word.BaseForm)) continue;
            dict[word.BaseForm] = word;
            if (word.AllForms != null)
            {
                foreach (var form in word.AllForms)
                {
                    if (!string.IsNullOrWhiteSpace(form) && !dict.ContainsKey(form))
                        dict[form] = word;
                }
            }
        }

        return dict;
    });

    /// <summary>
    /// All words in the lexicon (~100 entries covering verbs, preps, numbers, modals, nouns for commands).
    /// </summary>
    public static IReadOnlyList<PolishWord> AllWords { get; } = new List<PolishWord>
    {
        // Verbs - core actions (pobieranie, search, control)
        new("pobierz", new[] { "pobierz", "pobieram", "pobierasz", "pobierają", "pobrać", "pobieranie" }, "verb", new[] { "imperative", "infinitive" }),
        new("szukaj", new[] { "szukaj", "szukam", "szukasz", "szukają", "szukać", "szukanie" }, "verb", new[] { "imperative" }),
        new("znajdź", new[] { "znajdź", "znajduję", "znajdujesz", "znajdują", "znaleźć", "znajdowanie" }, "verb", new[] { "imperative" }),
        new("pokaż", new[] { "pokaż", "pokazuję", "pokazujesz", "pokazują", "pokazać", "pokazywanie" }, "verb", new[] { "imperative" }),
        new("wyświetl", new[] { "wyświetl", "wyświetlam", "wyświetlasz", "wyświetlają", "wyświetlić" }, "verb", new[] { "imperative" }),
        new("pauzuj", new[] { "pauzuj", "pauzuję", "pauzujesz", "pauzują", "pauzować", "wstrzymaj" }, "verb", new[] { "imperative" }),
        new("wznów", new[] { "wznów", "wznawiam", "wznawiasz", "wznawiają", "wznowić", "wznowienie" }, "verb", new[] { "imperative" }),
        new("wybierz", new[] { "wybierz", "wybieram", "wybierasz", "wybierają", "wybrać", "wybór" }, "verb", new[] { "imperative" }),
        new("startuj", new[] { "startuj", "startuję", "startujesz", "startują", "uruchom", "zacznij" }, "verb", new[] { "imperative" }),
        new("anuluj", new[] { "anuluj", "anuluję", "anulujesz", "anulują", "anulować", "stop" }, "verb", new[] { "imperative" }),
        new("usuń", new[] { "usuń", "usuwam", "usuwasz", "usuwają", "usunąć", "usunięcie" }, "verb", new[] { "imperative" }),
        new("lista", new[] { "lista", "listuj", "pokaż listę", "wyświetl listę" }, "verb", new[] { "imperative" }),

        // Nouns - objects
        new("pobieranie", new[] { "pobieranie", "pobrania", "download", "pobierz" }, "noun"),
        new("torrent", new[] { "torrent", "torrenty", "torent", "torrents" }, "noun"),
        new("plik", new[] { "plik", "pliki", "file", "files" }, "noun"),
        new("media", new[] { "media", "mediów", "biblioteka", "pliki media" }, "noun"),
        new("biblioteka", new[] { "biblioteka", "bibliotece", "library" }, "noun"),
        new("status", new[] { "status", "stan", "state" }, "noun"),
        new("job", new[] { "job", "jobs", "zadanie", "zadania" }, "noun"),
        new("dysk", new[] { "dysk", "dysku", "dyski", "dysku", "storage" }, "noun"),

        // Prepositions (przyimki) - important for Polish NL
        new("na", new[] { "na" }, "prep"),
        new("do", new[] { "do" }, "prep"),
        new("w", new[] { "w", "we" }, "prep"),
        new("z", new[] { "z", "ze" }, "prep"),
        new("od", new[] { "od" }, "prep"),
        new("dla", new[] { "dla" }, "prep"),
        new("po", new[] { "po" }, "prep"),
        new("za", new[] { "za" }, "prep"),
        new("bez", new[] { "bez" }, "prep"),
        new("przez", new[] { "przez" }, "prep"),
        new("przy", new[] { "przy" }, "prep"),
        new("między", new[] { "między" }, "prep"),
        new("nad", new[] { "nad" }, "prep"),
        new("pod", new[] { "pod" }, "prep"),
        new("przed", new[] { "przed" }, "prep"),
        new("poza", new[] { "poza" }, "prep"),

        // Numbers and ordinals (liczby)
        new("jeden", new[] { "jeden", "1", "jedna", "jedno" }, "num"),
        new("dwa", new[] { "dwa", "2", "dwie" }, "num"),
        new("trzy", new[] { "trzy", "3" }, "num"),
        new("cztery", new[] { "cztery", "4" }, "num"),
        new("pięć", new[] { "pięć", "5" }, "num"),
        new("sześć", new[] { "sześć", "6" }, "num"),
        new("siedem", new[] { "siedem", "7" }, "num"),
        new("osiem", new[] { "osiem", "8" }, "num"),
        new("dziewięć", new[] { "dziewięć", "9" }, "num"),
        new("dziesięć", new[] { "dziesięć", "10" }, "num"),
        new("zero", new[] { "zero", "0" }, "num"),
        new("pierwszy", new[] { "pierwszy", "1.", "pierwsza", "pierwsze" }, "adj"),
        new("drugi", new[] { "drugi", "2.", "druga" }, "adj"),
        new("trzeci", new[] { "trzeci", "3." }, "adj"),
        new("czwarty", new[] { "czwarty", "4." }, "adj"),
        new("piąty", new[] { "piąty", "5." }, "adj"),

        // Modal / auxiliary verbs (czasowniki modalne)
        new("mogę", new[] { "mogę", "możesz", "może", "możemy", "mogą", "można" }, "modal"),
        new("chcę", new[] { "chcę", "chcesz", "chce", "chcemy", "chcą", "chciałbym" }, "modal"),
        new("muszę", new[] { "muszę", "musisz", "musi", "musimy", "muszą" }, "modal"),
        new("trzeba", new[] { "trzeba", "należy", "powinienem" }, "modal"),
        new("mogę", new[] { "mogę", "potrafię" }, "modal"), // duplicate for coverage
        new("wiem", new[] { "wiem", "wiesz", "wie", "wiemy", "wiedzieć" }, "modal"),

        // Common command words / nouns
        new("pokaż", new[] { "pokaż", "wyświetl", "listuj", "pokaż mi" }, "verb"),
        new("znajdź", new[] { "znajdź", "szukaj", "wyszukaj" }, "verb"),
        new("pobierz", new[] { "pobierz", "ściągnij", "pobieranie" }, "verb"),
        new("status", new[] { "status", "stan", "jak tam", "co tam" }, "noun"),
        new("aktywny", new[] { "aktywny", "aktywne", "działający" }, "adj"),
        new("pauzowany", new[] { "pauzowany", "wstrzymany", "zatrzymany" }, "adj"),
        new("ukończony", new[] { "ukończony", "zakończony", "gotowy" }, "adj"),
        new("duży", new[] { "duży", "duże", "wielki" }, "adj"),
        new("mały", new[] { "mały", "małe" }, "adj"),
        new("nowy", new[] { "nowy", "nowe" }, "adj"),
        new("stary", new[] { "stary", "stare" }, "adj"),

        // More preps and connectors
        new("i", new[] { "i", "oraz", "a" }, "conj"),
        new("lub", new[] { "lub", "albo", "czy" }, "conj"),
        new("nie", new[] { "nie", "nien" }, "adv"),
        new("tak", new[] { "tak", "yes" }, "adv"),
        new("teraz", new[] { "teraz", "aktualnie", "obecnie" }, "adv"),
        new("później", new[] { "później", "potem", "następnie" }, "adv"),

        // Additional useful for bot
        new("wszystko", new[] { "wszystko", "wszystkie", "całość" }, "pron"),
        new("nic", new[] { "nic", "niczego" }, "pron"),
        new("co", new[] { "co", "czego", "jakie" }, "pron"),
        new("jak", new[] { "jak", "jaki", "jakie" }, "adv"),
        new("dlaczego", new[] { "dlaczego", "czemu", "po co" }, "adv"),
        new("kiedy", new[] { "kiedy", "kiedyś", "gdy" }, "adv"),
        new("gdzie", new[] { "gdzie", "gdzieś" }, "adv"),
        new("ile", new[] { "ile", "jak dużo", "jak wiele" }, "adv"),
        new("dużo", new[] { "dużo", "wiele", "sporo" }, "adv"),
        new("mało", new[] { "mało", "niewiele" }, "adv"),
    }.AsReadOnly();

    /// <summary>
    /// Fast lookup by any form (base or inflected). Case-insensitive.
    /// </summary>
    public static PolishWord? Find(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        return _byForm.Value.TryGetValue(word, out var found) ? found : null;
    }

    /// <summary>
    /// Returns all words that match the part of speech.
    /// </summary>
    public static IEnumerable<PolishWord> ByPartOfSpeech(string pos) =>
        AllWords.Where(w => w.PartOfSpeech.Equals(pos, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// For reflection-based loading (future: scan assembly for PolishWord records or IInflections).
    /// Currently returns the static list. Call this to "load" into memory.
    /// </summary>
    public static IReadOnlyList<PolishWord> LoadAllViaReflection()
    {
        // Placeholder for future: could scan for types with PolishWordAttribute or implement IInflections
        // For now just return the in-memory list so "od razu latały w pamięci"
        return AllWords;
    }

    /// <summary>
    /// Simple normalization helper: replace common Polish command words with English equivalents for LLM.
    /// Extend as needed.
    /// </summary>
    public static string NormalizeForLlm(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var normalized = text.ToLowerInvariant();

        // Basic mappings (expand with more from lexicon)
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pobierz"] = "download",
            ["pobierania"] = "downloads",
            ["szukaj"] = "search for",
            ["znajdź"] = "search for",
            ["znajdz"] = "search for",
            ["pokaż"] = "show",
            ["wyświetl"] = "list",
            ["pauzuj"] = "pause",
            ["wznów"] = "resume",
            ["wybierz"] = "select",
            ["pierwszy"] = "first",
            ["anuluj"] = "cancel",
            ["status"] = "status",
            ["pobieranie"] = "download",
            ["torrenty"] = "torrents",
            ["pliki"] = "files",
            ["biblioteka"] = "library",
            ["media"] = "media",
        };

        foreach (var (pl, en) in replacements)
        {
            normalized = normalized.Replace(pl, en);
        }

        return normalized;
    }
}

// Future extension points mentioned in the query
public interface IInflections
{
    string GetForm(string formHint);
}

public interface IExpects
{
    bool MatchesExpectation(string input);
}