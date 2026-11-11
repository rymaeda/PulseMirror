namespace PulseMirror.Instruments
{
    /// <summary>
    /// Numeric measurements that can be read from the instrument via SCPI.
    /// Map new members in the driver's GetMeasurementMnemonic.
    /// </summary>
    public enum MeasurementQuantity
    {
        Vpp,
        Vmax,
        Vmin,
        Vamplitude,
        Vtop,
        Vbase,
        Vaverage,
        Vrms,
        Overshoot,
        Preshoot,
        Frequency,
        Period,
        RiseTime,
        FallTime,
        PositiveWidth,
        NegativeWidth,
        PositiveDuty,
        NegativeDuty
    }
}