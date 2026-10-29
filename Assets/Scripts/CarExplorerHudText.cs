using System.Collections.Generic;
using System.Text;

/// <summary>One HUD string in English and Uzbek (Latin script).</summary>
public readonly struct HudString
{
    public readonly string English;
    public readonly string Uzbek;

    public HudString(string english, string uzbek)
    {
        English = english;
        Uzbek = uzbek;
    }

    public string Get(bool uzbek) => uzbek ? Uzbek : English;
}

/// <summary>A key (or controller button) and what it does, for the hint line and the help overlay.</summary>
public readonly struct HudKey
{
    public readonly HudString Key;
    public readonly HudString Action;

    /// <summary>A key whose label is the same in both languages (a letter, Tab, Esc).</summary>
    public HudKey(string key, string english, string uzbek) : this(key, key, english, uzbek) { }

    public HudKey(string key, string keyUzbek, string english, string uzbek)
    {
        Key = new HudString(key, keyUzbek);
        Action = new HudString(english, uzbek);
    }
}

/// <summary>The Car Explorer HUD's own texts. Node names and descriptions come from the explorer's model.</summary>
public static class CarExplorerHudText
{
    public static readonly HudString Structure = new HudString("STRUCTURE", "TUZILMA");
    public static readonly HudString PartsSuffix = new HudString(" parts", " ta qism");
    public static readonly HudString SystemsSuffix = new HudString(" systems", " ta tizim");
    public static readonly HudString Parts = new HudString("Parts", "Qismlar");
    public static readonly HudString Systems = new HudString("Systems", "Tizimlar");
    public static readonly HudString Material = new HudString("Material", "Material");
    public static readonly HudString Contains = new HudString("Contains", "Tarkibi");
    public static readonly HudString UnitsSuffix = new HudString(" units", " ta bo‘lak");

    public static readonly HudString[] ViewButtons =
    {
        new HudString("Normal", "Oddiy"), new HudString("X-ray", "Rentgen"), new HudString("Explode", "Ajratish")
    };

    public static readonly HudString[] ViewNames =
    {
        new HudString("Normal view", "Oddiy ko‘rinish"), new HudString("X-ray view", "Rentgen ko‘rinishi"),
        new HudString("Exploded view", "Ajratilgan ko‘rinish")
    };

    // Indexed by CarNodeKind.
    public static readonly HudString[] Kinds =
    {
        new HudString("VEHICLE", "AVTOMOBIL"), new HudString("SYSTEM", "TIZIM"), new HudString("ASSEMBLY", "YIG‘MA"),
        new HudString("SUB-ASSEMBLY", "KICHIK YIG‘MA"), new HudString("PART", "QISM")
    };

    public static readonly HudString StatusOut = new HudString("OUT OF THE CAR", "CHIQARILGAN");
    public static readonly HudString StatusApart = new HudString("APART", "AJRATILGAN");
    public static readonly HudString StatusMoving = new HudString("MOVING", "HARAKATDA");

    public static readonly HudString TakeOut = new HudString("Take out", "Chiqarish");
    public static readonly HudString Explode = new HudString("Explode", "Ajratish");
    public static readonly HudString Collapse = new HudString("Collapse", "Yig‘ish");
    public static readonly HudString PutBack = new HudString("Put back", "Joyiga qaytarish");
    public static readonly HudString Reset = new HudString("Reset", "Asl holat");
    public static readonly HudString Inspect = new HudString("Inspect", "Yaqindan ko‘rish");
    public static readonly HudString ExitInspect = new HudString("Exit inspect", "Yurishga qaytish");

    public static readonly HudString StructureTab = new HudString("Structure", "Tuzilma");
    public static readonly HudString ExploreChip = new HudString("Click a part to explore", "Qismni bosib o‘rganing");
    public static readonly HudString More = new HudString("More", "Batafsil");
    public static readonly HudString Less = new HudString("Less", "Qisqaroq");

    public static readonly HudString OverviewHint = new HudString(
        "Select a part on the car, or a row in STRUCTURE. Explode takes the selected unit apart one level at a time.",
        "Avtomobildagi qismni yoki TUZILMA ro‘yxatidagi qatorni tanlang. " +
        "Ajratish tanlangan bo‘lakni bosqichma-bosqich ochadi.");

    public static readonly HudString GreetingTitle = new HudString("Remy · Consultant", "Remy · Konsultant");
    public static readonly HudString GreetingBody = new HudString(
        "Welcome to Aurelle! Select any part of the car, or a row in STRUCTURE, and I will explain how it works.",
        "Aurelle ko‘rgazma zaliga xush kelibsiz! Avtomobilning istalgan qismini yoki TUZILMA ro‘yxatidagi " +
        "qatorni tanlang, men uning qanday ishlashini tushuntiraman.");

    public static readonly HudString HelpTitle = new HudString("Controls", "Boshqaruv");
    public static readonly HudString HelpDesktop = new HudString("KEYBOARD AND MOUSE", "KLAVIATURA VA SICHQONCHA");
    public static readonly HudString HelpXr = new HudString("XR CONTROLLERS", "XR KONTROLLERLARI");
    public static readonly HudString HelpCloseDesktop = new HudString(
        "H or click outside to close", "Yopish uchun H yoki tashqariga bosing");
    public static readonly HudString HelpCloseXr = new HudString(
        "Point and pull the trigger outside to close", "Yopish uchun tashqariga qaratib tetikni bosing");

