using System;
using System.Windows;
using System.Windows.Media;
namespace MainApp;
// Камера хаба: pan (px экрана) + zoom; screen = world * zoom + pan.
public class HubCamera
{
public double PanX, PanY, Zoom = 1.0;
public const double MinZoom = 0.25, MaxZoom = 4.0;
public Point ToWorld(Point s) => new((s.X - PanX) / Zoom, (s.Y - PanY) / Zoom);
    public Point ToScreen(Point w) => new(w.X * Zoom + PanX, w.Y * Zoom + PanY);
public MatrixTransform Transform => new(Zoom, 0, 0, Zoom, PanX, PanY);
public void SetZoom(double z, Point center)
{
z = Math.Clamp(z, MinZoom, MaxZoom);
PanX = center.X - (center.X - PanX) * (z / Zoom);
PanY = center.Y - (center.Y - PanY) * (z / Zoom);
Zoom = z;
}
public void PanBy(double dx, double dy) { PanX += dx; PanY += dy; }
}