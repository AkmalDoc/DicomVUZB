using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dicom;
using Dicom.Imaging;

namespace DicomVUZB
{
    public enum MprPlane
    {
        Axial = 0,
        Coronal = 1,
        Sagittal = 2,
        MipCoronal = 3
    }

    public enum MprRenderMode
    {
        Mpr = 0,
        Mip = 1,
        MinIp = 2,
        Average = 3
    }

    public sealed class MprVolume
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Depth { get; private set; }

        public float SpacingX { get; private set; }
        public float SpacingY { get; private set; }
        public float SpacingZ { get; private set; }
        public bool HasPhysicalSpacing { get; private set; }

        public double RescaleSlope { get; private set; }
        public double RescaleIntercept { get; private set; }
        public string Modality { get; private set; }
        public bool IsHounsfieldUnit { get; private set; }
        public float DefaultWindowCenter { get; private set; }
        public float DefaultWindowWidth { get; private set; }

        public string PatientName { get; private set; }
        public string PatientId { get; private set; }
        public string StudyDate { get; private set; }
        public string StudyDescription { get; private set; }
        public string SeriesDescription { get; private set; }
        public string SeriesNumber { get; private set; }

        private readonly float[][] slices;
        private readonly string[] sliceFilePaths;

        public MprVolume(
            int width,
            int height,
            float[][] slices,
            string[] sliceFilePaths,
            float spacingX,
            float spacingY,
            float spacingZ,
            bool hasPhysicalSpacing,
            double slope,
            double intercept,
            string modality,
            bool isHu,
            float defaultWc,
            float defaultWw,
            string patientName,
            string patientId,
            string studyDate,
            string studyDescription,
            string seriesDescription,
            string seriesNumber)
        {
            Width = width;
            Height = height;
            Depth = slices != null ? slices.Length : 0;
            this.slices = slices ?? new float[0][];
            this.sliceFilePaths = sliceFilePaths ?? new string[0];
            SpacingX = spacingX > 0f ? spacingX : 1f;
            SpacingY = spacingY > 0f ? spacingY : 1f;
            SpacingZ = spacingZ > 0f ? spacingZ : 1f;
            HasPhysicalSpacing = hasPhysicalSpacing;
            RescaleSlope = slope;
            RescaleIntercept = intercept;
            Modality = modality ?? string.Empty;
            IsHounsfieldUnit = isHu;
            DefaultWindowCenter = defaultWc;
            DefaultWindowWidth = Math.Max(1f, defaultWw);
            PatientName = patientName ?? "—";
            PatientId = patientId ?? "—";
            StudyDate = studyDate ?? "—";
            StudyDescription = studyDescription ?? "—";
            SeriesDescription = seriesDescription ?? "—";
            SeriesNumber = seriesNumber ?? "—";
        }

        public string GetAxialFilePath(int z)
        {
            if (sliceFilePaths == null || sliceFilePaths.Length == 0)
                return null;
            int clamped = Math.Max(0, Math.Min(sliceFilePaths.Length - 1, z));
            return sliceFilePaths[clamped];
        }

        public float GetVoxel(int x, int y, int z)
        {
            if (z < 0 || z >= Depth || x < 0 || x >= Width || y < 0 || y >= Height)
                return 0f;
            var slice = slices[z];
            int idx = y * Width + x;
            return (slice != null && idx >= 0 && idx < slice.Length) ? slice[idx] : 0f;
        }

        public int GetPlaneSliceCount(MprPlane plane)
        {
            switch (plane)
            {
                case MprPlane.Axial:
                    return Depth;
                case MprPlane.Coronal:
                case MprPlane.MipCoronal:
                    return Height;
                case MprPlane.Sagittal:
                    return Width;
                default:
                    return Depth;
            }
        }

        public void GetPlanePixelSpacing(MprPlane plane, out float pixelSpacingX, out float pixelSpacingY)
        {
            switch (plane)
            {
                case MprPlane.Axial:
                    pixelSpacingX = SpacingX;
                    pixelSpacingY = SpacingY;
                    break;
                case MprPlane.Coronal:
                case MprPlane.MipCoronal:
                    pixelSpacingX = SpacingX;
                    pixelSpacingY = SpacingZ;
                    break;
                case MprPlane.Sagittal:
                    pixelSpacingX = SpacingY;
                    pixelSpacingY = SpacingZ;
                    break;
                default:
                    pixelSpacingX = SpacingX;
                    pixelSpacingY = SpacingY;
                    break;
            }
        }