    public static readonly HudString CursorLocked = new HudString("free the cursor", "kursorni bo‘shatish");
    public static readonly HudString CursorFree = new HudString("look around", "atrofga qarash");

    /// <summary>Desktop hint near the car. The Tab entry's text follows the cursor state.</summary>
    public static readonly HudKey[] HintNear =
    {
        new HudKey("WASD", "move", "yurish"), new HudKey("Mouse", "Sichqoncha", "look", "qarash"),
        new HudKey("Click", "Bosish", "select", "tanlash"), new HudKey("T", "X-ray", "rentgen"),
        new HudKey("O", "explode", "ajratish"), new HudKey("R", "back", "orqaga"),
        new HudKey("Tab", "cursor", "kursor"), new HudKey("I", "hide panels", "panellarni yashirish"),
        new HudKey("H", "help", "yordam")
    };

    /// <summary>Desktop hint near the car while something is selected: F frames it with the inspect camera.</summary>
    public static readonly HudKey[] HintNearSelected =
    {
        new HudKey("WASD", "move", "yurish"), new HudKey("Mouse", "Sichqoncha", "look", "qarash"),
        new HudKey("Click", "Bosish", "select", "tanlash"), new HudKey("F", "inspect", "yaqindan ko‘rish"),
        new HudKey("T", "X-ray", "rentgen"), new HudKey("O", "explode", "ajratish"), new HudKey("R", "back", "orqaga"),
        new HudKey("Tab", "cursor", "kursor"), new HudKey("I", "hide panels", "panellarni yashirish"),
        new HudKey("H", "help", "yordam")
    };

    /// <summary>Desktop hint near the car while the visitor has hidden the panels (I).</summary>
    public static readonly HudKey[] HintHidden =
    {
        new HudKey("WASD", "move", "yurish"), new HudKey("Mouse", "Sichqoncha", "look", "qarash"),
        new HudKey("Click", "Bosish", "select", "tanlash"), new HudKey("Tab", "cursor", "kursor"),
        new HudKey("I", "show panels", "panellarni ko‘rsatish"), new HudKey("H", "help", "yordam")
    };

    // The same in a small window (CarExplorerHudScaler.Compact): only what the panels do not show already.
    public static readonly HudKey[] HintNearCompact =
    {
        new HudKey("Click", "Bosish", "select", "tanlash"), new HudKey("Tab", "cursor", "kursor"),
        new HudKey("I", "hide panels", "panellarni yashirish"), new HudKey("H", "help", "yordam")
    };

    public static readonly HudKey[] HintNearSelectedCompact =
    {
        new HudKey("F", "inspect", "yaqindan ko‘rish"), new HudKey("Tab", "cursor", "kursor"),
        new HudKey("I", "hide panels", "panellarni yashirish"), new HudKey("H", "help", "yordam")
    };

    public static readonly HudKey[] HintHiddenCompact =
    {
        new HudKey("Tab", "cursor", "kursor"), new HudKey("I", "show panels", "panellarni ko‘rsatish"),
        new HudKey("H", "help", "yordam")
    };

    public static readonly HudKey[] HintFarCompact =
    {
        new HudKey("WASD", "move", "yurish"), new HudKey("Tab", "cursor", "kursor"), new HudKey("H", "help", "yordam")
    };

    /// <summary>Desktop hint while the inspect camera orbits a node (CarExplorerInspect.Active).</summary>
    public static readonly HudKey[] HintInspect =
    {
        new HudKey("Drag", "Sudrash", "orbit", "aylantirish"), new HudKey("Right-drag", "O‘ng sudrash", "pan", "surish"),
        new HudKey("Wheel", "G‘ildirak", "zoom", "yaqinlashtirish"), new HudKey("F / Esc", "walk", "yurish"),
        new HudKey("H", "help", "yordam")
    };

    /// <summary>Desktop hint away from the car (outside, on the way in). The entrance doors open on approach.</summary>
    public static readonly HudKey[] HintFar =
    {
        new HudKey("WASD", "move", "yurish"), new HudKey("Mouse", "Sichqoncha", "look", "qarash"),
        new HudKey("Tab", "cursor", "kursor"), new HudKey("H", "help", "yordam")
    };

    public static readonly HudKey[] HintXr =
    {
        new HudKey("Trigger", "Tetik", "select", "tanlash"), new HudKey("Grip", "Yon tugma", "grab", "ushlash"),
        new HudKey("A", "next view", "keyingi ko‘rinish"), new HudKey("B", "back", "orqaga"),
        new HudKey("Menu", "Menyu", "reset", "asl holat")
    };

