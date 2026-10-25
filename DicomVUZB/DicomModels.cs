using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Dicom;

namespace DicomVUZB
{
    public enum AnatomicalPlane
    {
        Unknown = 0,
        Axial = 1,
        Coronal = 2,
        Sagittal = 3,
        Oblique = 4
    }

    public sealed class DicomImageItem
    {
        public string FilePath { get; set; }
        public int InstanceNumber { get; set; }
        public string SopInstanceUid { get; set; }
        public double? SliceLocation { get; set; }
        public double[] ImagePositionPatient { get; set; }
        public double[] ImageOrientationPatient { get; set; }
        public double PixelSpacingX { get; set; }
        public double PixelSpacingY { get; set; }
        public double SliceThickness { get; set; }
        public int Rows { get; set; }
        public int Columns { get; set; }
        public string FrameOfReferenceUid { get; set; }
        public AnatomicalPlane Plane { get; set; }
        public double? ComputedSlicePosition { get; set; }
        public bool SpatialMetadataProbed { get; set; }

        public bool HasSpatialGeometry
        {
            get
            {
                return ImagePositionPatient != null
                    && ImagePositionPatient.Length == 3
                    && ImageOrientationPatient != null
                    && ImageOrientationPatient.Length == 6;
            }
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(FilePath) ? "Изображение" : Path.GetFileName(FilePath);
        }
    }

    public sealed class DicomSeries
    {
        public DicomSeries()
        {
            Images = new List<DicomImageItem>();
        }

        public string SeriesInstanceUid { get; set; }
        public string SeriesDescription { get; set; }
        public int SeriesNumber { get; set; }
        public AnatomicalPlane Plane { get; set; }
        public string FrameOfReferenceUid { get; set; }
        public string Modality { get; set; }
        public List<DicomImageItem> Images { get; private set; }

        public string DisplayName
        {
            get
            {
                var description = string.IsNullOrWhiteSpace(SeriesDescription) ? "Без описания" : SeriesDescription;
                var planeTag = DicomSpatialHelper.GetPlaneShortTag(Plane);
                var prefix = string.IsNullOrEmpty(planeTag) ? string.Empty : "[" + planeTag + "] ";
                return string.Format("{0}. {1}{2} ({3})", SeriesNumber, prefix, description, Images.Count);
            }
        }
    }

    public sealed class DicomStudy
    {
        public DicomStudy()
        {
            Series = new List<DicomSeries>();
        }

        public string StudyInstanceUid { get; set; }
        public string PatientName { get; set; }
        public string PatientId { get; set; }
        public DateTime? StudyDate { get; set; }
        public string StudyDescription { get; set; }
        public List<DicomSeries> Series { get; private set; }

        public string DisplayName
        {
            get
            {
                var patient = string.IsNullOrWhiteSpace(PatientName) ? "Неизвестный пациент" : PatientName;
                var description = string.IsNullOrWhiteSpace(StudyDescription) ? "Исследование" : StudyDescription;
                return string.Format("{0} — {1} ({2})", patient, description, Series.Count);
            }
        }
    }

    public static class DicomSpatialHelper
    {
        public static string GetPlaneShortTag(AnatomicalPlane plane)
        {
            switch (plane)
            {
                case AnatomicalPlane.Axial:
                    return "Ax";
                case AnatomicalPlane.Coronal:
                    return "Cor";
                case AnatomicalPlane.Sagittal:
                    return "Sag";
                case AnatomicalPlane.Oblique:
                    return "Obl";
                default:
                    return string.Empty;
            }
        }

        public static string GetPlaneTitle(AnatomicalPlane plane)
        {
            switch (plane)
            {
                case AnatomicalPlane.Axial:
                    return "Axial (Аксиальная)";
                case AnatomicalPlane.Coronal:
                    return "Coronal (Корональная)";
                case AnatomicalPlane.Sagittal:
                    return "Sagittal (Сагиттальная)";
                case AnatomicalPlane.Oblique:
                    return "Oblique (Косая)";
                default:
                    return "Серия";
            }
        }

        public static bool ComputeNormal(double[] iop, out double nx, out double ny, out double nz)
        {
            nx = 0;
            ny = 0;
            nz = 1;
            if (iop == null || iop.Length < 6)
                return false;

            var rx = iop[0];
            var ry = iop[1];
            var rz = iop[2];
            var cx = iop[3];
            var cy = iop[4];
            var cz = iop[5];

            nx = ry * cz - rz * cy;
            ny = rz * cx - rx * cz;
            nz = rx * cy - ry * cx;

            var len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-6)
                return false;

            nx /= len;
            ny /= len;
            nz /= len;
            return true;
        }

