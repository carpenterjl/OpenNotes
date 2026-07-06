namespace OpenNotes.Services;

/// <summary>Pure HSV↔RGB conversion, factored out of <c>ColorPickerDialog</c> so the color-wheel
/// math is unit-testable without WPF. Hue is degrees [0,360); saturation/value are [0,1]; RGB are
/// bytes [0,255].</summary>
public static class ColorMath
{
    /// <summary>Convert an RGB triple to (Hue°, Saturation, Value). For greys (max==min) hue is 0.</summary>
    public static (double H, double S, double V) RgbToHsv(byte r, byte g, byte b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            if (max == rd) h = 60 * (((gd - bd) / delta) % 6);
            else if (max == gd) h = 60 * (((bd - rd) / delta) + 2);
            else h = 60 * (((rd - gd) / delta) + 4);
        }
        if (h < 0) h += 360;

        double s = max <= 0 ? 0 : delta / max;
        return (h, s, max);
    }

    /// <summary>Convert (Hue°, Saturation, Value) to an RGB triple. Inputs are clamped to valid
    /// ranges (hue wrapped to [0,360)).</summary>
    public static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360; // wrap into [0,360)
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);

        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        double m = v - c;

        double rd, gd, bd;
        if (h < 60) (rd, gd, bd) = (c, x, 0);
        else if (h < 120) (rd, gd, bd) = (x, c, 0);
        else if (h < 180) (rd, gd, bd) = (0, c, x);
        else if (h < 240) (rd, gd, bd) = (0, x, c);
        else if (h < 300) (rd, gd, bd) = (x, 0, c);
        else (rd, gd, bd) = (c, 0, x);

        return ((byte)Math.Round((rd + m) * 255),
                (byte)Math.Round((gd + m) * 255),
                (byte)Math.Round((bd + m) * 255));
    }
}
