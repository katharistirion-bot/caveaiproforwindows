using System.Windows;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Maps survey local X (east), Y (north), in metres, to canvas pixel coordinates by going through the satellite
/// backdrop's geographic bounding box and the image's letterboxed display rectangle in the host control.
///
/// <para>
/// Reduction (small-area approximation, sub-metre accurate at typical cave scales &lt;5 km):
/// </para>
/// <list type="number">
///   <item><description><c>lat = originLat + Y / 111320</c></description></item>
///   <item><description><c>lon = originLon + X / (111320 · cos(originLat))</c></description></item>
///   <item><description><c>imgPx = (lon − minLon) / spanLon · imageWidth</c></description></item>
///   <item><description><c>imgPy = (maxLat − lat) / spanLat · imageHeight</c></description></item>
///   <item><description><c>canvasPx = displayLeft + imgPx / imageWidth · displayWidth</c></description></item>
/// </list>
/// </summary>
public readonly record struct XRayGeoLayout(
    double OriginLat,
    double OriginLon,
    double LatPerMetre,
    double LonPerMetre,
    double MinLat,
    double MaxLat,
    double MinLon,
    double MaxLon,
    double ImagePixelWidth,
    double ImagePixelHeight,
    Rect DisplayRectInCanvas)
{
    /// <summary>Convert one survey local coordinate (X east m, Y north m) to canvas pixels.</summary>
    public Point WorldMetresToCanvas(double xMetres, double yMetres)
    {
        var lat = OriginLat + yMetres * LatPerMetre;
        var lon = OriginLon + xMetres * LonPerMetre;
        return GeoToCanvas(lat, lon);
    }

    /// <summary>Convert lat/lon directly to canvas pixels using the same letterboxed image rectangle.</summary>
    public Point GeoToCanvas(double lat, double lon)
    {
        var spanLat = Math.Max(1e-12, MaxLat - MinLat);
        var spanLon = Math.Max(1e-12, MaxLon - MinLon);
        var imgPx = (lon - MinLon) / spanLon * ImagePixelWidth;
        var imgPy = (MaxLat - lat) / spanLat * ImagePixelHeight;
        var canvasX = DisplayRectInCanvas.X + imgPx / Math.Max(1e-9, ImagePixelWidth) * DisplayRectInCanvas.Width;
        var canvasY = DisplayRectInCanvas.Y + imgPy / Math.Max(1e-9, ImagePixelHeight) * DisplayRectInCanvas.Height;
        return new Point(canvasX, canvasY);
    }

    /// <summary>Inverse of <see cref="GeoToCanvas"/> — canvas pixel to WGS-84 degrees.</summary>
    public (double Lat, double Lon)? TryCanvasToGeo(Point canvasPt)
    {
        var rect = DisplayRectInCanvas;
        if (rect.Width <= 0 || rect.Height <= 0)
            return null;

        var imgPx = (canvasPt.X - rect.X) / rect.Width * ImagePixelWidth;
        var imgPy = (canvasPt.Y - rect.Y) / rect.Height * ImagePixelHeight;
        var spanLat = Math.Max(1e-12, MaxLat - MinLat);
        var spanLon = Math.Max(1e-12, MaxLon - MinLon);
        var lon = MinLon + imgPx / Math.Max(1e-9, ImagePixelWidth) * spanLon;
        var lat = MaxLat - imgPy / Math.Max(1e-9, ImagePixelHeight) * spanLat;
        return (lat, lon);
    }
}

/// <summary>Builds <see cref="XRayGeoLayout"/> instances and computes the on-canvas image rectangle.</summary>
public static class XRayProjection
{
    /// <summary>Metres per degree latitude (mean Earth, accurate to ~0.5% in the survey-scale linear region).</summary>
    public const double MetresPerDegreeLatitude = 111320.0;

    /// <summary>
    /// Image rect inside the host (Stretch=Uniform): full image fits without distortion, padded with letterbox bars.
    /// </summary>
    public static Rect ComputeImageDisplayRectInCanvas(double imagePxW, double imagePxH, double hostW, double hostH)
    {
        if (imagePxW <= 0 || imagePxH <= 0 || hostW <= 0 || hostH <= 0)
            return new Rect(0, 0, Math.Max(0, hostW), Math.Max(0, hostH));
        var imgAspect = imagePxW / imagePxH;
        var hostAspect = hostW / hostH;
        double dispW, dispH, dispX, dispY;
        if (hostAspect > imgAspect)
        {
            dispH = hostH;
            dispW = dispH * imgAspect;
            dispX = (hostW - dispW) * 0.5;
            dispY = 0;
        }
        else
        {
            dispW = hostW;
            dispH = dispW / imgAspect;
            dispX = 0;
            dispY = (hostH - dispH) * 0.5;
        }

        return new Rect(dispX, dispY, dispW, dispH);
    }

    /// <summary>
    /// Compose a layout that uses <paramref name="originLat"/>/<paramref name="originLon"/> (project entrance) as the
    /// survey local-frame origin and the satellite bbox + display rectangle for canvas mapping.
    /// </summary>
    public static XRayGeoLayout Build(
        double originLat,
        double originLon,
        XRayBackdropMetadata bbox,
        double imagePxW,
        double imagePxH,
        Rect displayRectInCanvas)
    {
        var latPerMetre = 1.0 / MetresPerDegreeLatitude;
        var lonPerMetre = 1.0 / (MetresPerDegreeLatitude * Math.Max(1e-6, Math.Cos(originLat * Math.PI / 180.0)));
        return new XRayGeoLayout(
            OriginLat: originLat,
            OriginLon: originLon,
            LatPerMetre: latPerMetre,
            LonPerMetre: lonPerMetre,
            MinLat: bbox.MinLat,
            MaxLat: bbox.MaxLat,
            MinLon: bbox.MinLon,
            MaxLon: bbox.MaxLon,
            ImagePixelWidth: imagePxW,
            ImagePixelHeight: imagePxH,
            DisplayRectInCanvas: displayRectInCanvas);
    }
}