        public static AnatomicalPlane ClassifyPlane(double[] iop, string seriesDescription, string filePath)
        {
            double nx, ny, nz;
            if (ComputeNormal(iop, out nx, out ny, out nz))
            {
                var ax = Math.Abs(nx);
                var ay = Math.Abs(ny);
                var az = Math.Abs(nz);

                if (az >= ax && az >= ay && az > 0.55)
                    return AnatomicalPlane.Axial;
                if (ay >= ax && ay >= az && ay > 0.55)
                    return AnatomicalPlane.Coronal;
                if (ax >= ay && ax >= az && ax > 0.55)
                    return AnatomicalPlane.Sagittal;

                return AnatomicalPlane.Oblique;
            }

            return ClassifyPlaneFromText(seriesDescription, filePath);
        }

        public static AnatomicalPlane ClassifyPlaneFromText(string seriesDescription, string filePath)
        {
            var combined = ((seriesDescription ?? string.Empty) + " " + (filePath ?? string.Empty)).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(combined))
                return AnatomicalPlane.Unknown;

            if (combined.Contains("sag") || combined.Contains("саг"))
                return AnatomicalPlane.Sagittal;
            if (combined.Contains("cor") || combined.Contains("кор") || combined.Contains("фронт") || combined.Contains("front"))
                return AnatomicalPlane.Coronal;
            if (combined.Contains("ax") || combined.Contains("tra") || combined.Contains("акс") || combined.Contains("транс"))
                return AnatomicalPlane.Axial;

