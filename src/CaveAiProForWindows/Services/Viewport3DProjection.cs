using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace CaveAiProForWindows.Services;

/// <summary>Projects survey-world points onto a WPF <see cref="Viewport3D"/> canvas.</summary>
public static class Viewport3DProjection
{
    public static bool TryWorldToScreen(Viewport3D viewport, Point3D world, out Point screen)
    {
        screen = default;
        if (viewport.Camera is not ProjectionCamera camera)
            return false;

        var w = viewport.ActualWidth;
        var h = viewport.ActualHeight;
        if (w < 1 || h < 1)
            return false;

        var look = camera.LookDirection;
        if (look.LengthSquared < 1e-12)
            return false;
        look.Normalize();

        var up = camera.UpDirection;
        if (up.LengthSquared < 1e-12)
            up = new Vector3D(0, 0, 1);
        up.Normalize();

        var right = Vector3D.CrossProduct(look, up);
        if (right.LengthSquared < 1e-12)
            return false;
        right.Normalize();
        up = Vector3D.CrossProduct(right, look);
        up.Normalize();

        var rel = world - camera.Position;
        var cx = Vector3D.DotProduct(rel, right);
        var cy = Vector3D.DotProduct(rel, up);
        var cz = Vector3D.DotProduct(rel, look);

        if (cz <= Math.Max(1e-4, camera.NearPlaneDistance * 0.95))
            return false;

        double ndcX;
        double ndcY;
        if (camera is PerspectiveCamera pc)
        {
            var tanHalf = Math.Tan(pc.FieldOfView * Math.PI / 360.0);
            if (tanHalf < 1e-8)
                return false;
            var aspect = w / h;
            ndcX = (cx / cz) / (tanHalf * aspect);
            ndcY = (cy / cz) / tanHalf;
        }
        else if (camera is OrthographicCamera oc)
        {
            var halfW = Math.Max(0.01, oc.Width * 0.5);
            var halfH = halfW / Math.Max(0.01, w / h);
            ndcX = cx / halfW;
            ndcY = cy / halfH;
        }
        else
        {
            return false;
        }

        if (ndcX is < -1.25 or > 1.25 || ndcY is < -1.25 or > 1.25)
            return false;

        screen = new Point((ndcX + 1) * 0.5 * w, (1 - ndcY) * 0.5 * h);
        return screen.X >= -60 && screen.Y >= -60 && screen.X <= w + 60 && screen.Y <= h + 60;
    }
}
