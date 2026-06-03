using System;
using System.Collections.Generic;
using System.Linq;

namespace HSED_2_0;

public sealed record LiftStateSnapshot(int? PrimaryState, IReadOnlyList<int> AdditionalStates);

public static class LiftStateTracker
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<int, DateTime> TimedStates = new();
    private static readonly TimeSpan TimedStateLifetime = TimeSpan.FromSeconds(1);

    private static int? _primaryState;

    public static void RegisterState(int state)
    {
        lock (SyncRoot)
        {
            if (state == 0x11)
            {
                RuntimeErrorStore.ReportSafetyCircuitMissing();
                return;
            }

            RuntimeErrorStore.ClearSafetyCircuitMissing();

            if (LiftStateCatalog.IsPrimaryState(state))
            {
                _primaryState = state;
                return;
            }

            TimedStates[state] = DateTime.UtcNow + TimedStateLifetime;
        }
    }

    public static LiftStateSnapshot GetSnapshot()
    {
        lock (SyncRoot)
        {
            var now = DateTime.UtcNow;
            var expiredStates = TimedStates
                .Where(entry => entry.Value <= now)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (var state in expiredStates)
            {
                TimedStates.Remove(state);
            }

            var activeTimedStates = TimedStates.Keys
                .OrderBy(LiftStateCatalog.GetSecondaryPriority)
                .ThenBy(state => state)
                .ToList();

            if (_primaryState.HasValue)
            {
                return new LiftStateSnapshot(_primaryState.Value, activeTimedStates);
            }

            if (activeTimedStates.Count == 0)
            {
                return new LiftStateSnapshot(null, Array.Empty<int>());
            }

            var primaryState = activeTimedStates[0];
            activeTimedStates.RemoveAt(0);
            return new LiftStateSnapshot(primaryState, activeTimedStates);
        }
    }
}