        public DicomPixelDataProvider ExtractPlaneProvider(
            MprPlane plane,
            int sliceIndex,
            MprRenderMode renderMode = MprRenderMode.Mpr,
            float slabThicknessMm = 1f,
            float obliqueAngleDegrees = 0f,
            int pivotX = -1,
            int pivotY = -1)
        {
            return ExtractPlaneProvider(
                plane,
                sliceIndex,
                renderMode,
                slabThicknessMm,
                obliqueAngleDegrees,
                0f,
                0f,
                pivotX,
                pivotY,
                -1);
        }

        public DicomPixelDataProvider ExtractPlaneProvider(
            MprPlane plane,
            int sliceIndex,
            MprRenderMode renderMode,
            float slabThicknessMm,
            float angleAxialDegrees,
            float angleCoronalDegrees,
            float angleSagittalDegrees,
            int pivotX,
            int pivotY,
            int pivotZ)
        {
            bool hasAxialTilt = Math.Abs(angleAxialDegrees) > 0.05f;
            bool hasCoronalTilt = Math.Abs(angleCoronalDegrees) > 0.05f;
            bool hasSagittalTilt = Math.Abs(angleSagittalDegrees) > 0.05f;

            bool planeTilted = false;
            switch (plane)
            {
                case MprPlane.Axial:
                    planeTilted = hasCoronalTilt || hasSagittalTilt;
                    break;
                case MprPlane.Coronal:
                case MprPlane.MipCoronal:
                    planeTilted = hasAxialTilt || hasSagittalTilt;
                    break;
                case MprPlane.Sagittal:
                    planeTilted = hasAxialTilt || hasCoronalTilt;
                    break;
            }

            if (planeTilted)
            {
                var effMode = plane == MprPlane.MipCoronal && renderMode == MprRenderMode.Mpr
                    ? MprRenderMode.Mip
                    : renderMode;
                var effSlab = plane == MprPlane.MipCoronal
                    ? Math.Max(slabThicknessMm, 20f)
                    : slabThicknessMm;
                return ExtractOblique3DPlane(
                    plane,
                    effMode,
                    effSlab,
                    angleAxialDegrees,
                    angleCoronalDegrees,
                    angleSagittalDegrees,
                    pivotX >= 0 ? pivotX : Width / 2,
                    pivotY >= 0 ? pivotY : Height / 2,
                    pivotZ >= 0 ? pivotZ : Depth / 2);
            }

            switch (plane)
            {
                case MprPlane.Axial:
                    return ExtractAxial(sliceIndex, renderMode, slabThicknessMm);
                case MprPlane.Coronal:
                    return ExtractCoronal(sliceIndex, renderMode, slabThicknessMm);
                case MprPlane.Sagittal:
                    return ExtractSagittal(sliceIndex, renderMode, slabThicknessMm);
                case MprPlane.MipCoronal:
                    return ExtractCoronal(
                        sliceIndex,
                        renderMode == MprRenderMode.Mpr ? MprRenderMode.Mip : renderMode,
                        Math.Max(slabThicknessMm, 20f));
                default:
                    return ExtractAxial(sliceIndex, renderMode, slabThicknessMm);
            }
        }

        private float SampleVolumeTrilinear(float fx, float fy, float fz)
        {
            float bg = IsHounsfieldUnit ? -1000f : 0f;
            if (fx < 0f || fx > Width - 1 || fy < 0f || fy > Height - 1 || fz < 0f || fz > Depth - 1)
                return bg;

            int x0 = (int)fx;
            int y0 = (int)fy;
            int z0 = (int)fz;
            int x1 = x0 + 1 < Width ? x0 + 1 : x0;
            int y1 = y0 + 1 < Height ? y0 + 1 : y0;
            int z1 = z0 + 1 < Depth ? z0 + 1 : z0;

            float dx = fx - x0;
            float dy = fy - y0;
            float dz = fz - z0;

            var s0 = slices[z0];
            var s1 = slices[z1];
            int row0 = y0 * Width;
            int row1 = y1 * Width;

            float c000 = s0[row0 + x0];
            float c100 = s0[row0 + x1];
            float c010 = s0[row1 + x0];
            float c110 = s0[row1 + x1];

            float c00 = c000 + (c100 - c000) * dx;
            float c10 = c010 + (c110 - c010) * dx;
            float c0 = c00 + (c10 - c00) * dy;

            if (z0 == z1 || dz < 0.0001f)
                return c0;

            float c001 = s1[row0 + x0];
            float c101 = s1[row0 + x1];
            float c011 = s1[row1 + x0];
            float c111 = s1[row1 + x1];

            float c01 = c001 + (c101 - c001) * dx;
            float c11 = c011 + (c111 - c011) * dx;
            float c1 = c01 + (c11 - c01) * dy;

            return c0 + (c1 - c0) * dz;
        }

