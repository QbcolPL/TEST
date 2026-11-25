using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FieldTakHub.Builder.Services.Shell;

// Reguły generatora paczek .ftak (etap 1 porządkowania modułu 1). Bez WPF — testowane w FieldTakHub.Builder.Tests.
// Tam, gdzie telefon coś sprawdza (aplikacja Android, core/FtakReader.kt, core/Atak.kt, core/MeshChannel.kt),
// Builder sprawdza to samo, żeby ekran nie obiecywał czegoś, czego telefon nie przyjmie.

/// <summary>
/// Adres serwera TAK strony (pole OtsHost w paczce v3.0): sama nazwa albo nazwa:port.
/// Telefon dzieli go tak samo (hostOnly / portOf); bez portu łączy się na 8089.
/// </summary>
internal static class TakServerAddress
{
    public const int DefaultCotPort = 8089;

    /// <summary>Opis problemu albo null, gdy adres jest poprawny lub pusty (pusty = strona bez serwera).</summary>
    public static string? Describe(string? address, bool english)
    {
        string a = (address ?? string.Empty).Trim();
        if (a.Length == 0) return null;
        string L(string pl, string en) => english ? en : pl;

        if (a.Contains("://", StringComparison.Ordinal))
            return L("Wpisz sam adres, bez http:// ani tak:// — np. tak.example.org:8089.",
                     "Enter just the address, without http:// or tak:// — e.g. tak.example.org:8089.");
        if (a.Any(char.IsWhiteSpace))
            return L("Adres nie może zawierać spacji.", "The address cannot contain spaces.");
        if (a.IndexOfAny(new[] { '/', '\\', '?', '#', '@' }) >= 0)
            return L("Wpisz sam adres serwera, bez ścieżki i znaków / \\ ? # @.",
                     "Enter just the server address, without a path or the characters / \\ ? # @.");

        int colons = a.Count(c => c == ':');
        if (colons > 1)
            return L("Adresy IPv6 nie są obsługiwane — wpisz nazwę serwera albo adres IPv4.",
                     "IPv6 addresses are not supported — enter a server name or an IPv4 address.");

        string host = a;
        if (colons == 1)
        {
            int at = a.IndexOf(':');
            host = a.Substring(0, at);
            string portText = a.Substring(at + 1);
            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65535)
                return L("Port po dwukropku musi być liczbą od 1 do 65535 — np. tak.example.org:8089.",
                         "The port after the colon must be a number from 1 to 65535 — e.g. tak.example.org:8089.");
        }

        if (host.Length == 0)
            return L("Przed dwukropkiem brakuje nazwy serwera.", "The server name before the colon is missing.");
        bool validChars = host.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-');
        if (!validChars || host.StartsWith('.') || host.EndsWith('.') || host.StartsWith('-') || host.Contains("..", StringComparison.Ordinal))
            return L("Nazwa serwera może zawierać tylko litery, cyfry, kropki i myślniki.",
                     "The server name can only contain letters, digits, dots and hyphens.");
        return null;
    }
}

internal enum ChannelKeyKind { Missing, Invalid, PublicDefault, WrongLength, Aes128, Aes256 }

