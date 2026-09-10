// HA DeskLink - Sendspin streaming client (Phase E)
// 1:1 port of aiosendspin's client/time_sync.py (SendspinTimeFilter),
// itself a port of the ESPHome implementation. Two-dimensional Kalman
// filter tracking timestamp offset and clock drift between client and
// server from NTP-style time exchanges.
//
// This program is licensed under the GNU General Public License v3.
#nullable enable
using System;

namespace HaDeskLink.Sendspin;

/// <summary>
/// Time transformation parameters snapshot (immutable per update).
/// </summary>
internal sealed class TimeElement
{
    public long LastUpdate { get; init; }
    public double Offset { get; init; }
    public double Drift { get; init; }
    public bool UseDrift { get; init; }
}

/// <summary>
/// Two-dimensional Kalman filter for NTP-style time synchronization.
/// Tracks [offset, drift] with a 2D covariance matrix; adaptive forgetting
/// recovers quickly from network disruptions or server clock adjustments.
/// Mirrors the Python reference exactly (same constants, same math).
/// </summary>
public sealed class SendspinTimeFilter
{
    // Residual threshold as multiple of max_error for adaptive forgetting.
    private const double AdaptiveForgettingCutoff = 3.0;

    // Scale applied to max_error before use as measurement standard deviation.
    private const double MaxErrorScale = 0.5;

    // Drift^2 must exceed this * drift_covariance for drift compensation.
    private const double DriftSignificanceThresholdSquared = 2.0 * 2.0;

    private long _lastUpdate;
    private int _count;

    private double _offset;
    private double _drift;

    private double _offsetCovariance = double.PositiveInfinity;
    private double _offsetDriftCovariance;
    private double _driftCovariance;

    private readonly double _processVariance;
    private readonly double _driftProcessVariance;
    private readonly double _forgetVarianceFactor;

    private TimeElement _currentTimeElement = new();

    /// <param name="processStdDev">Offset process noise std-dev (µs per µs²).</param>
    /// <param name="forgetFactor">Variance inflation on outliers (default 2.0).</param>
    /// <param name="driftProcessStdDev">Drift process noise std-dev (default 1e-11).</param>
    public SendspinTimeFilter(double processStdDev = 0.0, double forgetFactor = 2.0,
        double driftProcessStdDev = 1e-11)
    {
        _processVariance = processStdDev * processStdDev;
        _driftProcessVariance = driftProcessStdDev * driftProcessStdDev;
        _forgetVarianceFactor = forgetFactor * forgetFactor;
    }