    public static readonly HudKey[] HelpDesktopKeys =
    {
        new HudKey("WASD", "Walk (arrow keys too)", "Yurish (strelkalar ham)"),
        new HudKey("Mouse", "Sichqoncha", "Look around while the cursor is locked",
            "Kursor qulflanganda atrofga qarash"),
        new HudKey("Click", "Bosish", "Select a part: screen centre, or the pointer when the cursor is free",
            "Qismni tanlash: ekran markazi yoki erkin kursor"),
        new HudKey("2× click", "2× bosish", "A row in STRUCTURE: take it apart one level",
            "TUZILMA qatori: bir bosqich ajratish"),
        new HudKey("F", "Inspect the selection: drag to orbit, right-drag to pan, wheel to zoom; F or Esc walks on",
            "Tanlanganni yaqindan ko‘rish: sudrab aylantiring, o‘ng tugma bilan suring, g‘ildirak bilan " +
            "yaqinlashtiring; F yoki Esc — yurishga qaytish"),
        new HudKey("T", "X-ray on / off", "Rentgenni yoqish / o‘chirish"),
        new HudKey("O", "Explode the whole car", "Butun avtomobilni ajratish"),
        new HudKey("R", "One step back", "Bir qadam orqaga"),
        new HudKey("Esc", "Clear the selection, free the cursor", "Tanlovni bekor qilish, kursorni bo‘shatish"),
        new HudKey("Tab", "Free / lock the cursor to use the panels",
            "Panellar uchun kursorni bo‘shatish / qulflash"),
        new HudKey("E", "Entrance doors: they open as you approach; E opens or closes them by hand",
            "Kirish eshiklari yaqinlashganda o‘zi ochiladi; E — qo‘lda ochish / yopish"),
        new HudKey("P", "STRUCTURE tree: open / close", "TUZILMA daraxti: ochish / yopish"),
        new HudKey("I", "Hide / show all panels", "Barcha panellarni yashirish / ko‘rsatish"),
        new HudKey("Q / U", "English / Uzbek", "Inglizcha / O‘zbekcha"),
        new HudKey("H", "This help", "Ushbu yordam")
    };

    public static readonly HudKey[] HelpXrKeys =
    {
        new HudKey("Trigger", "Tetik", "Select a part, press a panel button", "Qismni tanlash, panel tugmasini bosish"),
        new HudKey("Grip", "Yon tugma", "Grab a part and look at it closely", "Qismni ushlab, yaqindan ko‘rish"),
        new HudKey("A", "Next view: normal, X-ray, exploded", "Keyingi ko‘rinish: oddiy, rentgen, ajratilgan"),
        new HudKey("B", "One step back", "Bir qadam orqaga"),
        new HudKey("Menu", "Menyu", "Complete car (left controller)", "To‘liq avtomobil (chap kontroller)"),
        new HudKey("Sticks", "Joystiklar", "Move and turn", "Yurish va burilish")
    };
    // ------------------------------------------------------------------ materials

    private static Dictionary<string, string> materialTerms;
    private static readonly Dictionary<string, string> materialCache = new Dictionary<string, string>();

    /// <summary>
    /// The Uzbek (Latin) name of a material the model gives in English (CarStructureNode.Material), for the card.
    /// The car's materials are in a phrase table, so they read naturally; a material added later is translated term
    /// by term: list items (", "), "and", "or", "in", "over" and "with" become Uzbek phrases, grades and codes
    /// (6082-T6, 10.9, 100Cr6, EN-GJS-500, EPDM) stay as they are, and a word the table does not know stays in
    /// English. Each string is translated once and cached.
    /// </summary>
    public static string MaterialUzbek(string english)
    {
        if (string.IsNullOrEmpty(english)) return english ?? string.Empty;
        if (materialCache.TryGetValue(english, out string cached)) return cached;
        string trimmed = english.Trim();
        string uzbek = Capitalise(Terms.TryGetValue(trimmed.ToLowerInvariant(), out string whole)
            ? whole
            : TranslateList(trimmed));
        materialCache[english] = uzbek;
        return uzbek;
    }

    /// <summary>The material in the card's language.</summary>
    public static string MaterialName(string english, bool uzbek) => uzbek ? MaterialUzbek(english) : english;

    /// <summary>
    /// True when the material table knows the English term (a word or a phrase, any case). For tests: a word of a
    /// material that is neither known nor a grade stays in English on the Uzbek card.
    /// </summary>
    public static bool IsKnownMaterialTerm(string english) =>
        !string.IsNullOrEmpty(english) && Terms.ContainsKey(english.ToLowerInvariant());

    /// <summary>True for a grade, code or acronym that stays as it is (6082-T6, 10.9, 100Cr6, EN-GJS-500, EPDM).</summary>
    public static bool IsMaterialCode(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        bool letters = false, lower = false;
        foreach (char ch in word)
        {
            if (char.IsDigit(ch)) return true;
            if (char.IsLetter(ch))
            {
                letters = true;
                if (char.IsLower(ch)) lower = true;
            }
        }
        return letters && !lower && word.Length >= 2;
    }

