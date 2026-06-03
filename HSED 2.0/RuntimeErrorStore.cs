using System;
using System.Collections.Generic;
using System.Linq;

namespace HSED_2_0;

public sealed record RuntimeErrorEntry(string Id, string Title, string DetailText, DateTime? ActiveUntilUtc);

public static class RuntimeErrorStore
{
    public const string SafetyCircuitMissingId = "safety_circuit_missing";

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, RuntimeErrorEntry> Errors = new();

    public static void ReportSafetyCircuitMissing()
    {
        Upsert(
            SafetyCircuitMissingId,
            "Sicherheitskreis fehlt",
            "Der Sicherheitskreis wird aktuell als fehlend gemeldet. Bitte prüfen Sie die Sicherheitskreis-Kette und die zugehörigen Eingänge.",
            null);
    }

    public static void ClearSafetyCircuitMissing()
    {
        lock (SyncRoot)
        {
            Errors.Remove(SafetyCircuitMissingId);
        }
    }

    public static IReadOnlyList<RuntimeErrorEntry> GetSnapshot()
    {
        lock (SyncRoot)
        {
            PruneExpired(DateTime.UtcNow);
            return Errors.Values
                .OrderBy(entry => entry.Title)
                .ToList();
        }
    }

    private static void Upsert(string id, string title, string detailText, TimeSpan? lifetime)
    {
        lock (SyncRoot)
        {
            Errors[id] = new RuntimeErrorEntry(id, title, detailText, lifetime.HasValue ? DateTime.UtcNow + lifetime.Value : null);
        }
    }

    private static void PruneExpired(DateTime now)
    {
        var expiredIds = Errors
            .Where(entry => entry.Value.ActiveUntilUtc.HasValue && entry.Value.ActiveUntilUtc.Value <= now)
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var id in expiredIds)
        {
            Errors.Remove(id);
        }
    }
}