        private DicomPixelDataProvider ExtractOblique3DPlane(
            MprPlane plane,
            MprRenderMode mode,
            float slabThicknessMm,
            float angleAxialDegrees,
            float angleCoronalDegrees,
            float angleSagittalDegrees,
            int pivotX,
            int pivotY,
            int pivotZ)
        {
            // Ортонормированный базис в физическом пространстве (мм)
            double exX = 1.0, exY = 0.0, exZ = 0.0;
            double eyX = 0.0, eyY = 1.0, eyZ = 0.0;
            double ezX = 0.0, ezY = 0.0, ezZ = 1.0;

            if (Math.Abs(angleAxialDegrees) > 0.01f)
            {
                double r = angleAxialDegrees * Math.PI / 180.0;
                double c = Math.Cos(r), s = Math.Sin(r);
                double nxX = exX * c + eyX * s, nxY = exY * c + eyY * s, nxZ = exZ * c + eyZ * s;
                double nyX = -exX * s + eyX * c, nyY = -exY * s + eyY * c, nyZ = -exZ * s + eyZ * c;
                exX = nxX; exY = nxY; exZ = nxZ;
                eyX = nyX; eyY = nyY; eyZ = nyZ;
            }

            if (Math.Abs(angleCoronalDegrees) > 0.01f)
            {
                double r = angleCoronalDegrees * Math.PI / 180.0;
                double c = Math.Cos(r), s = Math.Sin(r);
                double nxX = exX * c + ezX * s, nxY = exY * c + ezY * s, nxZ = exZ * c + ezZ * s;
                double nzX = -exX * s + ezX * c, nzY = -exY * s + ezY * c, nzZ = -exZ * s + ezZ * c;
                exX = nxX; exY = nxY; exZ = nxZ;
                ezX = nzX; ezY = nzY; ezZ = nzZ;
            }

            if (Math.Abs(angleSagittalDegrees) > 0.01f)
            {
                double r = angleSagittalDegrees * Math.PI / 180.0;
                double c = Math.Cos(r), s = Math.Sin(r);
                double nyX = eyX * c + ezX * s, nyY = eyY * c + ezY * s, nyZ = eyZ * c + ezZ * s;
                double nzX = -eyX * s + ezX * c, nzY = -eyY * s + ezY * c, nzZ = -eyZ * s + ezZ * c;
                eyX = nyX; eyY = nyY; eyZ = nyZ;
                ezX = nzX; ezY = nzY; ezZ = nzZ;
            }

            int outWidth;
            int outHeight;
            float centerU;
            float centerV;
            float stepUMm;
            float stepVMm;
            float stepNMm;
            double uDirX, uDirY, uDirZ;
            double vDirX, vDirY, vDirZ;
            double nDirX, nDirY, nDirZ;
            int maxSlabCount;

            switch (plane)
            {
                case MprPlane.Axial:
                    outWidth = Width;
                    outHeight = Height;
                    centerU = pivotX;
                    centerV = pivotY;
                    stepUMm = SpacingX;
                    stepVMm = SpacingY;
                    stepNMm = SpacingZ;
                    uDirX = exX; uDirY = exY; uDirZ = exZ;
                    vDirX = eyX; vDirY = eyY; vDirZ = eyZ;
                    nDirX = ezX; nDirY = ezY; nDirZ = ezZ;
                    maxSlabCount = Depth;
                    break;
                case MprPlane.Coronal:
                case MprPlane.MipCoronal:
                    outWidth = Width;
                    outHeight = Depth;
                    centerU = pivotX;
                    centerV = pivotZ;
                    stepUMm = SpacingX;
                    stepVMm = SpacingZ;
                    stepNMm = SpacingY;
                    uDirX = exX; uDirY = exY; uDirZ = exZ;
                    vDirX = ezX; vDirY = ezY; vDirZ = ezZ;
                    nDirX = eyX; nDirY = eyY; nDirZ = eyZ;
                    maxSlabCount = Height;
                    break;
                default: // Sagittal
                    outWidth = Height;
                    outHeight = Depth;
                    centerU = pivotY;
                    centerV = pivotZ;
                    stepUMm = SpacingY;
                    stepVMm = SpacingZ;
                    stepNMm = SpacingX;
                    uDirX = eyX; uDirY = eyY; uDirZ = eyZ;
                    vDirX = ezX; vDirY = ezY; vDirZ = ezZ;
                    nDirX = exX; nDirY = exY; nDirZ = exZ;
                    maxSlabCount = Width;
                    break;
            }

            float invSpX = 1f / Math.Max(0.01f, SpacingX);
            float invSpY = 1f / Math.Max(0.01f, SpacingY);
            float invSpZ = 1f / Math.Max(0.01f, SpacingZ);

            // Шаги в воксельных координатах на 1 пиксель выходного изображения
            float duX = (float)(uDirX * stepUMm) * invSpX;
            float duY = (float)(uDirY * stepUMm) * invSpY;
            float duZ = (float)(uDirZ * stepUMm) * invSpZ;

            float dvX = (float)(vDirX * stepVMm) * invSpX;
            float dvY = (float)(vDirY * stepVMm) * invSpY;
            float dvZ = (float)(vDirZ * stepVMm) * invSpZ;

            float dnX = (float)(nDirX * stepNMm) * invSpX;
            float dnY = (float)(nDirY * stepNMm) * invSpY;
            float dnZ = (float)(nDirZ * stepNMm) * invSpZ;

            int halfRadius = ComputeHalfSlabRadius(stepNMm, slabThicknessMm, maxSlabCount);
            var output = new float[outWidth * outHeight];

            Parallel.For(0, outHeight, v =>
            {
                float dv = v - centerV;
                float rowBaseX = pivotX + dv * dvX;
                float rowBaseY = pivotY + dv * dvY;
                float rowBaseZ = pivotZ + dv * dvZ;
                int outRowOffset = v * outWidth;

                for (int u = 0; u < outWidth; u++)
                {
                    float du = u - centerU;
                    float bx = rowBaseX + du * duX;
                    float by = rowBaseY + du * duY;
                    float bz = rowBaseZ + du * duZ;

                    if (halfRadius <= 0)
                    {
                        output[outRowOffset + u] = SampleVolumeTrilinear(bx, by, bz);
                    }
                    else
                    {
                        int count = 2 * halfRadius + 1;
                        if (mode == MprRenderMode.Mip)
                        {
                            float best = float.MinValue;
                            for (int s = -halfRadius; s <= halfRadius; s++)
                            {
                                float val = SampleVolumeTrilinear(bx + s * dnX, by + s * dnY, bz + s * dnZ);
                                if (val > best) best = val;
                            }
                            output[outRowOffset + u] = best;
                        }
                        else if (mode == MprRenderMode.MinIp)
                        {
                            float best = float.MaxValue;
                            for (int s = -halfRadius; s <= halfRadius; s++)
                            {
                                float val = SampleVolumeTrilinear(bx + s * dnX, by + s * dnY, bz + s * dnZ);
                                if (val < best) best = val;
                            }
                            output[outRowOffset + u] = best;
                        }
                        else
                        {
                            float sum = 0f;
                            for (int s = -halfRadius; s <= halfRadius; s++)
                                sum += SampleVolumeTrilinear(bx + s * dnX, by + s * dnY, bz + s * dnZ);
                            output[outRowOffset + u] = sum / count;
                        }
                    }
                }
            });

            return new DicomPixelDataProvider(
                outWidth,
                outHeight,
                RescaleSlope,
                RescaleIntercept,
                Modality,
                IsHounsfieldUnit,
                output,
                DefaultWindowCenter,
                DefaultWindowWidth);
        }

