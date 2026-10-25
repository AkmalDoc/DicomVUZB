using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DicomVUZB
{
    public static class ExportService
    {
        public static void ExportImageToFile(Image image, string filePath, ImageFormat format)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (format == ImageFormat.Jpeg)
            {
                var bytes = PdfReportService.EncodeJpeg(image, 95L);
                File.WriteAllBytes(filePath, bytes);
            }
            else
            {
                image.Save(filePath, format);
            }
        }

        public static void ExportMeasurementsToCsv(
            string filePath,
            Dictionary<string, List<DicomMeasurement>> measurementsByImagePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));
            if (measurementsByImagePath == null || measurementsByImagePath.Count == 0)
                throw new InvalidOperationException("Нет измерений для экспорта в CSV.");

            var sb = new StringBuilder();
            // UTF-8 with BOM for Excel compatibility
            sb.AppendLine("Файл;Тип;Название;Значение / Длина / Угол;Площадь;Мин;Макс;Среднее;SD;Ед. изм.");

            foreach (var pair in measurementsByImagePath
                         .Where(p => p.Value != null && p.Value.Count > 0)
                         .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(pair.Key);

                foreach (var m in pair.Value)
                {
                    var type = m.Mode.ToString();
                    var name = string.IsNullOrWhiteSpace(m.Name) ? "" : m.Name.Replace(";", ",");
                    var valueStr = "";
                    var areaStr = "";
                    var minStr = "";
                    var maxStr = "";
                    var meanStr = "";
                    var sdStr = "";
                    var unitStr = "";

                    if (m.Mode == MeasurementMode.Distance)
                    {
                        var hasSpacing = m.PixelSpacingX > 0f && m.PixelSpacingY > 0f;
                        var dx = (m.P2.X - m.P1.X) * (hasSpacing ? m.PixelSpacingX : 1f);
                        var dy = (m.P2.Y - m.P1.Y) * (hasSpacing ? m.PixelSpacingY : 1f);
                        var dist = Math.Sqrt(dx * dx + dy * dy);
                        valueStr = dist.ToString("0.0", CultureInfo.InvariantCulture);
                        unitStr = hasSpacing ? "мм" : "px";
                    }
                    else if (m.Mode == MeasurementMode.Angle)
                    {
                        var v1x = m.P2.X - m.P1.X;
                        var v1y = m.P2.Y - m.P1.Y;
                        var v2x = m.P3.X - m.P1.X;
                        var v2y = m.P3.Y - m.P1.Y;
                        var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                        var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                        if (len1 >= 0.0001 && len2 >= 0.0001)
                        {
                            var dot = (v1x * v2x + v1y * v2y) / (len1 * len2);
                            dot = Math.Max(-1.0, Math.Min(1.0, dot));
                            var deg = Math.Acos(dot) * 180.0 / Math.PI;
                            valueStr = deg.ToString("0.0", CultureInfo.InvariantCulture);
                            unitStr = "°";
                        }
                    }
                    else if (m.Mode == MeasurementMode.RoiRectangle || m.Mode == MeasurementMode.RoiCircle)
                    {
                        var areaVal = m.RoiArea.HasValue ? (double)m.RoiArea.Value : PdfReportService.CalculateRoiGeometricArea(m);
                        areaStr = areaVal.ToString("0.0", CultureInfo.InvariantCulture);
                        minStr = m.RoiMin.HasValue ? m.RoiMin.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
                        maxStr = m.RoiMax.HasValue ? m.RoiMax.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
                        meanStr = m.RoiMean.HasValue ? m.RoiMean.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
                        sdStr = m.RoiStdDev.HasValue ? m.RoiStdDev.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
                        unitStr = m.IsHounsfieldUnit ? "HU" : "val";
                    }
                    else if (m.Mode == MeasurementMode.Polygon)
                    {
                        double perim, area;
                        PdfReportService.CalculatePolygonMetrics(m, out perim, out area);
                        valueStr = string.Format(CultureInfo.InvariantCulture, "P={0:0.0}", perim);
                        areaStr = area.ToString("0.0", CultureInfo.InvariantCulture);
                        unitStr = m.PixelSpacingX > 0f && m.PixelSpacingY > 0f ? "мм / мм²" : "px / px²";
                    }
                    else if (m.Mode == MeasurementMode.OpenPolyline || m.Mode == MeasurementMode.Spline)
                    {
                        var sxp = m.PixelSpacingX > 0f ? m.PixelSpacingX : 1f;
                        var syp = m.PixelSpacingY > 0f ? m.PixelSpacingY : 1f;
                        var points = m.Points != null && m.Points.Count >= 2
                            ? m.Points
                            : new List<System.Drawing.PointF> { m.P1, m.P2, m.P3 };
                        var length = 0d;
                        for (var i = 1; i < points.Count; i++)
                        {
                            var dxp = (points[i].X - points[i - 1].X) * sxp;
                            var dyp = (points[i].Y - points[i - 1].Y) * syp;
                            length += Math.Sqrt(dxp * dxp + dyp * dyp);
                        }
                        valueStr = length.ToString("0.0", CultureInfo.InvariantCulture);
                        unitStr = m.PixelSpacingX > 0f && m.PixelSpacingY > 0f ? "мм" : "px";
                    }
                    else if (m.Mode == MeasurementMode.Pencil)
                    {
                        var sxp = m.PixelSpacingX > 0f ? m.PixelSpacingX : 1f;
                        var syp = m.PixelSpacingY > 0f ? m.PixelSpacingY : 1f;
                        var points = m.Points != null && m.Points.Count >= 2
                            ? m.Points
                            : new List<System.Drawing.PointF> { m.P1, m.P2, m.P3 };
                        var length = 0d;
                        for (var i = 1; i < points.Count; i++)
                        {
                            var dxp = (points[i].X - points[i - 1].X) * sxp;
                            var dyp = (points[i].Y - points[i - 1].Y) * syp;
                            length += Math.Sqrt(dxp * dxp + dyp * dyp);
                        }
                        var width = m.StrokeWidth > 0f ? m.StrokeWidth : 1.2f;
                        var opacityPercent = (m.StrokeOpacity > 0 ? m.StrokeOpacity : 220) * 100f / 255f;
                        valueStr = string.Format(CultureInfo.InvariantCulture, "L={0:0.0} W={1:0.0}px O={2:0}%", length, width, opacityPercent);
                        unitStr = m.PixelSpacingX > 0f && m.PixelSpacingY > 0f ? "мм" : "px";
                    }
                    else if (m.Mode == MeasurementMode.CobbAngle)
                    {
                        if (m.Points != null && m.Points.Count >= 4)
                        {
                            var v1x = m.Points[1].X - m.Points[0].X;
                            var v1y = m.Points[1].Y - m.Points[0].Y;
                            var v2x = m.Points[3].X - m.Points[2].X;
                            var v2y = m.Points[3].Y - m.Points[2].Y;
                            var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                            var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                            if (len1 >= 0.0001 && len2 >= 0.0001)
                            {
                                var cos = (v1x * v2x + v1y * v2y) / (len1 * len2);
                                cos = Math.Max(-1.0, Math.Min(1.0, cos));
                                var deg = Math.Acos(cos) * 180.0 / Math.PI;
                                if (deg > 90.0)
                                    deg = 180.0 - deg;
                                valueStr = deg.ToString("0.0", CultureInfo.InvariantCulture);
                                unitStr = "°";
                            }
                        }
                    }
                    else if (m.Mode == MeasurementMode.Deviation)
                    {
                        var sxp = m.PixelSpacingX > 0f ? m.PixelSpacingX : 1f;
                        var syp = m.PixelSpacingY > 0f ? m.PixelSpacingY : 1f;
                        var ddx = (m.P2.X - m.P1.X) * sxp;
                        var ddy = (m.P2.Y - m.P1.Y) * syp;
                        var len = Math.Sqrt(ddx * ddx + ddy * ddy);
                        valueStr = string.Format(CultureInfo.InvariantCulture, "dx={0:0.0} dy={1:0.0} L={2:0.0}", ddx, ddy, len);
                        unitStr = m.PixelSpacingX > 0f && m.PixelSpacingY > 0f ? "мм" : "px";
                    }
                    else if (m.Mode == MeasurementMode.Arrow)
                    {
                        var sxp = m.PixelSpacingX > 0f ? m.PixelSpacingX : 1f;
                        var syp = m.PixelSpacingY > 0f ? m.PixelSpacingY : 1f;
                        var dxp = (m.P2.X - m.P1.X) * sxp;
                        var dyp = (m.P2.Y - m.P1.Y) * syp;
                        var len = Math.Sqrt(dxp * dxp + dyp * dyp);
                        valueStr = len.ToString("0.0", CultureInfo.InvariantCulture);
                        unitStr = m.PixelSpacingX > 0f && m.PixelSpacingY > 0f ? "мм" : "px";
                    }
                    else if (m.Mode == MeasurementMode.TextAnnotation)
                    {
                        valueStr = (m.TextValue ?? "").Replace(";", ",");
                    }

                    sb.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "\"{0}\";\"{1}\";\"{2}\";\"{3}\";\"{4}\";\"{5}\";\"{6}\";\"{7}\";\"{8}\";\"{9}\"\n",
                        fileName, type, name, valueStr, areaStr, minStr, maxStr, meanStr, sdStr, unitStr);
                }
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
    }
}