/// <summary>
/// Klucz kanału Meshtastic (PSK, Base64). Budowanie paczki zamienia brakujący albo uszkodzony klucz na publiczny
/// klucz domyślny Meshtastic, więc ekran musi to powiedzieć wprost.
/// </summary>
internal static class ChannelKeySpec
{
    public static ChannelKeyKind Classify(string? base64)
    {
        string k = (base64 ?? string.Empty).Trim();
        if (k.Length == 0) return ChannelKeyKind.Missing;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(k); }
        catch (FormatException) { return ChannelKeyKind.Invalid; }
        return bytes.Length switch
        {
            0 => ChannelKeyKind.Missing,
            1 => ChannelKeyKind.PublicDefault,
            16 => ChannelKeyKind.Aes128,
            32 => ChannelKeyKind.Aes256,
            _ => ChannelKeyKind.WrongLength
        };
    }

    public static bool IsPrivate(string? base64)
    {
        var kind = Classify(base64);
        return kind == ChannelKeyKind.Aes128 || kind == ChannelKeyKind.Aes256;
    }

    /// <summary>Opis problemu albo null, gdy klucz jest prywatny (16 B lub 32 B).</summary>
    public static string? Describe(string? base64, bool english)
    {
        switch (Classify(base64))
        {
            case ChannelKeyKind.Missing:
                return english
                    ? "No key — the package would get the public Meshtastic key. Click “New key”."
                    : "Brak klucza — paczka dostałaby publiczny klucz Meshtastic. Kliknij „Losuj klucz”.";
            case ChannelKeyKind.Invalid:
                return english
                    ? "The key is not valid Base64 — the package would get the public Meshtastic key."
                    : "Klucz nie jest poprawnym Base64 — paczka dostałaby publiczny klucz Meshtastic.";
            case ChannelKeyKind.PublicDefault:
                return english
                    ? "This is the public default key — anyone with Meshtastic can read the channel."
                    : "To publiczny klucz domyślny — kanał może czytać każdy z Meshtastic.";
            case ChannelKeyKind.WrongLength:
                int length = Convert.FromBase64String((base64 ?? string.Empty).Trim()).Length;
                return english
                    ? $"The key has {length} bytes — Meshtastic needs 16 (AES-128) or 32 (AES-256)."
                    : $"Klucz ma {length} B — Meshtastic wymaga 16 B (AES-128) albo 32 B (AES-256).";
            default:
                return null;
        }
    }

    public static string OkText(string? base64, bool english) =>
        Classify(base64) == ChannelKeyKind.Aes128
            ? (english ? "✓ private key (AES-128)" : "✓ klucz prywatny (AES-128)")
            : (english ? "✓ private key (AES-256)" : "✓ klucz prywatny (AES-256)");
}

/// <summary>Link rejestracji OpenTAK dla ATAK — te same warunki co Atak.isEnrollUri w telefonie.</summary>
internal static class EnrollmentLinkSpec
{
    public const string Prefix = "tak://com.atakmap.app/enroll";

    public static string? Describe(string? link, bool english)
    {
        string t = (link ?? string.Empty).Trim();
        if (t.Length == 0) return null;
        if (!t.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return english
                ? "This is not an ATAK enrollment link (tak://com.atakmap.app/enroll?…) — the phone will skip it."
                : "To nie jest link rejestracji ATAK (tak://com.atakmap.app/enroll?…) — telefon go pominie.";
        var query = Query(t);
        foreach (string key in new[] { "host", "username", "token" })
        {
            if (!query.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
                return english
                    ? $"The link has no “{key}” parameter — the phone will skip it."
                    : $"W linku brakuje parametru „{key}” — telefon go pominie.";
        }
        return null;
    }

    private static Dictionary<string, string> Query(string link)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int q = link.IndexOf('?');
        if (q < 0) return result;
        string query = link.Substring(q + 1);
        int hash = query.IndexOf('#');
        if (hash >= 0) query = query.Substring(0, hash);
        foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            string key = Unescape(eq < 0 ? part : part.Substring(0, eq));
            string value = eq < 0 ? string.Empty : Unescape(part.Substring(eq + 1));
            if (!result.ContainsKey(key)) result[key] = value;
        }
        return result;
    }

    private static string Unescape(string text)
    {
        try { return Uri.UnescapeDataString(text.Replace('+', ' ')); }
        catch { return text; }
    }
}

/// <summary>Własny link kanału z aplikacji Meshtastic — te same warunki co MeshChannel.isChannelUrl w telefonie.</summary>
internal static class MeshChannelLinkSpec
{
    public static bool IsChannelLink(string? url)
    {
        string t = (url ?? string.Empty).Trim().ToLowerInvariant();
        return t.StartsWith("https://meshtastic.org/e/", StringComparison.Ordinal) && t.Contains('#');
    }

    public static string? Describe(string? url, bool english)
    {
        if (string.IsNullOrWhiteSpace(url) || IsChannelLink(url)) return null;
        return english
            ? "This is not a Meshtastic channel link (https://meshtastic.org/e/#…) — the package will use the channel built from the name and key."
            : "To nie jest link kanału Meshtastic (https://meshtastic.org/e/#…) — paczka użyje kanału zbudowanego z nazwy i klucza.";
    }
}

