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
    public sealed class PdfImagePage
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[] JpegBytes { get; set; }
    }

    public static class PdfReportService
    {
        public static void GenerateReport(
            string outputPath,
            Dictionary<string, List<DicomMeasurement>> measurementsByImagePath,
            Func<string, List<DicomMeasurement>, Bitmap> createSnapshot)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentNullException(nameof(outputPath));
            if (measurementsByImagePath == null || measurementsByImagePath.Count == 0)
                throw new InvalidOperationException("Нет измерений для экспорта.");

            var pages = new List<PdfImagePage>();
            var reportLines = new List<string>
            {
                "DicomVUZB Measurements Report",
                "Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                string.Empty
            };

            foreach (var pair in measurementsByImagePath
                         .Where(p => p.Value != null && p.Value.Count > 0)
                         .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                var path = pair.Key;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                reportLines.Add("Image: " + Path.GetFileName(path));
                foreach (var measurement in pair.Value)
                    reportLines.Add(" - " + BuildMeasurementReportLine(measurement));
                reportLines.Add(string.Empty);

                if (createSnapshot != null)
                {
                    using (var snapshot = createSnapshot(path, pair.Value))
                    {
                        if (snapshot != null)
                        {
                            pages.Add(new PdfImagePage
                            {
                                Width = snapshot.Width,
                                Height = snapshot.Height,
                                JpegBytes = EncodeJpeg(snapshot, 92L)
                            });
                        }
                    }
                }
            }

            AppendReportPages(pages, reportLines);

            if (pages.Count == 0)
                throw new InvalidOperationException("Нет доступных снимков или данных для экспорта.");

            WritePdfWithImages(outputPath, pages);
        }

        private static void AppendReportPages(List<PdfImagePage> pages, List<string> lines)
        {
            if (pages == null || lines == null || lines.Count == 0)
                return;

            const int pageWidth = 1240;
            const int pageHeight = 1754;
            const int margin = 56;
            const int contentWidth = pageWidth - margin * 2;

            using (var font = new Font("Consolas", 18f, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var lineHeight = Math.Max(22, (int)Math.Ceiling(font.GetHeight() + 6f));
                var lineIndex = 0;

                while (lineIndex < lines.Count)
                {
                    using (var bitmap = new Bitmap(pageWidth, pageHeight, PixelFormat.Format24bppRgb))
                    using (var g = Graphics.FromImage(bitmap))
                    using (var brush = new SolidBrush(Color.Black))
                    {
                        g.Clear(Color.White);
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                        float y = margin;
                        var drewAny = false;

                        while (lineIndex < lines.Count)
                        {
                            var safeLine = SanitizePdfText(lines[lineIndex] ?? string.Empty);
                            if (string.IsNullOrEmpty(safeLine))
                            {
                                if (y + lineHeight > pageHeight - margin && drewAny)
                                    break;
                                y += lineHeight;
                                drewAny = true;
                                lineIndex++;
                                continue;
                            }

                            var measured = g.MeasureString(safeLine, font, contentWidth);
                            var blockHeight = Math.Max((float)lineHeight, (float)Math.Ceiling(measured.Height + 4f));
                            if (y + blockHeight > pageHeight - margin && drewAny)
                                break;

                            g.DrawString(safeLine, font, brush, new RectangleF(margin, y, contentWidth, pageHeight - margin - y));
                            y += blockHeight;
                            drewAny = true;
                            lineIndex++;
                        }

                        pages.Add(new PdfImagePage
                        {
                            Width = bitmap.Width,
                            Height = bitmap.Height,
                            JpegBytes = EncodeJpeg(bitmap, 92L)
                        });
                    }
                }
            }
        }

        public static void WritePdfWithImages(string path, List<PdfImagePage> pages)
        {
            if (pages == null || pages.Count == 0)
                throw new InvalidOperationException("Нет снимков для экспорта.");

            var objectData = new List<byte[]>
            {
                Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>\n"),
                null
            };

            var pageObjectNumbers = new List<int>();
            for (var i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                var pageObjNum = objectData.Count + 1;
                pageObjectNumbers.Add(pageObjNum);

                var contentText = string.Format(CultureInfo.InvariantCulture, "q {0} 0 0 {1} 0 0 cm /Im{2} Do Q\n", page.Width, page.Height, i + 1);
                var contentBytes = Encoding.ASCII.GetBytes(contentText);
                var contentObjNum = pageObjNum + 1;
                var imageObjNum = pageObjNum + 2;

                var pageObj = string.Format(CultureInfo.InvariantCulture,
                    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0} {1}] /Resources << /XObject << /Im{2} {3} 0 R >> >> /Contents {4} 0 R >>\n",
                    page.Width, page.Height, i + 1, imageObjNum, contentObjNum);

                var contentHeader = string.Format(CultureInfo.InvariantCulture, "<< /Length {0} >>\nstream\n", contentBytes.Length);
                var contentFooter = "\nendstream\n";
                var contentObject = Encoding.ASCII.GetBytes(contentHeader)
                    .Concat(contentBytes)
                    .Concat(Encoding.ASCII.GetBytes(contentFooter))
                    .ToArray();

                var imageHeader = string.Format(CultureInfo.InvariantCulture,
                    "<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>\nstream\n",
                    page.Width, page.Height, page.JpegBytes.Length);
                var imageFooter = "\nendstream\n";
                var imageObject = Encoding.ASCII.GetBytes(imageHeader)
                    .Concat(page.JpegBytes)
                    .Concat(Encoding.ASCII.GetBytes(imageFooter))
                    .ToArray();

                objectData.Add(Encoding.ASCII.GetBytes(pageObj));
                objectData.Add(contentObject);
                objectData.Add(imageObject);
            }

            objectData[1] = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,
                "<< /Type /Pages /Count {0} /Kids [{1}] >>\n",
                pages.Count,
                string.Join(" ", pageObjectNumbers.Select(n => n.ToString(CultureInfo.InvariantCulture) + " 0 R"))));

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var header = Encoding.ASCII.GetBytes("%PDF-1.4\n% 0 1 2 3\n");
                stream.Write(header, 0, header.Length);

                var offsets = new List<long> { 0L };
                for (var i = 0; i < objectData.Count; i++)
                {
                    offsets.Add(stream.Position);
                    var objHeader = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture, "{0} 0 obj\n", i + 1));
                    stream.Write(objHeader, 0, objHeader.Length);
                    var bytes = objectData[i];
                    stream.Write(bytes, 0, bytes.Length);
                    var endObj = Encoding.ASCII.GetBytes("endobj\n");
                    stream.Write(endObj, 0, endObj.Length);
                }

                var xrefPosition = stream.Position;
                var xrefHeader = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture, "xref\n0 {0}\n", objectData.Count + 1));
                stream.Write(xrefHeader, 0, xrefHeader.Length);
                var freeEntry = Encoding.ASCII.GetBytes("0000000000 65535 f \n");
                stream.Write(freeEntry, 0, freeEntry.Length);

                for (var i = 1; i <= objectData.Count; i++)
                {
                    var offsetLine = string.Format(CultureInfo.InvariantCulture, "{0:D10} 00000 n \n", offsets[i - 1]);
                    var offsetBytes = Encoding.ASCII.GetBytes(offsetLine);
                    stream.Write(offsetBytes, 0, offsetBytes.Length);
                }

                var trailer = string.Format(CultureInfo.InvariantCulture,
                    "trailer\n<< /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF\n",
                    objectData.Count + 1, xrefPosition);
                var trailerBytes = Encoding.ASCII.GetBytes(trailer);
                stream.Write(trailerBytes, 0, trailerBytes.Length);
            }
        }

        public static byte[] EncodeJpeg(Image image, long quality)
        {
            var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
            if (codec == null)
            {
                using (var fallback = new MemoryStream())
                {
                    image.Save(fallback, ImageFormat.Jpeg);
                    return fallback.ToArray();
                }
            }

            using (var stream = new MemoryStream())
            using (var parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                image.Save(stream, codec, parameters);
                return stream.ToArray();
            }
        }

        public static string SanitizePdfText(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var builder = new StringBuilder(input.Length);
            foreach (var ch in input)
                builder.Append(!char.IsControl(ch) ? ch : ' ');
            return builder.ToString();
        }

        public static void CalculatePolygonMetrics(DicomMeasurement measurement, out double perimeter, out double area)
        {
            perimeter = 0d;
            area = 0d;
            if (measurement == null)
                return;

            var sxp = measurement.PixelSpacingX > 0f ? (double)measurement.PixelSpacingX : 1.0;
            var syp = measurement.PixelSpacingY > 0f ? (double)measurement.PixelSpacingY : 1.0;
            var pts = measurement.Points != null && measurement.Points.Count > 3
                ? measurement.Points
                : new List<PointF> { measurement.P1, measurement.P2, measurement.P3 };

            var shoelace = 0d;
            for (var i = 0; i < pts.Count; i++)
            {
                var curr = pts[i];
                var next = pts[(i + 1) % pts.Count];
                var dx = (next.X - curr.X) * sxp;
                var dy = (next.Y - curr.Y) * syp;
                perimeter += Math.Sqrt(dx * dx + dy * dy);
                shoelace += (curr.X * sxp) * (next.Y * syp) - (next.X * sxp) * (curr.Y * syp);
            }

            area = Math.Abs(shoelace) * 0.5;
        }

        public static double CalculateRoiGeometricArea(DicomMeasurement measurement)
        {
            if (measurement == null)
                return 0d;

            var sxp = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? (double)measurement.PixelSpacingX : 1.0;
            var syp = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? (double)measurement.PixelSpacingY : 1.0;
            var width = Math.Abs(measurement.P2.X - measurement.P1.X) * sxp;
            var height = Math.Abs(measurement.P2.Y - measurement.P1.Y) * syp;
            if (measurement.Mode == MeasurementMode.RoiCircle)
                return Math.PI * (width * 0.5) * (height * 0.5);
            return width * height;
        }

        public static string BuildMeasurementReportLine(DicomMeasurement measurement)
        {
            if (measurement == null)
                return string.Empty;

            var label = string.IsNullOrWhiteSpace(measurement.Name)
                ? measurement.Mode.ToString()
                : measurement.Name.Trim();

            if (measurement.Mode == MeasurementMode.RoiRectangle || measurement.Mode == MeasurementMode.RoiCircle)
            {
                var areaUnit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "mm2" : "px2";
                var areaVal = measurement.RoiArea.HasValue ? (double)measurement.RoiArea.Value : CalculateRoiGeometricArea(measurement);
                var area = areaVal.ToString("0.0", CultureInfo.InvariantCulture);
                var min = measurement.RoiMin.HasValue ? measurement.RoiMin.Value.ToString("0", CultureInfo.InvariantCulture) : "-";
                var max = measurement.RoiMax.HasValue ? measurement.RoiMax.Value.ToString("0", CultureInfo.InvariantCulture) : "-";
                var mean = measurement.RoiMean.HasValue ? measurement.RoiMean.Value.ToString("0.0", CultureInfo.InvariantCulture) : "-";
                var sd = measurement.RoiStdDev.HasValue ? measurement.RoiStdDev.Value.ToString("0.0", CultureInfo.InvariantCulture) : "-";
                var valUnit = measurement.IsHounsfieldUnit ? "HU" : "val";
                return string.Format(CultureInfo.InvariantCulture, "{0}: S={1} {2}, Min={3} {4}, Max={5} {4}, Mean={6} {4}, SD={7}", label, area, areaUnit, min, valUnit, max, mean, sd);
            }

            if (measurement.Mode == MeasurementMode.Polygon)
            {
                double perim, area;
                CalculatePolygonMetrics(measurement, out perim, out area);
                var hasSpacing = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f;
                var lenUnit = hasSpacing ? "mm" : "px";
                var areaUnit = hasSpacing ? "mm2" : "px2";
                return string.Format(CultureInfo.InvariantCulture, "{0}: P={1:0.0} {2}, S={3:0.0} {4}", label, perim, lenUnit, area, areaUnit);
            }

            if (measurement.Mode == MeasurementMode.OpenPolyline || measurement.Mode == MeasurementMode.Spline)
            {
                var sxp = measurement.PixelSpacingX > 0f ? measurement.PixelSpacingX : 1f;
                var syp = measurement.PixelSpacingY > 0f ? measurement.PixelSpacingY : 1f;
                var points = measurement.Points != null && measurement.Points.Count >= 2
                    ? measurement.Points
                    : new List<PointF> { measurement.P1, measurement.P2, measurement.P3 };
                var length = 0d;
                for (var i = 1; i < points.Count; i++)
                {
                    var dxp = (points[i].X - points[i - 1].X) * sxp;
                    var dyp = (points[i].Y - points[i - 1].Y) * syp;
                    length += Math.Sqrt(dxp * dxp + dyp * dyp);
                }
                var lenUnit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "mm" : "px";
                return string.Format(CultureInfo.InvariantCulture, "{0}: L={1:0.0} {2}", label, length, lenUnit);
            }

            if (measurement.Mode == MeasurementMode.Pencil)
            {
                var sxp = measurement.PixelSpacingX > 0f ? measurement.PixelSpacingX : 1f;
                var syp = measurement.PixelSpacingY > 0f ? measurement.PixelSpacingY : 1f;
                var points = measurement.Points != null && measurement.Points.Count >= 2
                    ? measurement.Points
                    : new List<PointF> { measurement.P1, measurement.P2, measurement.P3 };
                var length = 0d;
                for (var i = 1; i < points.Count; i++)
                {
                    var dxp = (points[i].X - points[i - 1].X) * sxp;
                    var dyp = (points[i].Y - points[i - 1].Y) * syp;
                    length += Math.Sqrt(dxp * dxp + dyp * dyp);
                }
                var lenUnit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "mm" : "px";
                var width = measurement.StrokeWidth > 0f ? measurement.StrokeWidth : 1.2f;
                var opacityPercent = (measurement.StrokeOpacity > 0 ? measurement.StrokeOpacity : 220) * 100f / 255f;
                return string.Format(CultureInfo.InvariantCulture, "{0}: L={1:0.0} {2}, W={3:0.0}px, O={4:0}%", label, length, lenUnit, width, opacityPercent);
            }

            if (measurement.Mode == MeasurementMode.CobbAngle)
            {
                if (measurement.Points == null || measurement.Points.Count < 4)
                    return label + ": -";
                var v1x = measurement.Points[1].X - measurement.Points[0].X;
                var v1y = measurement.Points[1].Y - measurement.Points[0].Y;
                var v2x = measurement.Points[3].X - measurement.Points[2].X;
                var v2y = measurement.Points[3].Y - measurement.Points[2].Y;
                var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                if (len1 < 0.0001 || len2 < 0.0001)
                    return label + ": -";
                var cos = (v1x * v2x + v1y * v2y) / (len1 * len2);
                cos = Math.Max(-1.0, Math.Min(1.0, cos));
                var deg = Math.Acos(cos) * 180.0 / Math.PI;
                if (deg > 90.0)
                    deg = 180.0 - deg;
                return string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.0} deg", label, deg);
            }

            if (measurement.Mode == MeasurementMode.Deviation)
            {
                var sxp = measurement.PixelSpacingX > 0f ? measurement.PixelSpacingX : 1f;
                var syp = measurement.PixelSpacingY > 0f ? measurement.PixelSpacingY : 1f;
                var ddx = (measurement.P2.X - measurement.P1.X) * sxp;
                var ddy = (measurement.P2.Y - measurement.P1.Y) * syp;
                var len = Math.Sqrt(ddx * ddx + ddy * ddy);
                var lenUnit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "mm" : "px";
                return string.Format(CultureInfo.InvariantCulture, "{0}: dx={1:0.0}, dy={2:0.0}, L={3:0.0} {4}", label, ddx, ddy, len, lenUnit);
            }

            if (measurement.Mode == MeasurementMode.Arrow)
            {
                var sxp = measurement.PixelSpacingX > 0f ? measurement.PixelSpacingX : 1f;
                var syp = measurement.PixelSpacingY > 0f ? measurement.PixelSpacingY : 1f;
                var dxp = (measurement.P2.X - measurement.P1.X) * sxp;
                var dyp = (measurement.P2.Y - measurement.P1.Y) * syp;
                var len = Math.Sqrt(dxp * dxp + dyp * dyp);
                var lenUnit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "mm" : "px";
                return string.Format(CultureInfo.InvariantCulture, "{0}: L={1:0.0} {2}", label, len, lenUnit);
            }

            if (measurement.Mode == MeasurementMode.Angle)
            {
                var v1x = measurement.P2.X - measurement.P1.X;
                var v1y = measurement.P2.Y - measurement.P1.Y;
                var v2x = measurement.P3.X - measurement.P1.X;
                var v2y = measurement.P3.Y - measurement.P1.Y;
                var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                if (len1 < 0.0001 || len2 < 0.0001)
                    return label + ": -";
                var dot = (v1x * v2x + v1y * v2y) / (len1 * len2);
                dot = Math.Max(-1.0, Math.Min(1.0, dot));
                var deg = Math.Acos(dot) * 180.0 / Math.PI;
                return string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.0} deg", label, deg);
            }

            if (measurement.Mode == MeasurementMode.Distance)
            {
                var hasSpacing = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f;
                var dx = (measurement.P2.X - measurement.P1.X) * (hasSpacing ? measurement.PixelSpacingX : 1f);
                var dy = (measurement.P2.Y - measurement.P1.Y) * (hasSpacing ? measurement.PixelSpacingY : 1f);
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var unit = hasSpacing ? "mm" : "px";
                return string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.0} {2}", label, dist, unit);
            }

            if (measurement.Mode == MeasurementMode.TextAnnotation)
            {
                return string.Format("{0}: \"{1}\"", label, measurement.TextValue ?? string.Empty);
            }

            return label;
        }
    }
}
