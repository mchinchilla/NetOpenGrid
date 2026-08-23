namespace NetOpenGrid.Domain;

/// <summary>Base exception for all NetOpenGrid domain errors.</summary>
public class GridException : Exception
{
    public GridException(string message) : base(message) { }
    public GridException(string message, Exception innerException) : base(message, innerException) { }
}