        private int ComputeHalfSlabRadius(float stepMm, float slabThicknessMm, int maxCount)
        {
            if (slabThicknessMm <= 0f || stepMm <= 0f)
                return 0;
            int totalSlices = (int)Math.Round(slabThicknessMm / stepMm);
            int half = Math.Max(0, (totalSlices - 1) / 2);
            return Math.Min(maxCount - 1, half);
        }

        private DicomPixelDataProvider ExtractAxial(int zIndex, MprRenderMode mode, float slabThicknessMm)
        {
            int zCenter = Math.Max(0, Math.Min(Depth - 1, zIndex));
            int halfRadius = ComputeHalfSlabRadius(SpacingZ, slabThicknessMm, Depth);
            int zStart = Math.Max(0, zCenter - halfRadius);
            int zEnd = Math.Min(Depth - 1, zCenter + halfRadius);

            int totalPixels = Width * Height;
            var output = new float[totalPixels];

            if (zStart == zEnd)
            {
                var src = slices[zCenter];
                if (src != null)
                    Array.Copy(src, 0, output, 0, Math.Min(src.Length, output.Length));
            }
            else
            {
                int count = zEnd - zStart + 1;
                for (int i = 0; i < totalPixels; i++)
                {
                    if (mode == MprRenderMode.Mip)
                    {
                        float best = float.MinValue;
                        for (int z = zStart; z <= zEnd; z++)
                        {
                            float v = slices[z][i];
                            if (v > best) best = v;
                        }
                        output[i] = best;
                    }
                    else if (mode == MprRenderMode.MinIp)
                    {
                        float best = float.MaxValue;
                        for (int z = zStart; z <= zEnd; z++)
                        {
                            float v = slices[z][i];
                            if (v < best) best = v;
                        }
                        output[i] = best;
                    }
                    else
                    {
                        float sum = 0f;
                        for (int z = zStart; z <= zEnd; z++)
                            sum += slices[z][i];
                        output[i] = sum / count;
                    }
                }
            }

            return new DicomPixelDataProvider(
                Width,
                Height,
                RescaleSlope,
                RescaleIntercept,
                Modality,
                IsHounsfieldUnit,
                output,
                DefaultWindowCenter,
                DefaultWindowWidth);
        }

