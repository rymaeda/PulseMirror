namespace PulseMirror.Instruments
{
    /// <summary>
    /// On-screen measurements that can be toggled on the instrument.
    /// Add new members here and map them in the driver's BuildMeasurementCommand.
    /// </summary>
    public enum MeasurementType
    {
        /// <summary>The instrument's "all measurements" panel.</summary>
        All
    }
}