/// <summary>Nazwa kanału Meshtastic: urządzenia przyjmują najwyżej 11 bajtów UTF-8 (polska litera to 2 bajty).</summary>
internal static class ChannelNameSpec
{
    public const int MaxBytes = 11;

    /// <summary>Nazwa, którą dostanie urządzenie: przycięta do 11 bajtów, bez rozcinania znaków.</summary>
    public static string Effective(string? name)
    {
        string n = (name ?? string.Empty).Trim();
        if (Encoding.UTF8.GetByteCount(n) <= MaxBytes) return n;
        var sb = new StringBuilder();
        int bytes = 0;
        var elements = StringInfo.GetTextElementEnumerator(n);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            int count = Encoding.UTF8.GetByteCount(element);
            if (bytes + count > MaxBytes) break;
            sb.Append(element);
            bytes += count;
        }
        return sb.ToString();
    }

    public static string? Describe(string? name, bool english)
    {
        string n = (name ?? string.Empty).Trim();
        if (Encoding.UTF8.GetByteCount(n) <= MaxBytes) return null;
        string cut = Effective(n);
        return english
            ? $"Meshtastic accepts at most 11 bytes of channel name — devices will get “{cut}”."
            : $"Meshtastic przyjmuje najwyżej 11 bajtów nazwy kanału (polska litera to 2) — urządzenia dostaną „{cut}”.";
    }
}

/// <summary>
/// Wysyłanie pozycji przez ATAK. Paczka v3.0 niesie tylko nazwę profilu (PliProfile), a telefon zna dwa:
/// dynamiczny i stały (core/Atak.kt, reportingFromProfiles). Liczby w opisach to wartości, które ustawia telefon.
/// </summary>
internal static class PositionReportingSpec
{
    public const string Constant = "Constant";
    public const string Dynamic = "Dynamic";

    /// <summary>Pozycje listy w karcie strony (po polsku; wersja angielska idzie przez słownik BuilderI18n).</summary>
    public static readonly IReadOnlyList<string> Options = new[]
    {
        "Stałe — co 5 s (przez mesh co 15 s)",
        "Dynamiczne — od 2 do 20 s, zależnie od ruchu"
    };

    public static string Normalize(string? strategy) =>
        string.Equals((strategy ?? string.Empty).Trim(), Dynamic, StringComparison.OrdinalIgnoreCase) ? Dynamic : Constant;

    /// <summary>Profil w paczce: „Dynamic” albo dotychczasowy „ConstRateUnreliable” (telefon czyta go jako stały).</summary>
    public static string ToPliProfile(string? strategy) => Normalize(strategy) == Dynamic ? "Dynamic" : "ConstRateUnreliable";

    public static string Display(string? strategy) => Options[Normalize(strategy) == Dynamic ? 1 : 0];

    /// <summary>Pozycja listy (polska albo przetłumaczona) z powrotem na wartość modelu.</summary>
    public static string FromDisplay(string? display) =>
        (display ?? string.Empty).TrimStart().StartsWith("Dynam", StringComparison.OrdinalIgnoreCase) ? Dynamic : Constant;
}

/// <summary>
/// Region LoRa w nazwach Meshtastic (RegionCode). Wcześniej lista w karcie miała „US_915” i „ANZ_915”, których
/// nie znał ani koder linku kanału, ani telefon — obie strony zamieniały je na EU_868.
/// </summary>
internal static class LoraRegionSpec
{
    public static readonly IReadOnlyList<string> Options = new[] { "EU_868", "EU_433", "UA_868", "UA_433", "US", "ANZ" };

    private static readonly string[] Codes =
    {
        "UNSET", "US", "EU_433", "EU_868", "CN", "JP", "ANZ", "KR", "TW", "RU", "IN", "NZ_865", "TH", "LORA_24",
        "UA_433", "UA_868", "MY_433", "MY_919", "SG_923", "PH_433", "PH_868", "PH_915", "ANZ_433", "KZ_433", "KZ_863",
        "NP_865", "BR_902"
    };