        private DicomPixelDataProvider ExtractCoronal(int yIndex, MprRenderMode mode, float slabThicknessMm)
        {
            int yCenter = Math.Max(0, Math.Min(Height - 1, yIndex));
            int halfRadius = ComputeHalfSlabRadius(SpacingY, slabThicknessMm, Height);
            int yStart = Math.Max(0, yCenter - halfRadius);
            int yEnd = Math.Min(Height - 1, yCenter + halfRadius);
            int count = yEnd - yStart + 1;

            int outWidth = Width;
            int outHeight = Depth;
            var output = new float[outWidth * outHeight];

            for (int z = 0; z < Depth; z++)
            {
                var slice = slices[z];
                int outRowOffset = z * outWidth;
                if (yStart == yEnd)
                {
                    int srcRowOffset = yCenter * Width;
                    Array.Copy(slice, srcRowOffset, output, outRowOffset, outWidth);
                }
                else
                {
                    for (int x = 0; x < outWidth; x++)
                    {
                        if (mode == MprRenderMode.Mip)
                        {
                            float best = float.MinValue;
                            for (int y = yStart; y <= yEnd; y++)
                            {
                                float v = slice[y * Width + x];
                                if (v > best) best = v;
                            }
                            output[outRowOffset + x] = best;
                        }
                        else if (mode == MprRenderMode.MinIp)
                        {
                            float best = float.MaxValue;
                            for (int y = yStart; y <= yEnd; y++)
                            {
                                float v = slice[y * Width + x];
                                if (v < best) best = v;
                            }
                            output[outRowOffset + x] = best;
                        }
                        else
                        {
                            float sum = 0f;
                            for (int y = yStart; y <= yEnd; y++)
                                sum += slice[y * Width + x];
                            output[outRowOffset + x] = sum / count;
                        }
                    }
                }
            }

            return new DicomPixelDataProvider(
                outWidth,
                outHeight,
                RescaleSlope,
                RescaleIntercept,
                Modality,
                IsHounsfieldUnit,
                output,
                DefaultWindowCenter,
                DefaultWindowWidth);
        }

