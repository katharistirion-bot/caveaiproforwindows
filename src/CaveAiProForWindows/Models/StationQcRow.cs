namespace CaveAiProForWindows.Models;

public sealed class StationQcRow
{
    public StationQcRow(string name, float x, float y, float z, int traverseLegsFrom, int traverseLegsTo)
    {
        Name = name;
        X = x;
        Y = y;
        Z = z;
        TraverseLegsFrom = traverseLegsFrom;
        TraverseLegsTo = traverseLegsTo;
    }

    public string Name { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public int TraverseLegsFrom { get; }
    public int TraverseLegsTo { get; }
}
