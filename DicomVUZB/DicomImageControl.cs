using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DicomVUZB
{
    public enum ViewerTool
    {
        Pan,
        Measure,
        WindowLevel
    }

    public enum MeasurementMode
    {
        Distance,
        Deviation,
        Arrow,
        TextAnnotation,
        Pencil,
        Angle,
        RoiRectangle,
        RoiCircle,
        Polygon,
        OpenPolyline,
        Spline,
        CobbAngle
    }

    public sealed class DicomMeasurement
    {
        public Guid Id { get; set; }
        public MeasurementMode Mode { get; set; }
        public string Name { get; set; }
        public Color Color { get; set; }
        public PointF P1 { get; set; }
        public PointF P2 { get; set; }
        public PointF P3 { get; set; }
        public float PixelSpacingX { get; set; }
        public float PixelSpacingY { get; set; }
        public float? RoiArea { get; set; }
        public float? RoiMin { get; set; }
        public float? RoiMax { get; set; }
        public float? RoiMean { get; set; }
        public float? RoiStdDev { get; set; }
        public bool IsHounsfieldUnit { get; set; }
        public List<PointF> Points { get; set; }
        public float StrokeWidth { get; set; }
        public int StrokeOpacity { get; set; }
        public string TextValue { get; set; }
    }

    public sealed class DicomImageControl : Control
    {
        private enum MeasurementHitPart
        {
            None,
            Body,
            P1,
            P2,
            P3
        }

        private enum MprCrosshairHitPart
        {
            None,
            Center,
            HorizontalLine,
            VerticalLine,
            RotateHandle,
            SlabHorizontal,
            SlabVertical,
            HeaderBadge
        }

        private readonly object imageSyncRoot = new object();
        private Image image;
        private float zoom = 1f;
        private Point pan;
        private Point dragStart;
        private Point measureStart;
        private Point measureEnd;
        private Point angleVertex;
        private Point angleRay1End;
        private Point angleRay2End;
        private Point polygonP1;
        private Point polygonP2;
        private Point polygonP3;
        private readonly List<Point> openPolylinePoints = new List<Point>();
        private readonly List<Point> splinePoints = new List<Point>();
        private readonly List<Point> cobbPoints = new List<Point>();
        private readonly List<Point> pencilPoints = new List<Point>();
        private readonly List<DicomMeasurement> measurements = new List<DicomMeasurement>();
        private Guid? selectedMeasurementId;
        private MeasurementHitPart selectedMeasurementPart;
        private bool editingMeasurement;
        private Point editingAnchor;
        private bool dragging;
        private bool measuring;
        private bool angleFirstRayLocked;
        private bool angleSecondRayDefined;
        private bool polygonSecondPointLocked;
        private float pixelSpacingX = 1f;
        private float pixelSpacingY = 1f;
        private bool hasPixelSpacing;
        private RectangleF imageBounds = RectangleF.Empty;
        private string cursorOverlay = "X: — Y: — Val: —";
        private float windowCenter = 40f;
        private float windowWidth = 400f;
        private bool windowLevelEnabled;
        private bool adjustingWindowLevel;
        private Point windowLevelStartPoint;
        private float windowLevelStartCenter;
        private float windowLevelStartWidth;
        private Point wlHudPoint;
        private PointF? referencePoint;
        private Color activeMeasurementColor = Color.Lime;
        private int selectedMeasurementPointIndex = -1;
        private bool committingMeasurement;
        private MprCrosshairHitPart hoveredMprPart = MprCrosshairHitPart.None;
        private MprCrosshairHitPart activeMprDragPart = MprCrosshairHitPart.None;
        private float mprRotateStartMouseAngle;
        private float mprRotateStartAngleDegrees;
        private Cursor baseToolCursor = Cursors.Hand;
        private static readonly Random measurementColorRandom = new Random();

        public event Action<int> SliceScrollRequested;
        public event Action ResetWindowLevelRequested;
        public event Action WindowLevelChanged;
        public event Action<Point> WindowLevelMenuRequested;
        public event Action NormalizeWindowLevelRequested;
        public event Action<Point, DicomMeasurement> MeasurementContextMenuRequested;
        public event Action MeasurementsChanged;
        public event Action<PointF?> CursorImagePositionChanged;
        public event Func<Point, string> TextAnnotationTextRequested;
        public event Action<PointF, bool, bool> MprCrosshairMoved;
        public event Action<float> MprCrosshairAngleChanged;
        public event Action<float> MprSlabThicknessChanged;
        public event Action<Point> MprContextMenuRequested;

        private void BeginWindowLevelAdjust(Point location)
        {
            adjustingWindowLevel = true;
            windowLevelStartPoint = location;
            windowLevelStartCenter = windowCenter;
            windowLevelStartWidth = windowWidth;
            wlHudPoint = location;
            windowLevelEnabled = true;
            Capture = true;
        }

        public DicomImageControl()
        {
            DoubleBuffered = true;
            BackColor = Color.Black;
            ForeColor = Color.Lime;
            Tool = ViewerTool.Pan;
            MeasurementMode = MeasurementMode.Distance;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = true;
            ShowReferenceLines = true;
            PencilContinuousMode = false;
            PencilSimplifyEnabled = true;
            PencilSmoothingEnabled = true;
            PencilStrokeWidth = 1.2f;
            PencilOpacity = 220;
        }

        public void SetReferencePoint(PointF? point)
        {
            referencePoint = point;
            Invalidate();
        }

        public PointF? ReferencePoint
        {
            get { return referencePoint; }
        }

        public ViewerTool Tool { get; set; }
        public MeasurementMode MeasurementMode { get; set; }
        private bool inverted;
        public bool Inverted
        {
            get { return inverted; }
            set
            {
                if (inverted != value)
                {
                    inverted = value;
                    if (PixelDataProvider != null)
                    {
                        var newBmp = PixelDataProvider.RenderBitmap(windowCenter, windowWidth, inverted);
                        if (newBmp != null)
                            UpdateImageOnly(newBmp);
                    }
                    Invalidate();
                }
            }
        }
        public string Annotation { get; set; }
        public string OverlayTopLeft { get; set; }
        public string OverlayTopRight { get; set; }
        public string OverlayBottomLeft { get; set; }
        public string OverlayBottomRight { get; set; }
        public string OrientationTop { get; set; }
        public string OrientationBottom { get; set; }
        public string OrientationLeft { get; set; }
        public string OrientationRight { get; set; }
        public Color? ReferenceLineColorHorizontal { get; set; }
        public Color? ReferenceLineColorVertical { get; set; }
        public float ReferenceLineAngleDegrees { get; set; }
        public float ReferenceSlabThicknessMm { get; set; }
        public bool ShowReferenceLines { get; set; }
        public bool MprInteractiveCrosshairEnabled { get; set; }
        public bool MprPanelEnabled { get; set; }
        public bool IsMprCrosshairDragActive { get { return activeMprDragPart != MprCrosshairHitPart.None; } }
        public Cursor BaseToolCursor
        {
            get { return baseToolCursor; }
            set { baseToolCursor = value ?? Cursors.Default; }
        }
        public float Zoom { get { return zoom; } }
        public Point Pan { get { return pan; } }
        public float WindowCenter { get { return windowCenter; } }
        public float WindowWidth { get { return windowWidth; } }
        public bool WindowLevelEnabled { get { return windowLevelEnabled; } }
        public bool PencilContinuousMode { get; set; }
        public bool PencilSimplifyEnabled { get; set; }
        public bool PencilSmoothingEnabled { get; set; }
        public float PencilStrokeWidth { get; set; }
        public int PencilOpacity { get; set; }

        public void SetView(float zoomValue, Point panValue)
        {
            zoom = Math.Max(0.1f, Math.Min(12f, zoomValue));
            pan = panValue;
            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }
            if (currentImage != null)
            {
                imageBounds = CalculateImageBounds(currentImage, zoom, pan);
            }
            Invalidate();
        }

        public List<DicomMeasurement> GetMeasurements()
        {
            return measurements.Select(CloneMeasurement).ToList();
        }

        public Guid? GetSelectedMeasurementId()
        {
            return selectedMeasurementId;
        }

        public bool SelectMeasurement(Guid id)
        {
            var item = measurements.FirstOrDefault(m => m.Id == id);
            if (item == null)
                return false;

            selectedMeasurementId = item.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            selectedMeasurementPointIndex = -1;
            Invalidate();
            return true;
        }

        public bool RemoveSplinePoint(Guid id, PointF imagePoint)
        {
            var measurement = measurements.FirstOrDefault(m => m.Id == id && m.Mode == MeasurementMode.Spline);
            if (measurement == null)
                return false;

            NormalizeMeasurementPoints(measurement);
            if (measurement.Points == null || measurement.Points.Count <= 2)
                return false;

            var index = FindNearestPointIndex(measurement.Points, imagePoint);
            if (index < 0)
                return false;

            measurement.Points.RemoveAt(index);
            NormalizeMeasurementPoints(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            selectedMeasurementPointIndex = Math.Min(index, measurement.Points.Count - 1);
            if (MeasurementsChanged != null)
                MeasurementsChanged();
            Invalidate();
            return true;
        }

        public bool InsertSplinePoint(Guid id, PointF imagePoint)
        {
            var measurement = measurements.FirstOrDefault(m => m.Id == id && m.Mode == MeasurementMode.Spline);
            if (measurement == null)
                return false;

            NormalizeMeasurementPoints(measurement);
            if (measurement.Points == null || measurement.Points.Count < 2)
                return false;

            var insertIndex = measurement.Points.Count;
            var bestDistance = float.MaxValue;
            for (var i = 1; i < measurement.Points.Count; i++)
            {
                var distance = DistancePointToSegment(imagePoint, measurement.Points[i - 1], measurement.Points[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    insertIndex = i;
                }
            }

            measurement.Points.Insert(insertIndex, imagePoint);
            NormalizeMeasurementPoints(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            selectedMeasurementPointIndex = insertIndex;
            if (MeasurementsChanged != null)
                MeasurementsChanged();
            Invalidate();
            return true;
        }

        private static string LimitTextLength(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var compact = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (compact.Length <= maxLength)
                return compact;
            return compact.Substring(0, Math.Max(0, maxLength - 1)) + "…";
        }

        private static string WrapText(string value, int chunkLength)
        {
            if (string.IsNullOrWhiteSpace(value) || chunkLength <= 0)
                return value;

            var words = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return value;

            var lines = new List<string>();
            var current = string.Empty;
            foreach (var word in words)
            {
                if (current.Length == 0)
                {
                    current = word;
                    continue;
                }

                if (current.Length + 1 + word.Length <= chunkLength)
                {
                    current += " " + word;
                }
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }

            if (current.Length > 0)
                lines.Add(current);

            return string.Join("\n", lines);
        }

        private static string BuildTextAnnotationOverlayText(string rawText)
        {
            var limited = LimitTextLength(rawText, 180);
            if (string.IsNullOrWhiteSpace(limited))
                limited = "Text";
            var prefixed = "Text: " + limited;
            return WrapText(prefixed, 34);
        }

        private static void DrawTextAnnotationLabel(Graphics graphics, Font baseFont, Color color, Point anchor, string text)
        {
            var drawText = BuildTextAnnotationOverlayText(text);
            using (var font = new Font("Segoe UI", Math.Max(8f, baseFont.SizeInPoints), FontStyle.Bold, GraphicsUnit.Point))
            {
                var size = graphics.MeasureString(drawText, font, 240);
                var x = anchor.X + 8f;
                var y = anchor.Y + 6f;
                var rect = new RectangleF(x - 4f, y - 3f, size.Width + 8f, size.Height + 6f);
                using (var background = new SolidBrush(Color.FromArgb(170, 14, 14, 14)))
                using (var textBrush = new SolidBrush(color))
                using (var border = new Pen(Color.FromArgb(150, color), 1f))
                {
                    graphics.FillRectangle(background, rect);
                    graphics.DrawRectangle(border, Rectangle.Round(rect));
                    graphics.DrawString(drawText, font, textBrush, new RectangleF(x, y, 240f, size.Height + 2f));
                }
            }
        }

        public string FormatMeasurement(DicomMeasurement measurement)
        {
            if (measurement == null)
                return string.Empty;
            var clone = CloneMeasurement(measurement);
            CalculateRoiStatistics(clone);
            return BuildMeasurementText(clone);
        }

        public void SetMeasurements(IEnumerable<DicomMeasurement> items)
        {
            measurements.Clear();
            if (items != null)
            {
                foreach (var item in items)
                {
                    var clone = CloneMeasurement(item);
                    if (clone.Color.IsEmpty)
                        clone.Color = CreateRandomMeasurementColor();
                    NormalizeMeasurementPoints(clone);
                    CalculateRoiStatistics(clone);
                    measurements.Add(clone);
                }
            }

            selectedMeasurementId = null;
            selectedMeasurementPart = MeasurementHitPart.None;
            selectedMeasurementPointIndex = -1;
            referencePoint = null;
            Invalidate();
        }

        private static bool TryGetBitmapPixelIntensity(Bitmap bitmap, int x, int y, out int value)
        {
            value = -1;
            if (bitmap == null)
                return false;
            if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
                return false;

            try
            {
                var pixel = bitmap.GetPixel(x, y);
                value = (pixel.R + pixel.G + pixel.B) / 3;
                return true;
            }
            catch
            {
                value = -1;
                return false;
            }
        }

        private DicomPixelDataProvider pixelDataProvider;
        public DicomPixelDataProvider PixelDataProvider
        {
            get { return pixelDataProvider; }
            set
            {
                pixelDataProvider = value;
                if (pixelDataProvider != null)
                {
                    if (!windowLevelEnabled)
                    {
                        windowCenter = pixelDataProvider.DefaultWindowCenter;
                        windowWidth = pixelDataProvider.DefaultWindowWidth;
                    }
                    var newBmp = pixelDataProvider.RenderBitmap(windowCenter, windowWidth, Inverted);
                    if (newBmp != null)
                        UpdateImageOnly(newBmp);
                }
            }
        }

        public bool DeleteSelectedMeasurement()
        {
            if (selectedMeasurementId.HasValue)
                return RemoveMeasurement(selectedMeasurementId.Value);
            return false;
        }

        public bool RemoveMeasurement(Guid id)
        {
            var index = measurements.FindIndex(m => m.Id == id);
            if (index < 0)
                return false;
            measurements.RemoveAt(index);
            if (selectedMeasurementId == id)
            {
                selectedMeasurementId = null;
                selectedMeasurementPart = MeasurementHitPart.None;
                selectedMeasurementPointIndex = -1;
            }
            if (MeasurementsChanged != null)
                MeasurementsChanged();
            Invalidate();
            return true;
        }

        public bool UpdateMeasurementProperties(Guid id, string name, Color color, float? strokeWidth = null, int? strokeOpacity = null, string textValue = null)
        {
            var item = measurements.FirstOrDefault(m => m.Id == id);
            if (item == null)
                return false;
            item.Name = string.IsNullOrWhiteSpace(name) ? item.Name : name.Trim();
            item.Color = color;
            if (item.Mode == MeasurementMode.TextAnnotation && textValue != null)
                item.TextValue = textValue;
            if (item.Mode == MeasurementMode.Pencil)
            {
                item.StrokeWidth = Math.Max(0.2f, strokeWidth ?? (item.StrokeWidth > 0f ? item.StrokeWidth : PencilStrokeWidth));
                item.StrokeOpacity = Math.Max(1, Math.Min(255, strokeOpacity ?? (item.StrokeOpacity > 0 ? item.StrokeOpacity : PencilOpacity)));
            }
            if (MeasurementsChanged != null)
                MeasurementsChanged();
            Invalidate();
            return true;
        }

        private static DicomMeasurement CloneMeasurement(DicomMeasurement source)
        {
            if (source == null)
                return null;
            return new DicomMeasurement
            {
                Id = source.Id,
                Mode = source.Mode,
                Name = source.Name,
                Color = source.Color,
                P1 = source.P1,
                P2 = source.P2,
                P3 = source.P3,
                PixelSpacingX = source.PixelSpacingX,
                PixelSpacingY = source.PixelSpacingY,
                RoiArea = source.RoiArea,
                RoiMin = source.RoiMin,
                RoiMax = source.RoiMax,
                RoiMean = source.RoiMean,
                RoiStdDev = source.RoiStdDev,
                IsHounsfieldUnit = source.IsHounsfieldUnit,
                Points = source.Points != null ? source.Points.Select(p => new PointF(p.X, p.Y)).ToList() : null,
                StrokeWidth = source.StrokeWidth,
                StrokeOpacity = source.StrokeOpacity,
                TextValue = source.TextValue
            };
        }

        private static void NormalizeMeasurementPoints(DicomMeasurement measurement)
        {
            if (measurement == null)
                return;

            if (measurement.Mode == MeasurementMode.OpenPolyline)
            {
                if (measurement.Points == null || measurement.Points.Count < 2)
                    measurement.Points = new List<PointF> { measurement.P1, measurement.P2, measurement.P3 };
                measurement.Points = measurement.Points.Where((p, i) => i == 0 || p != measurement.Points[i - 1]).ToList();
                if (measurement.Points.Count < 2)
                    measurement.Points = new List<PointF> { measurement.P1, measurement.P2 };
                measurement.P1 = measurement.Points[0];
                measurement.P2 = measurement.Points[Math.Min(1, measurement.Points.Count - 1)];
                measurement.P3 = measurement.Points[measurement.Points.Count - 1];
                return;
            }

            if (measurement.Mode == MeasurementMode.TextAnnotation)
            {
                measurement.P3 = PointF.Empty;
                return;
            }

            if (measurement.Mode == MeasurementMode.Spline)
            {
                if (measurement.Points == null || measurement.Points.Count < 2)
                    measurement.Points = new List<PointF> { measurement.P1, measurement.P2, measurement.P3 };
                measurement.Points = measurement.Points.Where((p, i) => i == 0 || p != measurement.Points[i - 1]).ToList();
                if (measurement.Points.Count < 2)
                    measurement.Points = new List<PointF> { measurement.P1, measurement.P2 };
                measurement.P1 = measurement.Points[0];
                measurement.P2 = measurement.Points[Math.Min(1, measurement.Points.Count - 1)];
                measurement.P3 = measurement.Points[measurement.Points.Count - 1];
                return;
            }

            if (measurement.Mode == MeasurementMode.Pencil)
            {
                if (measurement.Points == null || measurement.Points.Count < 2)
                    measurement.Points = new List<PointF> { measurement.P1, measurement.P2, measurement.P3 };
                measurement.Points = measurement.Points.Where((p, i) => i == 0 || p != measurement.Points[i - 1]).ToList();
                if (measurement.Points.Count < 2)
                    measurement.Points = new List<PointF> { measurement.P1, measurement.P2 };
                if (measurement.StrokeWidth <= 0f)
                    measurement.StrokeWidth = 1.2f;
                if (measurement.StrokeOpacity <= 0)
                    measurement.StrokeOpacity = 220;
                measurement.P1 = measurement.Points[0];
                measurement.P2 = measurement.Points[Math.Min(1, measurement.Points.Count - 1)];
                measurement.P3 = measurement.Points[measurement.Points.Count - 1];
                return;
            }

            if (measurement.Mode == MeasurementMode.Polygon)
                measurement.Points = new List<PointF> { measurement.P1, measurement.P2, measurement.P3 };

            if (measurement.Mode == MeasurementMode.CobbAngle)
            {
                if (measurement.Points == null || measurement.Points.Count < 4)
                    measurement.Points = new List<PointF> { measurement.P1, measurement.P2, measurement.P3, measurement.P3 };
                measurement.P1 = measurement.Points[0];
                measurement.P2 = measurement.Points[Math.Min(1, measurement.Points.Count - 1)];
                measurement.P3 = measurement.Points[Math.Min(2, measurement.Points.Count - 1)];
            }
        }

        public void SetPixelSpacing(float spacingX, float spacingY)
        {
            hasPixelSpacing = spacingX > 0f && spacingY > 0f;
            pixelSpacingX = hasPixelSpacing ? spacingX : 1f;
            pixelSpacingY = hasPixelSpacing ? spacingY : 1f;
            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }
            if (currentImage != null)
            {
                imageBounds = CalculateImageBounds(currentImage, zoom, pan);
            }
            Invalidate();
        }

        public float CurrentPixelSpacingX { get { return pixelSpacingX; } }
        public float CurrentPixelSpacingY { get { return pixelSpacingY; } }
        public bool HasPixelSpacing { get { return hasPixelSpacing; } }

        public void SetWindowLevel(float center, float width, bool enabled)
        {
            windowCenter = center;
            windowWidth = Math.Max(1f, width);
            windowLevelEnabled = enabled;

            if (PixelDataProvider != null)
            {
                var newBmp = PixelDataProvider.RenderBitmap(windowCenter, windowWidth, Inverted);
                if (newBmp != null)
                    UpdateImageOnly(newBmp);
            }
            Invalidate();
        }

        private void UpdateImageOnly(Image newImage)
        {
            Image oldImage = null;
            lock (imageSyncRoot)
            {
                if (image != null && !ReferenceEquals(image, newImage))
                    oldImage = image;
                image = newImage;
            }
            if (oldImage != null)
            {
                try { oldImage.Dispose(); } catch { }
            }
            if (newImage != null)
            {
                imageBounds = CalculateImageBounds(newImage, zoom, pan);
            }
            Invalidate();
        }

        public void SetImage(Image value, bool resetView)
        {
            Image oldImage = null;
            lock (imageSyncRoot)
            {
                if (image != null && !ReferenceEquals(image, value))
                    oldImage = image;
                image = value;
            }
            if (oldImage != null)
            {
                try { oldImage.Dispose(); } catch { }
            }

            if (resetView)
            {
                zoom = 1f;
                pan = Point.Empty;
            }
            ResetMeasurement();
            measurements.Clear();
            selectedMeasurementId = null;
            selectedMeasurementPart = MeasurementHitPart.None;
            if (value != null)
            {
                imageBounds = CalculateImageBounds(value, zoom, pan);
            }
            Invalidate();
        }

        public void CommitPendingMeasurement()
        {
            if (!measuring || committingMeasurement)
                return;

            committingMeasurement = true;
            try
            {
                var committed = false;
                switch (MeasurementMode)
                {
                    case MeasurementMode.OpenPolyline:
                        committed = CommitOpenPolylineMeasurement();
                        break;
                    case MeasurementMode.Spline:
                        committed = CommitSplineMeasurement();
                        break;
                    case MeasurementMode.Pencil:
                        committed = CommitPencilMeasurement();
                        break;
                    case MeasurementMode.Polygon:
                        if (polygonSecondPointLocked)
                            committed = CommitPolygonMeasurement();
                        break;
                    case MeasurementMode.Angle:
                        if (angleFirstRayLocked)
                            committed = CommitAngleMeasurement();
                        break;
                    case MeasurementMode.CobbAngle:
                        if (cobbPoints.Count >= 4)
                            committed = CommitCobbMeasurement();
                        break;
                }

                if (committed)
                {
                    ResetMeasurement();
                    Invalidate();
                }
            }
            finally
            {
                committingMeasurement = false;
            }
        }

        private void ResetMeasurement()
        {
            measureStart = Point.Empty;
            measureEnd = Point.Empty;
            angleVertex = Point.Empty;
            angleRay1End = Point.Empty;
            angleRay2End = Point.Empty;
            polygonP1 = Point.Empty;
            polygonP2 = Point.Empty;
            polygonP3 = Point.Empty;
            openPolylinePoints.Clear();
            splinePoints.Clear();
            cobbPoints.Clear();
            pencilPoints.Clear();
            measuring = false;
            angleFirstRayLocked = false;
            angleSecondRayDefined = false;
            polygonSecondPointLocked = false;
            selectedMeasurementPointIndex = -1;
        }

        public bool InsertPencilPoint(Guid id, PointF imagePoint)
        {
            var measurement = measurements.FirstOrDefault(m => m.Id == id && m.Mode == MeasurementMode.Pencil);
            if (measurement == null)
                return false;

            NormalizeMeasurementPoints(measurement);
            if (measurement.Points == null || measurement.Points.Count < 2)
                return false;

            var insertIndex = measurement.Points.Count;
            var bestDistance = float.MaxValue;
            for (var i = 1; i < measurement.Points.Count; i++)
            {
                var distance = DistancePointToSegment(imagePoint, measurement.Points[i - 1], measurement.Points[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    insertIndex = i;
                }
            }

            measurement.Points.Insert(insertIndex, imagePoint);
            NormalizeMeasurementPoints(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            selectedMeasurementPointIndex = insertIndex;
            if (MeasurementsChanged != null)
                MeasurementsChanged();
            Invalidate();
            return true;
        }

        public bool RemovePencilPoint(Guid id, PointF imagePoint)
        {
            var measurement = measurements.FirstOrDefault(m => m.Id == id && m.Mode == MeasurementMode.Pencil);
            if (measurement == null)
                return false;

            NormalizeMeasurementPoints(measurement);
            if (measurement.Points == null || measurement.Points.Count <= 2)
                return false;

            var index = FindNearestPointIndex(measurement.Points, imagePoint);
            if (index < 0)
                return false;

            measurement.Points.RemoveAt(index);
            NormalizeMeasurementPoints(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            selectedMeasurementPointIndex = Math.Min(index, measurement.Points.Count - 1);
            if (MeasurementsChanged != null)
                MeasurementsChanged();
            Invalidate();
            return true;
        }

        private static int FindNearestPointIndex(List<PointF> points, PointF imagePoint)
        {
            if (points == null || points.Count == 0)
                return -1;

            var bestIndex = -1;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < points.Count; i++)
            {
                var dx = points[i].X - imagePoint.X;
                var dy = points[i].Y - imagePoint.Y;
                var distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        private static Color CreateRandomMeasurementColor()
        {
            lock (measurementColorRandom)
            {
                return Color.FromArgb(255,
                    measurementColorRandom.Next(70, 256),
                    measurementColorRandom.Next(70, 256),
                    measurementColorRandom.Next(70, 256));
            }
        }

        public RectangleF CalculateImageBounds(Image img, float currentZoom, Point currentPan)
        {
            if (img == null || img.Width <= 0 || img.Height <= 0 || Width <= 0 || Height <= 0)
                return RectangleF.Empty;

            float aspect = 1.0f;
            if (hasPixelSpacing && pixelSpacingX > 0f && pixelSpacingY > 0f)
            {
                aspect = pixelSpacingX / pixelSpacingY;
                if (aspect < 0.01f || aspect > 100f || float.IsNaN(aspect) || float.IsInfinity(aspect))
                    aspect = 1.0f;
            }

            float naturalWidth = img.Width * aspect;
            float naturalHeight = img.Height;

            float fitScale = Math.Min((float)Width / naturalWidth, (float)Height / naturalHeight);
            if (fitScale <= 0f || float.IsNaN(fitScale) || float.IsInfinity(fitScale))
                fitScale = 1.0f;

            float effectiveZoom = Math.Max(0.05f, Math.Min(50f, currentZoom));

            float destWidth = naturalWidth * fitScale * effectiveZoom;
            float destHeight = naturalHeight * fitScale * effectiveZoom;

            float destX = (Width - destWidth) / 2f + currentPan.X;
            float destY = (Height - destHeight) / 2f + currentPan.Y;

            return new RectangleF(destX, destY, destWidth, destHeight);
        }

        public RectangleF GetImageBounds()
        {
            if (imageBounds.Width > 0f && imageBounds.Height > 0f)
                return imageBounds;
            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }
            return CalculateImageBounds(currentImage, zoom, pan);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }
            if (currentImage != null)
            {
                imageBounds = CalculateImageBounds(currentImage, zoom, pan);
            }
            Invalidate();
        }

        public void ResetView()
        {
            zoom = 1f;
            pan = Point.Empty;
            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }
            if (currentImage != null)
            {
                imageBounds = CalculateImageBounds(currentImage, zoom, pan);
            }
            Invalidate();
        }

        public void FitToWindow()
        {
            ResetView();
        }

        public void SetActualPixels()
        {
            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }
            if (currentImage != null && Width > 0 && Height > 0)
            {
                float aspect = 1.0f;
                if (hasPixelSpacing && pixelSpacingX > 0f && pixelSpacingY > 0f)
                {
                    aspect = pixelSpacingX / pixelSpacingY;
                    if (aspect < 0.01f || aspect > 100f || float.IsNaN(aspect) || float.IsInfinity(aspect))
                        aspect = 1.0f;
                }
                float naturalWidth = currentImage.Width * aspect;
                float naturalHeight = currentImage.Height;
                float fitScale = Math.Min((float)Width / naturalWidth, (float)Height / naturalHeight);
                if (fitScale > 0.0001f)
                {
                    zoom = 1.0f / fitScale;
                    pan = Point.Empty;
                    imageBounds = CalculateImageBounds(currentImage, zoom, pan);
                    Invalidate();
                    return;
                }
            }
            ResetView();
        }

        public void ZoomBy(float factor)
        {
            ZoomByAt(factor, new Point(Width / 2, Height / 2));
        }

        public void ZoomByAt(float factor, Point focusPoint)
        {
            var oldZoom = zoom;
            var newZoom = Math.Max(0.1f, Math.Min(12f, zoom * factor));
            if (Math.Abs(newZoom - oldZoom) < 0.0001f)
                return;

            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }

            if (currentImage != null && Width > 0 && Height > 0)
            {
                var oldBounds = CalculateImageBounds(currentImage, oldZoom, pan);
                if (oldBounds.Width > 0f && oldBounds.Height > 0f)
                {
                    var normX = (focusPoint.X - oldBounds.X) / oldBounds.Width;
                    var normY = (focusPoint.Y - oldBounds.Y) / oldBounds.Height;

                    var newBoundsZeroPan = CalculateImageBounds(currentImage, newZoom, Point.Empty);
                    var targetScreenX = focusPoint.X - normX * newBoundsZeroPan.Width;
                    var targetScreenY = focusPoint.Y - normY * newBoundsZeroPan.Height;

                    pan.X = (int)Math.Round(targetScreenX - newBoundsZeroPan.X);
                    pan.Y = (int)Math.Round(targetScreenY - newBoundsZeroPan.Y);
                }
            }

            zoom = newZoom;
            if (currentImage != null)
            {
                imageBounds = CalculateImageBounds(currentImage, zoom, pan);
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Image toDispose = null;
                lock (imageSyncRoot)
                {
                    toDispose = image;
                    image = null;
                }
                if (toDispose != null)
                {
                    try { toDispose.Dispose(); } catch { }
                }
            }
            base.Dispose(disposing);
        }

        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [System.Security.SecurityCritical]
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Color.Black);

            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }

            if (currentImage == null)
            {
                using (var brush = new SolidBrush(Color.DimGray))
                    e.Graphics.DrawString("Откройте DICOM-файл или папку", Font, brush, 20, 20);
                return;
            }

            try
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var destination = CalculateImageBounds(currentImage, zoom, pan);
                imageBounds = destination;

                var destRect = Rectangle.Round(destination);
                if (destRect.Width > 0 && destRect.Height > 0 && currentImage.Width > 0 && currentImage.Height > 0)
                {
                    if (PixelDataProvider != null)
                    {
                        e.Graphics.DrawImage(currentImage, destRect, 0, 0, currentImage.Width, currentImage.Height, GraphicsUnit.Pixel);
                    }
                    else if (Inverted || windowLevelEnabled)
                    {
                        using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                        {
                            attributes.SetGamma(1f);
                            var contrast = windowLevelEnabled ? Math.Max(0.1f, 256f / windowWidth) : 1f;
                            var brightness = windowLevelEnabled ? (128f - windowCenter) / 255f : 0f;
                            var sign = Inverted ? -contrast : contrast;
                            var offset = Inverted ? 1f + brightness : brightness;
                            attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new[]
                            {
                                new[] { sign, 0f, 0f, 0f, 0f },
                                new[] { 0f, sign, 0f, 0f, 0f },
                                new[] { 0f, 0f, sign, 0f, 0f },
                                new[] { 0f, 0f, 0f, 1f, 0f },
                                new[] { offset, offset, offset, 0f, 1f }
                            }));
                            e.Graphics.DrawImage(currentImage, destRect, 0, 0, currentImage.Width, currentImage.Height, GraphicsUnit.Pixel, attributes);
                        }
                    }
                    else
                    {
                        e.Graphics.DrawImage(currentImage, destRect, 0, 0, currentImage.Width, currentImage.Height, GraphicsUnit.Pixel);
                    }
                }
            }
            catch (Exception)
            {
                // Подавляем возможные сбои нативного рендера GDI+
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            DrawReferenceLines(e.Graphics);

            DrawStoredMeasurements(e.Graphics);
            if (measuring)
            {
                switch (MeasurementMode)
                {
                    case MeasurementMode.Deviation:
                        DrawDeviationMeasurement(e.Graphics);
                        break;
                    case MeasurementMode.Arrow:
                        DrawArrowMeasurement(e.Graphics);
                        break;
                    case MeasurementMode.TextAnnotation:
                        DrawTextAnnotationPreview(e.Graphics);
                        break;
                    case MeasurementMode.Pencil:
                        DrawPencilMeasurement(e.Graphics);
                        break;
                    case MeasurementMode.Angle:
                        DrawAngleMeasurement(e.Graphics);
                        break;
                    case MeasurementMode.Polygon:
                        DrawPolygonMeasurement(e.Graphics);
                        break;
                    case MeasurementMode.OpenPolyline:
                        DrawOpenPolylineMeasurement(e.Graphics);
                        break;
                    case MeasurementMode.Spline:
                        DrawSplineMeasurement(e.Graphics);
                        break;
                    case MeasurementMode.CobbAngle:
                        DrawCobbMeasurement(e.Graphics);
                        break;
                    case MeasurementMode.RoiRectangle:
                    case MeasurementMode.RoiCircle:
                        DrawRoiMeasurementPreview(e.Graphics, MeasurementMode, measureStart, measureEnd);
                        break;
                    default:
                        DrawDistanceMeasurement(e.Graphics);
                        break;
                }
            }

            using (var overlayFont = new Font("Consolas", 11f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var overlayYellow = new SolidBrush(Color.FromArgb(255, 255, 239, 140)))
            using (var overlayOrange = new SolidBrush(Color.FromArgb(255, 255, 168, 132)))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            {
                if (MprPanelEnabled && !string.IsNullOrWhiteSpace(OverlayTopLeft))
                {
                    var headerSize = e.Graphics.MeasureString(OverlayTopLeft, overlayFont);
                    var headerRect = new RectangleF(8f, 6f, headerSize.Width + 10f, headerSize.Height + 6f);
                    var isHeaderHovered = hoveredMprPart == MprCrosshairHitPart.HeaderBadge;
                    using (var headerBg = new SolidBrush(Color.FromArgb(isHeaderHovered ? 195 : 120, 20, 26, 36)))
                    using (var headerPen = new Pen(Color.FromArgb(isHeaderHovered ? 230 : 120, 110, 195, 255), 1f))
                    {
                        e.Graphics.FillRectangle(headerBg, headerRect);
                        e.Graphics.DrawRectangle(headerPen, headerRect.X, headerRect.Y, headerRect.Width, headerRect.Height);
                    }
                }
                DrawOverlayBlock(e.Graphics, OverlayTopLeft, overlayFont, overlayOrange, shadowBrush, new PointF(12, 10), StringAlignment.Near, StringAlignment.Near);
                DrawOverlayBlock(e.Graphics, OverlayTopRight, overlayFont, overlayYellow, shadowBrush, new PointF(Width - 12, 10), StringAlignment.Far, StringAlignment.Near);

                var bottomLeftText = string.IsNullOrWhiteSpace(OverlayBottomLeft)
                    ? cursorOverlay
                    : cursorOverlay + "\n" + OverlayBottomLeft;
                DrawOverlayBlock(e.Graphics, bottomLeftText, overlayFont, overlayOrange, shadowBrush, new PointF(12, Height - 12), StringAlignment.Near, StringAlignment.Far);
                DrawOverlayBlock(e.Graphics, OverlayBottomRight, overlayFont, overlayYellow, shadowBrush, new PointF(Width - 12, Height - 12), StringAlignment.Far, StringAlignment.Far);

                DrawOverlayBlock(e.Graphics, string.IsNullOrEmpty(OrientationTop) ? "A" : OrientationTop, overlayFont, overlayYellow, shadowBrush, new PointF(Width / 2f, 10), StringAlignment.Center, StringAlignment.Near);
                DrawOverlayBlock(e.Graphics, string.IsNullOrEmpty(OrientationBottom) ? "P" : OrientationBottom, overlayFont, overlayYellow, shadowBrush, new PointF(Width / 2f, Height - 12), StringAlignment.Center, StringAlignment.Far);
                DrawOverlayBlock(e.Graphics, string.IsNullOrEmpty(OrientationLeft) ? "R" : OrientationLeft, overlayFont, overlayYellow, shadowBrush, new PointF(12, Height / 2f), StringAlignment.Near, StringAlignment.Center);
                DrawOverlayBlock(e.Graphics, string.IsNullOrEmpty(OrientationRight) ? "L" : OrientationRight, overlayFont, overlayYellow, shadowBrush, new PointF(Width - 12, Height / 2f), StringAlignment.Far, StringAlignment.Center);
                DrawLeftScale(e.Graphics);

                if (!string.IsNullOrWhiteSpace(Annotation))
                    DrawOverlayBlock(e.Graphics, Annotation, overlayFont, overlayYellow, shadowBrush, new PointF(12, 34), StringAlignment.Near, StringAlignment.Near);

                if (adjustingWindowLevel)
                    DrawWindowLevelHud(e.Graphics, overlayFont, overlayYellow, shadowBrush);
            }
        }

        private static float GetRayDistanceToRect(float cx, float cy, float dx, float dy, RectangleF rect)
        {
            var tMin = float.MaxValue;
            if (Math.Abs(dx) > 1e-5f)
            {
                var tx1 = (rect.Left - cx) / dx;
                var tx2 = (rect.Right - cx) / dx;
                if (tx1 > 0f && tx1 < tMin) tMin = tx1;
                if (tx2 > 0f && tx2 < tMin) tMin = tx2;
            }
            if (Math.Abs(dy) > 1e-5f)
            {
                var ty1 = (rect.Top - cy) / dy;
                var ty2 = (rect.Bottom - cy) / dy;
                if (ty1 > 0f && ty1 < tMin) tMin = ty1;
                if (ty2 > 0f && ty2 < tMin) tMin = ty2;
            }
            return tMin == float.MaxValue ? 0f : tMin;
        }

        private static float ComputeHandleDistanceOnRay(float cx, float cy, float dx, float dy, RectangleF bounds)
        {
            var maxRay = GetRayDistanceToRect(cx, cy, dx, dy, bounds);
            if (maxRay < 24f)
                return 0f;
            return Math.Max(22f, Math.Min(maxRay - 14f, maxRay * 0.72f));
        }

        private static float ComputeSlabHandleDistanceOnRay(float cx, float cy, float dx, float dy, RectangleF bounds)
        {
            var maxRay = GetRayDistanceToRect(cx, cy, dx, dy, bounds);
            if (maxRay < 42f)
                return 0f;
            return Math.Max(18f, Math.Min(maxRay * 0.42f, maxRay - 28f));
        }

        private float GetScreenPixelsPerMillimeter(Image currentImage, RectangleF bounds)
        {
            if (currentImage == null || bounds.Width <= 0f || bounds.Height <= 0f)
                return 1f;
            var spX = Math.Max(0.05f, pixelSpacingX);
            var spY = Math.Max(0.05f, pixelSpacingY);
            var pxPerMmX = (bounds.Width / Math.Max(1f, currentImage.Width)) / spX;
            var pxPerMmY = (bounds.Height / Math.Max(1f, currentImage.Height)) / spY;
            return Math.Max(0.25f, (pxPerMmX + pxPerMmY) * 0.5f);
        }

        private void DrawReferenceLines(Graphics graphics)
        {
            if (!ShowReferenceLines || !referencePoint.HasValue)
                return;

            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }
            var bounds = (imageBounds.Width > 0f && imageBounds.Height > 0f)
                ? imageBounds
                : CalculateImageBounds(currentImage, zoom, pan);

            if (currentImage == null || bounds.Width <= 0f || bounds.Height <= 0f)
                return;

            var pt = ImageToScreen(referencePoint.Value);
            var x = pt.X;
            var y = pt.Y;
            if (x < bounds.Left || x > bounds.Right || y < bounds.Top || y > bounds.Bottom)
                return;

            var vColor = ReferenceLineColorVertical ?? Color.FromArgb(185, 80, 200, 255);
            var hColor = ReferenceLineColorHorizontal ?? Color.FromArgb(185, 80, 200, 255);
            const float gap = 8f;

            var activePart = activeMprDragPart != MprCrosshairHitPart.None ? activeMprDragPart : hoveredMprPart;
            var hHighlighted = MprInteractiveCrosshairEnabled &&
                (activePart == MprCrosshairHitPart.HorizontalLine || activePart == MprCrosshairHitPart.Center || activePart == MprCrosshairHitPart.RotateHandle || activePart == MprCrosshairHitPart.SlabHorizontal);
            var vHighlighted = MprInteractiveCrosshairEnabled &&
                (activePart == MprCrosshairHitPart.VerticalLine || activePart == MprCrosshairHitPart.Center || activePart == MprCrosshairHitPart.RotateHandle || activePart == MprCrosshairHitPart.SlabVertical);

            var hWidth = hHighlighted ? 2.1f : 1.3f;
            var vWidth = vHighlighted ? 2.1f : 1.3f;

            var oldSmooth = graphics.SmoothingMode;
            var oldClip = graphics.Clip;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.SetClip(bounds);
            try
            {
                var rad = ReferenceLineAngleDegrees * Math.PI / 180.0;
                var cosA = (float)Math.Cos(rad);
                var sinA = (float)Math.Sin(rad);
                var maxLen = Math.Max(bounds.Width, bounds.Height) * 2f;

                using (var vPen = new Pen(vColor, vWidth) { DashStyle = DashStyle.Dash })
                using (var hPen = new Pen(hColor, hWidth) { DashStyle = DashStyle.Dash })
                {
                    if (MprInteractiveCrosshairEnabled || ReferenceLineColorHorizontal.HasValue || ReferenceLineColorVertical.HasValue || Math.Abs(ReferenceLineAngleDegrees) > 0.05f)
                    {
                        // Горизонтальная ось под углом ReferenceLineAngleDegrees
                        graphics.DrawLine(hPen, x - maxLen * cosA, y - maxLen * sinA, x - gap * cosA, y - gap * sinA);
                        graphics.DrawLine(hPen, x + gap * cosA, y + gap * sinA, x + maxLen * cosA, y + maxLen * sinA);

                        // Вертикальная ось под углом ReferenceLineAngleDegrees + 90°
                        graphics.DrawLine(vPen, x + maxLen * sinA, y - maxLen * cosA, x + gap * sinA, y - gap * cosA);
                        graphics.DrawLine(vPen, x - gap * sinA, y + gap * cosA, x - maxLen * sinA, y + maxLen * cosA);
                    }
                    else
                    {
                        graphics.DrawLine(vPen, x, bounds.Top, x, bounds.Bottom);
                        graphics.DrawLine(hPen, bounds.Left, y, bounds.Right, y);
                    }
                }

                if (MprInteractiveCrosshairEnabled)
                {
                    var pxPerMm = GetScreenPixelsPerMillimeter(currentImage, bounds);
                    var halfSlabPx = ReferenceSlabThicknessMm > 1.05f
                        ? Math.Max(3f, (ReferenceSlabThicknessMm * 0.5f) * pxPerMm)
                        : 0f;

                    var slabHHighlighted = activePart == MprCrosshairHitPart.SlabHorizontal;
                    var slabVHighlighted = activePart == MprCrosshairHitPart.SlabVertical;

                    // Отрисовка параллельных пунктирных границ толщины слоя (Slab Lines, как в RadiAnt DICOM)
                    if (halfSlabPx > 0.5f)
                    {
                        using (var hSlabPen = new Pen(Color.FromArgb(slabHHighlighted ? 210 : 130, hColor.R, hColor.G, hColor.B), slabHHighlighted ? 1.5f : 1.0f) { DashStyle = DashStyle.Dot })
                        using (var vSlabPen = new Pen(Color.FromArgb(slabVHighlighted ? 210 : 130, vColor.R, vColor.G, vColor.B), slabVHighlighted ? 1.5f : 1.0f) { DashStyle = DashStyle.Dot })
                        {
                            // Параллельные линии вдоль горизонтальной оси (смещение по нормали (-sinA, cosA))
                            var nxH = -sinA * halfSlabPx;
                            var nyH = cosA * halfSlabPx;
                            graphics.DrawLine(hSlabPen, x + nxH - maxLen * cosA, y + nyH - maxLen * sinA, x + nxH + maxLen * cosA, y + nyH + maxLen * sinA);
                            graphics.DrawLine(hSlabPen, x - nxH - maxLen * cosA, y - nyH - maxLen * sinA, x - nxH + maxLen * cosA, y - nyH + maxLen * sinA);

                            // Параллельные линии вдоль вертикальной оси (смещение по нормали (cosA, sinA))
                            var nxV = cosA * halfSlabPx;
                            var nyV = sinA * halfSlabPx;
                            graphics.DrawLine(vSlabPen, x + nxV + maxLen * sinA, y + nyV - maxLen * cosA, x + nxV - maxLen * sinA, y + nyV + maxLen * cosA);
                            graphics.DrawLine(vSlabPen, x - nxV + maxLen * sinA, y - nyV - maxLen * cosA, x - nxV - maxLen * sinA, y - nyV + maxLen * cosA);
                        }
                    }

                    // Маркеры толщины слоя (квадратики Slab на внутренних сегментах лучей, как в RadiAnt)
                    DrawMprSlabHandlesOnAxis(graphics, x, y, cosA, sinA, -sinA, cosA, halfSlabPx, bounds, hColor, slabHHighlighted);
                    DrawMprSlabHandlesOnAxis(graphics, x, y, -sinA, cosA, cosA, sinA, halfSlabPx, bounds, vColor, slabVHighlighted);

                    // Центральное кольцо перекрестия при наведении
                    if (activePart == MprCrosshairHitPart.Center)
                    {
                        using (var centerPen = new Pen(Color.FromArgb(230, 255, 255, 255), 1.5f))
                            graphics.DrawEllipse(centerPen, x - 5f, y - 5f, 10f, 10f);
                    }

                    // 4 кружка вращения (как в RadiAnt DICOM Viewer) на лучах перекрестия
                    var rotateHighlighted = activePart == MprCrosshairHitPart.RotateHandle;
                    var handleRadius = rotateHighlighted ? 5.8f : 4.6f;

                    DrawMprRotationHandle(graphics, x, y, cosA, sinA, bounds, hColor, handleRadius, rotateHighlighted);
                    DrawMprRotationHandle(graphics, x, y, -cosA, -sinA, bounds, hColor, handleRadius, rotateHighlighted);
                    DrawMprRotationHandle(graphics, x, y, -sinA, cosA, bounds, vColor, handleRadius, rotateHighlighted);
                    DrawMprRotationHandle(graphics, x, y, sinA, -cosA, bounds, vColor, handleRadius, rotateHighlighted);

                    // Индикатор текущего угла наклона или толщины Slab при перетаскивании
                    var showSlabBadge = activePart == MprCrosshairHitPart.SlabHorizontal || activePart == MprCrosshairHitPart.SlabVertical;
                    var showAngleBadge = activePart == MprCrosshairHitPart.RotateHandle || Math.Abs(ReferenceLineAngleDegrees) > 0.05f;
                    if (showSlabBadge || showAngleBadge)
                    {
                        var badgeText = showSlabBadge
                            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "Slab: {0:0.0} мм", Math.Max(0.5f, ReferenceSlabThicknessMm))
                            : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:+0.0;-0.0;0.0}°", ReferenceLineAngleDegrees);
                        using (var badgeFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                        using (var bgBrush = new SolidBrush(Color.FromArgb(190, 16, 20, 26)))
                        using (var borderPen = new Pen(Color.FromArgb(190, 110, 205, 255), 1f))
                        using (var textBrush = new SolidBrush(Color.FromArgb(245, 255, 240, 130)))
                        {
                            var textSize = graphics.MeasureString(badgeText, badgeFont);
                            var bx = Math.Max(bounds.Left + 6f, Math.Min(bounds.Right - textSize.Width - 12f, x + 12f));
                            var by = Math.Max(bounds.Top + 6f, Math.Min(bounds.Bottom - textSize.Height - 8f, y - textSize.Height - 10f));
                            var badgeRect = new RectangleF(bx, by, textSize.Width + 8f, textSize.Height + 4f);
                            graphics.FillRectangle(bgBrush, badgeRect);
                            graphics.DrawRectangle(borderPen, badgeRect.X, badgeRect.Y, badgeRect.Width, badgeRect.Height);
                            graphics.DrawString(badgeText, badgeFont, textBrush, bx + 4f, by + 2f);
                        }
                    }
                }
            }
            finally
            {
                graphics.Clip = oldClip;
                graphics.SmoothingMode = oldSmooth;
            }
        }

        private static void DrawMprSlabHandlesOnAxis(
            Graphics graphics,
            float cx,
            float cy,
            float axisDx,
            float axisDy,
            float normDx,
            float normDy,
            float halfSlabPx,
            RectangleF bounds,
            Color lineColor,
            bool highlighted)
        {
            var halfSize = highlighted ? 4.2f : 3.2f;
            var fillColor = highlighted
                ? Color.FromArgb(240, 255, 245, 160)
                : Color.FromArgb(205, 22, 26, 34);
            var strokeColor = Color.FromArgb(240, lineColor.R, lineColor.G, lineColor.B);

            using (var fillBrush = new SolidBrush(fillColor))
            using (var pen = new Pen(strokeColor, highlighted ? 1.8f : 1.3f))
            {
                var signs = new[] { 1f, -1f };
                foreach (var sign in signs)
                {
                    var rayDx = axisDx * sign;
                    var rayDy = axisDy * sign;
                    var dist = ComputeSlabHandleDistanceOnRay(cx, cy, rayDx, rayDy, bounds);
                    if (dist <= 0f)
                        continue;

                    var baseX = cx + rayDx * dist;
                    var baseY = cy + rayDy * dist;
                    if (halfSlabPx > 0.5f)
                    {
                        var p1x = baseX + normDx * halfSlabPx;
                        var p1y = baseY + normDy * halfSlabPx;
                        var p2x = baseX - normDx * halfSlabPx;
                        var p2y = baseY - normDy * halfSlabPx;
                        graphics.FillRectangle(fillBrush, p1x - halfSize, p1y - halfSize, halfSize * 2f, halfSize * 2f);
                        graphics.DrawRectangle(pen, p1x - halfSize, p1y - halfSize, halfSize * 2f, halfSize * 2f);
                        graphics.FillRectangle(fillBrush, p2x - halfSize, p2y - halfSize, halfSize * 2f, halfSize * 2f);
                        graphics.DrawRectangle(pen, p2x - halfSize, p2y - halfSize, halfSize * 2f, halfSize * 2f);
                    }
                    else
                    {
                        graphics.FillRectangle(fillBrush, baseX - halfSize, baseY - halfSize, halfSize * 2f, halfSize * 2f);
                        graphics.DrawRectangle(pen, baseX - halfSize, baseY - halfSize, halfSize * 2f, halfSize * 2f);
                    }
                }
            }
        }

        private static void DrawMprRotationHandle(
            Graphics graphics,
            float cx,
            float cy,
            float dx,
            float dy,
            RectangleF bounds,
            Color lineColor,
            float radius,
            bool highlighted)
        {
            var dist = ComputeHandleDistanceOnRay(cx, cy, dx, dy, bounds);
            if (dist <= 0f)
                return;

            var hx = cx + dx * dist;
            var hy = cy + dy * dist;
            var fillColor = highlighted
                ? Color.FromArgb(235, 255, 255, 255)
                : Color.FromArgb(210, 18, 22, 28);
            var strokeColor = highlighted
                ? Color.FromArgb(255, lineColor.R, lineColor.G, lineColor.B)
                : Color.FromArgb(235, lineColor.R, lineColor.G, lineColor.B);

            using (var fillBrush = new SolidBrush(fillColor))
            using (var outlinePen = new Pen(strokeColor, highlighted ? 2.2f : 1.7f))
            {
                graphics.FillEllipse(fillBrush, hx - radius, hy - radius, radius * 2f, radius * 2f);
                graphics.DrawEllipse(outlinePen, hx - radius, hy - radius, radius * 2f, radius * 2f);
            }
        }

        private MprCrosshairHitPart HitTestMprCrosshair(Point mousePt)
        {
            if (MprPanelEnabled && !string.IsNullOrWhiteSpace(OverlayTopLeft))
            {
                var headerHitRect = new Rectangle(6, 4, 240, 38);
                if (headerHitRect.Contains(mousePt))
                    return MprCrosshairHitPart.HeaderBadge;
            }

            if (!MprInteractiveCrosshairEnabled || !ShowReferenceLines || !referencePoint.HasValue)
                return MprCrosshairHitPart.None;

            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }
            var bounds = (imageBounds.Width > 0f && imageBounds.Height > 0f)
                ? imageBounds
                : CalculateImageBounds(currentImage, zoom, pan);

            if (currentImage == null || bounds.Width <= 0f || bounds.Height <= 0f)
                return MprCrosshairHitPart.None;

            var expandedBounds = RectangleF.Inflate(bounds, 8f, 8f);
            if (!expandedBounds.Contains(mousePt))
                return MprCrosshairHitPart.None;

            var center = ImageToScreen(referencePoint.Value);
            var cx = center.X;
            var cy = center.Y;
            if (cx < bounds.Left || cx > bounds.Right || cy < bounds.Top || cy > bounds.Bottom)
                return MprCrosshairHitPart.None;

            var rad = ReferenceLineAngleDegrees * Math.PI / 180.0;
            var cosA = (float)Math.Cos(rad);
            var sinA = (float)Math.Sin(rad);

            // 1. Проверка попадания в 4 кружка вращения (RotateHandle)
            var rayDirs = new[]
            {
                new PointF(cosA, sinA),
                new PointF(-cosA, -sinA),
                new PointF(-sinA, cosA),
                new PointF(sinA, -cosA)
            };
            const float handleHitRadiusSq = 11f * 11f;
            foreach (var dir in rayDirs)
            {
                var dist = ComputeHandleDistanceOnRay(cx, cy, dir.X, dir.Y, bounds);
                if (dist <= 0f)
                    continue;
                var hx = cx + dir.X * dist;
                var hy = cy + dir.Y * dist;
                var ddx = mousePt.X - hx;
                var ddy = mousePt.Y - hy;
                if (ddx * ddx + ddy * ddy <= handleHitRadiusSq)
                    return MprCrosshairHitPart.RotateHandle;
            }

            // 2. Проверка попадания в центр перекрестия (Center)
            var relX = mousePt.X - cx;
            var relY = mousePt.Y - cy;
            if (relX * relX + relY * relY <= 10f * 10f)
                return MprCrosshairHitPart.Center;

            // 3. Проекция на горизонтальную и вертикальную оси перекрестия
            var projH = relX * cosA + relY * sinA;
            var perpH = Math.Abs(-relX * sinA + relY * cosA);

            var projV = -relX * sinA + relY * cosA;
            var perpV = Math.Abs(relX * cosA + relY * sinA);

            // Проверка попадания в маркеры или пунктирные границы толщины слоя (Slab)
            var pxPerMm = GetScreenPixelsPerMillimeter(currentImage, bounds);
            var halfSlabPx = ReferenceSlabThicknessMm > 1.05f
                ? Math.Max(3f, (ReferenceSlabThicknessMm * 0.5f) * pxPerMm)
                : 0f;

            var hRaySign = projH >= 0f ? 1f : -1f;
            var hSlabDist = ComputeSlabHandleDistanceOnRay(cx, cy, cosA * hRaySign, sinA * hRaySign, bounds);
            if (hSlabDist > 0f && Math.Abs(Math.Abs(projH) - hSlabDist) <= 8f && Math.Abs(perpH - halfSlabPx) <= 8f)
                return MprCrosshairHitPart.SlabHorizontal;

            var vRaySign = projV >= 0f ? 1f : -1f;
            var vSlabDist = ComputeSlabHandleDistanceOnRay(cx, cy, -sinA * vRaySign, cosA * vRaySign, bounds);
            if (vSlabDist > 0f && Math.Abs(Math.Abs(projV) - vSlabDist) <= 8f && Math.Abs(perpV - halfSlabPx) <= 8f)
                return MprCrosshairHitPart.SlabVertical;

            if (halfSlabPx >= 5f)
            {
                if (Math.Abs(perpH - halfSlabPx) <= 5.5f && Math.Abs(projH) > 12f)
                    return MprCrosshairHitPart.SlabHorizontal;
                if (Math.Abs(perpV - halfSlabPx) <= 5.5f && Math.Abs(projV) > 12f)
                    return MprCrosshairHitPart.SlabVertical;
            }

            const float lineHitThreshold = 7f;
            if (perpH <= lineHitThreshold && perpH <= perpV)
            {
                var dirX = projH >= 0f ? cosA : -cosA;
                var dirY = projH >= 0f ? sinA : -sinA;
                var handleDist = ComputeHandleDistanceOnRay(cx, cy, dirX, dirY, bounds);
                if (handleDist > 0f && Math.Abs(projH) >= handleDist - 8f)
                    return MprCrosshairHitPart.RotateHandle;
                return MprCrosshairHitPart.HorizontalLine;
            }

            if (perpV <= lineHitThreshold)
            {
                var dirX = projV >= 0f ? -sinA : sinA;
                var dirY = projV >= 0f ? cosA : -cosA;
                var handleDist = ComputeHandleDistanceOnRay(cx, cy, dirX, dirY, bounds);
                if (handleDist > 0f && Math.Abs(projV) >= handleDist - 8f)
                    return MprCrosshairHitPart.RotateHandle;
                return MprCrosshairHitPart.VerticalLine;
            }

            return MprCrosshairHitPart.None;
        }

        private void ApplyMprPartCursor(MprCrosshairHitPart part)
        {
            switch (part)
            {
                case MprCrosshairHitPart.RotateHandle:
                case MprCrosshairHitPart.HeaderBadge:
                    Cursor = Cursors.Hand;
                    break;
                case MprCrosshairHitPart.Center:
                    Cursor = Cursors.SizeAll;
                    break;
                case MprCrosshairHitPart.HorizontalLine:
                case MprCrosshairHitPart.SlabHorizontal:
                    Cursor = Cursors.SizeNS;
                    break;
                case MprCrosshairHitPart.VerticalLine:
                case MprCrosshairHitPart.SlabVertical:
                    Cursor = Cursors.SizeWE;
                    break;
                default:
                    Cursor = baseToolCursor ?? Cursors.Default;
                    break;
            }
        }

        private static float NormalizeAngleDeltaDegrees(float delta)
        {
            while (delta > 180f) delta -= 360f;
            while (delta < -180f) delta += 360f;
            return delta;
        }

        private static float ClampMprObliqueAngleDegrees(float angle)
        {
            while (angle > 180f) angle -= 360f;
            while (angle < -180f) angle += 360f;
            if (angle > 89f) angle = 89f;
            if (angle < -89f) angle = -89f;
            return angle;
        }

        private bool TryGetImagePoint(Point location, out PointF imagePoint)
        {
            imagePoint = PointF.Empty;
            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }
            var bounds = (imageBounds.Width > 0f && imageBounds.Height > 0f)
                ? imageBounds
                : CalculateImageBounds(currentImage, zoom, pan);

            if (currentImage == null || bounds.Width <= 0f || bounds.Height <= 0f || currentImage.Width <= 0 || currentImage.Height <= 0)
                return false;
            if (!bounds.Contains(location))
                return false;

            var scaleX = bounds.Width / currentImage.Width;
            var scaleY = bounds.Height / currentImage.Height;
            var imageX = (location.X - bounds.X) / scaleX;
            var imageY = (location.Y - bounds.Y) / scaleY;
            imageX = Math.Max(0f, Math.Min(currentImage.Width - 1, imageX));
            imageY = Math.Max(0f, Math.Min(currentImage.Height - 1, imageY));
            imagePoint = new PointF(imageX, imageY);
            return true;
        }

        public bool TryGetImagePointAt(Point location, out PointF imagePoint)
        {
            return TryGetImagePoint(location, out imagePoint);
        }

        private void DrawWindowLevelHud(Graphics graphics, Font font, Brush textBrush, Brush shadowBrush)
        {
            var hudText = string.Format("WL: {0:0.#}\nWW: {1:0.#}", windowCenter, windowWidth);
            var textSize = graphics.MeasureString(hudText, font);
            var padding = 6;
            var x = wlHudPoint.X + 16;
            var y = wlHudPoint.Y + 16;
            if (x + textSize.Width + padding * 2 > Width - 4)
                x = Math.Max(4, wlHudPoint.X - (int)textSize.Width - padding * 2 - 12);
            if (y + textSize.Height + padding * 2 > Height - 4)
                y = Math.Max(4, wlHudPoint.Y - (int)textSize.Height - padding * 2 - 12);

            var box = new RectangleF(x, y, textSize.Width + padding * 2, textSize.Height + padding * 2);
            using (var bg = new SolidBrush(Color.FromArgb(165, 0, 0, 0)))
            using (var border = new Pen(Color.FromArgb(145, 255, 239, 140), 1f))
            {
                graphics.FillRectangle(bg, box);
                graphics.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
            }
            DrawOverlayBlock(graphics, hudText, font, textBrush, shadowBrush, new PointF(box.X + padding, box.Y + padding), StringAlignment.Near, StringAlignment.Near);
        }

        private void DrawStoredMeasurements(Graphics graphics)
        {
            foreach (var measurement in measurements)
                DrawMeasurement(graphics, measurement, selectedMeasurementId.HasValue && selectedMeasurementId.Value == measurement.Id);
        }

        private void DrawDistanceMeasurement(Graphics graphics)
        {
            using (var pen = new Pen(activeMeasurementColor, 1f))
                graphics.DrawLine(pen, measureStart, measureEnd);

            var p1 = ScreenToImage(measureStart);
            var p2 = ScreenToImage(measureEnd);
            var imageDx = p2.X - p1.X;
            var imageDy = p2.Y - p1.Y;
            var distance = Math.Sqrt(imageDx * imageDx * pixelSpacingX * pixelSpacingX + imageDy * imageDy * pixelSpacingY * pixelSpacingY);
            var unit = hasPixelSpacing ? "мм" : "px";
            using (var brush = new SolidBrush(activeMeasurementColor))
                graphics.DrawString(string.Format("{0:0.0} {1}", distance, unit), Font, brush, measureEnd.X + 6, measureEnd.Y + 6);
        }

        private void DrawDeviationMeasurement(Graphics graphics)
        {
            using (var pen = new Pen(activeMeasurementColor, 1f))
                graphics.DrawLine(pen, measureStart, measureEnd);

            var preview = new DicomMeasurement
            {
                Mode = MeasurementMode.Deviation,
                Name = "Deviation",
                P1 = ScreenToImage(measureStart),
                P2 = ScreenToImage(measureEnd),
                P3 = PointF.Empty,
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            using (var brush = new SolidBrush(activeMeasurementColor))
                graphics.DrawString(BuildMeasurementText(preview), Font, brush, measureEnd.X + 6, measureEnd.Y + 6);
        }

        private void DrawTextAnnotationPreview(Graphics graphics)
        {
            DrawArrowLine(graphics, activeMeasurementColor, 1f, measureStart, measureEnd);

            var text = "Text";
            using (var brush = new SolidBrush(activeMeasurementColor))
                graphics.DrawString(text, Font, brush, measureEnd.X + 6, measureEnd.Y + 6);
        }

        private void DrawPencilMeasurement(Graphics graphics)
        {
            if (pencilPoints.Count < 2)
                return;

            var pts = pencilPoints.Where((p, i) => i == 0 || p != pencilPoints[i - 1]).ToArray();
            if (pts.Length < 2)
                return;

            var width = Math.Max(0.2f, PencilStrokeWidth);
            var opacity = Math.Max(1, Math.Min(255, PencilOpacity));
            using (var pen = new Pen(Color.FromArgb(opacity, activeMeasurementColor), width))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (pts.Length >= 3)
                    graphics.DrawCurve(pen, pts, 0.35f);
                else
                    graphics.DrawLine(pen, pts[0], pts[1]);
            }

            var sxp = hasPixelSpacing && pixelSpacingX > 0f ? pixelSpacingX : 1f;
            var syp = hasPixelSpacing && pixelSpacingY > 0f ? pixelSpacingY : 1f;
            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }
            var bounds = (imageBounds.Width > 0f && imageBounds.Height > 0f)
                ? imageBounds
                : CalculateImageBounds(currentImage, zoom, pan);
            var scaleX = (currentImage != null && bounds.Width > 0f && currentImage.Width > 0) ? bounds.Width / currentImage.Width : 1f;
            var scaleY = (currentImage != null && bounds.Height > 0f && currentImage.Height > 0) ? bounds.Height / currentImage.Height : 1f;
            var length = 0d;
            for (var i = 1; i < pts.Length; i++)
            {
                var dxp = ((pts[i].X - pts[i - 1].X) / scaleX) * sxp;
                var dyp = ((pts[i].Y - pts[i - 1].Y) / scaleY) * syp;
                length += Math.Sqrt(dxp * dxp + dyp * dyp);
            }
            var lenUnit = hasPixelSpacing ? "мм" : "px";
            var opacityPercent = opacity * 100f / 255f;
            var previewText = string.Format("Pencil: L={0:0.0} {1}, W={2:0.0}px, O={3:0}%", length, lenUnit, width, opacityPercent);
            var labelPoint = pts[pts.Length - 1];
            using (var brush = new SolidBrush(activeMeasurementColor))
                graphics.DrawString(previewText, Font, brush, labelPoint.X + 6, labelPoint.Y + 6);
        }

        private void DrawSplineMeasurement(Graphics graphics)
        {
            if (splinePoints.Count < 2)
                return;

            using (var pen = new Pen(activeMeasurementColor, 1f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (splinePoints.Count >= 3)
                    graphics.DrawCurve(pen, splinePoints.ToArray(), 0.45f);
                else
                    graphics.DrawLine(pen, splinePoints[0], splinePoints[1]);
            }

            if (splinePoints.Count < 3)
            {
                var promptPoint = splinePoints[splinePoints.Count - 1];
                using (var brush = new SolidBrush(activeMeasurementColor))
                    graphics.DrawString("Добавьте точку", Font, brush, promptPoint.X + 6, promptPoint.Y + 6);
                return;
            }

            var committed = BuildCommittedSplineScreenPoints();
            if (committed.Count < 2)
                return;

            var preview = new DicomMeasurement
            {
                Mode = MeasurementMode.Spline,
                Name = "Spline",
                P1 = ScreenToImage(committed[0]),
                P2 = ScreenToImage(committed[Math.Min(1, committed.Count - 1)]),
                P3 = ScreenToImage(committed[committed.Count - 1]),
                Points = committed.Select(ScreenToImage).ToList(),
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            NormalizeMeasurementPoints(preview);
            var labelPoint = committed[committed.Count - 1];
            using (var brush = new SolidBrush(activeMeasurementColor))
                graphics.DrawString(BuildMeasurementText(preview), Font, brush, labelPoint.X + 6, labelPoint.Y + 6);
        }

        private static void DrawArrowLine(Graphics graphics, Color color, float width, Point from, Point to)
        {
            if (from == to)
                return;

            using (var arrowCap = new AdjustableArrowCap(5f, 6f, true))
            using (var pen = new Pen(color, width))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.CustomEndCap = arrowCap;
                graphics.DrawLine(pen, from, to);
            }
        }

        private void DrawArrowMeasurement(Graphics graphics)
        {
            DrawArrowLine(graphics, activeMeasurementColor, 1f, measureStart, measureEnd);

            var preview = new DicomMeasurement
            {
                Mode = MeasurementMode.Arrow,
                Name = "Arrow",
                P1 = ScreenToImage(measureStart),
                P2 = ScreenToImage(measureEnd),
                P3 = PointF.Empty,
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            using (var brush = new SolidBrush(activeMeasurementColor))
                graphics.DrawString(BuildMeasurementText(preview), Font, brush, measureEnd.X + 6, measureEnd.Y + 6);
        }

        private void DrawAngleMeasurement(Graphics graphics)
        {
            using (var pen = new Pen(activeMeasurementColor, 1f))
            {
                graphics.DrawLine(pen, angleVertex, angleRay1End);
                if (angleFirstRayLocked || angleSecondRayDefined)
                    graphics.DrawLine(pen, angleVertex, angleRay2End);
            }

            float angleDegrees;
            if (TryGetAngleDegrees(angleVertex, angleRay1End, angleRay2End, out angleDegrees))
            {
                var textPoint = new Point(Math.Min(Width - 100, angleRay2End.X + 6), Math.Min(Height - 20, angleRay2End.Y + 6));
                using (var brush = new SolidBrush(activeMeasurementColor))
                    graphics.DrawString(string.Format("{0:0.0}°", angleDegrees), Font, brush, textPoint);
            }
            else if (!angleFirstRayLocked)
            {
                using (var brush = new SolidBrush(activeMeasurementColor))
                    graphics.DrawString("Задайте первый луч", Font, brush, angleRay1End.X + 6, angleRay1End.Y + 6);
            }
            else
            {
                using (var brush = new SolidBrush(activeMeasurementColor))
                    graphics.DrawString("Задайте второй луч", Font, brush, angleRay2End.X + 6, angleRay2End.Y + 6);
            }
        }

        private void DrawMeasurement(Graphics graphics, DicomMeasurement measurement, bool selected)
        {
            var color = measurement.Color.IsEmpty ? Color.Lime : measurement.Color;
            var p1 = ImageToScreen(measurement.P1);
            var p2 = ImageToScreen(measurement.P2);
            var p3 = ImageToScreen(measurement.P3);
            using (var pen = new Pen(color, selected ? 1.2f : 0.9f))
            {
                if (measurement.Mode == MeasurementMode.Angle)
                {
                    graphics.DrawLine(pen, p1, p2);
                    graphics.DrawLine(pen, p1, p3);
                }
                else if (measurement.Mode == MeasurementMode.Polygon)
                {
                    graphics.DrawLine(pen, p1, p2);
                    graphics.DrawLine(pen, p2, p3);
                    graphics.DrawLine(pen, p3, p1);
                }
                else if (measurement.Mode == MeasurementMode.OpenPolyline)
                {
                    var points = measurement.Points != null && measurement.Points.Count >= 2
                        ? measurement.Points.Select(ImageToScreen).ToArray()
                        : new[] { p1, p2, p3 };
                    for (var i = 1; i < points.Length; i++)
                        graphics.DrawLine(pen, points[i - 1], points[i]);
                }
                else if (measurement.Mode == MeasurementMode.Spline)
                {
                    var points = measurement.Points != null && measurement.Points.Count >= 2
                        ? measurement.Points.Select(ImageToScreen).ToArray()
                        : new[] { p1, p2, p3 };
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    if (points.Length >= 3)
                        graphics.DrawCurve(pen, points, 0.45f);
                    else
                        graphics.DrawLine(pen, points[0], points[1]);
                }
                else if (measurement.Mode == MeasurementMode.Pencil)
                {
                    var points = measurement.Points != null && measurement.Points.Count >= 2
                        ? measurement.Points.Select(ImageToScreen).Where((p, i) => i == 0 || p != ImageToScreen(measurement.Points[i - 1])).ToArray()
                        : new[] { p1, p2, p3 };
                    var strokeWidth = measurement.StrokeWidth > 0f ? measurement.StrokeWidth : 1.2f;
                    var strokeOpacity = measurement.StrokeOpacity > 0 ? Math.Max(1, Math.Min(255, measurement.StrokeOpacity)) : 220;
                    using (var pencilPen = new Pen(Color.FromArgb(strokeOpacity, color), selected ? Math.Max(strokeWidth, 1.4f) : strokeWidth))
                    {
                        pencilPen.StartCap = LineCap.Round;
                        pencilPen.EndCap = LineCap.Round;
                        pencilPen.LineJoin = LineJoin.Round;
                        if (points.Length >= 3)
                            graphics.DrawCurve(pencilPen, points, 0.35f);
                        else if (points.Length == 2)
                            graphics.DrawLine(pencilPen, points[0], points[1]);
                    }
                }
                else if (measurement.Mode == MeasurementMode.CobbAngle)
                {
                    var points = measurement.Points != null && measurement.Points.Count >= 4
                        ? measurement.Points.Select(ImageToScreen).ToArray()
                        : new[] { p1, p2, p3, p3 };
                    graphics.DrawLine(pen, points[0], points[1]);
                    graphics.DrawLine(pen, points[2], points[3]);
                }
                else if (measurement.Mode == MeasurementMode.Arrow)
                {
                    DrawArrowLine(graphics, color, selected ? 1.2f : 0.9f, p1, p2);
                }
                else if (measurement.Mode == MeasurementMode.TextAnnotation)
                {
                    DrawArrowLine(graphics, color, selected ? 1.2f : 0.9f, p1, p2);
                }
                else if (measurement.Mode == MeasurementMode.RoiRectangle)
                    graphics.DrawRectangle(pen, NormalizeRectangle(p1, p2));
                else if (measurement.Mode == MeasurementMode.RoiCircle)
                    graphics.DrawEllipse(pen, NormalizeRectangle(p1, p2));
                else
                    graphics.DrawLine(pen, p1, p2);
            }

            var text = measurement.Mode == MeasurementMode.TextAnnotation
                ? BuildTextAnnotationOverlayText(measurement.TextValue)
                : BuildMeasurementText(measurement);
            var labelPoint = measurement.Mode == MeasurementMode.OpenPolyline && measurement.Points != null && measurement.Points.Count > 0
                ? ImageToScreen(measurement.Points[measurement.Points.Count - 1])
                : measurement.Mode == MeasurementMode.Spline && measurement.Points != null && measurement.Points.Count > 0
                    ? ImageToScreen(measurement.Points[measurement.Points.Count - 1])
                : measurement.Mode == MeasurementMode.Pencil && measurement.Points != null && measurement.Points.Count > 0
                    ? ImageToScreen(measurement.Points[measurement.Points.Count - 1])
                : measurement.Mode == MeasurementMode.CobbAngle && measurement.Points != null && measurement.Points.Count > 3
                    ? ImageToScreen(measurement.Points[3])
                : measurement.Mode == MeasurementMode.Angle || measurement.Mode == MeasurementMode.Polygon
                    ? p3
                    : p2;
            if (measurement.Mode == MeasurementMode.TextAnnotation)
                DrawTextAnnotationLabel(graphics, Font, color, labelPoint, measurement.TextValue);
            else
            {
                using (var brush = new SolidBrush(color))
                    graphics.DrawString(text, Font, brush, labelPoint.X + 6, labelPoint.Y + 6);
            }

            if (selected)
                DrawSelectionHandles(graphics, color, measurement.Mode, p1, p2, p3);
        }

        private void DrawSelectionHandles(Graphics graphics, Color color, MeasurementMode mode, Point p1, Point p2, Point p3)
        {
            DrawHandle(graphics, color, p1);
            if (mode != MeasurementMode.Pencil)
                DrawHandle(graphics, color, p2);
            if (mode == MeasurementMode.Angle || mode == MeasurementMode.Polygon || mode == MeasurementMode.Pencil)
                DrawHandle(graphics, color, p3);

            if ((mode == MeasurementMode.OpenPolyline || mode == MeasurementMode.Spline || mode == MeasurementMode.CobbAngle)
                && selectedMeasurementId.HasValue)
            {
                var selected = measurements.FirstOrDefault(m => m.Id == selectedMeasurementId.Value);
                if (selected != null && selected.Points != null)
                {
                    foreach (var point in selected.Points)
                        DrawHandle(graphics, color, ImageToScreen(point));
                }
            }
            else if (mode == MeasurementMode.Pencil && selectedMeasurementId.HasValue && selectedMeasurementPointIndex >= 0)
            {
                var selected = measurements.FirstOrDefault(m => m.Id == selectedMeasurementId.Value);
                if (selected != null && selected.Points != null && selectedMeasurementPointIndex < selected.Points.Count)
                    DrawHandle(graphics, color, ImageToScreen(selected.Points[selectedMeasurementPointIndex]));
            }
        }

        private void DrawPolygonMeasurement(Graphics graphics)
        {
            using (var pen = new Pen(activeMeasurementColor, 1f))
            {
                graphics.DrawLine(pen, polygonP1, polygonP2);
                if (polygonSecondPointLocked)
                {
                    graphics.DrawLine(pen, polygonP2, polygonP3);
                    graphics.DrawLine(pen, polygonP3, polygonP1);
                }
            }

            if (!polygonSecondPointLocked)
            {
                using (var brush = new SolidBrush(activeMeasurementColor))
                    graphics.DrawString("Задайте вторую точку", Font, brush, polygonP2.X + 6, polygonP2.Y + 6);
                return;
            }

            var preview = new DicomMeasurement
            {
                Mode = MeasurementMode.Polygon,
                Name = "Polygon",
                P1 = ScreenToImage(polygonP1),
                P2 = ScreenToImage(polygonP2),
                P3 = ScreenToImage(polygonP3),
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            using (var brush = new SolidBrush(activeMeasurementColor))
                graphics.DrawString(BuildMeasurementText(preview), Font, brush, polygonP3.X + 6, polygonP3.Y + 6);
        }

        private void DrawOpenPolylineMeasurement(Graphics graphics)
        {
            if (openPolylinePoints.Count < 2)
                return;

            using (var pen = new Pen(activeMeasurementColor, 1f))
            {
                for (var i = 1; i < openPolylinePoints.Count; i++)
                    graphics.DrawLine(pen, openPolylinePoints[i - 1], openPolylinePoints[i]);
            }

            if (openPolylinePoints.Count < 3)
            {
                var promptPoint = openPolylinePoints[openPolylinePoints.Count - 1];
                using (var brush = new SolidBrush(activeMeasurementColor))
                    graphics.DrawString("Добавьте точку", Font, brush, promptPoint.X + 6, promptPoint.Y + 6);
                return;
            }

            var committed = BuildCommittedOpenPolylineScreenPoints();
            if (committed.Count < 2)
                return;

            var preview = new DicomMeasurement
            {
                Mode = MeasurementMode.OpenPolyline,
                Name = "Open Polyline",
                P1 = ScreenToImage(committed[0]),
                P2 = ScreenToImage(committed[Math.Min(1, committed.Count - 1)]),
                P3 = ScreenToImage(committed[committed.Count - 1]),
                Points = committed.Select(ScreenToImage).ToList(),
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            NormalizeMeasurementPoints(preview);
            var labelPoint = committed[committed.Count - 1];
            using (var brush = new SolidBrush(activeMeasurementColor))
                graphics.DrawString(BuildMeasurementText(preview), Font, brush, labelPoint.X + 6, labelPoint.Y + 6);
        }

        private List<Point> BuildCommittedOpenPolylineScreenPoints()
        {
            var result = new List<Point>(openPolylinePoints);
            if (result.Count < 2)
                return result;

            while (result.Count > 1 && DistanceSquared(result[result.Count - 1], result[result.Count - 2]) < 9f)
                result.RemoveAt(result.Count - 1);

            return result;
        }

        private List<Point> BuildCommittedSplineScreenPoints()
        {
            var result = new List<Point>(splinePoints);
            if (result.Count < 2)
                return result;

            while (result.Count > 1 && DistanceSquared(result[result.Count - 1], result[result.Count - 2]) < 9f)
                result.RemoveAt(result.Count - 1);

            return result.Where((p, i) => i == 0 || p != result[i - 1]).ToList();
        }

        private List<Point> BuildCommittedPencilScreenPoints()
        {
            var result = new List<Point>(pencilPoints);
            if (result.Count < 2)
                return result;

            result = result.Where((p, i) => i == 0 || p != result[i - 1]).ToList();
            return result;
        }

        private List<PointF> BuildCommittedPencilImagePoints(List<Point> sourceScreenPoints)
        {
            if (sourceScreenPoints == null)
                return new List<PointF>();

            var points = sourceScreenPoints.Select(ScreenToImage).ToList();
            if (points.Count < 2)
                return points;

            if (PencilSimplifyEnabled)
            {
                var epsilon = Math.Max(0.15f, 0.45f / Math.Max(0.1f, zoom));
                points = SimplifyPath(points, epsilon);
            }

            if (PencilSmoothingEnabled)
                points = SmoothPath(points);

            points = points.Where((p, i) => i == 0 || p != points[i - 1]).ToList();
            return points;
        }

        private static List<PointF> SmoothPath(List<PointF> source)
        {
            if (source == null || source.Count < 3)
                return source == null ? new List<PointF>() : new List<PointF>(source);

            var result = new List<PointF> { source[0] };
            for (var i = 0; i < source.Count - 1; i++)
            {
                var p0 = source[i];
                var p1 = source[i + 1];
                var q = new PointF(0.75f * p0.X + 0.25f * p1.X, 0.75f * p0.Y + 0.25f * p1.Y);
                var r = new PointF(0.25f * p0.X + 0.75f * p1.X, 0.25f * p0.Y + 0.75f * p1.Y);
                result.Add(q);
                result.Add(r);
            }
            result.Add(source[source.Count - 1]);
            return result;
        }

        private static List<PointF> SimplifyPath(List<PointF> points, float epsilon)
        {
            if (points == null || points.Count < 3)
                return points == null ? new List<PointF>() : new List<PointF>(points);

            var keep = new bool[points.Count];
            keep[0] = true;
            keep[points.Count - 1] = true;
            SimplifyPath(points, 0, points.Count - 1, Math.Max(0.01f, epsilon), keep);

            var result = new List<PointF>();
            for (var i = 0; i < points.Count; i++)
            {
                if (keep[i])
                    result.Add(points[i]);
            }
            return result.Count >= 2 ? result : new List<PointF> { points[0], points[points.Count - 1] };
        }

        private static void SimplifyPath(List<PointF> points, int start, int end, float epsilon, bool[] keep)
        {
            if (end - start < 2)
                return;

            var maxDistance = 0f;
            var index = -1;
            for (var i = start + 1; i < end; i++)
            {
                var distance = DistancePointToSegment(points[i], points[start], points[end]);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    index = i;
                }
            }

            if (index > start && maxDistance >= epsilon)
            {
                keep[index] = true;
                SimplifyPath(points, start, index, epsilon, keep);
                SimplifyPath(points, index, end, epsilon, keep);
            }
        }

        private static bool TryGetCobbAngleDegrees(PointF a1, PointF a2, PointF b1, PointF b2, out float angleDegrees)
        {
            angleDegrees = 0f;
            var v1x = a2.X - a1.X;
            var v1y = a2.Y - a1.Y;
            var v2x = b2.X - b1.X;
            var v2y = b2.Y - b1.Y;
            var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
            var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);
            if (len1 < 0.0001 || len2 < 0.0001)
                return false;

            var dot = v1x * v2x + v1y * v2y;
            var cos = dot / (len1 * len2);
            cos = Math.Max(-1d, Math.Min(1d, cos));
            var angle = Math.Acos(cos) * 180d / Math.PI;
            if (angle > 90d)
                angle = 180d - angle;
            angleDegrees = (float)angle;
            return true;
        }

        private void DrawCobbMeasurement(Graphics graphics)
        {
            if (cobbPoints.Count < 2)
                return;

            using (var pen = new Pen(activeMeasurementColor, 1f))
            {
                graphics.DrawLine(pen, cobbPoints[0], cobbPoints[1]);
                if (cobbPoints.Count >= 4)
                    graphics.DrawLine(pen, cobbPoints[2], cobbPoints[3]);
            }

            var promptPoint = cobbPoints[cobbPoints.Count - 1];
            if (cobbPoints.Count < 4)
            {
                using (var brush = new SolidBrush(activeMeasurementColor))
                    graphics.DrawString("Задайте вторую линию", Font, brush, promptPoint.X + 6, promptPoint.Y + 6);
                return;
            }

            var preview = new DicomMeasurement
            {
                Mode = MeasurementMode.CobbAngle,
                Name = "Cobb Angle",
                P1 = ScreenToImage(cobbPoints[0]),
                P2 = ScreenToImage(cobbPoints[1]),
                P3 = ScreenToImage(cobbPoints[2]),
                Points = new List<PointF>
                {
                    ScreenToImage(cobbPoints[0]),
                    ScreenToImage(cobbPoints[1]),
                    ScreenToImage(cobbPoints[2]),
                    ScreenToImage(cobbPoints[3])
                },
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            using (var brush = new SolidBrush(activeMeasurementColor))
                graphics.DrawString(BuildMeasurementText(preview), Font, brush, cobbPoints[3].X + 6, cobbPoints[3].Y + 6);
        }

        private void DrawRoiMeasurementPreview(Graphics graphics, MeasurementMode mode, Point start, Point end)
        {
            using (var pen = new Pen(activeMeasurementColor, 1f))
            {
                var rect = NormalizeRectangle(start, end);
                if (mode == MeasurementMode.RoiCircle)
                    graphics.DrawEllipse(pen, rect);
                else
                    graphics.DrawRectangle(pen, rect);
            }

            var preview = new DicomMeasurement
            {
                Mode = mode,
                Name = mode == MeasurementMode.RoiCircle ? "ROI Circle" : "ROI Rect",
                P1 = ScreenToImage(start),
                P2 = ScreenToImage(end),
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            CalculateRoiStatistics(preview);
            using (var brush = new SolidBrush(activeMeasurementColor))
                graphics.DrawString(BuildMeasurementText(preview), Font, brush, end.X + 6, end.Y + 6);
        }

        private static void DrawHandle(Graphics graphics, Color color, Point point)
        {
            var rect = new Rectangle(point.X - 4, point.Y - 4, 8, 8);
            using (var brush = new SolidBrush(Color.FromArgb(220, color)))
            using (var border = new Pen(Color.Black, 1f))
            {
                graphics.FillEllipse(brush, rect);
                graphics.DrawEllipse(border, rect);
            }
        }

        private string BuildMeasurementText(DicomMeasurement measurement)
        {
            if (measurement.Mode == MeasurementMode.RoiRectangle || measurement.Mode == MeasurementMode.RoiCircle)
            {
                var areaUnit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "мм²" : "px²";
                var area = measurement.RoiArea.HasValue ? measurement.RoiArea.Value.ToString("0.0") : "—";
                var min = measurement.RoiMin.HasValue ? measurement.RoiMin.Value.ToString("0") : "—";
                var max = measurement.RoiMax.HasValue ? measurement.RoiMax.Value.ToString("0") : "—";
                var mean = measurement.RoiMean.HasValue ? measurement.RoiMean.Value.ToString("0.0") : "—";
                var sd = measurement.RoiStdDev.HasValue ? string.Format(" SD={0:0.0}", measurement.RoiStdDev.Value) : string.Empty;
                var valUnit = measurement.IsHounsfieldUnit ? " HU" : string.Empty;
                var roiTitle = string.IsNullOrWhiteSpace(measurement.Name)
                    ? (measurement.Mode == MeasurementMode.RoiCircle ? "ROI Circle" : "ROI Rect")
                    : measurement.Name;
                return string.Format("{0}: S={1} {2} Min={3}{6} Max={4}{6} Mean={5}{6}{7}", roiTitle, area, areaUnit, min, max, mean, valUnit, sd);
            }

            if (measurement.Mode == MeasurementMode.Angle)
            {
                float angle;
                var angleText = TryGetAngleDegrees(measurement.P1, measurement.P2, measurement.P3, out angle)
                    ? string.Format("{0:0.0}°", angle)
                    : "—";
                return string.IsNullOrWhiteSpace(measurement.Name) ? angleText : measurement.Name + ": " + angleText;
            }

            if (measurement.Mode == MeasurementMode.CobbAngle)
            {
                if (measurement.Points == null || measurement.Points.Count < 4)
                    return string.IsNullOrWhiteSpace(measurement.Name) ? "—" : measurement.Name + ": —";
                float cobb;
                var cobbText = TryGetCobbAngleDegrees(measurement.Points[0], measurement.Points[1], measurement.Points[2], measurement.Points[3], out cobb)
                    ? string.Format("{0:0.0}°", cobb)
                    : "—";
                return string.IsNullOrWhiteSpace(measurement.Name) ? cobbText : measurement.Name + ": " + cobbText;
            }

            if (measurement.Mode == MeasurementMode.Deviation)
            {
                var sxp = measurement.PixelSpacingX > 0f ? measurement.PixelSpacingX : 1f;
                var syp = measurement.PixelSpacingY > 0f ? measurement.PixelSpacingY : 1f;
                var ddx = (measurement.P2.X - measurement.P1.X) * sxp;
                var ddy = (measurement.P2.Y - measurement.P1.Y) * syp;
                var len = Math.Sqrt(ddx * ddx + ddy * ddy);
                var lenUnit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "мм" : "px";
                var title = string.IsNullOrWhiteSpace(measurement.Name) ? "Deviation" : measurement.Name;
                return string.Format("{0}: Δx={1:0.0} Δy={2:0.0} L={3:0.0} {4}", title, ddx, ddy, len, lenUnit);
            }

            if (measurement.Mode == MeasurementMode.Arrow)
            {
                var sxp = measurement.PixelSpacingX > 0f ? measurement.PixelSpacingX : 1f;
                var syp = measurement.PixelSpacingY > 0f ? measurement.PixelSpacingY : 1f;
                var dxp = (measurement.P2.X - measurement.P1.X) * sxp;
                var dyp = (measurement.P2.Y - measurement.P1.Y) * syp;
                var len = Math.Sqrt(dxp * dxp + dyp * dyp);
                var lenUnit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "мм" : "px";
                var title = string.IsNullOrWhiteSpace(measurement.Name) ? "Arrow" : measurement.Name;
                return string.Format("{0}: L={1:0.0} {2}", title, len, lenUnit);
            }

            if (measurement.Mode == MeasurementMode.TextAnnotation)
            {
                var compact = LimitTextLength(measurement.TextValue, 180);
                return string.IsNullOrWhiteSpace(compact) ? "Text" : "Text: " + compact;
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
                var lenUnit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "мм" : "px";
                var title = string.IsNullOrWhiteSpace(measurement.Name) ? "Pencil" : measurement.Name;
                var width = measurement.StrokeWidth > 0f ? measurement.StrokeWidth : 1.2f;
                var opacityPercent = (measurement.StrokeOpacity > 0 ? measurement.StrokeOpacity : 220) * 100f / 255f;
                return string.Format("{0}: L={1:0.0} {2}, W={3:0.0}px, O={4:0}%", title, length, lenUnit, width, opacityPercent);
            }

            if (measurement.Mode == MeasurementMode.Spline)
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
                var lenUnit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "мм" : "px";
                var title = string.IsNullOrWhiteSpace(measurement.Name) ? "Spline" : measurement.Name;
                return string.Format("{0}: L={1:0.0} {2}", title, length, lenUnit);
            }

            if (measurement.Mode == MeasurementMode.Polygon)
            {
                var sxp = measurement.PixelSpacingX > 0f ? measurement.PixelSpacingX : 1f;
                var syp = measurement.PixelSpacingY > 0f ? measurement.PixelSpacingY : 1f;
                var areaRaw = Math.Abs(
                    measurement.P1.X * (measurement.P2.Y - measurement.P3.Y)
                    + measurement.P2.X * (measurement.P3.Y - measurement.P1.Y)
                    + measurement.P3.X * (measurement.P1.Y - measurement.P2.Y)) * 0.5f;
                var area = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f
                    ? areaRaw * sxp * syp
                    : areaRaw;
                var perim = Math.Sqrt(Math.Pow((measurement.P2.X - measurement.P1.X) * sxp, 2) + Math.Pow((measurement.P2.Y - measurement.P1.Y) * syp, 2))
                            + Math.Sqrt(Math.Pow((measurement.P3.X - measurement.P2.X) * sxp, 2) + Math.Pow((measurement.P3.Y - measurement.P2.Y) * syp, 2))
                            + Math.Sqrt(Math.Pow((measurement.P1.X - measurement.P3.X) * sxp, 2) + Math.Pow((measurement.P1.Y - measurement.P3.Y) * syp, 2));
                var areaUnit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "мм²" : "px²";
                var lenUnit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "мм" : "px";
                var title = string.IsNullOrWhiteSpace(measurement.Name) ? "Polygon" : measurement.Name;
                return string.Format("{0}: P={1:0.0} {2} S={3:0.0} {4}", title, perim, lenUnit, area, areaUnit);
            }

            if (measurement.Mode == MeasurementMode.OpenPolyline)
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
                var lenUnit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "мм" : "px";
                var title = string.IsNullOrWhiteSpace(measurement.Name) ? "Open Polyline" : measurement.Name;
                return string.Format("{0}: L={1:0.0} {2}", title, length, lenUnit);
            }

            var sx = measurement.PixelSpacingX > 0f ? measurement.PixelSpacingX : 1f;
            var sy = measurement.PixelSpacingY > 0f ? measurement.PixelSpacingY : 1f;
            var dx = measurement.P2.X - measurement.P1.X;
            var dy = measurement.P2.Y - measurement.P1.Y;
            var distance = Math.Sqrt(dx * dx * sx * sx + dy * dy * sy * sy);
            var unit = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f ? "мм" : "px";
            var distanceText = string.Format("{0:0.0} {1}", distance, unit);
            return string.IsNullOrWhiteSpace(measurement.Name) ? distanceText : measurement.Name + ": " + distanceText;
        }

        private static Rectangle NormalizeRectangle(Point p1, Point p2)
        {
            var left = Math.Min(p1.X, p2.X);
            var top = Math.Min(p1.Y, p2.Y);
            var right = Math.Max(p1.X, p2.X);
            var bottom = Math.Max(p1.Y, p2.Y);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static RectangleF NormalizeRectangle(PointF p1, PointF p2)
        {
            var left = Math.Min(p1.X, p2.X);
            var top = Math.Min(p1.Y, p2.Y);
            var right = Math.Max(p1.X, p2.X);
            var bottom = Math.Max(p1.Y, p2.Y);
            return RectangleF.FromLTRB(left, top, right, bottom);
        }

        private static bool TryGetAngleDegrees(Point vertex, Point ray1, Point ray2, out float angleDegrees)
        {
            return TryGetAngleDegrees(new PointF(vertex.X, vertex.Y), new PointF(ray1.X, ray1.Y), new PointF(ray2.X, ray2.Y), out angleDegrees);
        }

        private static bool TryGetAngleDegrees(PointF vertex, PointF ray1, PointF ray2, out float angleDegrees)
        {
            angleDegrees = 0f;
            var v1x = ray1.X - vertex.X;
            var v1y = ray1.Y - vertex.Y;
            var v2x = ray2.X - vertex.X;
            var v2y = ray2.Y - vertex.Y;
            var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
            var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);
            if (len1 < 0.0001 || len2 < 0.0001)
                return false;
            var dot = v1x * v2x + v1y * v2y;
            var cos = dot / (len1 * len2);
            cos = Math.Max(-1d, Math.Min(1d, cos));
            angleDegrees = (float)(Math.Acos(cos) * 180d / Math.PI);
            return true;
        }

        private PointF ScreenToImage(Point point)
        {
            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }
            var bounds = (imageBounds.Width > 0f && imageBounds.Height > 0f)
                ? imageBounds
                : CalculateImageBounds(currentImage, zoom, pan);

            if (currentImage == null || bounds.Width <= 0f || bounds.Height <= 0f || currentImage.Width <= 0 || currentImage.Height <= 0)
                return PointF.Empty;

            var scaleX = bounds.Width / currentImage.Width;
            var scaleY = bounds.Height / currentImage.Height;
            return new PointF((point.X - bounds.X) / scaleX, (point.Y - bounds.Y) / scaleY);
        }

        private Point ImageToScreen(PointF point)
        {
            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }
            var bounds = (imageBounds.Width > 0f && imageBounds.Height > 0f)
                ? imageBounds
                : CalculateImageBounds(currentImage, zoom, pan);

            if (currentImage == null || bounds.Width <= 0f || bounds.Height <= 0f || currentImage.Width <= 0 || currentImage.Height <= 0)
                return Point.Empty;

            var scaleX = bounds.Width / currentImage.Width;
            var scaleY = bounds.Height / currentImage.Height;
            return new Point((int)Math.Round(bounds.X + point.X * scaleX), (int)Math.Round(bounds.Y + point.Y * scaleY));
        }

        private static float DistanceSquared(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private static float DistancePointToSegment(Point p, Point a, Point b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            if (dx == 0 && dy == 0)
                return (float)Math.Sqrt(DistanceSquared(p, a));
            var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (float)(dx * dx + dy * dy);
            t = Math.Max(0f, Math.Min(1f, t));
            var proj = new Point((int)Math.Round(a.X + dx * t), (int)Math.Round(a.Y + dy * t));
            return (float)Math.Sqrt(DistanceSquared(p, proj));
        }

        private static float DistancePointToSegment(PointF p, PointF a, PointF b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            if (Math.Abs(dx) < 0.0001f && Math.Abs(dy) < 0.0001f)
                return (float)Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

            var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0f, Math.Min(1f, t));
            var projX = a.X + dx * t;
            var projY = a.Y + dy * t;
            var ddx = p.X - projX;
            var ddy = p.Y - projY;
            return (float)Math.Sqrt(ddx * ddx + ddy * ddy);
        }

        private bool TryHitMeasurement(Point location, out DicomMeasurement hitMeasurement, out MeasurementHitPart hitPart)
        {
            const float handleThresholdSquared = 64f;
            const float lineThreshold = 6f;
            for (var i = measurements.Count - 1; i >= 0; i--)
            {
                var item = measurements[i];
                var p1 = ImageToScreen(item.P1);
                var p2 = ImageToScreen(item.P2);
                var p3 = ImageToScreen(item.P3);
                if (DistanceSquared(location, p1) <= handleThresholdSquared)
                {
                    hitMeasurement = item;
                    hitPart = MeasurementHitPart.P1;
                    return true;
                }
                if (DistanceSquared(location, p2) <= handleThresholdSquared)
                {
                    hitMeasurement = item;
                    hitPart = MeasurementHitPart.P2;
                    return true;
                }
                if (item.Mode == MeasurementMode.Angle && DistanceSquared(location, p3) <= handleThresholdSquared)
                {
                    hitMeasurement = item;
                    hitPart = MeasurementHitPart.P3;
                    return true;
                }
                if (item.Mode == MeasurementMode.Polygon && DistanceSquared(location, p3) <= handleThresholdSquared)
                {
                    hitMeasurement = item;
                    hitPart = MeasurementHitPart.P3;
                    return true;
                }
                if (item.Mode == MeasurementMode.OpenPolyline && DistanceSquared(location, p3) <= handleThresholdSquared)
                {
                    hitMeasurement = item;
                    hitPart = MeasurementHitPart.P3;
                    return true;
                }
                if (item.Mode == MeasurementMode.OpenPolyline && item.Points != null && item.Points.Count >= 2)
                {
                    var points = item.Points.Select(ImageToScreen).ToList();
                    if (points.Any(p => DistanceSquared(location, p) <= handleThresholdSquared))
                    {
                        hitMeasurement = item;
                        hitPart = MeasurementHitPart.Body;
                        return true;
                    }
                    for (var j = 1; j < points.Count; j++)
                    {
                        if (DistancePointToSegment(location, points[j - 1], points[j]) <= lineThreshold)
                        {
                            hitMeasurement = item;
                            hitPart = MeasurementHitPart.Body;
                            return true;
                        }
                    }
                }
                if (item.Mode == MeasurementMode.Spline && item.Points != null && item.Points.Count >= 2)
                {
                    var points = item.Points.Select(ImageToScreen).ToList();
                    var pointHitIndex = points.FindIndex(p => DistanceSquared(location, p) <= handleThresholdSquared);
                    if (pointHitIndex >= 0)
                    {
                        hitMeasurement = item;
                        hitPart = MeasurementHitPart.Body;
                        selectedMeasurementPointIndex = pointHitIndex;
                        return true;
                    }
                    for (var j = 1; j < points.Count; j++)
                    {
                        if (DistancePointToSegment(location, points[j - 1], points[j]) <= lineThreshold)
                        {
                            hitMeasurement = item;
                            hitPart = MeasurementHitPart.Body;
                            selectedMeasurementPointIndex = -1;
                            return true;
                        }
                    }
                }
                if (item.Mode == MeasurementMode.Pencil && item.Points != null && item.Points.Count >= 2)
                {
                    var points = item.Points.Select(ImageToScreen).ToList();
                    var pointHitIndex = points.FindIndex(p => DistanceSquared(location, p) <= handleThresholdSquared);
                    if (pointHitIndex >= 0)
                    {
                        hitMeasurement = item;
                        hitPart = MeasurementHitPart.Body;
                        selectedMeasurementPointIndex = pointHitIndex;
                        return true;
                    }
                    for (var j = 1; j < points.Count; j++)
                    {
                        if (DistancePointToSegment(location, points[j - 1], points[j]) <= lineThreshold)
                        {
                            hitMeasurement = item;
                            hitPart = MeasurementHitPart.Body;
                            selectedMeasurementPointIndex = -1;
                            return true;
                        }
                    }
                }
                if (item.Mode == MeasurementMode.CobbAngle && item.Points != null && item.Points.Count >= 4)
                {
                    var points = item.Points.Select(ImageToScreen).ToList();
                    if (points.Any(p => DistanceSquared(location, p) <= handleThresholdSquared))
                    {
                        hitMeasurement = item;
                        hitPart = MeasurementHitPart.Body;
                        return true;
                    }
                    if (DistancePointToSegment(location, points[0], points[1]) <= lineThreshold
                        || DistancePointToSegment(location, points[2], points[3]) <= lineThreshold)
                    {
                        hitMeasurement = item;
                        hitPart = MeasurementHitPart.Body;
                        return true;
                    }
                }
                if (item.Mode == MeasurementMode.RoiRectangle || item.Mode == MeasurementMode.RoiCircle)
                {
                    var rect = NormalizeRectangle(p1, p2);
                    var expanded = rect;
                    expanded.Inflate(6, 6);
                    if (item.Mode == MeasurementMode.RoiRectangle)
                    {
                        if (expanded.Contains(location))
                        {
                            hitMeasurement = item;
                            hitPart = MeasurementHitPart.Body;
                            return true;
                        }
                    }
                    else
                    {
                        var cx = rect.Left + rect.Width / 2f;
                        var cy = rect.Top + rect.Height / 2f;
                        var rx = Math.Max(1f, rect.Width / 2f);
                        var ry = Math.Max(1f, rect.Height / 2f);
                        var nx = (location.X - cx) / rx;
                        var ny = (location.Y - cy) / ry;
                        if (nx * nx + ny * ny <= 1.1f)
                        {
                            hitMeasurement = item;
                            hitPart = MeasurementHitPart.Body;
                            return true;
                        }
                    }
                }
                else if (DistancePointToSegment(location, p1, p2) <= lineThreshold
                    || (item.Mode == MeasurementMode.Angle && DistancePointToSegment(location, p1, p3) <= lineThreshold)
                    || (item.Mode == MeasurementMode.Polygon && (DistancePointToSegment(location, p2, p3) <= lineThreshold || DistancePointToSegment(location, p3, p1) <= lineThreshold))
                    || (item.Mode == MeasurementMode.OpenPolyline && DistancePointToSegment(location, p2, p3) <= lineThreshold)
                    || (item.Mode == MeasurementMode.Spline && DistancePointToSegment(location, p2, p3) <= lineThreshold))
                {
                    hitMeasurement = item;
                    hitPart = MeasurementHitPart.Body;
                    return true;
                }
            }
            hitMeasurement = null;
            hitPart = MeasurementHitPart.None;
            selectedMeasurementPointIndex = -1;
            return false;
        }

        private void CommitRoiMeasurement(MeasurementMode mode)
        {
            if (DistanceSquared(measureStart, measureEnd) < 16f)
                return;

            var measurement = new DicomMeasurement
            {
                Id = Guid.NewGuid(),
                Mode = mode,
                Name = mode == MeasurementMode.RoiCircle ? "ROI Circle" : "ROI Rect",
                Color = activeMeasurementColor,
                P1 = ScreenToImage(measureStart),
                P2 = ScreenToImage(measureEnd),
                P3 = PointF.Empty,
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            CalculateRoiStatistics(measurement);
            ResetMeasurement();
            measurements.Add(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            if (MeasurementsChanged != null)
                MeasurementsChanged();
        }

        private void CommitTextAnnotationMeasurement()
        {
            var startPoint = measureStart;
            var endPoint = measureEnd;
            if (DistanceSquared(startPoint, endPoint) < 9f)
                startPoint = new Point(Math.Max(0, endPoint.X - 20), Math.Max(0, endPoint.Y - 20));

            var startImg = ScreenToImage(startPoint);
            var endImg = ScreenToImage(endPoint);
            var endScreen = endPoint;
            var color = activeMeasurementColor;
            ResetMeasurement();

            var text = TextAnnotationTextRequested != null ? TextAnnotationTextRequested(endScreen) : null;
            if (string.IsNullOrWhiteSpace(text))
                return;

            var measurement = new DicomMeasurement
            {
                Id = Guid.NewGuid(),
                Mode = MeasurementMode.TextAnnotation,
                Name = "Text",
                TextValue = text.Trim(),
                Color = color,
                P1 = startImg,
                P2 = endImg,
                P3 = PointF.Empty,
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            measurements.Add(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            if (MeasurementsChanged != null)
                MeasurementsChanged();
        }

        private void CommitArrowMeasurement()
        {
            if (DistanceSquared(measureStart, measureEnd) < 9f)
                return;
            var measurement = new DicomMeasurement
            {
                Id = Guid.NewGuid(),
                Mode = MeasurementMode.Arrow,
                Name = "Arrow",
                Color = activeMeasurementColor,
                P1 = ScreenToImage(measureStart),
                P2 = ScreenToImage(measureEnd),
                P3 = PointF.Empty,
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            ResetMeasurement();
            measurements.Add(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            if (MeasurementsChanged != null)
                MeasurementsChanged();
        }

        private void CommitDeviationMeasurement()
        {
            if (DistanceSquared(measureStart, measureEnd) < 9f)
                return;
            var measurement = new DicomMeasurement
            {
                Id = Guid.NewGuid(),
                Mode = MeasurementMode.Deviation,
                Name = "Deviation",
                Color = activeMeasurementColor,
                P1 = ScreenToImage(measureStart),
                P2 = ScreenToImage(measureEnd),
                P3 = PointF.Empty,
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            ResetMeasurement();
            measurements.Add(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            if (MeasurementsChanged != null)
                MeasurementsChanged();
        }

        private bool CommitPolygonMeasurement()
        {
            if (DistanceSquared(polygonP1, polygonP2) < 9f || DistanceSquared(polygonP2, polygonP3) < 9f || DistanceSquared(polygonP1, polygonP3) < 9f)
                return false;

            var measurement = new DicomMeasurement
            {
                Id = Guid.NewGuid(),
                Mode = MeasurementMode.Polygon,
                Name = "Polygon",
                Color = activeMeasurementColor,
                P1 = ScreenToImage(polygonP1),
                P2 = ScreenToImage(polygonP2),
                P3 = ScreenToImage(polygonP3),
                Points = new List<PointF> { ScreenToImage(polygonP1), ScreenToImage(polygonP2), ScreenToImage(polygonP3) },
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            ResetMeasurement();
            measurements.Add(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            if (MeasurementsChanged != null)
                MeasurementsChanged();
            return true;
        }

        private void FinishSplineMeasurement()
        {
            if (!measuring || MeasurementMode != MeasurementMode.Spline)
                return;

            if (CommitSplineMeasurement())
                ResetMeasurement();
            Invalidate();
        }

        private bool CommitSplineMeasurement()
        {
            var committed = BuildCommittedSplineScreenPoints();
            if (committed.Count < 2)
                return false;

            var imagePoints = committed.Select(ScreenToImage).ToList();
            var measurement = new DicomMeasurement
            {
                Id = Guid.NewGuid(),
                Mode = MeasurementMode.Spline,
                Name = "Spline",
                Color = activeMeasurementColor,
                P1 = imagePoints[0],
                P2 = imagePoints[Math.Min(1, imagePoints.Count - 1)],
                P3 = imagePoints[imagePoints.Count - 1],
                Points = imagePoints,
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            NormalizeMeasurementPoints(measurement);
            ResetMeasurement();
            measurements.Add(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            if (MeasurementsChanged != null)
                MeasurementsChanged();
            return true;
        }

        private bool CommitPencilMeasurement()
        {
            var committed = BuildCommittedPencilScreenPoints();
            if (committed.Count < 2)
                return false;

            var imagePoints = BuildCommittedPencilImagePoints(committed);
            if (imagePoints.Count < 2)
                return false;

            var measurement = new DicomMeasurement
            {
                Id = Guid.NewGuid(),
                Mode = MeasurementMode.Pencil,
                Name = "Pencil",
                Color = activeMeasurementColor,
                P1 = imagePoints[0],
                P2 = imagePoints[Math.Min(1, imagePoints.Count - 1)],
                P3 = imagePoints[imagePoints.Count - 1],
                Points = imagePoints,
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f,
                StrokeWidth = Math.Max(0.2f, PencilStrokeWidth),
                StrokeOpacity = Math.Max(1, Math.Min(255, PencilOpacity))
            };
            NormalizeMeasurementPoints(measurement);
            ResetMeasurement();
            measurements.Add(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            if (MeasurementsChanged != null)
                MeasurementsChanged();
            return true;
        }

        private bool CommitCobbMeasurement()
        {
            if (cobbPoints.Count < 4)
                return false;
            if (DistanceSquared(cobbPoints[0], cobbPoints[1]) < 9f || DistanceSquared(cobbPoints[2], cobbPoints[3]) < 9f)
                return false;

            var points = cobbPoints.Take(4).Select(ScreenToImage).ToList();
            float angle;
            if (!TryGetCobbAngleDegrees(points[0], points[1], points[2], points[3], out angle))
                return false;

            var measurement = new DicomMeasurement
            {
                Id = Guid.NewGuid(),
                Mode = MeasurementMode.CobbAngle,
                Name = "Cobb Angle",
                Color = activeMeasurementColor,
                P1 = points[0],
                P2 = points[1],
                P3 = points[2],
                Points = points,
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            ResetMeasurement();
            measurements.Add(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            if (MeasurementsChanged != null)
                MeasurementsChanged();
            return true;
        }

        private void FinishOpenPolylineMeasurement()
        {
            if (!measuring || MeasurementMode != MeasurementMode.OpenPolyline)
                return;

            if (CommitOpenPolylineMeasurement())
                ResetMeasurement();
            Invalidate();
        }

        private bool CommitOpenPolylineMeasurement()
        {
            var committed = BuildCommittedOpenPolylineScreenPoints();
            if (committed.Count < 2)
                return false;

            var imagePoints = committed.Select(ScreenToImage).ToList();

            var measurement = new DicomMeasurement
            {
                Id = Guid.NewGuid(),
                Mode = MeasurementMode.OpenPolyline,
                Name = "Open Polyline",
                Color = activeMeasurementColor,
                P1 = imagePoints[0],
                P2 = imagePoints[Math.Min(1, imagePoints.Count - 1)],
                P3 = imagePoints[imagePoints.Count - 1],
                Points = imagePoints,
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            NormalizeMeasurementPoints(measurement);
            ResetMeasurement();
            measurements.Add(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            if (MeasurementsChanged != null)
                MeasurementsChanged();
            return true;
        }

        private void CalculateRoiStatistics(DicomMeasurement measurement)
        {
            if (measurement == null)
                return;
            if (measurement.Mode != MeasurementMode.RoiRectangle && measurement.Mode != MeasurementMode.RoiCircle)
                return;
            if (image == null)
                return;

            if (PixelDataProvider != null)
            {
                var rect = NormalizeRectangle(measurement.P1, measurement.P2);
                float? area, min, max, mean, stdDev;
                PixelDataProvider.CalculateRoiStatistics(
                    rect,
                    measurement.Mode,
                    measurement.PixelSpacingX,
                    measurement.PixelSpacingY,
                    out area,
                    out min,
                    out max,
                    out mean,
                    out stdDev);

                measurement.RoiArea = area;
                measurement.RoiMin = min;
                measurement.RoiMax = max;
                measurement.RoiMean = mean;
                measurement.RoiStdDev = stdDev;
                measurement.IsHounsfieldUnit = PixelDataProvider.IsHounsfieldUnit;
                return;
            }

            var bitmap = image as Bitmap;
            Bitmap temp = null;
            if (bitmap == null)
            {
                temp = new Bitmap(image);
                bitmap = temp;
            }

            try
            {
                var rect = NormalizeRectangle(measurement.P1, measurement.P2);
                var left = Math.Max(0, (int)Math.Floor(rect.Left));
                var top = Math.Max(0, (int)Math.Floor(rect.Top));
                var right = Math.Min(bitmap.Width - 1, (int)Math.Ceiling(rect.Right));
                var bottom = Math.Min(bitmap.Height - 1, (int)Math.Ceiling(rect.Bottom));
                if (right <= left || bottom <= top)
                {
                    measurement.RoiArea = null;
                    measurement.RoiMin = null;
                    measurement.RoiMax = null;
                    measurement.RoiMean = null;
                    return;
                }

                long sum;
                float min;
                float max;
                int count;
                var cx = (left + right) / 2f;
                var cy = (top + bottom) / 2f;
                var rx = Math.Max(1f, (right - left) / 2f);
                var ry = Math.Max(1f, (bottom - top) / 2f);

                if (!TryAccumulateBitmapRoiStats(bitmap, left, top, right, bottom, measurement.Mode, cx, cy, rx, ry, out sum, out min, out max, out count))
                {
                    sum = 0;
                    min = 255f;
                    max = 0f;
                    count = 0;
                    for (var y = top; y <= bottom; y++)
                    {
                        for (var x = left; x <= right; x++)
                        {
                            if (measurement.Mode == MeasurementMode.RoiCircle)
                            {
                                var nx = (x - cx) / rx;
                                var ny = (y - cy) / ry;
                                if (nx * nx + ny * ny > 1f)
                                    continue;
                            }

                            var pixel = bitmap.GetPixel(x, y);
                            var value = (pixel.R + pixel.G + pixel.B) / 3f;
                            if (value < min) min = value;
                            if (value > max) max = value;
                            sum += (int)value;
                            count++;
                        }
                    }
                }

                if (count <= 0)
                {
                    measurement.RoiArea = null;
                    measurement.RoiMin = null;
                    measurement.RoiMax = null;
                    measurement.RoiMean = null;
                    return;
                }

                var spacingX = measurement.PixelSpacingX > 0f ? measurement.PixelSpacingX : 1f;
                var spacingY = measurement.PixelSpacingY > 0f ? measurement.PixelSpacingY : 1f;
                var width = rect.Width;
                var height = rect.Height;
                var areaPixels = measurement.Mode == MeasurementMode.RoiCircle
                    ? (float)(Math.PI * (width / 2f) * (height / 2f))
                    : width * height;
                measurement.RoiArea = measurement.PixelSpacingX > 0f && measurement.PixelSpacingY > 0f
                    ? areaPixels * spacingX * spacingY
                    : areaPixels;
                measurement.RoiMin = min;
                measurement.RoiMax = max;
                measurement.RoiMean = (float)sum / count;
            }
            finally
            {
                if (temp != null)
                    temp.Dispose();
            }
        }

        private static bool TryAccumulateBitmapRoiStats(
            Bitmap bitmap,
            int left,
            int top,
            int right,
            int bottom,
            MeasurementMode mode,
            float cx,
            float cy,
            float rx,
            float ry,
            out long sum,
            out float min,
            out float max,
            out int count)
        {
            sum = 0;
            min = 255f;
            max = 0f;
            count = 0;

            var bytesPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
            if (bytesPerPixel != 1 && bytesPerPixel != 3 && bytesPerPixel != 4)
                return false;

            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = null;
            try
            {
                data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);
                var stride = data.Stride;
                var strideAbs = Math.Abs(stride);
                var buffer = new byte[strideAbs * bitmap.Height];
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

                for (var y = top; y <= bottom; y++)
                {
                    var row = stride >= 0 ? y : (bitmap.Height - 1 - y);
                    var rowOffset = row * strideAbs;
                    for (var x = left; x <= right; x++)
                    {
                        if (mode == MeasurementMode.RoiCircle)
                        {
                            var nx = (x - cx) / rx;
                            var ny = (y - cy) / ry;
                            if (nx * nx + ny * ny > 1f)
                                continue;
                        }

                        var pixelOffset = rowOffset + x * bytesPerPixel;
                        float value;
                        if (bytesPerPixel == 1)
                            value = buffer[pixelOffset];
                        else
                        {
                            var b = buffer[pixelOffset];
                            var g = buffer[pixelOffset + 1];
                            var r = buffer[pixelOffset + 2];
                            value = (r + g + b) / 3f;
                        }

                        if (value < min) min = value;
                        if (value > max) max = value;
                        sum += (int)value;
                        count++;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (data != null)
                    bitmap.UnlockBits(data);
            }
        }

        private void MoveSelectedMeasurement(Point newLocation)
        {
            if (!selectedMeasurementId.HasValue)
                return;
            Image currentImage;
            lock (imageSyncRoot)
            {
                currentImage = image;
            }
            var bounds = (imageBounds.Width > 0f && imageBounds.Height > 0f)
                ? imageBounds
                : CalculateImageBounds(currentImage, zoom, pan);

            if (currentImage == null || bounds.Width <= 0f || bounds.Height <= 0f || currentImage.Width <= 0 || currentImage.Height <= 0)
                return;

            var measurement = measurements.FirstOrDefault(m => m.Id == selectedMeasurementId.Value);
            if (measurement == null)
                return;

            var scaleX = bounds.Width / currentImage.Width;
            var scaleY = bounds.Height / currentImage.Height;
            var deltaX = (newLocation.X - editingAnchor.X) / scaleX;
            var deltaY = (newLocation.Y - editingAnchor.Y) / scaleY;
            if (Math.Abs(deltaX) < 0.0001f && Math.Abs(deltaY) < 0.0001f)
                return;

            NormalizeMeasurementPoints(measurement);
            switch (selectedMeasurementPart)
            {
                case MeasurementHitPart.P1:
                    measurement.P1 = new PointF(measurement.P1.X + deltaX, measurement.P1.Y + deltaY);
                    if ((measurement.Mode == MeasurementMode.OpenPolyline || measurement.Mode == MeasurementMode.Spline || measurement.Mode == MeasurementMode.Pencil || measurement.Mode == MeasurementMode.CobbAngle)
                        && measurement.Points != null && measurement.Points.Count > 0)
                        measurement.Points[0] = measurement.P1;
                    else if (measurement.Mode == MeasurementMode.Polygon)
                        measurement.Points = new List<PointF> { measurement.P1, measurement.P2, measurement.P3 };
                    break;
                case MeasurementHitPart.P2:
                    measurement.P2 = new PointF(measurement.P2.X + deltaX, measurement.P2.Y + deltaY);
                    if ((measurement.Mode == MeasurementMode.OpenPolyline || measurement.Mode == MeasurementMode.Spline || measurement.Mode == MeasurementMode.Pencil || measurement.Mode == MeasurementMode.CobbAngle)
                        && measurement.Points != null && measurement.Points.Count > 1)
                        measurement.Points[1] = measurement.P2;
                    else if (measurement.Mode == MeasurementMode.Polygon)
                        measurement.Points = new List<PointF> { measurement.P1, measurement.P2, measurement.P3 };
                    break;
                case MeasurementHitPart.P3:
                    measurement.P3 = new PointF(measurement.P3.X + deltaX, measurement.P3.Y + deltaY);
                    if ((measurement.Mode == MeasurementMode.OpenPolyline || measurement.Mode == MeasurementMode.Spline || measurement.Mode == MeasurementMode.Pencil)
                        && measurement.Points != null && measurement.Points.Count > 0)
                        measurement.Points[measurement.Points.Count - 1] = measurement.P3;
                    else if (measurement.Mode == MeasurementMode.CobbAngle && measurement.Points != null && measurement.Points.Count > 2)
                        measurement.Points[2] = measurement.P3;
                    else if (measurement.Mode == MeasurementMode.Polygon)
                        measurement.Points = new List<PointF> { measurement.P1, measurement.P2, measurement.P3 };
                    break;
                default:
                    if ((measurement.Mode == MeasurementMode.Pencil || measurement.Mode == MeasurementMode.Spline || measurement.Mode == MeasurementMode.OpenPolyline || measurement.Mode == MeasurementMode.CobbAngle)
                        && measurement.Points != null && measurement.Points.Count > 0 && selectedMeasurementPointIndex >= 0)
                    {
                        var index = Math.Min(selectedMeasurementPointIndex, measurement.Points.Count - 1);
                        measurement.Points[index] = new PointF(measurement.Points[index].X + deltaX, measurement.Points[index].Y + deltaY);
                        measurement.P1 = measurement.Points[0];
                        measurement.P2 = measurement.Points[Math.Min(1, measurement.Points.Count - 1)];
                        measurement.P3 = measurement.Mode == MeasurementMode.CobbAngle
                            ? measurement.Points[Math.Min(2, measurement.Points.Count - 1)]
                            : measurement.Points[measurement.Points.Count - 1];
                    }
                    else if ((measurement.Mode == MeasurementMode.OpenPolyline || measurement.Mode == MeasurementMode.Spline || measurement.Mode == MeasurementMode.CobbAngle || measurement.Mode == MeasurementMode.Pencil)
                        && measurement.Points != null && measurement.Points.Count > 0)
                    {
                        for (var i = 0; i < measurement.Points.Count; i++)
                            measurement.Points[i] = new PointF(measurement.Points[i].X + deltaX, measurement.Points[i].Y + deltaY);
                        measurement.P1 = measurement.Points[0];
                        measurement.P2 = measurement.Points[Math.Min(1, measurement.Points.Count - 1)];
                        measurement.P3 = measurement.Mode == MeasurementMode.CobbAngle
                            ? measurement.Points[Math.Min(2, measurement.Points.Count - 1)]
                            : measurement.Points[measurement.Points.Count - 1];
                    }
                    else
                    {
                        measurement.P1 = new PointF(measurement.P1.X + deltaX, measurement.P1.Y + deltaY);
                        measurement.P2 = new PointF(measurement.P2.X + deltaX, measurement.P2.Y + deltaY);
                        measurement.P3 = new PointF(measurement.P3.X + deltaX, measurement.P3.Y + deltaY);
                        if (measurement.Mode == MeasurementMode.Polygon)
                            measurement.Points = new List<PointF> { measurement.P1, measurement.P2, measurement.P3 };
                    }
                    break;
            }
        }

        private void CommitDistanceMeasurement()
        {
            if (DistanceSquared(measureStart, measureEnd) < 9f)
                return;
            var measurement = new DicomMeasurement
            {
                Id = Guid.NewGuid(),
                Mode = MeasurementMode.Distance,
                Name = "Distance",
                Color = activeMeasurementColor,
                P1 = ScreenToImage(measureStart),
                P2 = ScreenToImage(measureEnd),
                P3 = PointF.Empty,
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            ResetMeasurement();
            measurements.Add(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            if (MeasurementsChanged != null)
                MeasurementsChanged();
        }

        private bool CommitAngleMeasurement()
        {
            float angle;
            if (!TryGetAngleDegrees(angleVertex, angleRay1End, angleRay2End, out angle))
                return false;
            var measurement = new DicomMeasurement
            {
                Id = Guid.NewGuid(),
                Mode = MeasurementMode.Angle,
                Name = "Angle",
                Color = activeMeasurementColor,
                P1 = ScreenToImage(angleVertex),
                P2 = ScreenToImage(angleRay1End),
                P3 = ScreenToImage(angleRay2End),
                PixelSpacingX = hasPixelSpacing ? pixelSpacingX : 0f,
                PixelSpacingY = hasPixelSpacing ? pixelSpacingY : 0f
            };
            ResetMeasurement();
            measurements.Add(measurement);
            selectedMeasurementId = measurement.Id;
            selectedMeasurementPart = MeasurementHitPart.Body;
            if (MeasurementsChanged != null)
                MeasurementsChanged();
            return true;
        }

        private static void DrawLeftScale(Graphics graphics)
        {
            using (var pen = new Pen(Color.FromArgb(210, 220, 60, 60), 1f))
            {
                var x = 1f;
                var top = Math.Max(90f, graphics.VisibleClipBounds.Height / 2f - 110f);
                var bottom = Math.Min(graphics.VisibleClipBounds.Height - 90f, graphics.VisibleClipBounds.Height / 2f + 110f);
                graphics.DrawLine(pen, x, top, x, bottom);
                for (var y = top; y <= bottom; y += 14f)
                    graphics.DrawLine(pen, x, y, x + 4f, y);
            }
        }

        private void UpdateCursorOverlay(Point location)
        {
            PointF imagePoint;
            if (!TryGetImagePoint(location, out imagePoint))
            {
                cursorOverlay = "X: — Y: — Val: —";
                return;
            }

            var imageX = (int)imagePoint.X;
            var imageY = (int)imagePoint.Y;

            float huVal;
            if (PixelDataProvider != null && PixelDataProvider.TryGetPixelValue(imageX, imageY, out huVal))
            {
                cursorOverlay = PixelDataProvider.IsHounsfieldUnit
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "X: {0} Y: {1} Val: {2:0} HU", imageX, imageY, huVal)
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, "X: {0} Y: {1} Val: {2:0}", imageX, imageY, huVal);
                return;
            }

            var value = -1;
            var bitmap = image as Bitmap;
            if (bitmap != null)
            {
                TryGetBitmapPixelIntensity(bitmap, imageX, imageY, out value);
            }
            cursorOverlay = value >= 0
                ? string.Format("X: {0} Y: {1} Val: {2}", imageX, imageY, value)
                : string.Format("X: {0} Y: {1} Val: —", imageX, imageY);
        }

        private void RaiseCursorImagePositionChanged(Point location)
        {
            if (CursorImagePositionChanged == null)
                return;

            PointF imagePoint;
            CursorImagePositionChanged(TryGetImagePoint(location, out imagePoint) ? (PointF?)imagePoint : null);
        }

        private static void DrawOverlayBlock(Graphics graphics, string text, Font font, Brush textBrush, Brush shadowBrush, PointF point, StringAlignment horizontal, StringAlignment vertical)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            var format = new StringFormat { Alignment = horizontal, LineAlignment = vertical };
            var shadowPoint = new PointF(point.X + 1f, point.Y + 1f);
            graphics.DrawString(text, font, shadowBrush, shadowPoint, format);
            graphics.DrawString(text, font, textBrush, point, format);
            format.Dispose();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && (MprInteractiveCrosshairEnabled || MprPanelEnabled) && !measuring)
            {
                var hitMpr = HitTestMprCrosshair(e.Location);
                if (hitMpr == MprCrosshairHitPart.HeaderBadge)
                {
                    base.OnMouseDown(e);
                    if (MprContextMenuRequested != null)
                        MprContextMenuRequested(PointToScreen(new Point(10, 42)));
                    return;
                }
                if (hitMpr != MprCrosshairHitPart.None)
                {
                    activeMprDragPart = hitMpr;
                    hoveredMprPart = hitMpr;
                    dragging = false;
                    adjustingWindowLevel = false;
                    if (hitMpr == MprCrosshairHitPart.RotateHandle && referencePoint.HasValue)
                    {
                        var centerScreen = ImageToScreen(referencePoint.Value);
                        mprRotateStartMouseAngle = (float)(Math.Atan2(e.Y - centerScreen.Y, e.X - centerScreen.X) * 180.0 / Math.PI);
                        mprRotateStartAngleDegrees = ReferenceLineAngleDegrees;
                    }
                    else if (hitMpr != MprCrosshairHitPart.SlabHorizontal && hitMpr != MprCrosshairHitPart.SlabVertical)
                    {
                        PointF imgPt;
                        if (TryGetImagePoint(e.Location, out imgPt) && MprCrosshairMoved != null)
                        {
                            var moveX = hitMpr == MprCrosshairHitPart.Center || hitMpr == MprCrosshairHitPart.VerticalLine;
                            var moveY = hitMpr == MprCrosshairHitPart.Center || hitMpr == MprCrosshairHitPart.HorizontalLine;
                            MprCrosshairMoved(imgPt, moveX, moveY);
                        }
                    }
                    base.OnMouseDown(e);
                    ApplyMprPartCursor(activeMprDragPart);
                    Capture = true;
                    Invalidate();
                    return;
                }
            }

            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Middle)
            {
                BeginWindowLevelAdjust(e.Location);
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            if (Tool == ViewerTool.WindowLevel)
            {
                BeginWindowLevelAdjust(e.Location);
                return;
            }

            if (Tool == ViewerTool.Measure && !measuring)
            {
                DicomMeasurement hitMeasurement;
                MeasurementHitPart hitPart;
                if (TryHitMeasurement(e.Location, out hitMeasurement, out hitPart))
                {
                    selectedMeasurementId = hitMeasurement.Id;
                    selectedMeasurementPart = hitPart;
                    editingMeasurement = true;
                    editingAnchor = e.Location;
                    dragging = false;
                    measuring = false;
                    Capture = true;
                    Invalidate();
                    return;
                }
                selectedMeasurementId = null;
                selectedMeasurementPart = MeasurementHitPart.None;
                selectedMeasurementPointIndex = -1;
            }

            dragging = true;
            dragStart = e.Location;
            if (Tool == ViewerTool.Measure)
            {
                if (MeasurementMode == MeasurementMode.Angle)
                {
                    if (!measuring || (angleFirstRayLocked && angleSecondRayDefined))
                    {
                        measuring = true;
                        activeMeasurementColor = CreateRandomMeasurementColor();
                        angleFirstRayLocked = false;
                        angleSecondRayDefined = false;
                        angleVertex = e.Location;
                        angleRay1End = e.Location;
                        angleRay2End = e.Location;
                    }
                    else if (!angleFirstRayLocked)
                    {
                        angleRay1End = e.Location;
                    }
                    else if (angleFirstRayLocked && !angleSecondRayDefined)
                    {
                        angleRay2End = e.Location;
                    }
                }
                else if (MeasurementMode == MeasurementMode.Polygon)
                {
                    if (!measuring)
                    {
                        measuring = true;
                        activeMeasurementColor = CreateRandomMeasurementColor();
                        polygonSecondPointLocked = false;
                        polygonP1 = e.Location;
                        polygonP2 = e.Location;
                        polygonP3 = e.Location;
                    }
                    else if (!polygonSecondPointLocked)
                    {
                        polygonP2 = e.Location;
                        polygonP3 = e.Location;
                    }
                    else
                    {
                        polygonP3 = e.Location;
                    }
                }
                else if (MeasurementMode == MeasurementMode.OpenPolyline)
                {
                    if (!measuring)
                    {
                        measuring = true;
                        activeMeasurementColor = CreateRandomMeasurementColor();
                        openPolylinePoints.Clear();
                        openPolylinePoints.Add(e.Location);
                        openPolylinePoints.Add(e.Location);
                    }
                    else if (openPolylinePoints.Count >= 1)
                    {
                        openPolylinePoints[openPolylinePoints.Count - 1] = e.Location;
                    }
                }
                else if (MeasurementMode == MeasurementMode.Spline)
                {
                    if (!measuring)
                    {
                        measuring = true;
                        activeMeasurementColor = CreateRandomMeasurementColor();
                        splinePoints.Clear();
                        splinePoints.Add(e.Location);
                        splinePoints.Add(e.Location);
                    }
                    else if (splinePoints.Count >= 1)
                    {
                        splinePoints[splinePoints.Count - 1] = e.Location;
                    }
                }
                else if (MeasurementMode == MeasurementMode.TextAnnotation)
                {
                    measuring = true;
                    activeMeasurementColor = CreateRandomMeasurementColor();
                    measureStart = e.Location;
                    measureEnd = e.Location;
                }
                else if (MeasurementMode == MeasurementMode.Pencil)
                {
                    measuring = true;
                    activeMeasurementColor = CreateRandomMeasurementColor();
                    pencilPoints.Clear();
                    pencilPoints.Add(e.Location);
                }
                else if (MeasurementMode == MeasurementMode.CobbAngle)
                {
                    if (!measuring)
                    {
                        measuring = true;
                        activeMeasurementColor = CreateRandomMeasurementColor();
                        cobbPoints.Clear();
                        cobbPoints.Add(e.Location);
                        cobbPoints.Add(e.Location);
                    }
                    else if (cobbPoints.Count > 0)
                    {
                        cobbPoints[cobbPoints.Count - 1] = e.Location;
                    }
                }
                else if (MeasurementMode == MeasurementMode.RoiRectangle || MeasurementMode == MeasurementMode.RoiCircle)
                {
                    measuring = true;
                    activeMeasurementColor = CreateRandomMeasurementColor();
                    measureStart = e.Location;
                    measureEnd = e.Location;
                }
                else
                {
                    measuring = true;
                    activeMeasurementColor = CreateRandomMeasurementColor();
                    measureStart = e.Location;
                    measureEnd = e.Location;
                }
            }
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (activeMprDragPart != MprCrosshairHitPart.None)
            {
                if ((e.Button & MouseButtons.Left) != MouseButtons.Left)
                {
                    activeMprDragPart = MprCrosshairHitPart.None;
                    ApplyMprPartCursor(hoveredMprPart);
                    Invalidate();
                    return;
                }

                if (activeMprDragPart == MprCrosshairHitPart.RotateHandle && referencePoint.HasValue)
                {
                    var centerScreen = ImageToScreen(referencePoint.Value);
                    var dx = e.X - centerScreen.X;
                    var dy = e.Y - centerScreen.Y;
                    if (dx * dx + dy * dy >= 16f)
                    {
                        var curMouseAngle = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
                        var delta = NormalizeAngleDeltaDegrees(curMouseAngle - mprRotateStartMouseAngle);
                        var nextAngle = ClampMprObliqueAngleDegrees(mprRotateStartAngleDegrees + delta);
                        if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                            nextAngle = (float)Math.Round(nextAngle / 5.0) * 5f;
                        else if (Math.Abs(nextAngle) < 0.8f)
                            nextAngle = 0f;

                        ReferenceLineAngleDegrees = nextAngle;
                        if (MprCrosshairAngleChanged != null)
                            MprCrosshairAngleChanged(nextAngle);
                    }
                }
                else if ((activeMprDragPart == MprCrosshairHitPart.SlabHorizontal || activeMprDragPart == MprCrosshairHitPart.SlabVertical) && referencePoint.HasValue)
                {
                    Image currentImage;
                    lock (imageSyncRoot)
                    {
                        currentImage = image;
                    }
                    var bounds = (imageBounds.Width > 0f && imageBounds.Height > 0f)
                        ? imageBounds
                        : CalculateImageBounds(currentImage, zoom, pan);

                    if (currentImage != null && bounds.Width > 0f && bounds.Height > 0f)
                    {
                        var centerScreen = ImageToScreen(referencePoint.Value);
                        var relX = e.X - centerScreen.X;
                        var relY = e.Y - centerScreen.Y;
                        var rad = ReferenceLineAngleDegrees * Math.PI / 180.0;
                        var cosA = (float)Math.Cos(rad);
                        var sinA = (float)Math.Sin(rad);

                        var perpPx = activeMprDragPart == MprCrosshairHitPart.SlabHorizontal
                            ? Math.Abs(-relX * sinA + relY * cosA)
                            : Math.Abs(relX * cosA + relY * sinA);

                        var pxPerMm = GetScreenPixelsPerMillimeter(currentImage, bounds);
                        var nextSlabMm = (perpPx * 2f) / Math.Max(0.25f, pxPerMm);
                        if (nextSlabMm < 1.3f)
                            nextSlabMm = 1f;
                        else
                            nextSlabMm = (float)Math.Round(Math.Min(100f, nextSlabMm) * 2.0) / 2f;

                        ReferenceSlabThicknessMm = nextSlabMm;
                        if (MprSlabThicknessChanged != null)
                            MprSlabThicknessChanged(nextSlabMm);
                    }
                }
                else
                {
                    PointF imgPt;
                    if (TryGetImagePoint(e.Location, out imgPt) && MprCrosshairMoved != null)
                    {
                        var moveX = activeMprDragPart == MprCrosshairHitPart.Center || activeMprDragPart == MprCrosshairHitPart.VerticalLine;
                        var moveY = activeMprDragPart == MprCrosshairHitPart.Center || activeMprDragPart == MprCrosshairHitPart.HorizontalLine;
                        MprCrosshairMoved(imgPt, moveX, moveY);
                    }
                }

                ApplyMprPartCursor(activeMprDragPart);
                UpdateCursorOverlay(e.Location);
                Invalidate();
                return;
            }

            if (adjustingWindowLevel)
            {
                wlHudPoint = e.Location;
                var dx = e.X - windowLevelStartPoint.X;
                var dy = e.Y - windowLevelStartPoint.Y;
                windowWidth = Math.Max(1f, windowLevelStartWidth + dx * 2f);
                windowCenter = windowLevelStartCenter - dy;

                if (PixelDataProvider != null)
                {
                    var newBmp = PixelDataProvider.RenderBitmap(windowCenter, windowWidth, Inverted);
                    if (newBmp != null)
                        UpdateImageOnly(newBmp);
                }

                if (WindowLevelChanged != null)
                    WindowLevelChanged();
                UpdateCursorOverlay(e.Location);
                Invalidate();
                return;
            }

            if (editingMeasurement && Tool == ViewerTool.Measure)
            {
                MoveSelectedMeasurement(e.Location);
                if (selectedMeasurementId.HasValue)
                {
                    var edited = measurements.FirstOrDefault(m => m.Id == selectedMeasurementId.Value);
                    if (edited != null)
                        CalculateRoiStatistics(edited);
                }
                editingAnchor = e.Location;
                UpdateCursorOverlay(e.Location);
                Invalidate();
                return;
            }

            if (measuring && Tool == ViewerTool.Measure)
            {
                if (MeasurementMode == MeasurementMode.Angle)
                {
                    if (!angleFirstRayLocked)
                        angleRay1End = e.Location;
                    else
                        angleRay2End = e.Location;
                }
                else if (MeasurementMode == MeasurementMode.Polygon)
                {
                    if (!polygonSecondPointLocked)
                    {
                        polygonP2 = e.Location;
                        polygonP3 = e.Location;
                    }
                    else
                    {
                        polygonP3 = e.Location;
                    }
                }
                else if (MeasurementMode == MeasurementMode.OpenPolyline)
                {
                    if (openPolylinePoints.Count > 0)
                        openPolylinePoints[openPolylinePoints.Count - 1] = e.Location;
                }
                else if (MeasurementMode == MeasurementMode.Spline)
                {
                    if (splinePoints.Count > 0)
                        splinePoints[splinePoints.Count - 1] = e.Location;
                }
                else if (MeasurementMode == MeasurementMode.CobbAngle)
                {
                    if (cobbPoints.Count > 0)
                        cobbPoints[cobbPoints.Count - 1] = e.Location;
                }
                else if (dragging)
                {
                    if (MeasurementMode == MeasurementMode.Pencil)
                    {
                        if (pencilPoints.Count == 0 || pencilPoints[pencilPoints.Count - 1] != e.Location)
                            pencilPoints.Add(e.Location);
                    }
                    else
                    {
                        measureEnd = e.Location;
                    }
                }
            }
            else if (dragging)
            {
                pan.X += e.X - dragStart.X;
                pan.Y += e.Y - dragStart.Y;
                dragStart = e.Location;
                Image currentImage;
                lock (imageSyncRoot)
                {
                    currentImage = image;
                }
                if (currentImage != null)
                    imageBounds = CalculateImageBounds(currentImage, zoom, pan);
            }
            if ((MprInteractiveCrosshairEnabled || MprPanelEnabled) && !dragging && !measuring && !editingMeasurement && !adjustingWindowLevel)
            {
                var hitPart = HitTestMprCrosshair(e.Location);
                if (hitPart != hoveredMprPart)
                {
                    hoveredMprPart = hitPart;
                    ApplyMprPartCursor(hoveredMprPart);
                }
            }
            else if (hoveredMprPart != MprCrosshairHitPart.None)
            {
                hoveredMprPart = MprCrosshairHitPart.None;
                ApplyMprPartCursor(MprCrosshairHitPart.None);
            }

            UpdateCursorOverlay(e.Location);
            RaiseCursorImagePositionChanged(e.Location);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoveredMprPart != MprCrosshairHitPart.None && activeMprDragPart == MprCrosshairHitPart.None)
            {
                hoveredMprPart = MprCrosshairHitPart.None;
                ApplyMprPartCursor(MprCrosshairHitPart.None);
                Invalidate();
            }
            if (CursorImagePositionChanged != null)
                CursorImagePositionChanged(null);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && activeMprDragPart != MprCrosshairHitPart.None)
            {
                activeMprDragPart = MprCrosshairHitPart.None;
                hoveredMprPart = HitTestMprCrosshair(e.Location);
                ApplyMprPartCursor(hoveredMprPart);
                Capture = false;
                Invalidate();
                base.OnMouseUp(e);
                return;
            }
            if (e.Button == MouseButtons.Right && Tool == ViewerTool.Measure)
            {
                DicomMeasurement hitMeasurement;
                MeasurementHitPart hitPart;
                if (TryHitMeasurement(e.Location, out hitMeasurement, out hitPart))
                {
                    selectedMeasurementId = hitMeasurement.Id;
                    selectedMeasurementPart = hitPart;
                    if (MeasurementContextMenuRequested != null)
                        MeasurementContextMenuRequested(PointToScreen(e.Location), CloneMeasurement(hitMeasurement));
                    Invalidate();
                    base.OnMouseUp(e);
                    return;
                }
            }

            if (e.Button == MouseButtons.Right && (MprInteractiveCrosshairEnabled || MprPanelEnabled) && MprContextMenuRequested != null && !measuring)
            {
                MprContextMenuRequested(PointToScreen(e.Location));
                base.OnMouseUp(e);
                return;
            }
            if (e.Button == MouseButtons.Right && Tool == ViewerTool.WindowLevel && WindowLevelMenuRequested != null)
                WindowLevelMenuRequested(PointToScreen(e.Location));
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && Tool == ViewerTool.WindowLevel))
                adjustingWindowLevel = false;
            if (e.Button == MouseButtons.Left)
            {
                if (editingMeasurement)
                {
                    editingMeasurement = false;
                    dragging = false;
                    if (MeasurementsChanged != null)
                        MeasurementsChanged();
                    Invalidate();
                }
                if (Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.Angle && measuring)
                {
                    if (!angleFirstRayLocked)
                    {
                        if (DistanceSquared(angleVertex, angleRay1End) >= 9f)
                            angleFirstRayLocked = true;
                    }
                    else if (!angleSecondRayDefined)
                    {
                        if (DistanceSquared(angleVertex, angleRay2End) >= 9f && CommitAngleMeasurement())
                        {
                            angleSecondRayDefined = true;
                            ResetMeasurement();
                        }
                    }
                    Invalidate();
                }
                else if (Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.Distance && measuring)
                {
                    CommitDistanceMeasurement();
                    ResetMeasurement();
                    Invalidate();
                }
                else if (Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.Deviation && measuring)
                {
                    CommitDeviationMeasurement();
                    ResetMeasurement();
                    Invalidate();
                }
                else if (Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.Arrow && measuring)
                {
                    CommitArrowMeasurement();
                    ResetMeasurement();
                    Invalidate();
                }
                else if (Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.TextAnnotation && measuring)
                {
                    CommitTextAnnotationMeasurement();
                    ResetMeasurement();
                    Invalidate();
                }
                else if (Tool == ViewerTool.Measure && (MeasurementMode == MeasurementMode.RoiRectangle || MeasurementMode == MeasurementMode.RoiCircle) && measuring)
                {
                    CommitRoiMeasurement(MeasurementMode);
                    ResetMeasurement();
                    Invalidate();
                }
                else if (Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.Polygon && measuring)
                {
                    if (!polygonSecondPointLocked)
                    {
                        if (DistanceSquared(polygonP1, polygonP2) >= 9f)
                            polygonSecondPointLocked = true;
                    }
                    else if (CommitPolygonMeasurement())
                    {
                        ResetMeasurement();
                    }
                    Invalidate();
                }
                else if (Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.OpenPolyline && measuring)
                {
                    if (openPolylinePoints.Count >= 2)
                    {
                        var lastIndex = openPolylinePoints.Count - 1;
                        if (DistanceSquared(openPolylinePoints[lastIndex - 1], openPolylinePoints[lastIndex]) >= 9f)
                            openPolylinePoints.Add(openPolylinePoints[lastIndex]);
                    }
                    Invalidate();
                }
                else if (Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.Spline && measuring)
                {
                    if (splinePoints.Count >= 2)
                    {
                        var lastIndex = splinePoints.Count - 1;
                        if (DistanceSquared(splinePoints[lastIndex - 1], splinePoints[lastIndex]) >= 9f)
                            splinePoints.Add(splinePoints[lastIndex]);
                    }
                    Invalidate();
                }
                else if (Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.Pencil && measuring)
                {
                    if (pencilPoints.Count > 0 && pencilPoints[pencilPoints.Count - 1] != e.Location)
                        pencilPoints.Add(e.Location);
                    if (CommitPencilMeasurement())
                        ResetMeasurement();
                    else
                        ResetMeasurement();
                    Invalidate();
                }
                else if (Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.CobbAngle && measuring)
                {
                    if (cobbPoints.Count >= 2)
                    {
                        var lastIndex = cobbPoints.Count - 1;
                        if (DistanceSquared(cobbPoints[lastIndex - 1], cobbPoints[lastIndex]) >= 9f)
                        {
                            if (cobbPoints.Count < 4)
                                cobbPoints.Add(cobbPoints[lastIndex]);
                            else if (CommitCobbMeasurement())
                                ResetMeasurement();
                        }
                    }
                    Invalidate();
                }
                dragging = false;
            }
            if (!dragging && !adjustingWindowLevel)
                Capture = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.OpenPolyline)
                FinishOpenPolylineMeasurement();
            if (e.Button == MouseButtons.Left && Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.Spline)
                FinishSplineMeasurement();
            if (e.Button == MouseButtons.Middle && ResetWindowLevelRequested != null)
                ResetWindowLevelRequested();
            if (e.Button == MouseButtons.Right && Tool == ViewerTool.WindowLevel && NormalizeWindowLevelRequested != null)
                NormalizeWindowLevelRequested();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                if (DeleteSelectedMeasurement())
                {
                    e.Handled = true;
                    return;
                }
            }

            if (e.KeyCode == Keys.Enter && Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.OpenPolyline)
            {
                FinishOpenPolylineMeasurement();
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Enter && Tool == ViewerTool.Measure && MeasurementMode == MeasurementMode.Spline)
            {
                FinishSplineMeasurement();
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
                ZoomByAt(e.Delta > 0 ? 1.1f : 0.9f, e.Location);
            else if (SliceScrollRequested != null)
                SliceScrollRequested(e.Delta);
            base.OnMouseWheel(e);
        }
    }
}