        private DicomPixelDataProvider ExtractSagittal(int xIndex, MprRenderMode mode, float slabThicknessMm)
        {
            int xCenter = Math.Max(0, Math.Min(Width - 1, xIndex));
            int halfRadius = ComputeHalfSlabRadius(SpacingX, slabThicknessMm, Width);
            int xStart = Math.Max(0, xCenter - halfRadius);
            int xEnd = Math.Min(Width - 1, xCenter + halfRadius);
            int count = xEnd - xStart + 1;

            int outWidth = Height;
            int outHeight = Depth;
            var output = new float[outWidth * outHeight];

            for (int z = 0; z < Depth; z++)
            {
                var slice = slices[z];
                int outRowOffset = z * outWidth;
                if (xStart == xEnd)
                {
                    for (int y = 0; y < outWidth; y++)
                        output[outRowOffset + y] = slice[y * Width + xCenter];
                }
                else
                {
                    for (int y = 0; y < outWidth; y++)
                    {
                        int rowOffset = y * Width;
                        if (mode == MprRenderMode.Mip)
                        {
                            float best = float.MinValue;
                            for (int x = xStart; x <= xEnd; x++)
                            {
                                float v = slice[rowOffset + x];
                                if (v > best) best = v;
                            }
                            output[outRowOffset + y] = best;
                        }
                        else if (mode == MprRenderMode.MinIp)
                        {
                            float best = float.MaxValue;
                            for (int x = xStart; x <= xEnd; x++)
                            {
                                float v = slice[rowOffset + x];
                                if (v < best) best = v;
                            }
                            output[outRowOffset + y] = best;
                        }
                        else
                        {
                            float sum = 0f;
                            for (int x = xStart; x <= xEnd; x++)
                                sum += slice[rowOffset + x];
                            output[outRowOffset + y] = sum / count;
                        }
                    }
                }
            }

            return new DicomPixelDataProvider(
                outWidth,
                outHeight,
                RescaleSlope,
                RescaleIntercept,
                Modality,
                IsHounsfieldUnit,
                output,
                DefaultWindowCenter,
                DefaultWindowWidth);
        }
    }

    public static class MprVolumeService
    {
        private sealed class LoadedSliceEntry
        {
            public string FilePath;
            public int InstanceNumber;
            public double? SortCoordinateZ;
            public double[] ImagePosition;
            public float[] Pixels;
            public int Width;
            public int Height;
        }

        public static MprVolume BuildVolume(IList<DicomImageItem> items, Action<string> progressCallback = null)
        {
            if (items == null || items.Count < 3)
                throw new InvalidOperationException("Для построения MPR необходима серия минимум из 3 срезов.");

            var loadedSlices = new List<LoadedSliceEntry>(items.Count);
            int targetWidth = 0;
            int targetHeight = 0;
            float spacingX = 1f;
            float spacingY = 1f;
            float fallbackSpacingZ = 1f;
            bool hasPhysicalSpacing = false;
            double slope = 1.0;
            double intercept = 0.0;
            string modality = string.Empty;
            bool isHu = false;
            float defaultWc = 40f;
            float defaultWw = 400f;
            bool metadataCaptured = false;

            string patientName = "—";
            string patientId = "—";
            string studyDate = "—";
            string studyDescription = "—";
            string seriesDescription = "—";
            string seriesNumber = "—";

            double[] sliceNormal = null;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
                    continue;

                if (progressCallback != null && (i % 8 == 0 || i == items.Count - 1))
                    progressCallback(string.Format("Построение объёма MPR: срез {0} из {1}...", i + 1, items.Count));

                try
                {
                    var ext = Path.GetExtension(item.FilePath).ToLowerInvariant();
                    if (ext == ".jpg" || ext == ".jpeg" || ext == ".png")
                    {
                        using (var bmp = new Bitmap(item.FilePath))
                        {
                            if (targetWidth == 0 && targetHeight == 0)
                            {
                                targetWidth = bmp.Width;
                                targetHeight = bmp.Height;
                                defaultWc = 128f;
                                defaultWw = 256f;
                                modality = ext.TrimStart('.').ToUpperInvariant();
                            }
                            if (bmp.Width != targetWidth || bmp.Height != targetHeight)
                                continue;

                            var pixels = ExtractBitmapGrayscale(bmp);
                            loadedSlices.Add(new LoadedSliceEntry
                            {
                                FilePath = item.FilePath,
                                InstanceNumber = item.InstanceNumber > 0 ? item.InstanceNumber : (i + 1),
                                SortCoordinateZ = null,
                                Pixels = pixels,
                                Width = bmp.Width,
                                Height = bmp.Height
                            });
                        }
                        continue;
                    }

                    var dicomFile = DicomFile.Open(item.FilePath);
                    var dataset = dicomFile.Dataset;
                    var provider = DicomPixelDataProvider.Create(dataset, 0);
                    if (provider == null)
                        continue;

                    if (targetWidth == 0 && targetHeight == 0)
                    {
                        targetWidth = provider.Width;
                        targetHeight = provider.Height;
                    }
                    else if (provider.Width != targetWidth || provider.Height != targetHeight)
                    {
                        continue;
                    }

                    if (!metadataCaptured)
                    {
                        metadataCaptured = true;
                        slope = provider.RescaleSlope;
                        intercept = provider.RescaleIntercept;
                        modality = provider.Modality;
                        isHu = provider.IsHounsfieldUnit;
                        defaultWc = provider.DefaultWindowCenter;
                        defaultWw = provider.DefaultWindowWidth;

                        float sx, sy;
                        if (TryReadPixelSpacing(dataset, out sx, out sy))
                        {
                            spacingX = sx;
                            spacingY = sy;
                            hasPhysicalSpacing = true;
                        }

                        double st;
                        if (TryReadSingleDouble(dataset, DicomTag.SpacingBetweenSlices, out st) && st > 0)
                            fallbackSpacingZ = (float)st;
                        else if (TryReadSingleDouble(dataset, DicomTag.SliceThickness, out st) && st > 0)
                            fallbackSpacingZ = (float)st;
                        else
                            fallbackSpacingZ = spacingX;

                        sliceNormal = TryReadSliceNormal(dataset);

                        patientName = ReadTagText(dataset, DicomTag.PatientName);
                        patientId = ReadTagText(dataset, DicomTag.PatientID);
                        var rawDate = ReadTagText(dataset, DicomTag.StudyDate);
                        if (rawDate.Length == 8)
                            rawDate = string.Format("{0}-{1}-{2}", rawDate.Substring(0, 4), rawDate.Substring(4, 2), rawDate.Substring(6, 2));
                        studyDate = rawDate;
                        studyDescription = ReadTagText(dataset, DicomTag.StudyDescription);
                        seriesDescription = ReadTagText(dataset, DicomTag.SeriesDescription);
                        seriesNumber = ReadTagText(dataset, DicomTag.SeriesNumber);
                    }

                    double[] ipp = TryReadImagePositionPatient(dataset);
                    double? zCoord = null;
                    if (ipp != null && ipp.Length == 3)
                    {
                        if (sliceNormal != null && sliceNormal.Length == 3)
                            zCoord = ipp[0] * sliceNormal[0] + ipp[1] * sliceNormal[1] + ipp[2] * sliceNormal[2];
                        else
                            zCoord = ipp[2];
                    }
                    else
                    {
                        double sl;
                        if (TryReadSingleDouble(dataset, DicomTag.SliceLocation, out sl))
                            zCoord = sl;
                        else if (item.SliceLocation.HasValue)
                            zCoord = item.SliceLocation.Value;
                    }

                    loadedSlices.Add(new LoadedSliceEntry
                    {
                        FilePath = item.FilePath,
                        InstanceNumber = item.InstanceNumber > 0 ? item.InstanceNumber : (i + 1),
                        SortCoordinateZ = zCoord,
                        ImagePosition = ipp,
                        Pixels = provider.GetRawPixels(),
                        Width = provider.Width,
                        Height = provider.Height
                    });
                }
                catch
                {
                }
            }

            if (loadedSlices.Count < 3)
                throw new InvalidOperationException("Не удалось загрузить достаточное количество однородных срезов (минимум 3) для построения 3D-объёма MPR.");

            bool canSortByZ = loadedSlices.Count(s => s.SortCoordinateZ.HasValue) >= loadedSlices.Count / 2;
            List<LoadedSliceEntry> ordered;
            if (canSortByZ)
            {
                // В системе координат пациента DICOM +Z направлена к голове (Superior).
                // Сортируем по убыванию Z, чтобы z = 0 соответствовал верху (Superior) на корональной и сагиттальной проекциях.
                ordered = loadedSlices
                    .OrderByDescending(s => s.SortCoordinateZ ?? double.MinValue)
                    .ThenBy(s => s.InstanceNumber)
                    .ToList();
            }
            else
            {
                ordered = loadedSlices
                    .OrderBy(s => s.InstanceNumber)
                    .ToList();
            }

            float spacingZ = fallbackSpacingZ;
            var zDiffs = new List<double>();
            for (int i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1];
                var curr = ordered[i];
                if (prev.ImagePosition != null && curr.ImagePosition != null)
                {
                    double dx = curr.ImagePosition[0] - prev.ImagePosition[0];
                    double dy = curr.ImagePosition[1] - prev.ImagePosition[1];
                    double dz = curr.ImagePosition[2] - prev.ImagePosition[2];
                    double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (dist > 0.05 && dist < 50.0)
                        zDiffs.Add(dist);
                }
                else if (prev.SortCoordinateZ.HasValue && curr.SortCoordinateZ.HasValue)
                {
                    double dist = Math.Abs(curr.SortCoordinateZ.Value - prev.SortCoordinateZ.Value);
                    if (dist > 0.05 && dist < 50.0)
                        zDiffs.Add(dist);
                }
            }

            if (zDiffs.Count > 0)
            {
                zDiffs.Sort();
                spacingZ = (float)zDiffs[zDiffs.Count / 2];
            }

            var sliceArrays = ordered.Select(s => s.Pixels).ToArray();
            var slicePaths = ordered.Select(s => s.FilePath).ToArray();

            return new MprVolume(
                targetWidth,
                targetHeight,
                sliceArrays,
                slicePaths,
                spacingX,
                spacingY,
                spacingZ,
                hasPhysicalSpacing,
                slope,
                intercept,
                modality,
                isHu,
                defaultWc,
                defaultWw,
                patientName,
                patientId,
                studyDate,
                studyDescription,
                seriesDescription,
                seriesNumber);
        }

        private static float[] ExtractBitmapGrayscale(Bitmap bmp)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            var result = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    result[row + x] = (c.R + c.G + c.B) / 3f;
                }
            }
            return result;
        }

        private static bool TryReadPixelSpacing(DicomDataset dataset, out float spacingX, out float spacingY)
        {
            spacingX = 1f;
            spacingY = 1f;
            if (dataset == null)
                return false;

            DicomTag[] tags =
            {
                DicomTag.PixelSpacing,
                DicomTag.ImagerPixelSpacing,
                DicomTag.NominalScannedPixelSpacing
            };

            foreach (var tag in tags)
            {
                if (!dataset.Contains(tag))
                    continue;
                try
                {
                    var values = dataset.GetValues<string>(tag);
                    double sy, sx;
                    if (values != null && values.Length >= 2
                        && double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out sy)
                        && double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out sx)
                        && sy > 0 && sx > 0)
                    {
                        spacingX = (float)sx;
                        spacingY = (float)sy;
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static bool TryReadSingleDouble(DicomDataset dataset, DicomTag tag, out double value)
        {
            value = 0.0;
            if (dataset == null || !dataset.Contains(tag))
                return false;
            try
            {
                var raw = dataset.GetString(tag);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var token = raw.Split('\\')[0].Trim();
                    if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                        return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private static double[] TryReadImagePositionPatient(DicomDataset dataset)
        {
            if (dataset == null || !dataset.Contains(DicomTag.ImagePositionPatient))
                return null;
            try
            {
                var values = dataset.GetValues<string>(DicomTag.ImagePositionPatient);
                if (values != null && values.Length >= 3)
                {
                    double x, y, z;
                    if (double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                        && double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                        && double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                    {
                        return new[] { x, y, z };
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static double[] TryReadSliceNormal(DicomDataset dataset)
        {
            if (dataset == null || !dataset.Contains(DicomTag.ImageOrientationPatient))
                return null;
            try
            {
                var values = dataset.GetValues<string>(DicomTag.ImageOrientationPatient);
                if (values != null && values.Length >= 6)
                {
                    double rx, ry, rz, cx, cy, cz;
                    if (double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out rx)
                        && double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out ry)
                        && double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out rz)
                        && double.TryParse(values[3], NumberStyles.Float, CultureInfo.InvariantCulture, out cx)
                        && double.TryParse(values[4], NumberStyles.Float, CultureInfo.InvariantCulture, out cy)
                        && double.TryParse(values[5], NumberStyles.Float, CultureInfo.InvariantCulture, out cz))
                    {
                        double nx = ry * cz - rz * cy;
                        double ny = rz * cx - rx * cz;
                        double nz = rx * cy - ry * cx;
                        double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (len > 0.0001)
                            return new[] { nx / len, ny / len, nz / len };
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static string ReadTagText(DicomDataset dataset, DicomTag tag)
        {
            if (dataset == null || !dataset.Contains(tag))
                return "—";
            try
            {
                var val = dataset.GetString(tag);
                if (!string.IsNullOrWhiteSpace(val))
                    return val.Trim();
            }
            catch
            {
            }
            return "—";
        }
    }
}

