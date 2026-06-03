using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace HSED_2_0;

public sealed record LiftStatePresentation(string Text, IBrush Brush, bool DoorOverlayFinished = true);

public static class LiftStateCatalog
{
    private static readonly Dictionary<int, string> GermanTexts = new()
    {
        [0x01] = "Notbrems.",
        [0x02] = "Init",
        [0x03] = "Suche",
        [0x04] = "Stillst.",
        [0x05] = "Fahrt",
        [0x06] = "Einfahrt",
        [0x07] = "Fehler",
        [0x08] = "CAN-Fehl.",
        [0x09] = "Inspekt.",
        [0x0A] = "Rückhol.",
        [0x0B] = "!Defekt!",
        [0x0C] = "Terminal",
        [0x0D] = "BG fehlen",
        [0x0E] = "Anfahren",
        [0x0F] = "Korrektur",
        [0x10] = "Lernfahrt",
        [0x11] = "SK fehlt",
        [0x12] = "Türfehler",
        [0x13] = "Tür offen",
        [0x14] = "Initfahrt",
        [0x15] = "Setup",
        [0x16] = "Antrieb",
        [0x17] = "Fehler,NH",
        [0x18] = "CAN-DRV",
        [0x19] = "SKb.Fahrt",
        [0x1A] = "Schützf.",
        [0x1B] = "Bremsfehl",
        [0x1C] = "AntrFahrt",
        [0x1D] = "CAN-FVE",
        [0x1E] = "CAN-ASE",
        [0x1F] = "CAN-PSE",
        [0x20] = "Fehler VO",
        [0x21] = "Fehler VU",
        [0x22] = "Fehler VOU",
        [0x23] = "Übertemp.",
        [0x24] = "Bremsüb.",
        [0x25] = "SK Dreht.",
        [0x26] = "SK FK-Tür",
        [0x27] = "SK SchTür",
        [0x28] = "Türz.Fehl",
        [0x29] = "Zonenfehl",
        [0x2A] = "KH5n.an",
        [0x2B] = "KH5n.aus",
        [0x2C] = "Akkubetr.",
        [0x2D] = "Schachtt.",
        [0x2E] = "FK-Licht",
        [0x2F] = "Überlast",
        [0x30] = "Nachholen",
        [0x31] = "CAN fehlt",
        [0x32] = "Fahrzeit",
        [0x33] = "Türtest",
        [0x34] = "Endschalt",
        [0x35] = "USV-Evak.",
        [0x36] = "Lichtvorh",
        [0x37] = "Üb.geschw",
        [0x38] = "Richtung!",
        [0x39] = "Hyd.druck",
        [0x3A] = "Fehl. SGE",
        [0x3B] = "Notabsenk",
        [0x3C] = "Schlupf >",
        [0x3D] = "Posdefekt",
        [0x3E] = "Notaus-T.",
        [0x3F] = "Montagef.",
        [0x40] = "Serv.mode",
        [0x41] = "InspGrube",
        [0x42] = "Totm.Stop",
        [0x43] = "AWG2 Fehl",
        [0x44] = "Schutzr.",
        [0x45] = "Schürze",
        [0x46] = "Evak.wart",
        [0x47] = "Geländer",
        [0x48] = "Phasenf.",
        [0x49] = "Stütze",
        [0x4A] = "Pos.abw.",
        [0x4B] = "Aufs.fehl",
        [0x4C] = "Aufs.einf",
        [0x4D] = "Aufs.ausf",
        [0x4E] = "Anheben",
        [0x4F] = "Aufsetzen",
        [0x50] = "Aufs.test",
        [0x51] = "Auf.druck",
        [0x52] = "Aufs.sign",
        [0x53] = "Kein Aufs",
        [0x54] = "Fehl.Korr",
        [0x55] = "Begrenzer",
        [0x56] = "SK4 Start",
        [0x57] = "OP einf.",
        [0x58] = "OP innen",
        [0x59] = "OP drehen",
        [0x5A] = "OP ausf.",
        [0x5B] = "OP außen",
        [0x5C] = "OP Entr.",
        [0x5D] = "OP Fehl.",
        [0x5E] = "Ramp.fahr",
        [0x5F] = "FU-Param.",
        [0x60] = "ASE Softw",
        [0x61] = "FVE Softw",
        [0x62] = "Tech.Test",
        [0x63] = "CAN-Tür",
        [0x64] = "Unkontr.B",
        [0x65] = "Verzöger.",
        [0x66] = "Ventilf.",
        [0x67] = "Rieg.test",
        [0x68] = "AWG-Fehl.",
        [0x69] = "SK-PSU",
        [0x6A] = "Demo-Mode",
        [0x6B] = "Quickstrt",
        [0x6C] = "Akkufehl.",
        [0x6D] = "Lichtschr",
        [0x6E] = "Palette->",
        [0x6F] = "Palette<-",
        [0x70] = "Pal.limit",
        [0x71] = "Notbefr.",
        [0x72] = "SK-Brücke",
        [0x73] = "Fehl-SKBr",
        [0x74] = "Fangkont.",
        [0x75] = "SKvorFang",
        [0x76] = "SK Tür",
        [0x77] = "LS-Fehler",
        [0x78] = "Türriegel",
        [0x79] = "Sich.fkt.",
        [0x7A] = "Bremstest",
        [0x7B] = "Brems.def",
        [0x7C] = "CAN-ISS",
        [0x7D] = "Insp.pos",
        [0x7E] = "Vent.test",
        [0x7F] = "Vent.def",
        [0x80] = "Türhemmg.",
        [0x81] = "Geblockt",
        [0x82] = "Schlaffs.",
        [0x83] = "Seildiff.",
        [0x84] = "Tür-Temp.",
        [0x85] = "USV-Fehl.",
        [0x86] = "Kolbenf.",
        [0x87] = "Tuning",
        [0x88] = "Pal.fehl.",
        [0x89] = "Surfing",
        [0x8A] = "USV-Lad.",
        [0x8B] = "Insp.MR",
        [0x8C] = "Max.-Last",
        [0x8D] = "Setup PSU",
        [0x8E] = "Par.fehl.",
        [0x8F] = "Sich.stop",
        [0x90] = "SK-ISS",
        [0x91] = "Setup ISS",
        [0x92] = "Fing.sch.",
        [0x93] = "BypFehler",
        [0x94] = "Notruf",
        [0x95] = "ResSRFehl",
        [0x96] = "Paternost",
        [0x97] = "Pat.Reset",
        [0x98] = "ISS-Slave",
        [0x99] = "ISSBypass",
        [0x9A] = "!Wartung!"
    };

