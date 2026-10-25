using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace DicomVUZB
{
    public sealed class SessionState
    {
        public string LastPath { get; set; }
        public int LayoutRows { get; set; }
        public int LayoutColumns { get; set; }
        public int ActiveViewerIndex { get; set; }
        public string SelectedSeriesInstanceUid { get; set; }
        public List<PanelSessionState> Panels { get; set; }
        public List<MeasurementBucketState> Measurements { get; set; }

        public SessionState()
        {
            Panels = new List<PanelSessionState>();
            Measurements = new List<MeasurementBucketState>();
        }
    }

    public sealed class PanelSessionState
    {
        public List<string> ImagePaths { get; set; }
        public int ImageIndex { get; set; }
        public string CurrentImagePath { get; set; }
        public float Zoom { get; set; }
        public int PanX { get; set; }
        public int PanY { get; set; }
        public float WindowCenter { get; set; }
        public float WindowWidth { get; set; }
        public bool WindowLevelEnabled { get; set; }
        public bool Inverted { get; set; }

        public PanelSessionState()
        {
            ImagePaths = new List<string>();
        }
    }

    public sealed class MeasurementBucketState
    {
        public string ImagePath { get; set; }
        public List<MeasurementState> Items { get; set; }

        public MeasurementBucketState()
        {
            Items = new List<MeasurementState>();
        }
    }

    public sealed class MeasurementState
    {
        public Guid Id { get; set; }
        public int Mode { get; set; }
        public string Name { get; set; }
        public int ColorArgb { get; set; }
        public float P1X { get; set; }
        public float P1Y { get; set; }
        public float P2X { get; set; }
        public float P2Y { get; set; }
        public float P3X { get; set; }
        public float P3Y { get; set; }
        public string PointsSerialized { get; set; }
        public float PixelSpacingX { get; set; }
        public float PixelSpacingY { get; set; }
        public bool HasRoiArea { get; set; }
        public float RoiArea { get; set; }
        public bool HasRoiMin { get; set; }
        public float RoiMin { get; set; }
        public bool HasRoiMax { get; set; }
        public float RoiMax { get; set; }
        public bool HasRoiMean { get; set; }
        public float RoiMean { get; set; }
        public bool HasRoiStdDev { get; set; }
        public float RoiStdDev { get; set; }
        public float StrokeWidth { get; set; }
        public int StrokeOpacity { get; set; }
        public string TextValue { get; set; }
    }

    public static class SessionStateService
    {
        private static readonly string SessionDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DicomVUZB");
        private static readonly string SessionFilePath = Path.Combine(SessionDirectory, "session-state.xml");

        public static bool TryLoadSession(out SessionState state)
        {
            state = null;
            try
            {
                if (!File.Exists(SessionFilePath))
                    return false;

                using (var stream = File.OpenRead(SessionFilePath))
                {
                    var serializer = new XmlSerializer(typeof(SessionState));
                    state = serializer.Deserialize(stream) as SessionState;
                }
                return state != null;
            }
            catch
            {
                return false;
            }
        }

        public static void SaveSession(SessionState state)
        {
            if (state == null)
                return;

            try
            {
                Directory.CreateDirectory(SessionDirectory);
                using (var stream = File.Create(SessionFilePath))
                {
                    var serializer = new XmlSerializer(typeof(SessionState));
                    serializer.Serialize(stream, state);
                }
            }
            catch
            {
            }
        }

        public static void ClearSession()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                    File.Delete(SessionFilePath);
            }
            catch
            {
            }
        }

        public static string SerializePoints(List<PointF> points)
        {
            if (points == null || points.Count == 0)
                return null;
            return string.Join(";", points.Select(p => string.Format(CultureInfo.InvariantCulture, "{0},{1}", p.X, p.Y)));
        }

        public static List<PointF> DeserializePoints(string serialized)
        {
            var result = new List<PointF>();
            if (string.IsNullOrWhiteSpace(serialized))
                return result;

            var pairs = serialized.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var values = pair.Split(',');
                float x, y;
                if (values.Length == 2
                    && float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                    && float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                {
                    result.Add(new PointF(x, y));
                }
            }
            return result;
        }
    }
}

