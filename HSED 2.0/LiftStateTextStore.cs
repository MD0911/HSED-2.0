using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HSED_2_0;

public sealed record LiftStateTextSnapshot(
    string? HcStateText,
    string? MaintenanceText,
    string? CallStateText,
    string? DriverDirectionsText,
    string? LightsScreenStatusText)
{
    private static readonly TimeSpan PrimaryRotationInterval = TimeSpan.FromSeconds(2.5);

    public string? GetActivePrimaryText(DateTime utcNow)
    {
        bool hasHcState = !string.IsNullOrWhiteSpace(HcStateText);
        bool hasCallState = !string.IsNullOrWhiteSpace(CallStateText);
        bool canRotateCallState = LiftStateCatalog.IsWhiteStateText(HcStateText);

        if (!hasCallState)
            return HcStateText;

        if (!hasHcState)
            return CallStateText;

        if (!canRotateCallState)
            return HcStateText;

        long slot = utcNow.Ticks / PrimaryRotationInterval.Ticks;
        return slot % 2 == 0 ? HcStateText : CallStateText;
    }

    public IReadOnlyList<string> GetAdditionalTexts()
    {
        bool canRotateCallState = LiftStateCatalog.IsWhiteStateText(HcStateText);
        bool showCallStateAsAdditional = !canRotateCallState
            && !string.IsNullOrWhiteSpace(CallStateText)
            && !IsNormalCallState(CallStateText);

        var values = new List<string>(4);

        if (showCallStateAsAdditional)
            values.Add(CallStateText!.Trim());

        AddIfPresent(values, MaintenanceText);
        AddIfPresent(values, DriverDirectionsText);
        AddIfPresent(values, LightsScreenStatusText);

        return values;
    }

    private static bool IsNormalCallState(string? value)
    {
        return string.Equals(value?.Trim(), "normal", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddIfPresent(List<string> values, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        string resolvedValue = value.Trim();
        if (!values.Contains(resolvedValue, StringComparer.Ordinal))
            values.Add(resolvedValue);
    }
}

public static class LiftStateTextStore
{
    private static readonly object SyncRoot = new();
    private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMilliseconds(500);

    private static LiftStateTextSnapshot _snapshot = new(null, null, null, null, null);
    private static DateTime _lastRefreshUtc = DateTime.MinValue;
    private static int _refreshInProgress;

    public static LiftStateTextSnapshot GetSnapshot()
    {
        lock (SyncRoot)
        {
            return _snapshot;
        }
    }

    public static void RequestRefreshFromDevice()
    {
        if (Interlocked.Exchange(ref _refreshInProgress, 1) != 0)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                lock (SyncRoot)
                {
                    if (now - _lastRefreshUtc < MinimumRefreshInterval)
                        return;
                }

                var snapshot = new LiftStateTextSnapshot(
                    HseCom.ReadHcStateText(),
                    HseCom.ReadMaintenanceText(),
                    HseCom.ReadCallStateText(),
                    HseCom.ReadDriverDirectionsText(),
                    null);

                lock (SyncRoot)
                {
                    _snapshot = snapshot;
                    _lastRefreshUtc = now;
                }
            }
            finally
            {
                Interlocked.Exchange(ref _refreshInProgress, 0);
            }
        });
    }

    public static void RefreshFromMonitoringCycle()
    {
        RequestRefreshFromDevice();
    }
}