    public static string Normalize(string? region)
    {
        string r = (region ?? string.Empty).Trim().ToUpperInvariant();
        if (r == "US_915") return "US";
        if (r == "ANZ_915") return "ANZ";
        return Array.IndexOf(Codes, r) > 0 ? r : "EU_868";
    }

    /// <summary>Numer regionu w protokole Meshtastic (1 = US, 3 = EU_868, 6 = ANZ…).</summary>
    public static int Code(string? region) => Array.IndexOf(Codes, Normalize(region));
}

/// <summary>Ważność paczek w godzinach od zbudowania; 0 — paczka nie wygasa.</summary>
internal static class PackageValiditySpec
{
    public const int MaxHours = 876000;
    public const string JsonKey = "packageValidityHours";

    public static bool TryParseHours(string? text, out int hours)
    {
        hours = 0;
        string t = (text ?? string.Empty).Trim();
        if (t.EndsWith("h", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 1).Trim();
        if (!int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value > MaxHours) return false;
        hours = value;
        return true;
    }

    /// <summary>Wartość z pliku projektu; brak albo bzdura — 0 (tak zachowywały się dotąd paczki).</summary>
    public static int FromJson(JsonNode? node) => node != null && TryParseHours(node.ToString(), out int hours) ? hours : 0;
}

/// <summary>Przykładowe dane, które Builder kiedyś sam wpisywał, a które trafiały do paczek i na plakaty.</summary>
internal static class SampleDataSpec
{
    /// <summary>Zmyślony numer, który był domyślnym telefonem alarmowym każdej strony.</summary>
    public const string RetiredSamplePhone = "+48 600 000 000";

    public static string CleanEmergencyPhone(string? phone)
    {
        string p = (phone ?? string.Empty).Trim();
        string digits = new string(p.Where(char.IsDigit).ToArray());
        return digits == "48600000000" ? string.Empty : p;
    }

    public static string SampleOverlayFileName(string sideFolder) => "strefa_" + sideFolder + ".kml";

    /// <summary>Dokładna treść przykładowej nakładki, którą tworzył przycisk folderu nakładek strony.</summary>
    public static string SampleOverlayContent(string sideFolder)
    {
        string upper = sideFolder.ToUpperInvariant();
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document><name>Warstwa "
            + upper + "</name><Placemark><name>HQ " + upper
            + "</name><Point><coordinates>19.0238,49.9135,0</coordinates></Point></Placemark></Document></kml>";
    }

    /// <summary>Tylko nietknięty plik przykładowy (ta sama nazwa i ta sama treść) — zmieniony przez kogoś zostaje.</summary>
    public static bool IsGeneratedSampleOverlay(string? fileName, string? sideFolder, string? content)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(sideFolder) || content == null) return false;
        return string.Equals(fileName, SampleOverlayFileName(sideFolder), StringComparison.OrdinalIgnoreCase)
            && string.Equals(content.TrimStart('﻿'), SampleOverlayContent(sideFolder), StringComparison.Ordinal);
    }

    /// <summary>
    /// Dwa zestawy kanałów głosowych, które dawny Builder dopisywał stronie bez kanałów — przy każdym otwarciu projektu,
    /// więc nie dało się ich trwale usunąć. Częstotliwości były zmyślone (145.500 MHz to kanał wywoławczy krótkofalowców).
    /// </summary>
    private static readonly (string Role, string Band, string Freq, string Tone)[][] RetiredSampleVoiceSets =
    {
        new[]
        {
            ("Dowodzenie HQ", "PMR", "CH 1 (446.00625)", "CTCSS 67.0 Hz"),
            ("Taktyczny Alfa", "VHF/UHF", "145.500 MHz", "CTCSS 88.5 Hz"),
            ("Ratunkowy ORGA", "PMR", "CH 8 (446.09375)", "Brak tonu")
        },
        new[]
        {
            ("Dowodzenie HQ", "PMR", "CH 4 (446.04375)", "CTCSS 100.0 Hz"),
            ("Taktyczny Bravo", "DMR", "433.450 MHz", "CC1 TS1 TG99"),
            ("Ratunkowy ORGA", "PMR", "CH 8 (446.09375)", "Brak tonu")
        }
    };

    /// <summary>Cała lista kanałów strony to nietknięty zestaw przykładowy. Lista zmieniona choćby jednym polem zostaje.</summary>
    public static bool IsRetiredSampleVoiceSet(IReadOnlyList<(string? Role, string? Band, string? Freq, string? Tone)>? channels)
    {
        if (channels == null || channels.Count == 0) return false;
        return RetiredSampleVoiceSets.Any(set => set.Length == channels.Count
            && set.Select((row, i) => Same(row.Role, channels[i].Role) && Same(row.Band, channels[i].Band)
                && Same(row.Freq, channels[i].Freq) && Same(row.Tone, channels[i].Tone)).All(match => match));

        static bool Same(string expected, string? actual) => string.Equals(expected, (actual ?? string.Empty).Trim(), StringComparison.Ordinal);
    }
}

