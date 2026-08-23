namespace NetOpenGrid.Domain;

/// <summary>Thrown when a grid configuration is invalid.</summary>
public sealed class GridConfigurationException : GridException
{
    public GridConfigurationException(string message) : base(message) { }
}