    // "A, B; C": each item on its own (commas inside brackets stay with their item).
    private static string TranslateList(string text)
    {
        StringBuilder joined = null;
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '(') depth++;
            else if (ch == ')') depth = depth > 0 ? depth - 1 : 0;
            else if ((ch == ',' || ch == ';') && depth == 0)
            {
                if (joined == null) joined = new StringBuilder();
                joined.Append(TranslateItem(text.Substring(start, i - start).Trim())).Append(ch).Append(' ');
                start = i + 1;
            }
        }
        string last = TranslateItem(text.Substring(start).Trim());
        return joined == null ? last : joined.Append(last).ToString();
    }

    private static readonly string[] JoinsEnglish = { " and ", " & ", " or ", " / ", " + " };
    private static readonly string[] JoinsUzbek = { " va ", " va ", " yoki ", " / ", " + " };

    private static string TranslateItem(string item)
    {
        if (item.Length == 0) return item;
        if (Terms.TryGetValue(item.ToLowerInvariant(), out string whole)) return whole;

        // "A (B)": the note in brackets on its own.
        int open = item.IndexOf('(');
        int close = item.LastIndexOf(')');
        if (open >= 0 && close > open)
        {
            string before = item.Substring(0, open).Trim();
            string inside = item.Substring(open + 1, close - open - 1).Trim();
            string after = item.Substring(close + 1).Trim();
            string result = (before.Length > 0 ? TranslateItem(before) + " " : string.Empty) + "(" +
                            TranslateList(inside) + ")";
            return after.Length > 0 ? result + " " + TranslateItem(after) : result;
        }

        // Two materials: "A and B", "A or B", "A / B".
        for (int i = 0; i < JoinsEnglish.Length; i++)
        {
            int at = item.IndexOf(JoinsEnglish[i], System.StringComparison.OrdinalIgnoreCase);
            if (at <= 0) continue;
            return TranslateItem(item.Substring(0, at).Trim()) + JoinsUzbek[i] +
                   TranslateItem(item.Substring(at + JoinsEnglish[i].Length).Trim());
        }

        // One material in, over or with another: Uzbek names the place first.
        int inAt = item.IndexOf(" in ", System.StringComparison.OrdinalIgnoreCase);
        if (inAt > 0)
        {
            string place = TranslateItem(item.Substring(inAt + 4).Trim());
            string what = TranslateItem(item.Substring(0, inAt).Trim());
            bool word = place.Length > 0 && char.IsLetter(place[place.Length - 1]) &&
                        char.IsLower(place[place.Length - 1]);
            return place + (word ? "dagi " : "-dagi ") + what;
        }
        foreach (string over in new[] { " over ", " on " })
        {
            int at = item.IndexOf(over, System.StringComparison.OrdinalIgnoreCase);
            if (at <= 0) continue;
            return TranslateItem(item.Substring(at + over.Length).Trim()) + " ustida " +
                   TranslateItem(item.Substring(0, at).Trim());
        }
        int withAt = item.IndexOf(" with ", System.StringComparison.OrdinalIgnoreCase);
        if (withAt > 0)
            return TranslateItem(item.Substring(0, withAt).Trim()) + ", " +
                   TranslateItem(item.Substring(withAt + 6).Trim()) + " bilan";

        return TranslateWords(item);
    }

    // Word by word, taking the longest known term first ("chilled cast iron" before "cast iron" before "iron").
    private static string TranslateWords(string text)
    {
        string[] words = text.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        var result = new StringBuilder();
        int i = 0;
        while (i < words.Length)
        {
            // English articles have no Uzbek word ("in a glass-fibre sleeve").
            string article = words[i].ToLowerInvariant();
            if (article == "a" || article == "an" || article == "the")
            {
                i++;
                continue;
            }
            string term = null;
            int taken = 1;
            for (int n = System.Math.Min(4, words.Length - i); n >= 2 && term == null; n--)
            {
                if (Terms.TryGetValue(string.Join(" ", words, i, n).ToLowerInvariant(), out string found))
                {
                    term = found;
                    taken = n;
                }
            }
            if (term == null) term = TranslateWord(words[i]);
            if (result.Length > 0) result.Append(' ');
            result.Append(term);
            i += taken;
        }
        return result.ToString();
    }

    private static string TranslateWord(string word)
    {
        string lower = word.ToLowerInvariant();
        if (Terms.TryGetValue(lower, out string known)) return known;
        if (IsMaterialCode(word)) return word;
        // A plural the table knows in the singular: "inserts" -> "qo‘ymalar".
        if (lower.Length > 3 && lower[lower.Length - 1] == 's' &&
            Terms.TryGetValue(lower.Substring(0, lower.Length - 1), out string single) && single.IndexOf(' ') < 0)
            return single + "lar";
        // Hyphenated: each part on its own ("zinc-nickel" -> "rux-nikel", "laser-welded" -> "lazer payvandlangan").
        int dash = lower.IndexOf('-');
        if (dash > 0 && dash < lower.Length - 1)
        {
            string[] parts = word.Split('-');
            bool participle = parts[parts.Length - 1].EndsWith("ed", System.StringComparison.OrdinalIgnoreCase);
            var joined = new StringBuilder();
            bool any = false;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                string translated = part;
                if (Terms.TryGetValue(part.ToLowerInvariant(), out string found))
                {
                    translated = found;
                    any = true;
                }
                if (i > 0) joined.Append(participle ? ' ' : '-');
                joined.Append(translated);
            }
            if (any) return joined.ToString();
        }
        // Unknown: stays in English.
        return word;
    }

    private static string Capitalise(string text)
    {
        if (string.IsNullOrEmpty(text) || !char.IsLower(text[0])) return text;
        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    private static Dictionary<string, string> Terms
    {
        get
        {
            if (materialTerms != null) return materialTerms;
            var t = new Dictionary<string, string>(400);
            void Add(string uzbek, params string[] english)
            {
                foreach (string key in english) t[key] = uzbek;
            }

            // Steels.
            Add("po‘lat", "steel");
            Add("po‘latlar", "steels");
            Add("quyma po‘lat", "cast steel");
            Add("bolg‘alangan po‘lat", "forged steel", "drop-forged steel");
            Add("prujina po‘lati", "spring steel");
            Add("klapan po‘lati", "valve steel");
            Add("podshipnik po‘lati", "bearing steel");
            Add("asbob po‘lati", "tool steel");
            Add("zanglamaydigan po‘lat", "stainless steel", "stainless");
            Add("uglerodli po‘lat", "carbon steel");
            Add("kam uglerodli po‘lat", "low-carbon steel", "mild steel");
            Add("yuqori uglerodli po‘lat", "high-carbon steel");
            Add("legirlangan po‘lat", "alloy steel");
            Add("borli po‘lat", "boron steel");
            Add("xromli po‘lat", "chrome steel");
            Add("konstruksion po‘lat", "structural steel");
            Add("elektrotexnik po‘lat", "electrical steel", "silicon steel");
            Add("list po‘lat", "sheet steel");
            Add("yuqori mustahkam po‘lat", "high-strength steel");
            Add("xrom-kremniyli prujina po‘lati", "chrome-silicon spring steel");
            Add("xrom-kremniyli", "chrome-silicon");
            Add("xrom-vanadiyli", "chrome-vanadium", "chrome vanadium");
            Add("xrom-molibdenli", "chrome-molybdenum", "chrome-moly", "chromoly");
            Add("po‘lat quvur", "steel tube", "steel tubing", "steel pipe");

            // Cast irons.
            Add("temir", "iron");
            Add("cho‘yan", "cast iron", "cast-iron");
            Add("kulrang cho‘yan", "grey iron", "gray iron", "grey cast iron", "gray cast iron");
            Add("oqartirilgan cho‘yan", "chilled cast iron", "chilled iron");
            Add("yuqori mustahkam cho‘yan", "ductile iron", "ductile cast iron");
            Add("sharsimon grafitli cho‘yan", "nodular iron", "nodular cast iron", "spheroidal graphite iron",
                "sg iron");
            Add("bolg‘alanuvchan cho‘yan", "malleable iron");
            Add("vermikulyar grafitli cho‘yan", "compacted graphite iron");

            // Light and other metals.
            Add("alyuminiy", "aluminium", "aluminum");
            Add("quyma alyuminiy", "cast aluminium", "cast aluminum", "cast-aluminium", "cast-aluminum");
            Add("alyuminiy qotishmasi", "aluminium alloy", "aluminum alloy");
            Add("magniy", "magnesium");
            Add("magniy qotishmasi", "magnesium alloy");
            Add("titan", "titanium");
            Add("titan qotishmasi", "titanium alloy");
            Add("yengil qotishma", "light alloy", "light-alloy");
            Add("qotishma", "alloy");
            Add("mis", "copper");
            Add("mis qotishmasi", "copper alloy");
            Add("jez", "brass");
            Add("bronza", "bronze");
            Add("xrom", "chrome", "chromium");
            Add("rux", "zinc");
            Add("nikel", "nickel");
            Add("qalay", "tin");
            Add("qo‘rg‘oshin", "lead");
            Add("volfram", "tungsten");
            Add("molibden", "molybdenum");
            Add("vanadiy", "vanadium");
            Add("marganes", "manganese");
            Add("kremniy", "silicon");
            Add("uglerod", "carbon");
            Add("grafit", "graphite");
            Add("litiy", "lithium");
            Add("neodim", "neodymium");
            Add("oltin", "gold");
            Add("kumush", "silver");
            Add("platina", "platinum");
            Add("palladiy", "palladium");
            Add("rodiy", "rhodium");
            Add("kobalt", "cobalt");
            Add("ferrit", "ferrite");
            Add("metall", "metal");
            Add("sinterlangan metall", "sintered metal", "powder metal");

            // Coatings and finishes.
            Add("rux lamelli qoplama", "zinc flake", "zinc-flake", "zinc flake coating");
            Add("ruxlangan", "zinc-plated", "zinc plated", "galvanised", "galvanized");
            Add("issiq usulda ruxlangan", "hot-dip galvanised", "hot-dip galvanized");
            Add("nikellangan", "nickel-plated");
            Add("xromlangan", "chrome-plated", "chromed", "chromium-plated");
            Add("qattiq xromlangan", "hard-chromed", "hard-chrome-plated", "hard chromed");
            Add("anodlangan", "anodised", "anodized");
            Add("bo‘yalgan", "painted");
            Add("qora bo‘yalgan", "black-painted");
            Add("bo‘yoq", "paint");
            Add("kukun bo‘yoqli", "powder-coated", "powder coated");
            Add("kukun bo‘yoq", "powder coat", "powder coating");
            Add("kataforez qoplamali", "e-coated", "electrocoated", "cataphoretic");
            Add("kataforez qoplama", "e-coat", "electrocoat");
            Add("qoplamali", "coated");
            Add("qoplama", "coating", "lining", "linings");
            Add("qoplangan", "plated");
            Add("laklangan", "lacquered", "varnished");
            Add("lak", "lacquer", "varnish");
            Add("emallangan", "enamelled", "enameled");
            Add("emal", "enamel");
            Add("fosfatlangan", "phosphated");
            Add("moylangan", "oiled", "greased");
            Add("grunt", "primer");
            Add("gruntlangan", "primed");
            Add("jilolangan", "polished");
            Add("cho‘tkalangan", "brushed");
            Add("yarim mat", "satin");
            Add("mat", "matte", "matt");
            Add("yaltiroq", "gloss", "glossy");

            // Colours.
            Add("qora", "black");
            Add("oq", "white");
            Add("kulrang", "grey", "gray");
            Add("qizil", "red");
            Add("ko‘k", "blue");
            Add("yashil", "green");
            Add("sariq", "yellow");
            Add("to‘q sariq", "orange");
            Add("jigarrang", "brown");
            Add("to‘q", "dark");
            Add("yorqin", "bright");

            // How it is made.
            Add("quyma", "cast", "moulded", "molded");
            Add("bosim ostida quyilgan", "die-cast", "die cast", "pressure die-cast", "injection-moulded",
                "injection-molded", "injection moulded", "injection molded");
            Add("qolipga quyilgan", "sand-cast", "sand cast");
            Add("aniq quyma", "investment-cast", "investment cast");
            Add("puflab shakllangan", "blow-moulded", "blow-molded");
            Add("bolg‘alangan", "forged", "drop-forged");
            Add("shtamplangan", "pressed", "stamped");
            Add("mexanik ishlangan", "machined");
            Add("prokatlangan", "rolled");
            Add("sovuq prokatlangan", "cold-rolled");
            Add("issiq prokatlangan", "hot-rolled");
            Add("ekstruziyalangan", "extruded");
            Add("cho‘zilgan", "drawn");
            Add("sovuq cho‘zilgan", "cold-drawn");
            Add("payvandlangan", "welded");
            Add("kavsharlangan", "brazed", "soldered");
            Add("toblangan", "hardened", "tempered", "quenched", "toughened");
            Add("sementlangan", "case-hardened", "carburised", "carburized");
            Add("induksion toblangan", "induction-hardened");
            Add("to‘liq toblangan", "through-hardened");
            Add("termik ishlangan", "heat-treated");
            Add("yumshatilgan", "annealed");
            Add("azotlangan", "nitrided");
            Add("drobli ishlangan", "shot-peened");
            Add("sinterlangan", "sintered");
            Add("o‘rilgan", "braided");
            Add("to‘qilgan", "woven", "knitted");
            Add("tikilgan", "stitched");
            Add("armirlangan", "reinforced");
            Add("shisha tolali", "glass-filled", "glass-fibre", "glass-fiber", "glass fibre-reinforced",
                "glass-fibre-reinforced", "glass-fiber-reinforced");
            Add("shisha tola bilan armirlangan", "glass-reinforced");
            Add("tola bilan armirlangan", "fibre-reinforced", "fiber-reinforced");
            Add("to‘ldirilgan", "filled");
            Add("ko‘p qatlamli", "laminated");
            Add("tonirovkalangan", "tinted");
            Add("izolyatsiyalangan", "insulated");
            Add("yopishtirilgan", "bonded");
            Add("vulkanlangan", "vulcanised", "vulcanized");
            Add("teshikli", "perforated");
            Add("gofrirlangan", "corrugated");
            Add("ko‘pirtirilgan", "expanded");
            Add("quvursimon", "tubular");
            Add("ichi bo‘sh", "hollow");
            Add("yaxlit", "solid");
            Add("egiluvchan", "flexible");
            Add("qattiq", "rigid", "hard");
            Add("yumshoq", "soft");
            Add("yuqori mustahkam", "high-strength");
            Add("kam uglerodli", "low-carbon");
            Add("yuqori uglerodli", "high-carbon");
            Add("yengil", "light", "lightweight");
            Add("og‘ir", "heavy");
            Add("issiqbardosh", "heat-resistant");
            Add("yuqori haroratga chidamli", "high-temperature");
            Add("olovbardosh", "fire-resistant", "flame-retardant");
            Add("yarim shaffof", "translucent");
            Add("shaffof", "transparent", "clear");
            Add("tabiiy", "natural");
            Add("sintetik", "synthetic");
            Add("organik", "organic");
            Add("yarim metall", "semi-metallic");
            Add("kam metalli", "low-metallic");
            Add("yuqori kuchlanishli", "high-voltage", "high voltage");
            Add("qo‘rg‘oshin-kislotali", "lead-acid");
            Add("litiy-ionli", "lithium-ion", "li-ion");
            Add("asbestsiz", "asbestos-free");
            Add("lazer", "laser");

            // Rubbers and plastics.
            Add("rezina", "rubber");
            Add("tabiiy kauchuk", "natural rubber");
            Add("sintetik kauchuk", "synthetic rubber");
            Add("xloropren kauchuk", "chloroprene rubber");
            Add("xloropren", "chloroprene");
            Add("neopren", "neoprene");
            Add("nitril kauchuk", "nitrile rubber");
            Add("nitril", "nitrile");
            Add("silikon rezina", "silicone rubber");
            Add("silikon", "silicone");
            Add("ftorelastomer", "fluoroelastomer");
            Add("EPDM", "epdm");
            Add("NBR", "nbr");
            Add("FKM", "fkm");
            Add("elastomer", "elastomer");
            Add("termoplastik elastomer", "thermoplastic elastomer");
            Add("termoplastik", "thermoplastic");
            Add("termoreaktiv plastmassa", "thermoset");
            Add("plastmassa", "plastic");
            Add("plastmassalar", "plastics");
            Add("polimer", "polymer");
            Add("poliamid", "polyamide");
            Add("neylon", "nylon");
            Add("shisha tolali neylon", "glass-filled nylon");
            Add("shisha tolali poliamid", "glass-filled polyamide");
            Add("polipropilen", "polypropylene");
            Add("polietilen", "polyethylene");
            Add("polikarbonat", "polycarbonate");
            Add("poliuretan", "polyurethane");
            Add("poliefir", "polyester");
            Add("polistirol", "polystyrene");
            Add("poliatsetal", "polyoxymethylene", "acetal");
            Add("akril", "acrylic");
            Add("PVX", "pvc");
            Add("ABS", "abs");
            Add("PTFE", "ptfe");
            Add("epoksid", "epoxy");
            Add("smola", "resin");
            Add("kompozit", "composite");
            Add("tola", "fibre", "fiber");
            Add("uglerod tolasi", "carbon fibre", "carbon fiber");
            Add("uglerod tolali", "carbon-fibre", "carbon-fiber");
            Add("shisha tola", "glass fibre", "glass fiber");
            Add("uglerod tolali kompozit", "carbon fibre composite", "carbon fiber composite",
                "carbon-fibre composite", "carbon-fiber composite");
            Add("uglerod tola bilan armirlangan polimer", "carbon fibre reinforced polymer",
                "carbon-fibre reinforced polymer", "carbon fiber reinforced polymer",
                "carbon-fiber reinforced polymer", "cfrp");
            Add("shisha tola bilan armirlangan polimer", "glass fibre reinforced polymer",
                "glass-fibre reinforced polymer", "glass fiber reinforced polymer", "gfrp");
            Add("aramid", "aramid");
            Add("kevlar", "kevlar");

            // Glass, ceramics, trim.
            Add("shisha", "glass");
            Add("xavfsiz shisha", "safety glass");
            Add("toblangan shisha", "tempered glass", "toughened glass");
            Add("ko‘p qatlamli shisha", "laminated glass");
            Add("keramika", "ceramic", "ceramics");
            Add("uglerod-keramika", "carbon-ceramic");
            Add("charm", "leather");
            Add("sintetik charm", "synthetic leather", "faux leather", "artificial leather");
            Add("mato", "fabric", "cloth");
            Add("tekstil", "textile");
            Add("alkantara", "alcantara");
            Add("zamsh", "suede");
            Add("paxta", "cotton");
            Add("jun", "wool");
            Add("vinil", "vinyl");
            Add("gilam", "carpet");
            Add("ko‘pik", "foam");
            Add("poliuretan ko‘pik", "polyurethane foam");
            Add("kigiz", "felt");
            Add("po‘kak", "cork");
            Add("yog‘och", "wood");
            Add("qog‘oz", "paper");
            Add("karton", "cardboard");

            // Forms and other terms.
            Add("filtr materiali", "filter media", "filter medium");
            Add("filtr qog‘ozi", "filter paper");
            Add("filtr", "filter");
            Add("material", "media", "material");
            Add("friksion material", "friction material");
            Add("friksion", "friction");
            Add("aralashma", "compound");
            Add("quvur", "tube", "tubing", "pipe");
            Add("shlang", "hose");
            Add("o‘rilgan shlang", "braided hose");
            Add("sim", "wire");
            Add("kabel", "cable");
            Add("list", "sheet");
            Add("plastina", "plate");
            Add("sterjen", "bar", "rod");
            Add("to‘r", "mesh");
            Add("korpus", "housing", "case");
            Add("qobiq", "jacket", "sheath");
            Add("izolyatsiya", "insulation");
            Add("o‘zak", "core");
            Add("o‘ram", "winding");
            Add("qo‘yma", "insert");
            Add("vtulka", "bushing", "bush");
            Add("zichlagich", "seal");
            Add("qistirma", "gasket");
            Add("ekran", "shield");
            Add("issiqlik ekrani", "heat shield");
            Add("magnit", "magnet");
            Add("elektrolit", "electrolyte");
            Add("kislota", "acid");
            Add("suyuqlik", "fluid");
            Add("moy", "oil");
            Add("surkov moyi", "grease");
            Add("gel", "gel");
            Add("asalari uyasimon", "honeycomb");
            Add("element", "element");
            Add("klemma", "terminal");
            Add("akkumulyator", "battery");

            // More terms for systems still to come (brakes, electrics, cooling, trim): new internals translate too.
            Add("fenol", "phenolic");
            Add("fenol smolasi", "phenolic resin");
            Add("slyuda", "mica");
            Add("missiz", "copper-free");
            Add("qo‘rg‘oshinsiz", "lead-free");
            Add("qalaylangan", "tin-plated", "tinned");
            Add("kumushlangan", "silver-plated");
            Add("oltin qoplangan", "gold-plated");
            Add("qoplamali", "lined");
            Add("tsellyuloza", "cellulose");
            Add("burmali", "pleated");
            Add("faollashtirilgan ko‘mir", "activated carbon", "activated charcoal");
            Add("shpon", "veneer");
            Add("yog‘och shpon", "wood veneer");
            Add("yong‘oq", "walnut");
            Add("mikrotola", "microfibre", "microfiber");
            Add("linza", "lens");
            Add("reflektor", "reflector");
            Add("plastina", "lamination", "lamina");
            Add("plastinalar", "laminations");
            Add("sulfat", "sulphuric", "sulfuric");
            Add("sulfat kislota", "sulphuric acid", "sulfuric acid");
            Add("sovutish suyuqligi", "coolant");
            Add("antifriz", "antifreeze");
            Add("glikol", "glycol");
            Add("tormoz suyuqligi", "brake fluid");
            Add("motor moyi", "engine oil");
            Add("shovqin yutuvchi", "sound-deadening", "sound-absorbing", "acoustic");
            Add("izolyatsion", "insulating");
            Add("issiqlik izolyatsion", "heat-insulating", "thermal");
            Add("dempfer", "damping");
            Add("bitum", "bitumen");
            Add("mastika", "mastic");
            Add("germetik", "sealant");
            Add("yelim", "adhesive", "glue");
            Add("ruxlangan", "galvannealed");
            Add("mis-nikel", "copper-nickel");
            Add("nikel-metallgidrid", "nickel-metal hydride", "nimh");
            Add("ko‘mir", "charcoal");
            Add("teri", "hide");
            Add("kord", "cord");
            Add("po‘lat kord", "steel cord");
            Add("to‘qima", "weave");
            Add("dekorativ", "decorative");
            Add("yuqori zichlikdagi", "high-density");
            Add("past zichlikdagi", "low-density");
            Add("issiqqa chidamli", "heat-resistant");
            Add("zanglashga chidamli", "corrosion-resistant");
            Add("o‘tkazuvchan", "conductive");
            Add("tormoz", "brake");
            Add("ishqalanish qoplamasi", "friction lining", "brake lining");
            Add("disk", "disc", "disk");
            Add("kolodka", "pad");
            Add("kolodkalar", "pads");
            Add("qoplam", "skin");
            Add("bo‘yoq qatlami", "paint layer");
            Add("element", "cell");
            Add("elementlar", "cells");
            Add("to‘shama", "mat");
            Add("to‘shamalar", "mats");
            Add("tormoz kolodkasi", "brake pad");
            Add("tormoz kolodkalari", "brake pads");
            Add("tormoz diski", "brake disc", "brake disk", "brake rotor");
            Add("va", "and");
            Add("yoki", "or");
            Add("bilan", "with");

            // Terms of the generated internals' materials (brakes, drivetrain, fuel, cooling, electrical, structure).
            Add("elektronika", "electronics");
            Add("issiqlik tarqatgich", "heat sink");
            Add("qopqoq", "cover");
            Add("quritgich", "desiccant");
            Add("himoya quvuri", "conduit");
            Add("lenta", "tape");
            Add("ikki fazali", "dual-phase");
            Add("issiq shakllangan", "hot-formed");
            Add("aniq shtamplangan", "fine-blanked");
            Add("choksiz", "seamless");
            Add("o‘zgaruvchan qalinlikda prokatlangan", "tailor rolled", "tailor-rolled");
            Add("koekstruziya", "coextrusion");
            Add("xomut", "clamp");
            Add("yo‘l", "track");
            Add("g‘ilof", "boot", "sleeve");
            Add("uch", "end");
            Add("ushlagich", "retainer");
            Add("g‘altak", "coil");
            Add("kollektor", "header");
            Add("to‘qima qobiq", "braid");
            Add("korpus", "body");
            Add("stakan", "can");
            Add("dasta", "grip");
            Add("tayoqcha", "stalk");
            Add("qulf", "buckle");
            Add("astar", "liner");
            Add("rezba", "thread");
            Add("kontakt", "contact");
            // Units and codes written the same way in Uzbek.
            Add("mm", "mm");
            Add("mm²", "mm²");
            Add("MPa", "mpa");
            Add("NdFeB", "ndfeb");
            Add("x", "x");

            // The car's materials as a whole, where a phrase reads better than its words.
            Add("alyuminiy korpusdagi po‘lat", "steel in aluminium housing", "steel in aluminum housing");
            Add("po‘lat ustida plastmassa", "plastic over steel");
            Add("alyuminiy korpus, mis o‘ramlar", "aluminium housing, copper windings");

            materialTerms = t;
            return t;
        }
    }
}