/// <summary>Ustawienia strony do sprawdzenia; null — pole nieznane, bez sprawdzania.</summary>
internal sealed record SideSettings(
    string Name,
    string? ChannelKey = null,
    string? ServerAddress = null,
    string? EnrollmentLink = null,
    string? ChannelLink = null,
    string? ChannelName = null);

/// <summary>Uwaga do strony. Blocking — paczek nie da się zbudować poprawnie (np. dwie strony z tą samą nazwą).</summary>
internal sealed record SideProblem(string Side, string Message, bool Blocking);

internal static class SideSettingsSpec
{
    /// <summary>Nazwa folderu strony w out\ — ta sama zamiana co TacticalConfigPanel.SanitizeFileName + wielkie litery.</summary>
    public static string FolderKey(string? name)
    {
        string n = string.IsNullOrWhiteSpace(name) ? "Operacja_TAK" : name;
        foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        return n.ToUpperInvariant();
    }

    public static IReadOnlyList<SideProblem> Check(IEnumerable<SideSettings>? sides, bool english)
    {
        var list = (sides ?? Enumerable.Empty<SideSettings>()).Where(s => s != null).ToList();
        var problems = new List<SideProblem>();

        foreach (var group in list.GroupBy(s => FolderKey(s.Name)).Where(g => g.Count() > 1))
        {
            string names = string.Join(", ", group.Select(s => (s.Name ?? string.Empty).Trim()).Distinct(StringComparer.Ordinal));
            problems.Add(new SideProblem((group.First().Name ?? string.Empty).Trim(),
                english
                    ? $"Two factions share the name {names} — one package would overwrite the other. Rename one of them."
                    : $"Dwie strony mają tę samą nazwę ({names}) — paczka jednej nadpisałaby paczkę drugiej. Zmień nazwę jednej z nich.",
                true));
        }

        foreach (var s in list)
        {
            string side = (s.Name ?? string.Empty).Trim();
            bool customChannel = MeshChannelLinkSpec.IsChannelLink(s.ChannelLink);
            if (s.ChannelKey != null && !customChannel) Add(side, ChannelKeySpec.Describe(s.ChannelKey, english));
            if (s.ChannelName != null && !customChannel) Add(side, ChannelNameSpec.Describe(s.ChannelName, english));
            if (s.ChannelLink != null) Add(side, MeshChannelLinkSpec.Describe(s.ChannelLink, english));
            if (s.ServerAddress != null) Add(side, TakServerAddress.Describe(s.ServerAddress, english));
            if (s.EnrollmentLink != null) Add(side, EnrollmentLinkSpec.Describe(s.EnrollmentLink, english));
        }
        return problems;

        void Add(string side, string? message)
        {
            if (message != null) problems.Add(new SideProblem(side, message, false));
        }
    }
}

