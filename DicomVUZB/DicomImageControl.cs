using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DicomVUZB
{
    public enum ViewerTool
    {
        Pan,
        Measure
    }

    public sealed class DicomImageControl : Control
    {
        private Image image;
        private float zoom = 1f;
        private Point pan;
        private Point dragStart;
        private Point measureStart;
        private Point measureEnd;
        private bool dragging;
        private bool measuring;

        public DicomImageControl()
        {
            DoubleBuffered = true;
            BackColor = Color.Black;
            ForeColor = Color.Lime;
            Tool = ViewerTool.Pan;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public ViewerTool Tool { get; set; }
        public bool Inverted { get; set; }
        public string Annotation { get; set; }
        public float Zoom { get { return zoom; } }

        public void SetImage(Image value)
        {
            if (image != null)
                image.Dispose();
            image = value;
            zoom = 1f;
            pan = Point.Empty;
            measureStart = Point.Empty;
            measureEnd = Point.Empty;
            Invalidate();
        }

        public void ResetView()
        {
            zoom = 1f;
            pan = Point.Empty;
            Invalidate();
        }

        public void ZoomBy(float factor)
        {
            zoom = Math.Max(0.1f, Math.Min(12f, zoom * factor));
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && image != null)
                image.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Color.Black);
            if (image == null)
            {
                using (var brush = new SolidBrush(Color.DimGray))
                    e.Graphics.DrawString("Откройте DICOM-файл или папку", Font, brush, 20, 20);
                return;
            }

            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var width = image.Width * zoom;
            var height = image.Height * zoom;
            var destination = new RectangleF((Width - width) / 2f + pan.X, (Height - height) / 2f + pan.Y, width, height);
            if (Inverted)
            {
                using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                {
                    attributes.SetGamma(1f);
                    attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new[]
                    {
                        -1f, 0, 0, 0, 0, 0, -1f, 0, 0, 0, 0, 0, -1f, 0, 0, 0, 0, 0, 1f, 0, 1f, 1f, 1f, 0, 1f
                    }));
                    e.Graphics.DrawImage(image, Rectangle.Round(destination), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            else
                e.Graphics.DrawImage(image, destination);

            if (measuring)
            {
                using (var pen = new Pen(Color.Lime, 2f))
                    e.Graphics.DrawLine(pen, measureStart, measureEnd);
                var dx = measureEnd.X - measureStart.X;
                var dy = measureEnd.Y - measureStart.Y;
                var pixels = Math.Sqrt(dx * dx + dy * dy) / zoom;
                e.Graphics.DrawString(string.Format("{0:0.0} px", pixels), Font, Brushes.Lime, measureEnd.X + 6, measureEnd.Y + 6);
            }

            if (!string.IsNullOrWhiteSpace(Annotation))
                e.Graphics.DrawString(Annotation, Font, Brushes.Yellow, 12, 12);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            dragging = true;
            dragStart = e.Location;
            if (Tool == ViewerTool.Measure)
            {
                measuring = true;
                measureStart = e.Location;
                measureEnd = e.Location;
            }
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging)
                return;
            if (Tool == ViewerTool.Measure)
                measureEnd = e.Location;
            else
            {
                pan.X += e.X - dragStart.X;
                pan.Y += e.Y - dragStart.Y;
                dragStart = e.Location;
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            Capture = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ZoomBy(e.Delta > 0 ? 1.1f : 0.9f);
            base.OnMouseWheel(e);
        }
    }
}
