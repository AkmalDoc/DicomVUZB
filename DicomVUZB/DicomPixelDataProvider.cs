using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using Dicom;
using Dicom.Imaging;

namespace DicomVUZB
{
    public sealed class DicomPixelDataProvider
    {
        public int Width { get; }
        public int Height { get; }
        public double RescaleSlope { get; }
        public double RescaleIntercept { get; }
        public string Modality { get; }
        public bool IsHounsfieldUnit { get; }
        public float DefaultWindowCenter { get; }
        public float DefaultWindowWidth { get; }

        public float[] GetRawPixels()
        {
            return pixelBuffer;
        }

        private readonly float[] pixelBuffer;
        private readonly int[] argbBuffer;

        public DicomPixelDataProvider(int width, int height, double slope, double intercept, string modality, bool isHu, float[] pixels, float defaultWc = 128f, float defaultWw = 256f)
        {
            Width = width;
            Height = height;
            RescaleSlope = slope;
            RescaleIntercept = intercept;
            Modality = modality ?? string.Empty;
            IsHounsfieldUnit = isHu;
            pixelBuffer = pixels;
            DefaultWindowCenter = defaultWc;
            DefaultWindowWidth = Math.Max(1f, defaultWw);
            argbBuffer = new int[width * height];
        }

        public static DicomPixelDataProvider Create(DicomDataset dataset, int frameIndex = 0)
        {
            if (dataset == null)
                return null;

            try
            {
                if (!dataset.Contains(DicomTag.PixelData))
                    return null;

                var pixelData = DicomPixelData.Create(dataset);
                var width = pixelData.Width;
                var height = pixelData.Height;
                if (width <= 0 || height <= 0)
                    return null;

                double slope = 1.0;
                if (!TryGetFirstDicomDouble(dataset, DicomTag.RescaleSlope, out slope) || Math.Abs(slope) < 0.000001)
                    slope = 1.0;

                double intercept = 0.0;
                if (!TryGetFirstDicomDouble(dataset, DicomTag.RescaleIntercept, out intercept))
                    intercept = 0.0;

                string modality = string.Empty;
                dataset.TryGetSingleValue(DicomTag.Modality, out modality);
                bool isHu = string.Equals(modality, "CT", StringComparison.OrdinalIgnoreCase)
                            || (dataset.Contains(DicomTag.RescaleIntercept) && dataset.Contains(DicomTag.RescaleSlope));

                var frameBuffer = pixelData.GetFrame(Math.Max(0, Math.Min(frameIndex, pixelData.NumberOfFrames - 1)));
                var data = frameBuffer.Data;
                if (data == null || data.Length == 0)
                    return null;

                var totalPixels = width * height;
                var buffer = new float[totalPixels];
                var bitsAllocated = pixelData.BitsAllocated;
                var isSigned = pixelData.PixelRepresentation == PixelRepresentation.Signed;

                if (bitsAllocated == 16)
                {
                    int bytesNeeded = totalPixels * 2;
                    int maxCount = Math.Min(totalPixels, data.Length / 2);
                    for (int i = 0; i < maxCount; i++)
                    {
                        int byteIdx = i * 2;
                        short rawShort = (short)(data[byteIdx] | (data[byteIdx + 1] << 8));
                        float rawVal = isSigned ? (float)rawShort : (float)(ushort)rawShort;
                        buffer[i] = isHu ? (float)(rawVal * slope + intercept) : rawVal;
                    }
                }
                else if (bitsAllocated == 8)
                {
                    int maxCount = Math.Min(totalPixels, data.Length);
                    for (int i = 0; i < maxCount; i++)
                    {
                        byte rawByte = data[i];
                        buffer[i] = isHu ? (float)(rawByte * slope + intercept) : rawByte;
                    }
                }
                else
                {
                    return null;
                }

                double defaultWc = 128.0;
                double defaultWw = 256.0;
                bool hasWc = TryGetFirstDicomDouble(dataset, DicomTag.WindowCenter, out defaultWc);
                bool hasWw = TryGetFirstDicomDouble(dataset, DicomTag.WindowWidth, out defaultWw);
                if (!hasWc || !hasWw || defaultWw <= 0.0)
                {
                    float min = float.MaxValue;
                    float max = float.MinValue;
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        if (buffer[i] < min) min = buffer[i];
                        if (buffer[i] > max) max = buffer[i];
                    }
                    if (max > min)
                    {
                        defaultWc = (min + max) / 2.0;
                        defaultWw = max - min;
                    }
                    else
                    {
                        defaultWc = 128.0;
                        defaultWw = 256.0;
                    }
                }

                return new DicomPixelDataProvider(width, height, slope, intercept, modality, isHu, buffer, (float)defaultWc, (float)defaultWw);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetFirstDicomDouble(DicomDataset dataset, DicomTag tag, out double value)
        {
            value = 0.0;
            if (dataset == null || !dataset.Contains(tag))
                return false;

            try
            {
                if (dataset.TryGetSingleValue(tag, out value))
                    return true;
            }
            catch
            {
            }

            try
            {
                var values = dataset.GetValues<double>(tag);
                if (values != null && values.Length > 0)
                {
                    value = values[0];
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                var raw = dataset.GetString(tag);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var parts = raw.Split(new[] { '\\', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        var token = parts[0].Trim();
                        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                            double.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public Bitmap RenderBitmap(float windowCenter, float windowWidth, bool inverted = false)
        {
            if (pixelBuffer == null || Width <= 0 || Height <= 0 || argbBuffer == null)
                return null;

            float w = Math.Max(1f, windowWidth);
            float minVal = windowCenter - w / 2f;
            float invW = 255f / w;

            int totalPixels = Width * Height;
            int maxCount = Math.Min(totalPixels, Math.Min(pixelBuffer.Length, argbBuffer.Length));

            lock (argbBuffer)
            {
                for (int i = 0; i < maxCount; i++)
                {
                    float val = (pixelBuffer[i] - minVal) * invW;
                    int gray;
                    if (val <= 0f) gray = 0;
                    else if (val >= 255f) gray = 255;
                    else gray = (int)val;

                    if (inverted) gray = 255 - gray;

                    argbBuffer[i] = unchecked((int)0xFF000000) | (gray << 16) | (gray << 8) | gray;
                }

                var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                BitmapData bmpData = null;
                try
                {
                    bmpData = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    Marshal.Copy(argbBuffer, 0, bmpData.Scan0, maxCount);
                }
                finally
                {
                    if (bmpData != null)
                        bmp.UnlockBits(bmpData);
                }
                return bmp;
            }
        }

        public bool TryGetPixelValue(int x, int y, out float value)
        {
            value = 0f;
            if (pixelBuffer == null || x < 0 || x >= Width || y < 0 || y >= Height)
                return false;

            int index = y * Width + x;
            if (index >= 0 && index < pixelBuffer.Length)
            {
                value = pixelBuffer[index];
                return true;
            }
            return false;
        }

        public void CalculateRoiStatistics(
            RectangleF imageRect,
            MeasurementMode mode,
            float pixelSpacingX,
            float pixelSpacingY,
            out float? area,
            out float? min,
            out float? max,
            out float? mean,
            out float? stdDev)
        {
            area = null;
            min = null;
            max = null;
            mean = null;
            stdDev = null;

            if (pixelBuffer == null || Width <= 0 || Height <= 0)
                return;

            int left = Math.Max(0, (int)Math.Floor(imageRect.Left));
            int top = Math.Max(0, (int)Math.Floor(imageRect.Top));
            int right = Math.Min(Width - 1, (int)Math.Ceiling(imageRect.Right));
            int bottom = Math.Min(Height - 1, (int)Math.Ceiling(imageRect.Bottom));

            if (right < left || bottom < top)
                return;

            float cx = (left + right) / 2f;
            float cy = (top + bottom) / 2f;
            float rx = Math.Max(1f, (right - left) / 2f);
            float ry = Math.Max(1f, (bottom - top) / 2f);

            int count = 0;
            double sum = 0.0;
            float minVal = float.MaxValue;
            float maxVal = float.MinValue;

            for (int y = top; y <= bottom; y++)
            {
                int rowOffset = y * Width;
                for (int x = left; x <= right; x++)
                {
                    if (mode == MeasurementMode.RoiCircle)
                    {
                        float nx = (x - cx) / rx;
                        float ny = (y - cy) / ry;
                        if (nx * nx + ny * ny > 1f)
                            continue;
                    }

                    int idx = rowOffset + x;
                    if (idx < 0 || idx >= pixelBuffer.Length)
                        continue;

                    float val = pixelBuffer[idx];
                    if (val < minVal) minVal = val;
                    if (val > maxVal) maxVal = val;
                    sum += val;
                    count++;
                }
            }

            if (count == 0)
                return;

            double meanVal = sum / count;

            // Second pass for Standard Deviation (SD)
            double sumSqDiff = 0.0;
            for (int y = top; y <= bottom; y++)
            {
                int rowOffset = y * Width;
                for (int x = left; x <= right; x++)
                {
                    if (mode == MeasurementMode.RoiCircle)
                    {
                        float nx = (x - cx) / rx;
                        float ny = (y - cy) / ry;
                        if (nx * nx + ny * ny > 1f)
                            continue;
                    }

                    int idx = rowOffset + x;
                    if (idx < 0 || idx >= pixelBuffer.Length)
                        continue;

                    double diff = pixelBuffer[idx] - meanVal;
                    sumSqDiff += diff * diff;
                }
            }

            double sdVal = count > 1 ? Math.Sqrt(sumSqDiff / (count - 1)) : 0.0;

            float sx = pixelSpacingX > 0f ? pixelSpacingX : 1f;
            float sy = pixelSpacingY > 0f ? pixelSpacingY : 1f;
            float widthMm = imageRect.Width * sx;
            float heightMm = imageRect.Height * sy;
            float areaCalculated = mode == MeasurementMode.RoiCircle
                ? (float)(Math.PI * (widthMm / 2f) * (heightMm / 2f))
                : widthMm * heightMm;

            area = areaCalculated;
            min = minVal;
            max = maxVal;
            mean = (float)meanVal;
            stdDev = (float)sdVal;
        }
    }
}