            return AnatomicalPlane.Unknown;
        }

        public static double? ComputeSlicePosition(double[] ipp, double[] iop, double? sliceLocation)
        {
            double nx, ny, nz;
            if (ipp != null && ipp.Length == 3 && ComputeNormal(iop, out nx, out ny, out nz))
                return ipp[0] * nx + ipp[1] * ny + ipp[2] * nz;

            if (sliceLocation.HasValue)
                return sliceLocation.Value;

            if (ipp != null && ipp.Length == 3)
                return ipp[2];

            return null;
        }

        public static void PopulateFromDataset(DicomImageItem item, DicomDataset dataset, string seriesDescription)
        {
            if (item == null || dataset == null)
                return;

            item.SpatialMetadataProbed = true;
            item.ImagePositionPatient = ReadDoubleArray(dataset, DicomTag.ImagePositionPatient, 3);
            item.ImageOrientationPatient = ReadDoubleArray(dataset, DicomTag.ImageOrientationPatient, 6);

            var spacing = ReadDoubleArray(dataset, DicomTag.PixelSpacing, 2)
                ?? ReadDoubleArray(dataset, DicomTag.ImagerPixelSpacing, 2);
            if (spacing != null && spacing.Length >= 2 && spacing[0] > 0 && spacing[1] > 0)
            {
                item.PixelSpacingY = spacing[0];
                item.PixelSpacingX = spacing[1];
            }
            else
            {
                if (item.PixelSpacingX <= 0) item.PixelSpacingX = 1.0;
                if (item.PixelSpacingY <= 0) item.PixelSpacingY = 1.0;
            }

            double thickness;
            if (dataset.TryGetSingleValue(DicomTag.SliceThickness, out thickness) && thickness > 0)
                item.SliceThickness = thickness;

            int rows, cols;
            if (dataset.TryGetSingleValue(DicomTag.Rows, out rows) && rows > 0)
                item.Rows = rows;
            if (dataset.TryGetSingleValue(DicomTag.Columns, out cols) && cols > 0)
                item.Columns = cols;

            string forUid;
            if (dataset.TryGetSingleValue(DicomTag.FrameOfReferenceUID, out forUid) && !string.IsNullOrWhiteSpace(forUid))
                item.FrameOfReferenceUid = forUid.Trim();

            double sliceLoc;
            if (!item.SliceLocation.HasValue && dataset.TryGetSingleValue(DicomTag.SliceLocation, out sliceLoc))
                item.SliceLocation = sliceLoc;

            item.Plane = ClassifyPlane(item.ImageOrientationPatient, seriesDescription, item.FilePath);
            item.ComputedSlicePosition = ComputeSlicePosition(item.ImagePositionPatient, item.ImageOrientationPatient, item.SliceLocation);
        }

        public static void EnsureItemSpatialMetadata(DicomImageItem item)
        {
            EnsureItemSpatialMetadata(item, null);
        }

        public static void EnsureItemSpatialMetadata(DicomImageItem item, string seriesDescription)
        {
            if (item == null || item.SpatialMetadataProbed)
                return;

            item.SpatialMetadataProbed = true;
            if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
                return;

            var ext = Path.GetExtension(item.FilePath).ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp")
            {
                item.Plane = ClassifyPlaneFromText(seriesDescription, item.FilePath);
                return;
            }

            try
            {
                var dicomFile = DicomFile.Open(item.FilePath);
                if (dicomFile != null && dicomFile.Dataset != null)
                    PopulateFromDataset(item, dicomFile.Dataset, seriesDescription);
            }
            catch
            {
                item.Plane = ClassifyPlaneFromText(seriesDescription, item.FilePath);
            }
        }

        public static void FinalizeSeriesSpatialMetadata(DicomSeries series)
        {
            if (series == null || series.Images == null || series.Images.Count == 0)
                return;

            // Ensure at least first, middle, and last slices have spatial metadata probed
            EnsureItemSpatialMetadata(series.Images[0], series.SeriesDescription);
            if (series.Images.Count > 1)
                EnsureItemSpatialMetadata(series.Images[series.Images.Count / 2], series.SeriesDescription);
            if (series.Images.Count > 2)
                EnsureItemSpatialMetadata(series.Images[series.Images.Count - 1], series.SeriesDescription);

            var planeCounts = new Dictionary<AnatomicalPlane, int>();
            foreach (var img in series.Images)
            {
                if (img.Plane == AnatomicalPlane.Unknown)
                    img.Plane = ClassifyPlaneFromText(series.SeriesDescription, img.FilePath);
                if (img.Plane != AnatomicalPlane.Unknown)
                {
                    int c;
                    planeCounts.TryGetValue(img.Plane, out c);
                    planeCounts[img.Plane] = c + 1;
                }
                if (string.IsNullOrWhiteSpace(series.FrameOfReferenceUid) && !string.IsNullOrWhiteSpace(img.FrameOfReferenceUid))
                    series.FrameOfReferenceUid = img.FrameOfReferenceUid;
            }

            if (planeCounts.Count > 0)
                series.Plane = planeCounts.OrderByDescending(kv => kv.Value).First().Key;
            else
                series.Plane = ClassifyPlaneFromText(series.SeriesDescription, series.Images[0].FilePath);

            // Propagate series plane to items that were Unknown
            if (series.Plane != AnatomicalPlane.Unknown)
            {
                foreach (var img in series.Images)
                {
                    if (img.Plane == AnatomicalPlane.Unknown)
                        img.Plane = series.Plane;
                }
            }
        }

        public static bool TryImagePixelToPatient3D(DicomImageItem item, float pixelX, float pixelY, out double px, out double py, out double pz)
        {
            px = 0;
            py = 0;
            pz = 0;
            if (item == null || !item.HasSpatialGeometry)
                return false;

            var ipp = item.ImagePositionPatient;
            var iop = item.ImageOrientationPatient;
            var sx = item.PixelSpacingX > 0 ? item.PixelSpacingX : 1.0;
            var sy = item.PixelSpacingY > 0 ? item.PixelSpacingY : 1.0;

            px = ipp[0] + pixelX * sx * iop[0] + pixelY * sy * iop[3];
            py = ipp[1] + pixelX * sx * iop[1] + pixelY * sy * iop[4];
            pz = ipp[2] + pixelX * sx * iop[2] + pixelY * sy * iop[5];
            return true;
        }

        public static bool TryPatient3DToImagePixel(DicomImageItem item, double px, double py, double pz, out float pixelX, out float pixelY, out double signedPlaneDistance)
        {
            pixelX = 0f;
            pixelY = 0f;
            signedPlaneDistance = 0.0;
            if (item == null || !item.HasSpatialGeometry)
                return false;

            var ipp = item.ImagePositionPatient;
            var iop = item.ImageOrientationPatient;
            var sx = item.PixelSpacingX > 0 ? item.PixelSpacingX : 1.0;
            var sy = item.PixelSpacingY > 0 ? item.PixelSpacingY : 1.0;

            var dx = px - ipp[0];
            var dy = py - ipp[1];
            var dz = pz - ipp[2];

            var projRowMm = dx * iop[0] + dy * iop[1] + dz * iop[2];
            var projColMm = dx * iop[3] + dy * iop[4] + dz * iop[5];

            double nx, ny, nz;
            if (ComputeNormal(iop, out nx, out ny, out nz))
                signedPlaneDistance = dx * nx + dy * ny + dz * nz;

            pixelX = (float)(projRowMm / sx);
            pixelY = (float)(projColMm / sy);
            return true;
        }

        private static double[] ReadDoubleArray(DicomDataset dataset, DicomTag tag, int expectedCount)
        {
            if (dataset == null || !dataset.Contains(tag))
                return null;

            try
            {
                var rawValues = dataset.GetValues<string>(tag);
                if (rawValues == null || rawValues.Length == 0)
                    return null;

                var tokens = new List<string>();
                foreach (var raw in rawValues)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;
                    foreach (var part in raw.Split('\\'))
                    {
                        if (!string.IsNullOrWhiteSpace(part))
                            tokens.Add(part.Trim());
                    }
                }

                if (tokens.Count < expectedCount)
                    return null;

                var result = new double[expectedCount];
                for (var i = 0; i < expectedCount; i++)
                {
                    double val;
                    if (!double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out val) &&
                        !double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.CurrentCulture, out val))
                        return null;
                    result[i] = val;
                }
                return result;
            }
            catch
            {
                return null;
            }
        }
    }
}
