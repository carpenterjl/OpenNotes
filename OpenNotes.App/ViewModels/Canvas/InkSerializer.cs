using System.IO;
using System.Windows.Ink;

namespace OpenNotes.ViewModels.Canvas;

/// <summary>Converts between live <see cref="StrokeCollection"/>s and the ISF (Ink Serialized
/// Format) byte blobs persisted in the <c>.taskcanvas</c> archive: page floating ink as a
/// <c>pages/{id}.isf</c> entry, node-bound ink as <see cref="Models.DiagramNode.InkData"/>.</summary>
public static class InkSerializer
{
    /// <summary>ISF bytes for the collection, or null when it is empty (no ink → no archive entry).</summary>
    public static byte[]? ToBytes(StrokeCollection strokes)
    {
        if (strokes.Count == 0) return null;
        using var stream = new MemoryStream();
        strokes.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Deserialize ISF bytes; null, empty, or corrupt input yields an empty collection
    /// (bad ink must never prevent a page from opening).</summary>
    public static StrokeCollection FromBytes(byte[]? data)
    {
        if (data is not { Length: > 0 }) return [];
        try
        {
            using var stream = new MemoryStream(data);
            return new StrokeCollection(stream);
        }
        catch
        {
            return [];
        }
    }
}