    private static readonly HashSet<int> RunningStates =
    [
        0x03, 0x05, 0x0E, 0x10, 0x14, 0x1C, 0x30, 0x33, 0x35, 0x3B, 0x3F,
        0x4C, 0x4D, 0x4E, 0x4F, 0x50, 0x57, 0x58, 0x59, 0x5A, 0x5B, 0x5E, 0x62,
        0x6E, 0x6F, 0x71, 0x79, 0x7A, 0x7D, 0x7E, 0x87, 0x89, 0x06
    ];

    private static readonly HashSet<int> ServiceStates =
    [
        0x09, 0x0A, 0x0C, 0x15, 0x24, 0x40, 0x41, 0x46, 0x5F, 0x60, 0x61, 0x6A,
        0x6B, 0x8A, 0x8B, 0x8D, 0x91, 0x94, 0x0F
    ];

    private static readonly HashSet<int> WarningStates =
    [
        0x13, 0x2C, 0x2D, 0x2E, 0x2F, 0x36, 0x44, 0x45, 0x47, 0x49, 0x51,
        0x52, 0x53, 0x55, 0x5C, 0x70, 0x72, 0x74, 0x75, 0x78, 0x80, 0x8C, 0x92,
        0x9A
    ];

    private static readonly HashSet<int> PrimaryStates =
    [
        0x04, 0x05, 0x06
    ];

    public static LiftStatePresentation GetPresentation(int state)
    {
        var text = GermanTexts.TryGetValue(state, out var knownText)
            ? knownText
            : $"Zustand 0x{state:X2}";

        var brush = GetBrush(state);
        var doorOverlayFinished = state != 0x06;

        return new LiftStatePresentation(text, brush, doorOverlayFinished);
    }

    public static LiftStatePresentation GetPresentationForStateText(string? stateText)
    {
        string resolvedText = stateText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedText))
            return new LiftStatePresentation(string.Empty, new SolidColorBrush(Colors.White));

        if (resolvedText.Contains("Bereit", StringComparison.OrdinalIgnoreCase)
            || resolvedText.Contains("Stillst", StringComparison.OrdinalIgnoreCase)
            || resolvedText.Contains("Stillstand", StringComparison.OrdinalIgnoreCase))
        {
            return new LiftStatePresentation(resolvedText, new SolidColorBrush(Colors.White));
        }

        if (resolvedText.Contains("Einfahrt", StringComparison.OrdinalIgnoreCase))
            return new LiftStatePresentation(resolvedText, CreateBrush(0xFA, 0xCC, 0x15), false);

        if (resolvedText.Contains("Fahrt", StringComparison.OrdinalIgnoreCase))
            return new LiftStatePresentation(resolvedText, CreateBrush(0x22, 0xC5, 0x5E));

        var knownState = GermanTexts.FirstOrDefault(entry =>
            string.Equals(entry.Value, resolvedText, StringComparison.OrdinalIgnoreCase));

        if (!knownState.Equals(default(KeyValuePair<int, string>)))
        {
            var fallback = GetPresentation(knownState.Key);
            return fallback with { Text = resolvedText };
        }

        return new LiftStatePresentation(resolvedText, new SolidColorBrush(Colors.White));
    }

    public static bool IsWhiteStateText(string? stateText)
    {
        string resolvedText = stateText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedText))
            return false;

        if (resolvedText.Contains("Bereit", StringComparison.OrdinalIgnoreCase)
            || resolvedText.Contains("Stillst", StringComparison.OrdinalIgnoreCase)
            || resolvedText.Contains("Stillstand", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var knownState = GermanTexts.FirstOrDefault(entry =>
            string.Equals(entry.Value, resolvedText, StringComparison.OrdinalIgnoreCase));

        return knownState.Key == 0x04;
    }

    public static bool IsPrimaryState(int state)
    {
        return PrimaryStates.Contains(state);
    }

    public static int GetSecondaryPriority(int state)
    {
        if (ServiceStates.Contains(state))
            return 0;

        if (WarningStates.Contains(state))
            return 1;

        return 2;
    }

    private static IBrush GetBrush(int state)
    {
        if (state == 0x04)
            return new SolidColorBrush(Colors.White);

        if (state == 0x06)
            return CreateBrush(0xFA, 0xCC, 0x15);

        if (RunningStates.Contains(state))
            return CreateBrush(0x22, 0xC5, 0x5E);

        if (ServiceStates.Contains(state))
            return CreateBrush(0x60, 0xA5, 0xFA);

        if (WarningStates.Contains(state))
            return CreateBrush(0xFA, 0xCC, 0x15);

        return CreateBrush(0xEF, 0x44, 0x44);
    }

    private static IBrush CreateBrush(byte red, byte green, byte blue)
    {
        return new SolidColorBrush(Color.FromRgb(red, green, blue));
    }
}
