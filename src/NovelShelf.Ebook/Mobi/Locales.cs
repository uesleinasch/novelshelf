namespace NovelShelf.Ebook.Mobi;

internal static class Locales
{
    private static readonly Dictionary<string, uint> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["und"] = 0, ["ar"] = 1, ["bg"] = 2, ["ca"] = 3, ["zh"] = 4, ["cs"] = 5, ["da"] = 6, ["de"] = 7,
        ["el"] = 8, ["en"] = 9, ["es"] = 10, ["fi"] = 11, ["fr"] = 12, ["he"] = 13, ["hu"] = 14, ["is"] = 15,
        ["it"] = 16, ["ja"] = 17, ["ko"] = 18, ["nl"] = 19, ["no"] = 20, ["nb"] = 20, ["pl"] = 21, ["pt"] = 22,
        ["rm"] = 23, ["ro"] = 24, ["ru"] = 25, ["hr"] = 26, ["sr"] = 26, ["sk"] = 27, ["sq"] = 28, ["sv"] = 29,
        ["th"] = 30, ["tr"] = 31, ["ur"] = 32, ["id"] = 33, ["uk"] = 34, ["be"] = 35, ["sl"] = 36, ["et"] = 37,
        ["lv"] = 38, ["lt"] = 39, ["fa"] = 41, ["vi"] = 42, ["hy"] = 43, ["az"] = 44, ["eu"] = 45, ["wen"] = 46,
        ["mk"] = 47, ["st"] = 48, ["ts"] = 49, ["tn"] = 50, ["xh"] = 52, ["zu"] = 53, ["af"] = 54, ["ka"] = 55,
        ["fo"] = 56, ["hi"] = 57, ["mt"] = 58, ["smi"] = 59, ["ms"] = 62, ["kk"] = 63, ["sw"] = 65, ["uz"] = 67,
        ["tt"] = 68, ["bn"] = 69, ["pa"] = 70, ["gu"] = 71, ["or"] = 72, ["ta"] = 73, ["te"] = 74, ["kn"] = 75,
        ["ml"] = 76, ["as"] = 77, ["mr"] = 78, ["sa"] = 79, ["kok"] = 87, ["ne"] = 97,
    };

    public static string BaseLanguage(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return "und";
        return tag.Split('-', '_')[0].ToLowerInvariant();
    }

    public static uint LocaleCode(string tag) =>
        Codes.TryGetValue(BaseLanguage(tag), out var code) ? code : 0;
}
