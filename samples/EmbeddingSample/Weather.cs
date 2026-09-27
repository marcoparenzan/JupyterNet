namespace EmbeddingSample;

/// <summary>
/// The same shape PySharp's own README uses to demonstrate injecting a .NET object into a script
/// (<c>engine.SetVariable("weather", new Weather())</c>) — deliberately mirrored here, since
/// <see cref="JupyterNet.Engine.NotebookSession.SetVariableAsync"/> is the same idea for JupyterNet:
/// a plain object with a property, a method taking an argument, and a property to iterate.
/// </summary>
public sealed class Weather
{
    public string City { get; set; } = "Trento";
    public double TempC(int day) => 20.0 + day;
    public string[] Forecast => ["sun", "rain"];
}