/// <summary>
/// Losowe adresy paczek w sieci Wi-Fi: /deploy/&lt;token&gt;.ftak zamiast /deploy/&lt;NAZWA STRONY&gt;.ftak,
/// którą każdy w tej samej sieci mógł zgadnąć. Jeden token na stronę, dopóki Builder działa.
/// </summary>
internal sealed class LanShareTokens
{
    private readonly Dictionary<string, string> _byFaction = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _byToken = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public string TokenFor(string faction)
    {
        lock (_gate)
        {
            if (_byFaction.TryGetValue(faction, out string? existing)) return existing;
            string token;
            do token = NewToken(); while (_byToken.ContainsKey(token));
            _byFaction[faction] = token;
            _byToken[token] = faction;
            return token;
        }
    }

    public bool TryResolve(string? token, out string faction)
    {
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(token) && _byToken.TryGetValue(token, out string? found))
            {
                faction = found;
                return true;
            }
        }
        faction = string.Empty;
        return false;
    }

    /// <summary>128 bitów losowości jako 32 małe znaki szesnastkowe — bezpieczne w adresie URL.</summary>
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}

/// <summary>
/// Migawka do budowania paczek z bieżącego stanu projektu (ten sam JSON, który zapisuje autozapis i plik .fthproj).
/// Wcześniej budowanie czytało plik projektu z dysku — bez zapisu był to szablon nowego projektu
/// („NOWA OPERACJA”, domyślne strony, pusty klucz), a z zapisanym projektem — jego ostatnia zapisana wersja.
/// </summary>
internal static class WorkspaceSnapshotSpec
{
    public static CleanWorkspaceSnapshot FromWorkspaceJson(JsonObject? root, string? fallbackEventName)
    {
        var state = new CleanWorkspaceSnapshot();
        if (!string.IsNullOrWhiteSpace(fallbackEventName)) state.EventName = fallbackEventName.Trim();
        if (root == null) return state;

        string eventName = Str(root, "eventName");
        if (!string.IsNullOrWhiteSpace(eventName)) state.EventName = eventName.Trim();

        if (root["factions"] is JsonArray factions)
        {
            foreach (var item in factions.OfType<JsonObject>())
            {
                var fac = new CleanFactionProfileModel
                {
                    Name = Fallback(Str(item, "Name", "name"), "BLUFOR"),
                    ColorHex = Fallback(Str(item, "ColorHex", "colorHex"), "#2684FF"),
                    ChannelName = Str(item, "ChannelName", "channelName").Trim(),
                    Psk = Str(item, "Psk", "psk").Trim(),
                    ChannelUrl = Str(item, "ChannelUrl", "channelUrl").Trim(),
                    LoraRegion = LoraRegionSpec.Normalize(Str(item, "LoraRegion", "loraRegion")),
                    LoraModemPreset = Fallback(Str(item, "LoraModemPreset", "loraModemPreset"), "SHORT_FAST"),
                    HopLimit = int.TryParse(Str(item, "HopLimit", "hopLimit"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int hop) ? hop : 3,
                    OtsHost = Str(item, "OtsHost", "otsHost").Trim(),
                    EnrollmentUri = Str(item, "EnrollmentUri", "enrollmentUri").Trim(),
                    EmergencyPhone = SampleDataSpec.CleanEmergencyPhone(Str(item, "EmergencyPhone", "emergencyPhone")),
                    PliProfile = PositionReportingSpec.ToPliProfile(Str(item, "PliStrategy", "pliStrategy")),
                    MeshProfile = "StationaryMesh"
                };

                if ((item["VoiceChannels"] ?? item["voiceChannels"]) is JsonArray voice)
                {
                    foreach (var channel in voice.OfType<JsonObject>())
                    {
                        fac.VoiceChannels.Add(new CleanVoiceChannelItem
                        {
                            RoleName = Str(channel, "roleName", "RoleName").Trim(),
                            BandType = Fallback(Str(channel, "bandType", "BandType"), "PMR"),
                            FreqMhz = Str(channel, "freqMhz", "FreqMhz").Trim(),
                            ToneOrCode = Str(channel, "toneOrCode", "ToneOrCode").Trim()
                        });
                    }
                }
                state.Factions.Add(fac);
            }
        }
        return state;
    }

    private static string Str(JsonObject obj, params string[] keys)
    {
        foreach (string key in keys)
        {
            string? value = obj[key]?.ToString();
            if (value != null) return value;
        }
        return string.Empty;
    }

    private static string Fallback(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