    /// <summary>
    /// Process a new time-sync measurement through the Kalman filter.
    /// measurement = ((T2-T1)+(T3-T4))/2, maxError = ((T4-T1)-(T3-T2))/2, both in µs.
    /// </summary>
    public void Update(long measurement, long maxError, long timeAdded)
    {
        if (timeAdded <= _lastUpdate)
        {
            return; // skip non-monotonic timestamps
        }

        double dt = timeAdded - _lastUpdate;
        _lastUpdate = timeAdded;

        double updateStdDev = maxError * MaxErrorScale;
        double measurementVariance = updateStdDev * updateStdDev;

        if (_count <= 0)
        {
            // First measurement establishes the offset baseline.
            _count++;
            _offset = measurement;
            _offsetCovariance = measurementVariance;
            _drift = 0.0;
            _currentTimeElement = new TimeElement
            {
                LastUpdate = _lastUpdate, Offset = _offset, Drift = _drift,
            };
            return;
        }

        if (_count == 1)
        {
            // Second measurement: initial drift estimation from finite differences.
            _count++;
            _drift = (measurement - _offset) / dt;
            _offset = measurement;
            _driftCovariance = (_offsetCovariance + measurementVariance) / (dt * dt);
            _offsetCovariance = measurementVariance;
            _currentTimeElement = new TimeElement
            {
                LastUpdate = _lastUpdate, Offset = _offset, Drift = _drift,
            };
            return;
        }

        // ── Kalman prediction ──
        double offset = _offset + _drift * dt;
        double dtSquared = dt * dt;

        double driftProcessVariance = dt * _driftProcessVariance;
        double newDriftCovariance = _driftCovariance + driftProcessVariance;

        const double offsetDriftProcessVariance = 0.0;
        double newOffsetDriftCovariance =
            _offsetDriftCovariance + _driftCovariance * dt + offsetDriftProcessVariance;

        double offsetProcessVariance = dt * _processVariance;
        double newOffsetCovariance =
            _offsetCovariance + 2 * _offsetDriftCovariance * dt
            + _driftCovariance * dtSquared + offsetProcessVariance;

        // ── Innovation and adaptive forgetting ──
        double residual = measurement - offset;
        double maxResidualCutoff = maxError * AdaptiveForgettingCutoff;

        if (_count < 100)
        {
            _count++;
        }
        else if (Math.Abs(residual) > maxResidualCutoff)
        {
            newDriftCovariance *= _forgetVarianceFactor;
            newOffsetDriftCovariance *= _forgetVarianceFactor;
            newOffsetCovariance *= _forgetVarianceFactor;
        }

        // ── Kalman update ──
        double uncertainty = 1.0 / Math.Max(newOffsetCovariance + measurementVariance, 1e-9);

        double offsetGain = newOffsetCovariance * uncertainty;
        double driftGain = newOffsetDriftCovariance * uncertainty;

        _offset = offset + offsetGain * residual;
        _drift += driftGain * residual;

        _driftCovariance = newDriftCovariance - driftGain * newOffsetDriftCovariance;
        _offsetDriftCovariance = newOffsetDriftCovariance - driftGain * newOffsetCovariance;
        _offsetCovariance = newOffsetCovariance - offsetGain * newOffsetCovariance;

        bool useDrift = _drift * _drift
            > DriftSignificanceThresholdSquared * _driftCovariance;

        _currentTimeElement = new TimeElement
        {
            LastUpdate = _lastUpdate, Offset = _offset, Drift = _drift, UseDrift = useDrift,
        };
    }

    /// <summary>
    /// Convert a client timestamp (µs) to the equivalent server timestamp (µs).
    /// T_server = T_client + offset + drift * (T_client - T_last_update).
    /// </summary>
    public long ComputeServerTime(long clientTime)
    {
        var element = _currentTimeElement;
        double effectiveDrift = element.UseDrift ? element.Drift : 0.0;
        double dt = clientTime - element.LastUpdate;
        double offset = Math.Round(element.Offset + effectiveDrift * dt);
        return clientTime + (long)offset;
    }

    /// <summary>
    /// Convert a server timestamp (µs) to the equivalent client timestamp (µs).
    /// T_client = (T_server - offset + drift * T_last_update) / (1 + drift).
    /// </summary>
    public long ComputeClientTime(long serverTime)
    {
        var element = _currentTimeElement;
        double effectiveDrift = element.UseDrift ? element.Drift : 0.0;
        double value = (serverTime - element.Offset + effectiveDrift * element.LastUpdate)
            / (1.0 + effectiveDrift);
        return (long)Math.Round(value);
    }

    /// <summary>Reset the filter state.</summary>
    public void Reset()
    {
        _count = 0;
        _lastUpdate = 0;
        _offset = 0.0;
        _drift = 0.0;
        _offsetCovariance = double.PositiveInfinity;
        _offsetDriftCovariance = 0.0;
        _driftCovariance = 0.0;
        _currentTimeElement = new();
    }

    /// <summary>Number of time-sync measurements processed.</summary>
    public int Count => _count;

    /// <summary>True once at least 2 measurements converged (finite covariance).</summary>
    public bool IsSynchronized =>
        _count >= 2 && !double.IsPositiveInfinity(_offsetCovariance) && !double.IsInfinity(_offsetCovariance);

    /// <summary>Standard deviation estimate in microseconds.</summary>
    public long Error => (long)Math.Round(Math.Sqrt(_offsetCovariance));

    /// <summary>Offset covariance (variance) estimate.</summary>
    public long Covariance => (long)Math.Round(_offsetCovariance);

    /// <summary>Current filtered offset estimate in microseconds.</summary>
    public double Offset => _offset;
}