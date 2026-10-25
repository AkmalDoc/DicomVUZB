using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using Dicom;
using Dicom.Imaging;

namespace DicomVUZB
{
    public partial class Form1 : Form
    {
        private sealed class ViewerColorTable : ProfessionalColorTable
        {
            private static readonly Color Bar = Color.FromArgb(164, 164, 164);
            public override Color ToolStripGradientBegin { get { return Bar; } }
            public override Color ToolStripGradientMiddle { get { return Bar; } }
            public override Color ToolStripGradientEnd { get { return Color.FromArgb(150, 150, 150); } }
            public override Color MenuStripGradientBegin { get { return Bar; } }
            public override Color MenuStripGradientEnd { get { return Color.FromArgb(150, 150, 150); } }
            public override Color MenuItemSelected { get { return Color.FromArgb(185, 185, 185); } }
            public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(185, 185, 185); } }
            public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(170, 170, 170); } }
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(172, 172, 172); } }
            public override Color ToolStripContentPanelGradientBegin { get { return Color.FromArgb(150, 150, 150); } }
            public override Color ToolStripContentPanelGradientEnd { get { return Color.FromArgb(150, 150, 150); } }
            public override Color ToolStripBorder { get { return Color.FromArgb(118, 118, 118); } }
        }

        private string RequestTextAnnotationValue(DicomImageControl sourceViewer, Point anchor)
        {
            using (var dialog = new Form
            {
                Text = "Текстовая аннотация",
                StartPosition = FormStartPosition.CenterParent,
                Width = 420,
                Height = 170,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            })
            {
                var label = new Label { Left = 12, Top = 12, Width = 380, Text = "Введите текст аннотации:" };
                var textBox = new TextBox { Left = 12, Top = 34, Width = 380, Text = string.Empty };
                var ok = new Button { Text = "OK", Left = 236, Width = 75, Top = 74, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Отмена", Left = 317, Width = 75, Top = 74, DialogResult = DialogResult.Cancel };
                dialog.Controls.AddRange(new Control[] { label, textBox, ok, cancel });
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                dialog.ActiveControl = textBox;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return null;

                var text = textBox.Text == null ? string.Empty : textBox.Text.Trim();
                return text.Length == 0 ? null : text;
            }
        }

        private static Bitmap CreateIcon(string kind)
        {
            var bitmap = new Bitmap(24, 24);
            using (var g = Graphics.FromImage(bitmap))
            using (var pen = new Pen(Color.FromArgb(35, 35, 35), 1.8f))
            using (var brush = new SolidBrush(Color.FromArgb(210, 210, 210)))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                if (kind == "file") { g.FillRectangle(brush, 5, 3, 12, 18); g.DrawRectangle(pen, 5, 3, 12, 18); g.DrawLine(pen, 8, 9, 14, 9); g.DrawLine(pen, 8, 13, 14, 13); }
                else if (kind == "folder") { var p = new[] { new Point(3, 7), new Point(9, 7), new Point(11, 5), new Point(20, 5), new Point(21, 19), new Point(3, 19) }; g.FillPolygon(brush, p); g.DrawPolygon(pen, p); }
                else if (kind == "prev" || kind == "next") { var right = kind == "next"; g.DrawLine(pen, right ? 6 : 18, 12, right ? 18 : 6, 12); g.DrawLine(pen, right ? 12 : 12, 7, right ? 18 : 6, 12); g.DrawLine(pen, right ? 12 : 12, 17, right ? 18 : 6, 12); }
                else if (kind == "reset") { g.DrawArc(pen, 5, 5, 14, 14, 35, 285); g.DrawLine(pen, 5, 5, 5, 10); g.DrawLine(pen, 5, 5, 10, 5); }
                else if (kind == "zoom+") { g.DrawEllipse(pen, 3, 3, 12, 12); g.DrawLine(pen, 14, 14, 20, 20); g.DrawLine(pen, 6, 9, 12, 9); g.DrawLine(pen, 9, 6, 9, 12); }
                else if (kind == "zoom-") { g.DrawEllipse(pen, 3, 3, 12, 12); g.DrawLine(pen, 14, 14, 20, 20); g.DrawLine(pen, 6, 9, 12, 9); }
                else if (kind == "measure") { g.DrawLine(pen, 4, 18, 19, 5); g.DrawLine(pen, 4, 18, 9, 18); g.DrawLine(pen, 4, 18, 4, 13); g.DrawLine(pen, 19, 5, 14, 5); g.DrawLine(pen, 19, 5, 19, 10); }
                else if (kind == "zip") { g.FillRectangle(brush, 5, 3, 14, 18); g.DrawRectangle(pen, 5, 3, 14, 18); for (int y = 5; y <= 12; y += 3) { g.DrawLine(pen, 10, y, 14, y); } g.FillRectangle(pen.Brush, 11, 14, 3, 4); }
                else if (kind == "rar") { g.FillRectangle(brush, 5, 3, 14, 18); g.DrawRectangle(pen, 5, 3, 14, 18); g.DrawLine(pen, 8, 7, 16, 7); g.DrawLine(pen, 8, 11, 16, 11); g.DrawLine(pen, 8, 15, 16, 15); }
                else if (kind == "mpr") { g.DrawRectangle(pen, 3, 3, 18, 18); g.DrawLine(pen, 12, 3, 12, 21); g.DrawLine(pen, 3, 12, 21, 12); g.DrawEllipse(pen, 9, 9, 6, 6); }
                else if (kind == "sync")
                {
                    g.DrawRectangle(pen, 3, 4, 8, 8);
                    g.DrawRectangle(pen, 13, 12, 8, 8);
                    g.DrawLine(pen, 11, 8, 17, 8);
                    g.DrawLine(pen, 17, 8, 17, 12);
                    g.DrawLine(pen, 7, 12, 7, 16);
                    g.DrawLine(pen, 7, 16, 13, 16);
                    g.FillEllipse(pen.Brush, 5, 6, 4, 4);
                    g.FillEllipse(pen.Brush, 15, 14, 4, 4);
                }
                else { g.DrawEllipse(pen, 8, 7, 8, 10); g.DrawLine(pen, 8, 12, 4, 10); g.DrawLine(pen, 16, 12, 20, 10); g.DrawLine(pen, 10, 7, 10, 3); g.DrawLine(pen, 10, 3, 12, 3); g.DrawLine(pen, 12, 3, 12, 7); }
            }
            return bitmap;
        }
        private readonly List<string> activeTempDirectories = new List<string>();
        private bool isLoadingZip;
        private bool isLoadingRar;
        private DicomRepository repository;
        private DicomDirectoryRepository directoryRepository;
        private TreeView studiesTree;
        private FlowLayoutPanel seriesStrip;
        private Label patientHeader;
        private Label studyHeader;
        private DicomImageControl viewer;
        private VScrollBar sliceScrollBar;
        private Label status;
        private Panel infoPanel;
        private TabControl rightTabControl;
        private TableLayoutPanel infoTable;
        private ListView measurementsListView;
        private readonly Dictionary<string, Label> infoValues = new Dictionary<string, Label>();
        private ToolStripButton previousButton;
        private ToolStripButton nextButton;
        private ToolStripDropDownButton layoutPickerButton;
        private ToolStripDropDown layoutGridDropDown;
        private TableLayoutPanel layoutPreviewGrid;
        private readonly List<DicomImageItem> images = new List<DicomImageItem>();
        private ToolStripDropDownButton measureToolsButton;
        private ToolStripButton panToolButton;
        private ToolStripButton cinePlayPauseButton;
        private ToolStripDropDownButton cineSpeedButton;
        private ToolStripMenuItem measureDistanceMenuItem;
        private ToolStripMenuItem measureAngleMenuItem;
        private ToolStripMenuItem measureRoiRectMenuItem;
        private ToolStripMenuItem measureRoiCircleMenuItem;
        private ToolStripMenuItem measurePolygonMenuItem;
        private ToolStripMenuItem measureOpenPolylineMenuItem;
        private ToolStripMenuItem measureSplineMenuItem;
        private ToolStripMenuItem measureCobbMenuItem;
        private ToolStripMenuItem measureDeviationMenuItem;
        private ToolStripMenuItem measureArrowMenuItem;
        private ToolStripMenuItem measureTextMenuItem;
        private ToolStripMenuItem measurePencilMenuItem;
        private ToolStripMenuItem measure3dCursorMenuItem;
        private sealed class WlState
        {
            public string Name;
            public float? Center;
            public float? Width;
        }

        private sealed class ViewerPanelState
        {
            public readonly List<DicomImageItem> Images = new List<DicomImageItem>();
            public int ImageIndex;
            public string CurrentImagePath;
            public bool CinePlaying;
            public int CineFps = 12;
            public int CineDirection = 1;
            public int CineStep = 1;
            public int? CineLoopStart;
            public int? CineLoopEnd;
            public MprPlane? AssignedMprPlane;
            public MprRenderMode? AssignedMprRenderMode;
            public float? AssignedMprSlabThicknessMm;
            public DicomSeries AssignedSeries;
            public AnatomicalPlane AssignedSyncPlane;
        }

        private static string SerializeMeasurementPoints(List<PointF> points)
        {
            return SessionStateService.SerializePoints(points);
        }

        private static List<PointF> DeserializeMeasurementPoints(string serialized)
        {
            return SessionStateService.DeserializePoints(serialized);
        }

        private const int MaxLayoutGridSize = 5;
        private const string SeriesDragDataFormat = "DicomVUZB.Series";

        private readonly Stack<WlState> wlUndoStack = new Stack<WlState>();
        private readonly Stack<WlState> wlRedoStack = new Stack<WlState>();
        private DateTime lastWlHistoryCaptureUtc = DateTime.MinValue;
        private string wlPresetName = "Auto";
        private float? wlPresetCenter;
        private float? wlPresetWidth;
        private string currentWindowLine = "WL: — WW: —";
        private float? autoWlCenter;
        private float? autoWlWidth;
        private float currentAppliedWlCenter;
        private float currentAppliedWlWidth;
        private readonly string userPresetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DicomVUZB", "wl-user-preset.txt");
        private readonly string viewerLayoutPresetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DicomVUZB", "viewer-layout-preset.txt");
        private readonly string sessionStatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DicomVUZB", "session-state.xml");
        private ToolStripDropDownButton wlButton;
        private ToolStripMenuItem wlMenuItem;
        private readonly List<WlState> recentWlPresets = new List<WlState>();
        private int currentViewerLayoutRows = 1;
        private int currentViewerLayoutColumns = 1;
        private const int StatusUpdateThrottleMs = 220;
        private string statusBaseText = string.Empty;
        private DateTime lastStatusUpdateUtc = DateTime.MinValue;
        private string pendingStatusText;
        private SplitContainer mainSplit;
        private Panel topBarHost;
        private bool fullScreenMode;
        private bool distractionFreeMode;
        private FormBorderStyle savedBorderStyle;
        private FormWindowState savedWindowState;
        private bool savedTopBarVisible;
        private bool savedStatusVisible;
        private bool savedPreviewVisible;
        private Panel selectedSeriesCard;
        private int imageIndex;
        private bool updatingSliceScrollBar;
        private bool interfaceBuilt;
        private Timer cineTimer;
        private Timer statusUpdateTimer;
        private bool syncCursorReferenceLines = true;
        private bool cursor3dModeEnabled;
        private PointF? cursor3dLockedPoint;
        private bool syncWindowLevelAcrossPanels;
        private bool syncSliceScrollAcrossPanels;
        private TableLayoutPanel viewerLayoutTable;
        private Panel primaryViewerHost;
        private readonly List<DicomImageControl> secondaryViewers = new List<DicomImageControl>();
        private readonly Dictionary<DicomImageControl, Panel> viewerHosts = new Dictionary<DicomImageControl, Panel>();
        private readonly Dictionary<Panel, DicomImageControl> hostViewers = new Dictionary<Panel, DicomImageControl>();
        private readonly Dictionary<DicomImageControl, ViewerPanelState> viewerStates = new Dictionary<DicomImageControl, ViewerPanelState>();
        private readonly List<DicomStudy> currentStudies = new List<DicomStudy>();
        private DicomImageControl activeViewer;
        private Panel hoverViewerHost;
        private Image currentRenderedImage;
        private readonly Dictionary<string, List<DicomMeasurement>> measurementsByImagePath = new Dictionary<string, List<DicomMeasurement>>(StringComparer.OrdinalIgnoreCase);
        private string currentImagePath;
        private Point seriesDragStartPoint;
        private DicomSeries pendingDragSeries;
        private string lastLoadedPath;
        private bool restoringSession;
        private Cursor pencilCursor;
        private IntPtr pencilCursorHandle;
        private bool mprModeEnabled;
        private MprVolume currentMprVolume;
        private MprRenderMode currentMprRenderMode = MprRenderMode.Mpr;
        private float currentMprSlabThicknessMm = 1f;
        private float mprObliqueAngleDegrees;
        private float mprAngleCoronalDegrees;
        private float mprAngleSagittalDegrees;
        private bool mprShowCrosshair = true;
        private DicomImageControl mprMaximizedViewer;
        private int mprVoxelX;
        private int mprVoxelY;
        private int mprVoxelZ;
        private bool mprCrosshairDragging;
        private ToolStripDropDownButton mprToolbarButton;
        private ToolStripMenuItem mprToggleMenuItem;
        private bool orthoSeriesSyncEnabled;
        private bool orthoSyncCrosshairDragging;
        private bool orthoSyncHasPatient3DPoint;
        private double orthoSyncPatientX;
        private double orthoSyncPatientY;
        private double orthoSyncPatientZ;
        private float orthoSyncNormX = 0.5f;
        private float orthoSyncNormY = 0.5f;
        private float orthoSyncNormZ = 0.5f;
        private ToolStripDropDownButton syncSeriesToolbarButton;
        private ToolStripMenuItem syncSeriesToggleMenuItem;
        private readonly Dictionary<string, DicomPixelDataProvider> cachedSliceProviders = new Dictionary<string, DicomPixelDataProvider>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> cachedSliceProviderOrder = new LinkedList<string>();
        private const int MaxCachedSliceProviders = 96;

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CreateIconIndirect(ref IconInfo icon);

        [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
        private static extern bool DeleteObject(IntPtr hObject);

        public Form1()
        {
            InitializeComponent();
            if (!IsDesignTime())
                Load += delegate { EnsureInterface(); };
        }

        private static bool IsDesignTime()
        {
            return LicenseManager.UsageMode == LicenseUsageMode.Designtime;
        }

        private DicomImageControl ActiveViewerControl
        {
            get { return activeViewer ?? viewer; }
        }

        private void SetActiveViewer(DicomImageControl target)
        {
            if (target == null)
                target = viewer;

            if (ReferenceEquals(target, activeViewer))
                return;

            var previous = ActiveViewerControl;
            if (previous != null)
            {
                SaveCurrentImageMeasurements();
                CaptureStateFromWorkingSet(previous);
            }

            activeViewer = target;
            AttachSliceScrollBarToActiveViewerHost();
            ApplyViewerCursor(ActiveViewerControl);
            UpdateActiveViewerVisualState();

            if (mprModeEnabled && currentMprVolume != null)
            {
                var st = GetViewerState(activeViewer);
                if (st.AssignedMprPlane.HasValue)
                    currentImagePath = GetMprMeasurementKey(st.AssignedMprPlane.Value, GetMprPlaneSliceIndex(st.AssignedMprPlane.Value));
                UpdateMprScrollBarForActiveViewer();
                UpdateMprDicomInfoPanel();
                if (measurementsListView != null)
                    RefreshMeasurementsList();
                if (!string.IsNullOrWhiteSpace(statusBaseText))
                    SetStatusText(statusBaseText);
                UpdateCineUiState();
                return;
            }

            RestoreStateToWorkingSet(activeViewer);

            if (images.Count > 0)
                ShowImage(Math.Max(0, Math.Min(imageIndex, images.Count - 1)));
            else
            {
                ActiveViewerControl.SetImage(null, true);
                RefreshMeasurementsList();
                UpdateSliceScrollBar(0);
            }

            if (measurementsListView != null)
                RefreshMeasurementsList();
            if (!string.IsNullOrWhiteSpace(statusBaseText))
                SetStatusText(statusBaseText);

            var state = ActivePanelState;
            if (cineTimer != null)
            {
                cineTimer.Interval = Math.Max(1, 1000 / Math.Max(1, state.CineFps));
                if (state.CinePlaying)
                    cineTimer.Start();
                else
                    cineTimer.Stop();
            }
            UpdateCineUiState();
        }

        private void AttachSliceScrollBarToActiveViewerHost()
        {
            if (sliceScrollBar == null)
                return;

            Panel host;
            if (!viewerHosts.TryGetValue(ActiveViewerControl, out host) || host == null)
                return;

            if (sliceScrollBar.Parent == host)
                return;

            var previousParent = sliceScrollBar.Parent;
            if (previousParent != null)
                previousParent.Controls.Remove(sliceScrollBar);

            host.Controls.Add(sliceScrollBar);
            sliceScrollBar.BringToFront();
        }

        private void UpdateActiveViewerVisualState()
        {
            foreach (var pair in viewerHosts)
            {
                pair.Value.BackColor = GetViewerHostBackColor(pair.Key, pair.Value == hoverViewerHost);
                pair.Value.Padding = pair.Key == ActiveViewerControl ? new Padding(1) : new Padding(0);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            ReleaseCustomCursors();
            SaveLastSession();
            CleanupPreviousZipFolders(null);
        }

        private void ReleaseCustomCursors()
        {
            if (pencilCursor != null)
            {
                pencilCursor.Dispose();
                pencilCursor = null;
            }

            if (pencilCursorHandle != IntPtr.Zero)
            {
                DestroyIcon(pencilCursorHandle);
                pencilCursorHandle = IntPtr.Zero;
            }
        }

        private Cursor GetPencilCursor()
        {
            if (pencilCursor != null)
                return pencilCursor;

            using (var bitmap = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bitmap))
                using (var body = new Pen(Color.FromArgb(95, 95, 95), 2.2f))
                using (var highlight = new Pen(Color.FromArgb(230, 230, 230), 1.2f))
                using (var tip = new SolidBrush(Color.FromArgb(248, 222, 160)))
                using (var lead = new SolidBrush(Color.FromArgb(36, 36, 36)))
                using (var edge = new Pen(Color.FromArgb(85, 85, 85), 1.1f))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    body.StartCap = LineCap.Round;
                    body.EndCap = LineCap.Round;
                    highlight.StartCap = LineCap.Round;
                    highlight.EndCap = LineCap.Round;

                    g.DrawLine(body, 25, 7, 9, 24);
                    g.DrawLine(body, 23, 5, 7, 22);
                    g.DrawLine(highlight, 24, 6, 10, 21);

                    var nib = new[]
                    {
                        new PointF(6.0f, 25.0f),
                        new PointF(11.5f, 21.8f),
                        new PointF(8.9f, 27.2f)
                    };
                    g.FillPolygon(tip, nib);
                    g.DrawPolygon(edge, nib);
                    g.FillEllipse(lead, 5.3f, 24.2f, 2.2f, 2.2f);
                }

                var colorBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                var maskBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                try
                {
                    var icon = new IconInfo
                    {
                        fIcon = false,
                        xHotspot = 6,
                        yHotspot = 25,
                        hbmColor = colorBitmap,
                        hbmMask = maskBitmap
                    };
                    pencilCursorHandle = CreateIconIndirect(ref icon);
                }
                finally
                {
                    if (colorBitmap != IntPtr.Zero)
                        DeleteObject(colorBitmap);
                    if (maskBitmap != IntPtr.Zero)
                        DeleteObject(maskBitmap);
                }
            }

            if (pencilCursorHandle == IntPtr.Zero)
                return Cursors.Cross;

            pencilCursor = new Cursor(pencilCursorHandle);
            return pencilCursor;
        }

        private Cursor ResolveViewerCursor(DicomImageControl targetViewer)
        {
            if (targetViewer == null)
                return Cursors.Default;

            if (targetViewer.Tool == ViewerTool.Measure)
                return targetViewer.MeasurementMode == MeasurementMode.Pencil ? Cursors.Default : Cursors.Cross;
            if (targetViewer.Tool == ViewerTool.WindowLevel)
                return Cursors.SizeAll;
            return Cursors.Hand;
        }

        private void ApplyViewerCursor(DicomImageControl targetViewer)
        {
            if (targetViewer == null)
                return;
            var cursor = ResolveViewerCursor(targetViewer);
            targetViewer.BaseToolCursor = cursor;
            if (!targetViewer.IsMprCrosshairDragActive)
                targetViewer.Cursor = cursor;
        }

        private void DeletePersistedPresets()
        {
            try
            {
                if (File.Exists(viewerLayoutPresetPath))
                    File.Delete(viewerLayoutPresetPath);
            }
            catch
            {
            }

            try
            {
                if (File.Exists(userPresetPath))
                    File.Delete(userPresetPath);
            }
            catch
            {
            }
        }

        private void ResetPersistedUserState()
        {
            try
            {
                if (File.Exists(sessionStatePath))
                    File.Delete(sessionStatePath);
            }
            catch
            {
            }

            DeletePersistedPresets();
        }

        private void ResetAllParametersToDefault()
        {
            DeletePersistedPresets();

            if (cineTimer != null)
                cineTimer.Stop();

            foreach (var state in viewerStates.Values)
            {
                state.CinePlaying = false;
                state.CineFps = 12;
                state.CineDirection = 1;
                state.CineStep = 1;
                state.CineLoopStart = null;
                state.CineLoopEnd = null;
                state.AssignedMprPlane = null;
                state.AssignedMprRenderMode = null;
                state.AssignedMprSlabThicknessMm = null;
                state.AssignedSeries = null;
                state.AssignedSyncPlane = AnatomicalPlane.Unknown;
            }

            mprModeEnabled = false;
            currentMprVolume = null;
            currentMprRenderMode = MprRenderMode.Mpr;
            currentMprSlabThicknessMm = 1f;
            mprObliqueAngleDegrees = 0f;
            mprAngleCoronalDegrees = 0f;
            mprAngleSagittalDegrees = 0f;
            mprShowCrosshair = true;
            mprMaximizedViewer = null;
            mprCrosshairDragging = false;
            if (mprToggleMenuItem != null)
                mprToggleMenuItem.Checked = false;
            UpdateMprButtonCaption();

            orthoSeriesSyncEnabled = false;
            orthoSyncCrosshairDragging = false;
            orthoSyncHasPatient3DPoint = false;
            orthoSyncNormX = 0.5f;
            orthoSyncNormY = 0.5f;
            orthoSyncNormZ = 0.5f;
            ClearCachedSliceProviders();
            UpdateSyncSeriesButtonState();

            measurementsByImagePath.Clear();
            ClearWlHistory();
            recentWlPresets.Clear();
            wlPresetName = "Auto";
            wlPresetCenter = null;
            wlPresetWidth = null;

            cursor3dModeEnabled = false;
            cursor3dLockedPoint = null;
            syncCursorReferenceLines = true;
            syncWindowLevelAcrossPanels = false;
            syncSliceScrollAcrossPanels = false;
            if (measure3dCursorMenuItem != null)
                measure3dCursorMenuItem.Checked = false;

            if (distractionFreeMode)
                ToggleDistractionFreeMode();
            if (fullScreenMode)
                ToggleFullScreenMode();
            if (mainSplit != null)
                mainSplit.Panel1Collapsed = false;
            if (topBarHost != null)
                topBarHost.Visible = true;
            if (status != null)
                status.Visible = true;

            if (currentViewerLayoutRows != 1 || currentViewerLayoutColumns != 1 || secondaryViewers.Count > 0)
                ApplyViewerLayoutPreset(1, 1, false);

            if (viewer != null)
            {
                viewer.SetMeasurements(null);
                viewer.SetReferencePoint(null);
                viewer.ShowReferenceLines = true;
                viewer.ReferenceLineColorHorizontal = null;
                viewer.ReferenceLineColorVertical = null;
                viewer.ReferenceLineAngleDegrees = 0f;
                viewer.ReferenceSlabThicknessMm = 1f;
                viewer.MprInteractiveCrosshairEnabled = false;
                viewer.MprPanelEnabled = false;
                viewer.OrientationTop = "A";
                viewer.OrientationBottom = "P";
                viewer.OrientationLeft = "R";
                viewer.OrientationRight = "L";
                viewer.Inverted = false;
                viewer.MeasurementMode = MeasurementMode.Distance;
                viewer.Tool = ViewerTool.Pan;
                viewer.PencilContinuousMode = false;
                viewer.PencilSimplifyEnabled = true;
                viewer.PencilSmoothingEnabled = true;
                viewer.PencilStrokeWidth = 1.2f;
                viewer.PencilOpacity = 220;
                viewer.ResetView();
            }

            foreach (var sv in secondaryViewers)
            {
                sv.SetMeasurements(null);
                sv.SetReferencePoint(null);
                sv.ShowReferenceLines = true;
                sv.ReferenceLineColorHorizontal = null;
                sv.ReferenceLineColorVertical = null;
                sv.ReferenceLineAngleDegrees = 0f;
                sv.ReferenceSlabThicknessMm = 1f;
                sv.MprInteractiveCrosshairEnabled = false;
                sv.MprPanelEnabled = false;
                sv.OrientationTop = "A";
                sv.OrientationBottom = "P";
                sv.OrientationLeft = "R";
                sv.OrientationRight = "L";
                sv.Inverted = false;
                sv.MeasurementMode = MeasurementMode.Distance;
                sv.Tool = ViewerTool.Pan;
                sv.ResetView();
            }

            if (measureDistanceMenuItem != null) measureDistanceMenuItem.Checked = true;
            if (measureAngleMenuItem != null) measureAngleMenuItem.Checked = false;
            if (measureRoiRectMenuItem != null) measureRoiRectMenuItem.Checked = false;
            if (measureRoiCircleMenuItem != null) measureRoiCircleMenuItem.Checked = false;
            if (measureDeviationMenuItem != null) measureDeviationMenuItem.Checked = false;
            if (measureArrowMenuItem != null) measureArrowMenuItem.Checked = false;
            if (measureTextMenuItem != null) measureTextMenuItem.Checked = false;
            if (measurePencilMenuItem != null) measurePencilMenuItem.Checked = false;
            if (measurePolygonMenuItem != null) measurePolygonMenuItem.Checked = false;
            if (measureOpenPolylineMenuItem != null) measureOpenPolylineMenuItem.Checked = false;
            if (measureSplineMenuItem != null) measureSplineMenuItem.Checked = false;
            if (measureCobbMenuItem != null) measureCobbMenuItem.Checked = false;
            if (measureToolsButton != null) measureToolsButton.ToolTipText = "Измерения и инструменты";

            if (wlButton != null)
                PopulateWlPresetItems(wlButton);
            if (wlMenuItem != null)
                PopulateWlPresetItems(wlMenuItem);

            RefreshWlPresetCaption();
            SetActiveViewer(viewer);
            ActivateViewerTool(ViewerTool.Pan);
            RefreshMeasurementsList();
            UpdateCineUiState();
        }

        private void SaveLastSession()
        {
            if (restoringSession)
                return;

            try
            {
                // Сбрасываем все пользовательские параметры к исходному состоянию
                // и сохраняем только путь к последнему открытому файлу/папке/архиву.
                ResetAllParametersToDefault();

                var state = new SessionState
                {
                    LastPath = lastLoadedPath,
                    LayoutRows = 1,
                    LayoutColumns = 1,
                    Panels = new List<PanelSessionState>(),
                    Measurements = new List<MeasurementBucketState>(),
                    ActiveViewerIndex = 0,
                    SelectedSeriesInstanceUid = null
                };

                SessionStateService.SaveSession(state);
            }
            catch
            {
            }
        }

        private void RestoreLastSession()
        {
            ResetAllParametersToDefault();

            SessionState state;
            if (!SessionStateService.TryLoadSession(out state) || state == null)
                return;

            restoringSession = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(state.LastPath) && (File.Exists(state.LastPath) || Directory.Exists(state.LastPath)))
                {
                    lastLoadedPath = state.LastPath;
                    LoadPath(state.LastPath);
                }
            }
            finally
            {
                restoringSession = false;
            }
        }


        private List<MeasurementBucketState> SerializeMeasurements()
        {
            var buckets = new List<MeasurementBucketState>();
            foreach (var entry in measurementsByImagePath)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                    continue;

                var items = new List<MeasurementState>();
                foreach (var measurement in entry.Value)
                {
                    items.Add(new MeasurementState
                    {
                        Id = measurement.Id,
                        Mode = (int)measurement.Mode,
                        Name = measurement.Name,
                        ColorArgb = measurement.Color.ToArgb(),
                        P1X = measurement.P1.X,
                        P1Y = measurement.P1.Y,
                        P2X = measurement.P2.X,
                        P2Y = measurement.P2.Y,
                        P3X = measurement.P3.X,
                        P3Y = measurement.P3.Y,
                        PointsSerialized = SerializeMeasurementPoints(measurement.Points),
                        PixelSpacingX = measurement.PixelSpacingX,
                        PixelSpacingY = measurement.PixelSpacingY,
                        HasRoiArea = measurement.RoiArea.HasValue,
                        RoiArea = measurement.RoiArea ?? 0f,
                        HasRoiMin = measurement.RoiMin.HasValue,
                        RoiMin = measurement.RoiMin ?? 0f,
                        HasRoiMax = measurement.RoiMax.HasValue,
                        RoiMax = measurement.RoiMax ?? 0f,
                        HasRoiMean = measurement.RoiMean.HasValue,
                        RoiMean = measurement.RoiMean ?? 0f,
                        HasRoiStdDev = measurement.RoiStdDev.HasValue,
                        RoiStdDev = measurement.RoiStdDev ?? 0f,
                        StrokeWidth = measurement.StrokeWidth,
                        StrokeOpacity = measurement.StrokeOpacity,
                        TextValue = measurement.TextValue
                    });
                }

                buckets.Add(new MeasurementBucketState
                {
                    ImagePath = entry.Key,
                    Items = items
                });
            }
            return buckets;
        }

        private void RestoreMeasurements(List<MeasurementBucketState> buckets)
        {
            measurementsByImagePath.Clear();
            if (buckets == null)
                return;

            foreach (var bucket in buckets)
            {
                if (bucket == null || string.IsNullOrWhiteSpace(bucket.ImagePath) || bucket.Items == null)
                    continue;

                var items = new List<DicomMeasurement>();
                foreach (var item in bucket.Items)
                {
                    var mode = item.Mode;
                    if (!Enum.IsDefined(typeof(MeasurementMode), mode))
                        mode = (int)MeasurementMode.Distance;

                    items.Add(new DicomMeasurement
                    {
                        Id = item.Id,
                        Mode = (MeasurementMode)mode,
                        Name = item.Name,
                        Color = Color.FromArgb(item.ColorArgb),
                        P1 = new PointF(item.P1X, item.P1Y),
                        P2 = new PointF(item.P2X, item.P2Y),
                        P3 = new PointF(item.P3X, item.P3Y),
                        Points = DeserializeMeasurementPoints(item.PointsSerialized),
                        PixelSpacingX = item.PixelSpacingX,
                        PixelSpacingY = item.PixelSpacingY,
                        RoiArea = item.HasRoiArea ? (float?)item.RoiArea : null,
                        RoiMin = item.HasRoiMin ? (float?)item.RoiMin : null,
                        RoiMax = item.HasRoiMax ? (float?)item.RoiMax : null,
                        RoiMean = item.HasRoiMean ? (float?)item.RoiMean : null,
                        RoiStdDev = item.HasRoiStdDev ? (float?)item.RoiStdDev : null,
                        StrokeWidth = item.StrokeWidth,
                        StrokeOpacity = item.StrokeOpacity,
                        TextValue = item.TextValue
                    });
                }

                measurementsByImagePath[bucket.ImagePath] = items;
            }
        }

        private void RestorePanelAssignments(List<PanelSessionState> panels)
        {
            if (panels == null)
                return;

            var imageLookup = BuildImageLookupByPath();
            var orderedViewers = GetOrderedViewers();
            for (var i = 0; i < orderedViewers.Count && i < panels.Count; i++)
            {
                var panel = panels[i];
                var state = GetViewerState(orderedViewers[i]);
                state.Images.Clear();
                if (panel.ImagePaths != null)
                {
                    foreach (var imagePath in panel.ImagePaths)
                    {
                        DicomImageItem source;
                        if (imageLookup.TryGetValue(imagePath, out source))
                            state.Images.Add(CloneImageItem(source));
                        else if (!string.IsNullOrWhiteSpace(imagePath))
                            state.Images.Add(new DicomImageItem { FilePath = imagePath, InstanceNumber = state.Images.Count + 1 });
                    }
                }

                state.ImageIndex = Math.Max(0, Math.Min(Math.Max(0, state.Images.Count - 1), panel.ImageIndex));
                state.CurrentImagePath = panel.CurrentImagePath;
            }
        }

        private Dictionary<string, DicomImageItem> BuildImageLookupByPath()
        {
            var lookup = new Dictionary<string, DicomImageItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var study in currentStudies)
            {
                foreach (var series in study.Series)
                {
                    foreach (var item in series.Images)
                    {
                        if (item == null || string.IsNullOrWhiteSpace(item.FilePath) || lookup.ContainsKey(item.FilePath))
                            continue;
                        lookup[item.FilePath] = item;
                    }
                }
            }
            return lookup;
        }

        private static DicomImageItem CloneImageItem(DicomImageItem source)
        {
            return new DicomImageItem
            {
                FilePath = source.FilePath,
                InstanceNumber = source.InstanceNumber,
                SopInstanceUid = source.SopInstanceUid,
                SliceLocation = source.SliceLocation,
                ImagePositionPatient = source.ImagePositionPatient,
                ImageOrientationPatient = source.ImageOrientationPatient,
                PixelSpacingX = source.PixelSpacingX,
                PixelSpacingY = source.PixelSpacingY,
                SliceThickness = source.SliceThickness,
                Rows = source.Rows,
                Columns = source.Columns,
                FrameOfReferenceUid = source.FrameOfReferenceUid,
                Plane = source.Plane,
                ComputedSlicePosition = source.ComputedSlicePosition,
                SpatialMetadataProbed = source.SpatialMetadataProbed
            };
        }

        private Panel FindSeriesCardByUid(string seriesUid)
        {
            if (string.IsNullOrWhiteSpace(seriesUid) || seriesStrip == null)
                return null;

            foreach (Control control in seriesStrip.Controls)
            {
                var panel = control as Panel;
                var series = panel != null ? panel.Tag as DicomSeries : null;
                if (series == null)
                    continue;
                if (string.Equals(series.SeriesInstanceUid, seriesUid, StringComparison.OrdinalIgnoreCase))
                    return panel;
            }

            return null;
        }

        private Color GetViewerHostBackColor(DicomImageControl hostViewer, bool isHover)
        {
            if (isHover)
                return Color.FromArgb(190, 150, 80);
            return hostViewer == ActiveViewerControl
                ? Color.FromArgb(70, 120, 200)
                : Color.Black;
        }

        private void SetHoverViewerHost(Panel host)
        {
            if (hoverViewerHost == host)
                return;
            hoverViewerHost = host;
            UpdateActiveViewerVisualState();
        }

        private void AttachActivationHandlers(DicomImageControl target)
        {
            target.MouseDown += delegate (object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                    SetActiveViewer(target);
            };
        }

        private List<DicomImageControl> GetOrderedViewers()
        {
            var ordered = new List<DicomImageControl>();
            if (viewerLayoutTable == null)
            {
                if (ActiveViewerControl != null)
                    ordered.Add(ActiveViewerControl);
                return ordered;
            }

            for (var row = 0; row < viewerLayoutTable.RowCount; row++)
            {
                for (var col = 0; col < viewerLayoutTable.ColumnCount; col++)
                {
                    var panel = viewerLayoutTable.GetControlFromPosition(col, row) as Panel;
                    if (panel == null)
                        continue;

                    DicomImageControl panelViewer;
                    if (!hostViewers.TryGetValue(panel, out panelViewer) || panelViewer == null)
                        continue;

                    ordered.Add(panelViewer);
                }
            }

            if (ordered.Count == 0 && ActiveViewerControl != null)
                ordered.Add(ActiveViewerControl);
            return ordered;
        }

        private void SwitchActiveViewerByTab(bool reverse)
        {
            var ordered = GetOrderedViewers();
            if (ordered.Count <= 1)
                return;

            var current = ActiveViewerControl;
            var currentIndex = ordered.IndexOf(current);
            if (currentIndex < 0)
                currentIndex = 0;

            var delta = reverse ? -1 : 1;
            var nextIndex = (currentIndex + delta + ordered.Count) % ordered.Count;
            SetActiveViewer(ordered[nextIndex]);
            ordered[nextIndex].Focus();
        }

        private ViewerPanelState GetViewerState(DicomImageControl target)
        {
            ViewerPanelState state;
            if (!viewerStates.TryGetValue(target, out state))
            {
                state = new ViewerPanelState();
                viewerStates[target] = state;
            }
            return state;
        }

        private void CaptureStateFromWorkingSet(DicomImageControl target)
        {
            if (target == null)
                return;

            var state = GetViewerState(target);
            state.Images.Clear();
            state.Images.AddRange(images);
            state.ImageIndex = imageIndex;
            state.CurrentImagePath = currentImagePath;
        }

        private int GetActiveSliceCountForCine()
        {
            if (mprModeEnabled && currentMprVolume != null)
            {
                var st = GetViewerState(ActiveViewerControl);
                var plane = st.AssignedMprPlane ?? MprPlane.Axial;
                return GetMprPlaneSliceCount(plane);
            }
            return images.Count;
        }

        private int GetActiveSliceIndexForCine()
        {
            if (mprModeEnabled && currentMprVolume != null)
            {
                var st = GetViewerState(ActiveViewerControl);
                var plane = st.AssignedMprPlane ?? MprPlane.Axial;
                return GetMprPlaneSliceIndex(plane);
            }
            return imageIndex;
        }

        private void OnCineTimerTick(object sender, EventArgs e)
        {
            var state = ActivePanelState;
            if (!state.CinePlaying)
                return;
            if (GetActiveSliceCountForCine() <= 1)
            {
                StopCine();
                return;
            }

            NavigateSeriesByStep(state.CineDirection * Math.Max(1, state.CineStep));
        }

        private void SetCineFps(int fps)
        {
            var state = ActivePanelState;
            state.CineFps = Math.Max(1, Math.Min(60, fps));
            if (cineTimer != null)
                cineTimer.Interval = Math.Max(1, 1000 / state.CineFps);
            UpdateCineUiState();
        }

        private void SetCineStep(int step)
        {
            ActivePanelState.CineStep = Math.Max(1, Math.Min(3, step));
            UpdateCineUiState();
        }

        private void ToggleCineDirection()
        {
            ActivePanelState.CineDirection = ActivePanelState.CineDirection >= 0 ? -1 : 1;
            UpdateCineUiState();
        }

        private void ToggleCinePlayback()
        {
            if (ActivePanelState.CinePlaying)
            {
                StopCine();
                return;
            }
            StartCine();
        }

        private void NavigateSeriesByStep(int step)
        {
            if (mprModeEnabled && currentMprVolume != null)
            {
                NavigateMprByStep(step);
                return;
            }

            if (images.Count <= 1 || step == 0)
                return;

            var state = ActivePanelState;
            var loopStart = 0;
            var loopEnd = images.Count - 1;
            if (TryGetActiveLoopRange(state, images.Count, out loopStart, out loopEnd))
            {
                if (imageIndex < loopStart || imageIndex > loopEnd)
                    SelectImage(loopStart);
            }

            var target = imageIndex + step;
            if (target < loopStart)
                target = loopEnd;
            else if (target > loopEnd)
                target = loopStart;

            if (target != imageIndex)
                SelectImage(target);

            if (orthoSeriesSyncEnabled)
                return;

            if (!syncSliceScrollAcrossPanels)
                return;

            foreach (var pair in viewerStates.ToList())
            {
                if (pair.Key == ActiveViewerControl)
                    continue;
                var panelState = pair.Value;
                if (panelState.Images.Count <= 1)
                    continue;

                var otherStart = 0;
                var otherEnd = panelState.Images.Count - 1;
                TryGetActiveLoopRange(panelState, panelState.Images.Count, out otherStart, out otherEnd);

                var index = panelState.ImageIndex + step;
                if (index < otherStart)
                    index = otherEnd;
                else if (index > otherEnd)
                    index = otherStart;
                RenderViewerPanelSlice(pair.Key, index, false);
            }
        }

        private static bool TryGetActiveLoopRange(ViewerPanelState state, int imageCount, out int start, out int end)
        {
            start = 0;
            end = Math.Max(0, imageCount - 1);
            if (state == null || imageCount <= 1 || !state.CineLoopStart.HasValue || !state.CineLoopEnd.HasValue)
                return false;

            var a = Math.Max(0, Math.Min(imageCount - 1, state.CineLoopStart.Value));
            var b = Math.Max(0, Math.Min(imageCount - 1, state.CineLoopEnd.Value));
            start = Math.Min(a, b);
            end = Math.Max(a, b);
            return end > start;
        }

        private void SetLoopPointA()
        {
            if (GetActiveSliceCountForCine() <= 1)
                return;
            ActivePanelState.CineLoopStart = GetActiveSliceIndexForCine();
            UpdateCineUiState();
        }

        private void SetLoopPointB()
        {
            if (GetActiveSliceCountForCine() <= 1)
                return;
            ActivePanelState.CineLoopEnd = GetActiveSliceIndexForCine();
            UpdateCineUiState();
        }

        private void ClearLoopRange()
        {
            ActivePanelState.CineLoopStart = null;
            ActivePanelState.CineLoopEnd = null;
            UpdateCineUiState();
        }

        private void NavigateToFirstSlice()
        {
            if (mprModeEnabled && currentMprVolume != null)
            {
                NavigateMprToBoundary(true);
                return;
            }

            if (images.Count > 0)
                SelectImage(0);
        }

        private void NavigateToLastSlice()
        {
            if (mprModeEnabled && currentMprVolume != null)
            {
                NavigateMprToBoundary(false);
                return;
            }

            if (images.Count > 0)
                SelectImage(images.Count - 1);
        }

        private void StartCine()
        {
            var state = ActivePanelState;
            if (GetActiveSliceCountForCine() <= 1 || cineTimer == null)
                return;
            state.CinePlaying = true;
            cineTimer.Interval = Math.Max(1, 1000 / Math.Max(1, state.CineFps));
            cineTimer.Start();
            UpdateCineUiState();
        }

        private void StopCine()
        {
            ActivePanelState.CinePlaying = false;
            if (cineTimer != null)
                cineTimer.Stop();
            UpdateCineUiState();
        }

        private void UpdateCineUiState()
        {
            var state = ActivePanelState;
            if (cinePlayPauseButton != null)
                cinePlayPauseButton.Text = state.CinePlaying ? "⏸" : "▶";
            if (cinePlayPauseButton != null)
                cinePlayPauseButton.ToolTipText = state.CinePlaying ? "Пауза cine (Пробел)" : "Запуск cine (Пробел)";
            if (cineSpeedButton != null)
                cineSpeedButton.Text = string.Format("{0} fps ×{1}", state.CineFps, Math.Max(1, state.CineStep));

            if (!string.IsNullOrWhiteSpace(statusBaseText) && status != null)
            {
                string loopText = string.Empty;
                int loopStart;
                int loopEnd;
                if (TryGetActiveLoopRange(state, GetActiveSliceCountForCine(), out loopStart, out loopEnd))
                    loopText = string.Format(" A-B:{0}-{1}", loopStart + 1, loopEnd + 1);

                var cineText = state.CinePlaying
                    ? string.Format(" | Cine: {0} fps ×{1} {2}{3}", state.CineFps, Math.Max(1, state.CineStep), state.CineDirection >= 0 ? "▶" : "◀", loopText)
                    : "";
                status.Text = string.Format("{0}{1} | Tool: {2}", statusBaseText, cineText, GetToolCaption());
            }
        }

        private void RestoreStateToWorkingSet(DicomImageControl target)
        {
            var state = GetViewerState(target);
            images.Clear();
            images.AddRange(state.Images);

            if (images.Count == 0)
            {
                imageIndex = 0;
                currentImagePath = null;
                return;
            }

            imageIndex = Math.Max(0, Math.Min(images.Count - 1, state.ImageIndex));
            currentImagePath = state.CurrentImagePath;
        }

        private ViewerPanelState ActivePanelState
        {
            get { return GetViewerState(ActiveViewerControl); }
        }

        private void EnsureInterface()
        {
            if (IsDesignTime())
                return;
            InitializeRuntimeDependencies();
            if (interfaceBuilt) return;
            interfaceBuilt = true;
            BuildInterface();
        }

        private void InitializeRuntimeDependencies()
        {
            if (repository != null)
                return;

            repository = new DicomRepository();
            directoryRepository = new DicomDirectoryRepository();
            studiesTree = new TreeView();
            seriesStrip = new FlowLayoutPanel();
            patientHeader = new Label();
            studyHeader = new Label();
            viewer = new DicomImageControl();
            sliceScrollBar = new VScrollBar();
            status = new Label();
            infoPanel = new Panel();
            rightTabControl = new TabControl();
            infoTable = new TableLayoutPanel();
            measurementsListView = new ListView();
            previousButton = new ToolStripButton("◀");
            nextButton = new ToolStripButton("▶");
            cineTimer = new Timer { Interval = 1000 / 12 };
            cineTimer.Tick += OnCineTimerTick;
            statusUpdateTimer = new Timer { Interval = StatusUpdateThrottleMs };
            statusUpdateTimer.Tick += OnStatusUpdateTimerTick;
        }

        private void BuildInterface()
        {
            DeletePersistedPresets();
            Text = "DicomVUZB — DICOM Viewer";
            MinimumSize = new Size(1024, 700);
            WindowState = FormWindowState.Maximized;
            BackColor = Color.Black;
            Font = new Font("Segoe UI", 9f);
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            var renderer = new ToolStripProfessionalRenderer(new ViewerColorTable());
            var menu = new MenuStrip { Dock = DockStyle.Top, Renderer = renderer, BackColor = Color.FromArgb(164, 164, 164), ForeColor = Color.FromArgb(36, 36, 36), Padding = new Padding(6, 0, 0, 0), Height = 22, ShowItemToolTips = true };
            var file = new ToolStripMenuItem("Файл");
            file.DropDownItems.Add("Открыть файл...", null, delegate { OpenFile(); });
            file.DropDownItems.Add("Открыть папку...", null, delegate { OpenFolder(); });
            file.DropDownItems.Add("Открыть ZIP-архив...", null, delegate { OpenZipFile(); });
            file.DropDownItems.Add("Открыть RAR-архив...", null, delegate { OpenRarFile(); });
            file.DropDownItems.Add("Открыть DICOMDIR...", null, delegate { OpenDicomDir(); });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("Экспорт снимка (PNG/JPEG)...", null, delegate { ExportCurrentImage(); });
            file.DropDownItems.Add("Экспорт измерений в CSV...", null, delegate { ExportMeasurementsToCsv(); });
            file.DropDownItems.Add("Экспорт измерений в PDF...", null, delegate { ExportMeasurementsToPdf(); });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("Выход", null, delegate { Close(); });
            var view = new ToolStripMenuItem("Вид");
            view.DropDownItems.Add("Вписать в окно (Сброс вида)", null, delegate { ActiveViewerControl.ResetView(); });
            view.DropDownItems.Add("Реальный размер (1:1)", null, delegate { ActiveViewerControl.SetActualPixels(); });
            view.DropDownItems.Add("Инверсия", null, delegate { var target = ActiveViewerControl; target.Inverted = !target.Inverted; target.Invalidate(); SyncSecondaryViewers(); });
            wlMenuItem = new ToolStripMenuItem("WL/WW пресет");
            PopulateWlPresetItems(wlMenuItem);
            view.DropDownItems.Add(wlMenuItem);
            var measureMenu = new ToolStripMenuItem("Измерение");
            measureDistanceMenuItem = new ToolStripMenuItem("Линейка", null, delegate { SetMeasurementMode(MeasurementMode.Distance); }) { Checked = true, CheckOnClick = false };
            measureAngleMenuItem = new ToolStripMenuItem("Угол", null, delegate { SetMeasurementMode(MeasurementMode.Angle); }) { CheckOnClick = false };
            measureRoiRectMenuItem = new ToolStripMenuItem("ROI прямоугольник", null, delegate { SetMeasurementMode(MeasurementMode.RoiRectangle); }) { CheckOnClick = false };
            measureRoiCircleMenuItem = new ToolStripMenuItem("ROI круг", null, delegate { SetMeasurementMode(MeasurementMode.RoiCircle); }) { CheckOnClick = false };
            measureDeviationMenuItem = new ToolStripMenuItem("Отклонение", null, delegate { SetMeasurementMode(MeasurementMode.Deviation); }) { CheckOnClick = false };
            measureArrowMenuItem = new ToolStripMenuItem("Стрелка", null, delegate { SetMeasurementMode(MeasurementMode.Arrow); }) { CheckOnClick = false };
            measureTextMenuItem = new ToolStripMenuItem("Текст", null, delegate { SetMeasurementMode(MeasurementMode.TextAnnotation); }) { CheckOnClick = false };
            measurePencilMenuItem = new ToolStripMenuItem("Карандаш", null, delegate { SetMeasurementMode(MeasurementMode.Pencil); }) { CheckOnClick = false };
            measurePolygonMenuItem = new ToolStripMenuItem("Полигон", null, delegate { SetMeasurementMode(MeasurementMode.Polygon); }) { CheckOnClick = false };
            measureOpenPolylineMenuItem = new ToolStripMenuItem("Открытый полигон", null, delegate { SetMeasurementMode(MeasurementMode.OpenPolyline); }) { CheckOnClick = false };
            measureSplineMenuItem = new ToolStripMenuItem("Spline (кривая)", null, delegate { SetMeasurementMode(MeasurementMode.Spline); }) { CheckOnClick = false };
            measureCobbMenuItem = new ToolStripMenuItem("Угол Кобба", null, delegate { SetMeasurementMode(MeasurementMode.CobbAngle); }) { CheckOnClick = false };
            measureMenu.DropDownItems.Add(measureDistanceMenuItem);
            measureMenu.DropDownItems.Add(measureAngleMenuItem);
            measureMenu.DropDownItems.Add(measureRoiRectMenuItem);
            measureMenu.DropDownItems.Add(measureRoiCircleMenuItem);
            measureMenu.DropDownItems.Add(measureDeviationMenuItem);
            measureMenu.DropDownItems.Add(measureArrowMenuItem);
            measureMenu.DropDownItems.Add(measureTextMenuItem);
            measureMenu.DropDownItems.Add(measurePencilMenuItem);
            measureMenu.DropDownItems.Add(measurePolygonMenuItem);
            measureMenu.DropDownItems.Add(measureOpenPolylineMenuItem);
            measureMenu.DropDownItems.Add(measureSplineMenuItem);
            measureMenu.DropDownItems.Add(measureCobbMenuItem);
            measureMenu.DropDownItems.Add(new ToolStripSeparator());
            measureMenu.DropDownItems.Add("Активировать инструмент", null, delegate { ActivateViewerTool(ViewerTool.Measure); });
            view.DropDownItems.Add(measureMenu);
            view.DropDownItems.Add("Панорамирование", null, delegate { ActivateViewerTool(ViewerTool.Pan); });
            view.DropDownItems.Add("WL инструмент", null, delegate { ActivateViewerTool(ViewerTool.WindowLevel); });
            var advanced = new ToolStripMenuItem("Расширения");
            advanced.DropDownItems.Add("Открыть DICOMDIR...", null, delegate { OpenDicomDir(); });
            mprToggleMenuItem = new ToolStripMenuItem("Мультипланарная реконструкция (MPR)", null, delegate { ToggleMprMode(); })
            {
                ShortcutKeys = Keys.Control | Keys.M,
                ShowShortcutKeys = true,
                CheckOnClick = false
            };
            advanced.DropDownItems.Add(mprToggleMenuItem);
            syncSeriesToggleMenuItem = new ToolStripMenuItem("Авто-синхронизация проекций (Axial / Sagittal / Coronal)", null, delegate { ToggleOrthoSeriesSync(); })
            {
                ShortcutKeys = Keys.Control | Keys.Shift | Keys.S,
                ShowShortcutKeys = true,
                CheckOnClick = false,
                Enabled = false
            };
            advanced.DropDownItems.Add(syncSeriesToggleMenuItem);
            advanced.DropDownItems.Add(new ToolStripSeparator());
            advanced.DropDownItems.Add("PACS / 3D — подготовлено для следующего этапа", null, delegate { MessageBox.Show("Сетевой PACS и 3D-рендеринг требуют отдельной настройки.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information); });
            menu.Items.Add(file);
            menu.Items.Add(view);
            menu.Items.Add(advanced);
            foreach (ToolStripMenuItem item in menu.Items)
            {
                item.ForeColor = Color.FromArgb(36, 36, 36);
                item.Padding = new Padding(6, 2, 6, 2);
            }
            MainMenuStrip = menu;

            var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, Renderer = renderer, BackColor = Color.FromArgb(164, 164, 164), ForeColor = Color.FromArgb(36, 36, 36), Padding = new Padding(3, 1, 3, 1), Height = 30, ImageScalingSize = new Size(18, 18), ShowItemToolTips = true };
            var appMenuButton = new ToolStripDropDownButton("☰") { AutoSize = false, Width = 24, Height = 22, ForeColor = Color.FromArgb(36, 36, 36), ToolTipText = "Главное меню" };
            appMenuButton.DropDownItems.Add(new ToolStripMenuItem("New window", null, delegate { new Form1().Show(); }) { ShortcutKeys = Keys.Control | Keys.N, ShowShortcutKeys = true });
            var duplicateWindowItem = PlaceholderMenuItem("Duplicate window", "Дублирование окна");
            duplicateWindowItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.N;
            duplicateWindowItem.ShowShortcutKeys = true;
            appMenuButton.DropDownItems.Add(duplicateWindowItem);
            appMenuButton.DropDownItems.Add(new ToolStripSeparator());
            appMenuButton.DropDownItems.Add(new ToolStripMenuItem("Open DICOM folder", null, delegate { OpenFolder(); }) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.O, ShowShortcutKeys = true });
            appMenuButton.DropDownItems.Add(new ToolStripMenuItem("Open DICOM file", null, delegate { OpenFile(); }) { ShortcutKeys = Keys.Control | Keys.O, ShowShortcutKeys = true });
            appMenuButton.DropDownItems.Add(new ToolStripMenuItem("Open DICOMDIR", null, delegate { OpenDicomDir(); }));
            appMenuButton.DropDownItems.Add(new ToolStripMenuItem("Open ZIP file", null, delegate { OpenZipFile(); }));
            appMenuButton.DropDownItems.Add(new ToolStripMenuItem("Open RAR file", null, delegate { OpenRarFile(); }));
            appMenuButton.DropDownItems.Add(new ToolStripSeparator());
            appMenuButton.DropDownItems.Add(new ToolStripMenuItem("Export image (PNG/JPEG)...", null, delegate { ExportCurrentImage(); }));
            appMenuButton.DropDownItems.Add(new ToolStripMenuItem("Export measurements (CSV)...", null, delegate { ExportMeasurementsToCsv(); }));
            appMenuButton.DropDownItems.Add(new ToolStripMenuItem("Export report (PDF)...", null, delegate { ExportMeasurementsToPdf(); }));
            appMenuButton.DropDownItems.Add(new ToolStripSeparator());
            var addImages = new ToolStripMenuItem("Add DICOM images");
            addImages.DropDownItems.Add("Add DICOM folder", null, delegate { OpenFolder(); });
            addImages.DropDownItems.Add("Add DICOM file", null, delegate { OpenFile(); });
            addImages.DropDownItems.Add("Add DICOMDIR", null, delegate { OpenDicomDir(); });
            addImages.DropDownItems.Add("Add ZIP file", null, delegate { OpenZipFile(); });
            addImages.DropDownItems.Add("Add RAR file", null, delegate { OpenRarFile(); });
            appMenuButton.DropDownItems.Add(addImages);
            appMenuButton.DropDownItems.Add(new ToolStripSeparator());
            appMenuButton.DropDownItems.Add(PlaceholderMenuItem("Import to database", "Импорт в базу данных"));
            appMenuButton.DropDownItems.Add(new ToolStripMenuItem("Exit", null, delegate { Close(); }) { ShortcutKeys = Keys.Control | Keys.Alt | Keys.F4, ShowShortcutKeys = true });
            toolbar.Items.Add(appMenuButton);
            toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(2, 0, 2, 0) });

            toolbar.Items.Add(ToolButton(CreateIcon("file"), "Открыть DICOM-файл", delegate { OpenFile(); }));
            toolbar.Items.Add(ToolButton(CreateIcon("folder"), "Open DICOM folder (Ctrl+Shift+O)", delegate { OpenFolder(); }));
            toolbar.Items.Add(ToolButton(CreateIcon("zip"), "Открыть ZIP-архив", delegate { OpenZipFile(); }));
            toolbar.Items.Add(ToolButton(CreateIcon("rar"), "Открыть RAR-архив", delegate { OpenRarFile(); }));
            toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(2, 0, 2, 0) });

            previousButton.Click += delegate { NavigateSeriesByStep(-1); };
            nextButton.Click += delegate { NavigateSeriesByStep(1); };
            StyleButton(previousButton, CreateIcon("prev"), "Предыдущее изображение");
            StyleButton(nextButton, CreateIcon("next"), "Следующее изображение");
            toolbar.Items.Add(previousButton);
            toolbar.Items.Add(nextButton);
            syncSeriesToolbarButton = CreateSyncSeriesToolbarButton();
            toolbar.Items.Add(syncSeriesToolbarButton);
            toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(2, 0, 2, 0) });

            cinePlayPauseButton = new ToolStripButton("▶")
            {
                AutoSize = false,
                Width = 28,
                Height = 22,
                ToolTipText = "Запуск cine (Пробел)"
            };
            cinePlayPauseButton.Click += delegate { ToggleCinePlayback(); };
            cineSpeedButton = new ToolStripDropDownButton("12 fps")
            {
                AutoSize = false,
                Width = 58,
                Height = 22,
                ToolTipText = "Скорость cine"
            };
            foreach (var fps in new[] { 6, 10, 12, 16, 24, 30 })
            {
                var f = fps;
                cineSpeedButton.DropDownItems.Add(new ToolStripMenuItem(f + " fps", null, delegate { SetCineFps(f); }));
            }
            cineSpeedButton.DropDownItems.Add(new ToolStripSeparator());
            cineSpeedButton.DropDownItems.Add(new ToolStripMenuItem("Шаг 1 кадр", null, delegate { SetCineStep(1); }) { ShortcutKeyDisplayString = "Ctrl+1" });
            cineSpeedButton.DropDownItems.Add(new ToolStripMenuItem("Шаг 2 кадра", null, delegate { SetCineStep(2); }) { ShortcutKeyDisplayString = "Ctrl+2" });
            cineSpeedButton.DropDownItems.Add(new ToolStripMenuItem("Шаг 3 кадра", null, delegate { SetCineStep(3); }) { ShortcutKeyDisplayString = "Ctrl+3" });
            cineSpeedButton.DropDownItems.Add(new ToolStripSeparator());
            cineSpeedButton.DropDownItems.Add(new ToolStripMenuItem("Направление: вперёд", null, delegate { ActivePanelState.CineDirection = 1; UpdateCineUiState(); }));
            cineSpeedButton.DropDownItems.Add(new ToolStripMenuItem("Направление: назад", null, delegate { ActivePanelState.CineDirection = -1; UpdateCineUiState(); }));
            cineSpeedButton.DropDownItems.Add(new ToolStripMenuItem("Сменить направление", null, delegate { ToggleCineDirection(); }) { ShortcutKeyDisplayString = "X" });
            cineSpeedButton.DropDownItems.Add(new ToolStripSeparator());
            cineSpeedButton.DropDownItems.Add(new ToolStripMenuItem("Установить точку A", null, delegate { SetLoopPointA(); }) { ShortcutKeyDisplayString = "Ctrl+A" });
            cineSpeedButton.DropDownItems.Add(new ToolStripMenuItem("Установить точку B", null, delegate { SetLoopPointB(); }) { ShortcutKeyDisplayString = "Ctrl+B" });
            cineSpeedButton.DropDownItems.Add(new ToolStripMenuItem("Очистить диапазон A-B", null, delegate { ClearLoopRange(); }) { ShortcutKeyDisplayString = "Ctrl+Shift+C" });
            toolbar.Items.Add(cinePlayPauseButton);
            toolbar.Items.Add(cineSpeedButton);
            toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(2, 0, 2, 0) });

            toolbar.Items.Add(ToolButton(CreateIcon("reset"), "Сбросить масштаб", delegate { ActiveViewerControl.ResetView(); }));
            toolbar.Items.Add(ToolButton(CreateIcon("zoom+"), "Увеличить", delegate { ActiveViewerControl.ZoomBy(1.25f); }));
            toolbar.Items.Add(ToolButton(CreateIcon("zoom-"), "Уменьшить", delegate { ActiveViewerControl.ZoomBy(0.8f); }));
            toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(2, 0, 2, 0) });
            layoutPickerButton = new ToolStripDropDownButton("▦")
            {
                AutoSize = false,
                Width = 44,
                Height = 24,
                ToolTipText = "Выбор мультиоконной раскладки"
            };
            layoutGridDropDown = CreateLayoutGridDropDown();
            layoutPickerButton.DropDown = layoutGridDropDown;
            toolbar.Items.Add(layoutPickerButton);
            wlButton = new ToolStripDropDownButton(CreateIcon("measure"))
            {
                AutoSize = false,
                Width = 78,
                Height = 22,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextAlign = ContentAlignment.MiddleRight,
                ImageAlign = ContentAlignment.MiddleLeft,
                Text = "WL Auto",
                ToolTipText = "WL/WW preset"
            };
            wlButton.Click += delegate { ActivateViewerTool(ViewerTool.WindowLevel); };
            PopulateWlPresetItems(wlButton);
            toolbar.Items.Add(wlButton);
            toolbar.Items.Add(ToolButton(CreateIcon("reset"), "Reset WL (Ctrl+R)", delegate { ResetWlToAuto(); }));
            toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(2, 0, 2, 0) });

            measureToolsButton = new ToolStripDropDownButton
            {
                Image = CreateIcon("measure"),
                Text = string.Empty,
                ToolTipText = "Измерения и инструменты",
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                AutoSize = false,
                Width = 26,
                Height = 22,
                Margin = new Padding(1, 0, 1, 0),
                Padding = new Padding(1),
                BackColor = Color.FromArgb(164, 164, 164),
                ForeColor = Color.FromArgb(36, 36, 36)
            };
            measureToolsButton.DropDownItems.Add(new ToolStripMenuItem("Длина", null, delegate { SetMeasurementMode(MeasurementMode.Distance); }) { ShortcutKeyDisplayString = "L", ShowShortcutKeys = true });
            measureToolsButton.DropDownItems.Add(new ToolStripMenuItem("Эллипс", null, delegate { SetMeasurementMode(MeasurementMode.RoiCircle); }) { ShortcutKeyDisplayString = "E", ShowShortcutKeys = true });
            measureToolsButton.DropDownItems.Add(new ToolStripMenuItem("Прямоугольник", null, delegate { SetMeasurementMode(MeasurementMode.RoiRectangle); }) { ShortcutKeyDisplayString = "U", ShowShortcutKeys = true });
            measureToolsButton.DropDownItems.Add(new ToolStripMenuItem("Угол", null, delegate { SetMeasurementMode(MeasurementMode.Angle); }) { ShortcutKeyDisplayString = "A", ShowShortcutKeys = true });
            measureToolsButton.DropDownItems.Add(new ToolStripSeparator());
            measureToolsButton.DropDownItems.Add(new ToolStripMenuItem("Замкнутый полигон", null, delegate { SetMeasurementMode(MeasurementMode.Polygon); }) { ShortcutKeyDisplayString = "G", ShowShortcutKeys = true });
            measureToolsButton.DropDownItems.Add(new ToolStripMenuItem("Открытый полигон", null, delegate { SetMeasurementMode(MeasurementMode.OpenPolyline); }) { ShortcutKeyDisplayString = "O", ShowShortcutKeys = true });
            measureToolsButton.DropDownItems.Add(new ToolStripMenuItem("Spline", null, delegate { SetMeasurementMode(MeasurementMode.Spline); }) { ShortcutKeyDisplayString = "S", ShowShortcutKeys = true });
            measureToolsButton.DropDownItems.Add(new ToolStripMenuItem("Угол Кобба", null, delegate { SetMeasurementMode(MeasurementMode.CobbAngle); }) { ShortcutKeyDisplayString = "C", ShowShortcutKeys = true });
            measureToolsButton.DropDownItems.Add(new ToolStripMenuItem("Отклонение", null, delegate { SetMeasurementMode(MeasurementMode.Deviation); }) { ShortcutKeyDisplayString = "D", ShowShortcutKeys = true });
            measureToolsButton.DropDownItems.Add(new ToolStripMenuItem("Стрелка", null, delegate { SetMeasurementMode(MeasurementMode.Arrow); }) { ShortcutKeyDisplayString = "Y", ShowShortcutKeys = true });
            measureToolsButton.DropDownItems.Add(new ToolStripMenuItem("Текст", null, delegate { SetMeasurementMode(MeasurementMode.TextAnnotation); }) { ShortcutKeyDisplayString = "T", ShowShortcutKeys = true });
            measureToolsButton.DropDownItems.Add(new ToolStripMenuItem("Карандаш", null, delegate { SetMeasurementMode(MeasurementMode.Pencil); }) { ShortcutKeyDisplayString = "P", ShowShortcutKeys = true });
            measureToolsButton.DropDownItems.Add(new ToolStripSeparator());
            measure3dCursorMenuItem = new ToolStripMenuItem("3D-курсор", null, delegate { Toggle3dCursorMode(); }) { CheckOnClick = false, ShortcutKeyDisplayString = "Q", ShowShortcutKeys = true };
            measureToolsButton.DropDownItems.Add(measure3dCursorMenuItem);
            measureToolsButton.DropDownItems.Add(new ToolStripSeparator());
            measureToolsButton.DropDownItems.Add(PlaceholderMenuItem("Редактировать измерения", "Редактирование измерений"));
            measureToolsButton.DropDownItems.Add(PlaceholderMenuItem("Дополнительные инструменты", "Дополнительные инструменты"));
            measureToolsButton.DropDownItems.Add(PlaceholderMenuItem("Вычисления", "Вычисления"));
            measureToolsButton.DropDownItems.Add(new ToolStripSeparator());
            measureToolsButton.DropDownItems.Add(PlaceholderMenuItem("Фильтры изображения", "Фильтры изображения"));
            panToolButton = ToolButton(CreateIcon("pan"), "Панорамирование", delegate { ActivateViewerTool(ViewerTool.Pan); });
            toolbar.Items.Add(measureToolsButton);
            toolbar.Items.Add(panToolButton);
            toolbar.Items.Add(GhostButton(CreateIcon("zoom+"), "Аннотации", "Аннотации"));
            toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(2, 0, 2, 0) });

            mprToolbarButton = CreateMprToolbarButton();
            toolbar.Items.Add(mprToolbarButton);
            toolbar.Items.Add(GhostButton(CreateIcon("folder"), "3D", "3D"));
            toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(2, 0, 2, 0) });
            var panelButton = new ToolStripDropDownButton(CreateIcon("pan"))
            {
                AutoSize = false,
                Width = 26,
                Height = 22,
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                ToolTipText = "Панели"
            };
            var openMultipleSeriesItem = new ToolStripMenuItem("Открыть несколько серий", null, delegate { OpenFolder(); }) { ShortcutKeys = Keys.F4, ShowShortcutKeys = true };
            var splitSeriesItem = new ToolStripMenuItem("Разделить серию", null, delegate { MessageBox.Show(this, "Функция «Разделить серию» будет добавлена на следующем этапе.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information); }) { ShortcutKeys = Keys.Control | Keys.F5, ShowShortcutKeys = true };
            var closeActivePanelItem = new ToolStripMenuItem("Закрыть активную панель", null, delegate { CloseActivePanel(); }) { ShortcutKeys = Keys.Control | Keys.F4, ShowShortcutKeys = true };
            var closeAllPanelsItem = new ToolStripMenuItem("Закрыть все панели", null, delegate { CloseAllPanels(); }) { ShortcutKeys = Keys.Shift | Keys.F4, ShowShortcutKeys = true };
            var seriesPreviewItem = new ToolStripMenuItem("Панель предпросмотра серий", null, delegate { ToggleSeriesPreviewBar(); }) { ShortcutKeys = Keys.Control | Keys.Alt | Keys.F2, ShowShortcutKeys = true, Checked = true, CheckOnClick = false };
            var toolbarStatusItem = new ToolStripMenuItem("Панель инструментов и строка состояния", null, delegate { ToggleToolbarAndStatusBar(); }) { ShortcutKeys = Keys.Control | Keys.Alt | Keys.F3, ShowShortcutKeys = true, Checked = true, CheckOnClick = false };
            var syncCursorItem = new ToolStripMenuItem("Синхронизировать курсор/опорные линии", null, delegate
            {
                syncCursorReferenceLines = !syncCursorReferenceLines;
                if (!syncCursorReferenceLines)
                {
                    cursor3dModeEnabled = false;
                    cursor3dLockedPoint = null;
                    foreach (var panelViewer in viewerHosts.Keys)
                        panelViewer.SetReferencePoint(null);
                    if (measure3dCursorMenuItem != null)
                        measure3dCursorMenuItem.Checked = false;
                }
            }) { Checked = syncCursorReferenceLines, CheckOnClick = false };
            var syncWlItem = new ToolStripMenuItem("Синхронизировать WL/WW", null, delegate { syncWindowLevelAcrossPanels = !syncWindowLevelAcrossPanels; }) { Checked = syncWindowLevelAcrossPanels, CheckOnClick = false };
            var syncScrollItem = new ToolStripMenuItem("Синхронизировать прокрутку срезов", null, delegate { syncSliceScrollAcrossPanels = !syncSliceScrollAcrossPanels; }) { Checked = syncSliceScrollAcrossPanels, CheckOnClick = false };
            var fullScreenItem = new ToolStripMenuItem("Полноэкранный режим", null, delegate { ToggleFullScreenMode(); }) { ShortcutKeyDisplayString = "F", ShowShortcutKeys = true, CheckOnClick = false };
            var distractionItem = new ToolStripMenuItem("Режим без отвлечений", null, delegate { ToggleDistractionFreeMode(); }) { ShortcutKeys = Keys.Shift | Keys.Alt | Keys.Enter, ShowShortcutKeys = true, CheckOnClick = false };
            panelButton.DropDownItems.Add(openMultipleSeriesItem);
            panelButton.DropDownItems.Add(splitSeriesItem);
            panelButton.DropDownItems.Add(new ToolStripSeparator());
            panelButton.DropDownItems.Add(closeActivePanelItem);
            panelButton.DropDownItems.Add(closeAllPanelsItem);
            panelButton.DropDownItems.Add(new ToolStripSeparator());
            panelButton.DropDownItems.Add(seriesPreviewItem);
            panelButton.DropDownItems.Add(toolbarStatusItem);
            panelButton.DropDownItems.Add(new ToolStripSeparator());
            panelButton.DropDownItems.Add(syncCursorItem);
            panelButton.DropDownItems.Add(syncWlItem);
            panelButton.DropDownItems.Add(syncScrollItem);
            panelButton.DropDownItems.Add(new ToolStripSeparator());
            panelButton.DropDownItems.Add(fullScreenItem);
            panelButton.DropDownItems.Add(distractionItem);
            panelButton.DropDownOpening += delegate
            {
                seriesPreviewItem.Checked = mainSplit != null && !mainSplit.Panel1Collapsed;
                toolbarStatusItem.Checked = topBarHost != null && topBarHost.Visible && status.Visible;
                syncCursorItem.Checked = syncCursorReferenceLines;
                syncWlItem.Checked = syncWindowLevelAcrossPanels;
                syncScrollItem.Checked = syncSliceScrollAcrossPanels;
                fullScreenItem.Checked = fullScreenMode;
                distractionItem.Checked = distractionFreeMode;
            };
            toolbar.Items.Add(panelButton);
            UpdateCineUiState();
            topBarHost = new Panel
            {
                Dock = DockStyle.Top,
                Height = menu.Height + toolbar.Height + 1,
                BackColor = Color.FromArgb(164, 164, 164)
            };
            var toolbarBottomLine = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.FromArgb(108, 108, 108)
            };
            topBarHost.Controls.Add(toolbar);
            topBarHost.Controls.Add(menu);
            topBarHost.Controls.Add(toolbarBottomLine);

            var leftHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(88, 88, 88), Padding = new Padding(0) };
            patientHeader.Dock = DockStyle.Top;
            patientHeader.Height = 28;
            patientHeader.ForeColor = Color.White;
            patientHeader.BackColor = Color.FromArgb(74, 64, 64);
            patientHeader.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            patientHeader.TextAlign = ContentAlignment.MiddleCenter;
            patientHeader.Text = "Пациент";
            studyHeader.Dock = DockStyle.Top;
            studyHeader.Height = 66;
            studyHeader.ForeColor = Color.FromArgb(235, 235, 235);
            studyHeader.BackColor = Color.FromArgb(74, 64, 64);
            studyHeader.Font = new Font("Segoe UI", 8f);
            studyHeader.TextAlign = ContentAlignment.MiddleCenter;
            studyHeader.Text = "—";
            seriesStrip.Dock = DockStyle.Fill;
            seriesStrip.FlowDirection = FlowDirection.TopDown;
            seriesStrip.WrapContents = false;
            seriesStrip.AutoScroll = true;
            seriesStrip.BackColor = Color.FromArgb(90, 90, 90);
            seriesStrip.AllowDrop = false;
            leftHost.Controls.Add(seriesStrip);
            leftHost.Controls.Add(studyHeader);
            leftHost.Controls.Add(patientHeader);

            viewer.Dock = DockStyle.Fill;
            viewer.MinimumSize = new Size(300, 300);
            viewer.SliceScrollRequested += delegate (int delta) { OnViewerSliceScrollRequested(viewer, delta); };
            viewer.WindowLevelChanged += OnViewerWindowLevelChanged;
            viewer.ResetWindowLevelRequested += ResetWlToAuto;
            viewer.WindowLevelMenuRequested += OnViewerWindowLevelMenuRequested;
            viewer.NormalizeWindowLevelRequested += NormalizeWl;
            viewer.MeasurementContextMenuRequested += OnViewerMeasurementContextMenuRequested;
            viewer.MeasurementsChanged += OnViewerMeasurementsChanged;
            viewer.CursorImagePositionChanged += delegate (PointF? point) { OnViewerCursorPositionChanged(viewer, point); };
            viewer.TextAnnotationTextRequested += delegate (Point anchor) { return RequestTextAnnotationValue(viewer, anchor); };
            viewer.MouseDown += delegate (object sender, MouseEventArgs e) { OnViewer3dCursorMouseDown(viewer, e); };
            viewer.MouseMove += delegate (object sender, MouseEventArgs e) { OnViewerMprMouseMove(viewer, e); };
            viewer.MouseUp += delegate (object sender, MouseEventArgs e) { OnViewerMprMouseUp(viewer, e); };
            viewer.MprCrosshairMoved += delegate (PointF pt, bool moveX, bool moveY) { OnViewerMprCrosshairMoved(viewer, pt, moveX, moveY); };
            viewer.MprCrosshairAngleChanged += delegate (float angle) { OnViewerMprCrosshairAngleChanged(viewer, angle); };
            viewer.MprSlabThicknessChanged += delegate (float slabMm) { OnViewerMprSlabThicknessChanged(viewer, slabMm); };
            viewer.MprContextMenuRequested += delegate (Point screenPt) { OnViewerMprContextMenuRequested(viewer, screenPt); };
            viewer.DoubleClick += delegate { OnViewerMprDoubleClick(viewer); };
            viewer.MouseEnter += delegate { viewer.Focus(); };
            AttachActivationHandlers(viewer);
            sliceScrollBar.Dock = DockStyle.Right;
            sliceScrollBar.Width = 14;
            sliceScrollBar.Visible = false;
            sliceScrollBar.Minimum = 0;
            sliceScrollBar.SmallChange = 1;
            sliceScrollBar.LargeChange = 1;
            sliceScrollBar.ValueChanged += OnSliceScrollBarValueChanged;
            primaryViewerHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            primaryViewerHost.Controls.Add(viewer);
            primaryViewerHost.Controls.Add(sliceScrollBar);
            viewerHosts[viewer] = primaryViewerHost;
            hostViewers[primaryViewerHost] = viewer;
            AttachViewerHostDragDrop(primaryViewerHost);
            GetViewerState(viewer);
            viewerLayoutTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            SetStatusText("Перетащите DICOM-файл, папку или архив (ZIP/RAR) сюда");
            status.Dock = DockStyle.Bottom;
            status.Height = 24;
            status.BackColor = Color.FromArgb(32, 32, 32);
            status.ForeColor = Color.FromArgb(190, 190, 190);
            status.Padding = new Padding(8, 4, 0, 0);

            BuildInfoPanel();
            mainSplit = new SplitContainer { Dock = DockStyle.Fill, BackColor = Color.FromArgb(35, 35, 35) };
            mainSplit.Panel1.Controls.Add(leftHost);
            var viewerSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2, BackColor = Color.FromArgb(35, 35, 35) };
            viewerSplit.Panel1.BackColor = Color.Black;
            viewerSplit.Panel1.Controls.Add(viewerLayoutTable);
            viewerSplit.Panel2.Controls.Add(infoPanel);
            mainSplit.Panel2.Controls.Add(viewerSplit);
            Controls.Add(mainSplit);
            Controls.Add(status);
            Controls.Add(topBarHost);

            ConfigureMainSplit(mainSplit);
            // The nested SplitContainer has no useful Width while it is being
            // constructed. Set the dependent properties only after docking has
            // calculated the actual client width; otherwise SplitterDistance
            // can be validated against Width == 0 during form startup.
            ConfigureViewerSplit(viewerSplit);
            viewerSplit.Panel2Collapsed = true;
            KeyPreview = true;
            KeyDown += OnFormKeyDown;
            FormClosing += OnFormClosing;
            RefreshWlPresetCaption();
            ActivateViewerTool(ViewerTool.Pan);
            ApplyViewerLayoutPreset(1, 1, false);
            SetActiveViewer(viewer);
            RestoreLastSession();
            System.Threading.Tasks.Task.Run((Action)ZipArchiveService.CleanupOrphanedTempDirectories);
        }

        private static void ConfigureMainSplit(SplitContainer split)
        {
            split.PerformLayout();
            var availableWidth = split.Width;
            if (availableWidth <= split.SplitterWidth) return;

            var desiredDistance = availableWidth / 4;
            var maximumDistance = availableWidth - split.Panel2MinSize;
            desiredDistance = Math.Min(maximumDistance, desiredDistance);
            desiredDistance = Math.Max(split.Panel1MinSize, desiredDistance);
            split.SplitterDistance = desiredDistance;
        }

        private static void ConfigureViewerSplit(SplitContainer viewerSplit)
        {
            viewerSplit.PerformLayout();
            var availableWidth = viewerSplit.Width;
            var splitterWidth = viewerSplit.SplitterWidth;
            if (availableWidth <= splitterWidth) return;

            // Start with the unconstrained state. Panel2MinSize validates the
            // current SplitterDistance immediately, so establish a valid
            // distance before applying the final right-panel constraint.
            viewerSplit.Panel2MinSize = 0;
            var panel2MinSize = Math.Min(260, availableWidth - splitterWidth - viewerSplit.Panel1MinSize);
            panel2MinSize = Math.Max(0, panel2MinSize);

            var desiredDistance = availableWidth - panel2MinSize;
            desiredDistance = Math.Max(viewerSplit.Panel1MinSize, desiredDistance);
            viewerSplit.SplitterDistance = desiredDistance;
            viewerSplit.Panel2MinSize = panel2MinSize;
        }

        private void ApplyViewerLayoutPreset(int rows, int columns, bool persist)
        {
            CaptureStateFromWorkingSet(ActiveViewerControl);
            var preservedImages = images.ToList();
            var preservedImageIndex = imageIndex;
            var preservedCurrentImagePath = currentImagePath;
            rows = Math.Max(1, Math.Min(MaxLayoutGridSize, rows));
            columns = Math.Max(1, Math.Min(MaxLayoutGridSize, columns));
            currentViewerLayoutRows = rows;
            currentViewerLayoutColumns = columns;
            if (viewerLayoutTable == null || primaryViewerHost == null)
                return;

            viewerLayoutTable.SuspendLayout();
            viewerLayoutTable.Controls.Clear();
            viewerLayoutTable.ColumnStyles.Clear();
            viewerLayoutTable.RowStyles.Clear();
            secondaryViewers.Clear();
            viewerHosts.Clear();
            hostViewers.Clear();
            viewerStates.Clear();

            viewerLayoutTable.RowCount = rows;
            viewerLayoutTable.ColumnCount = columns;

            for (int row = 0; row < rows; row++)
                viewerLayoutTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
            for (int col = 0; col < columns; col++)
                viewerLayoutTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));

            int cellCount = rows * columns;
            for (int i = 0; i < cellCount; i++)
            {
                Control host;
                if (i == 0)
                {
                    host = primaryViewerHost;
                    viewerHosts[viewer] = primaryViewerHost;
                    hostViewers[primaryViewerHost] = viewer;
                    AttachViewerHostDragDrop(primaryViewerHost);
                }
                else
                {
                    var secondaryViewer = CreateSecondaryViewer();
                    secondaryViewers.Add(secondaryViewer);
                    var secondaryHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Margin = new Padding(0), Padding = new Padding(0) };
                    secondaryHost.Controls.Add(secondaryViewer);
                    viewerHosts[secondaryViewer] = secondaryHost;
                    hostViewers[secondaryHost] = secondaryViewer;
                    AttachViewerHostDragDrop(secondaryHost);
                    host = secondaryHost;
                }

                var row = i / columns;
                var col = i % columns;
                viewerLayoutTable.Controls.Add(host, col, row);
            }

            viewerLayoutTable.ResumeLayout();
            if (!viewerHosts.ContainsKey(ActiveViewerControl))
                activeViewer = viewer;
            AttachSliceScrollBarToActiveViewerHost();
            var primaryState = GetViewerState(viewer);
            primaryState.Images.Clear();
            primaryState.Images.AddRange(preservedImages);
            primaryState.ImageIndex = preservedImageIndex;
            primaryState.CurrentImagePath = preservedCurrentImagePath;
            SyncSecondaryViewers();
            UpdateLayoutPickerCaption();
            UpdateActiveViewerVisualState();
            viewer.Invalidate();
            foreach (var sv in secondaryViewers)
            {
                sv.Invalidate();
            }

            if (persist)
                SaveViewerLayoutPreset();
        }

        private ToolStripDropDown CreateLayoutGridDropDown()
        {
            var dropDown = new ToolStripDropDown { Padding = new Padding(1), AutoSize = false };
            var hostPanel = new Panel { Width = 150, Height = 150, BackColor = Color.FromArgb(72, 72, 72), Padding = new Padding(5), Margin = Padding.Empty };
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = MaxLayoutGridSize,
                RowCount = MaxLayoutGridSize,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.FromArgb(72, 72, 72)
            };
            layoutPreviewGrid = grid;

            for (var i = 0; i < MaxLayoutGridSize; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / MaxLayoutGridSize));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / MaxLayoutGridSize));
            }

            for (var r = 1; r <= MaxLayoutGridSize; r++)
            {
                for (var c = 1; c <= MaxLayoutGridSize; c++)
                {
                    var rr = r;
                    var cc = c;
                    var cell = new Panel { Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(112, 112, 112), Tag = rr + ";" + cc, BorderStyle = BorderStyle.None };
                    cell.MouseEnter += delegate { PaintLayoutSelection(grid, rr, cc); };
                    cell.Click += delegate
                    {
                        var wasMpr = mprModeEnabled;
                        if (wasMpr)
                            DisableMprMode(false);
                        if (orthoSeriesSyncEnabled && rr == 1 && cc == 1)
                            DisableOrthoSeriesSync(false);
                        ApplyViewerLayoutPreset(rr, cc, true);
                        if (wasMpr && images.Count > 0)
                            ShowImage(Math.Max(0, Math.Min(imageIndex, images.Count - 1)));
                        dropDown.Close();
                    };
                    grid.Controls.Add(cell, c - 1, r - 1);
                }
            }

            grid.Paint += delegate (object sender, PaintEventArgs e)
            {
                DrawLayoutSelectionBorder(grid, e.Graphics);
            };
            grid.MouseLeave += delegate { PaintLayoutSelection(grid, currentViewerLayoutRows, currentViewerLayoutColumns); };

            hostPanel.Controls.Add(grid);
            var host = new ToolStripControlHost(hostPanel) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = hostPanel.Size };
            dropDown.Items.Add(host);
            dropDown.Size = new Size(hostPanel.Width + 2, hostPanel.Height + 2);
            dropDown.Opening += delegate
            {
                PaintLayoutSelection(grid, currentViewerLayoutRows, currentViewerLayoutColumns);
            };

            return dropDown;
        }

        private static void PaintLayoutSelection(TableLayoutPanel grid, int rows, int columns)
        {
            foreach (Control control in grid.Controls)
            {
                var panel = control as Panel;
                if (panel == null)
                    continue;
                var parts = (panel.Tag as string ?? string.Empty).Split(';');
                int r;
                int c;
                if (parts.Length != 2 || !int.TryParse(parts[0], out r) || !int.TryParse(parts[1], out c))
                    continue;
                panel.BackColor = r <= rows && c <= columns ? Color.FromArgb(198, 198, 198) : Color.FromArgb(112, 112, 112);
            }

            grid.Tag = rows + ";" + columns;
            grid.Invalidate();
        }

        private static void DrawLayoutSelectionBorder(TableLayoutPanel grid, Graphics graphics)
        {
            if (grid == null)
                return;

            var raw = grid.Tag as string;
            if (string.IsNullOrWhiteSpace(raw))
                return;

            var parts = raw.Split(';');
            int rows;
            int columns;
            if (parts.Length != 2 || !int.TryParse(parts[0], out rows) || !int.TryParse(parts[1], out columns))
                return;

            rows = Math.Max(1, Math.Min(MaxLayoutGridSize, rows));
            columns = Math.Max(1, Math.Min(MaxLayoutGridSize, columns));

            var first = grid.GetControlFromPosition(0, 0);
            var last = grid.GetControlFromPosition(columns - 1, rows - 1);
            if (first == null || last == null)
                return;

            var left = first.Left - 1;
            var top = first.Top - 1;
            var right = last.Right;
            var bottom = last.Bottom;
            var rect = Rectangle.FromLTRB(left, top, right, bottom);

            using (var pen = new Pen(Color.FromArgb(230, 235, 235, 235), 1f))
                graphics.DrawRectangle(pen, rect);
        }

        private void UpdateLayoutPickerCaption()
        {
            if (layoutPickerButton == null)
                return;
            layoutPickerButton.Text = "▦";
            layoutPickerButton.ToolTipText = string.Format("Раскладка: {0}×{1}", currentViewerLayoutRows, currentViewerLayoutColumns);
        }

        private DicomImageControl CreateSecondaryViewer()
        {
            var secondaryViewer = new DicomImageControl
            {
                Dock = DockStyle.Fill,
                MinimumSize = new Size(220, 220),
                Tool = ActiveViewerControl.Tool,
                MeasurementMode = ActiveViewerControl.MeasurementMode,
                Inverted = ActiveViewerControl.Inverted
            };
            secondaryViewer.SliceScrollRequested += delegate (int delta) { OnViewerSliceScrollRequested(secondaryViewer, delta); };
            secondaryViewer.WindowLevelChanged += OnViewerWindowLevelChanged;
            secondaryViewer.ResetWindowLevelRequested += ResetWlToAuto;
            secondaryViewer.WindowLevelMenuRequested += OnViewerWindowLevelMenuRequested;
            secondaryViewer.NormalizeWindowLevelRequested += NormalizeWl;
            secondaryViewer.MeasurementContextMenuRequested += OnViewerMeasurementContextMenuRequested;
            secondaryViewer.MeasurementsChanged += OnViewerMeasurementsChanged;
            secondaryViewer.CursorImagePositionChanged += delegate (PointF? point) { OnViewerCursorPositionChanged(secondaryViewer, point); };
            secondaryViewer.TextAnnotationTextRequested += delegate (Point anchor) { return RequestTextAnnotationValue(secondaryViewer, anchor); };
            secondaryViewer.MouseDown += delegate (object sender, MouseEventArgs e) { OnViewer3dCursorMouseDown(secondaryViewer, e); };
            secondaryViewer.MouseMove += delegate (object sender, MouseEventArgs e) { OnViewerMprMouseMove(secondaryViewer, e); };
            secondaryViewer.MouseUp += delegate (object sender, MouseEventArgs e) { OnViewerMprMouseUp(secondaryViewer, e); };
            secondaryViewer.MprCrosshairMoved += delegate (PointF pt, bool moveX, bool moveY) { OnViewerMprCrosshairMoved(secondaryViewer, pt, moveX, moveY); };
            secondaryViewer.MprCrosshairAngleChanged += delegate (float angle) { OnViewerMprCrosshairAngleChanged(secondaryViewer, angle); };
            secondaryViewer.MprSlabThicknessChanged += delegate (float slabMm) { OnViewerMprSlabThicknessChanged(secondaryViewer, slabMm); };
            secondaryViewer.MprContextMenuRequested += delegate (Point screenPt) { OnViewerMprContextMenuRequested(secondaryViewer, screenPt); };
            secondaryViewer.DoubleClick += delegate { OnViewerMprDoubleClick(secondaryViewer); };
            secondaryViewer.MouseEnter += delegate { secondaryViewer.Focus(); };
            AttachActivationHandlers(secondaryViewer);
            GetViewerState(secondaryViewer);
            return secondaryViewer;
        }

        private void AttachViewerHostDragDrop(Panel host)
        {
            if (host == null)
                return;
            host.AllowDrop = true;
            host.DragEnter -= OnViewerHostDragEnter;
            host.DragOver -= OnViewerHostDragOver;
            host.DragLeave -= OnViewerHostDragLeave;
            host.DragDrop -= OnViewerHostDragDrop;
            host.DragEnter += OnViewerHostDragEnter;
            host.DragOver += OnViewerHostDragOver;
            host.DragLeave += OnViewerHostDragLeave;
            host.DragDrop += OnViewerHostDragDrop;
        }

        private bool TryGetSeriesFromDragData(IDataObject dataObject, out DicomSeries series)
        {
            series = null;
            if (dataObject == null)
                return false;

            if (dataObject.GetDataPresent(SeriesDragDataFormat))
                series = dataObject.GetData(SeriesDragDataFormat) as DicomSeries;
            if (series == null && dataObject.GetDataPresent(typeof(DicomSeries)))
                series = dataObject.GetData(typeof(DicomSeries)) as DicomSeries;
            return series != null && series.Images != null && series.Images.Count > 0;
        }

        private void OnViewerHostDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                return;
            }

            var host = sender as Panel;
            DicomSeries series;
            if (host != null && TryGetSeriesFromDragData(e.Data, out series))
            {
                e.Effect = DragDropEffects.Copy;
                SetHoverViewerHost(host);
                return;
            }

            e.Effect = DragDropEffects.None;
            SetHoverViewerHost(null);
        }

        private void OnViewerHostDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                return;
            }

            var host = sender as Panel;
            DicomSeries series;
            if (host != null && TryGetSeriesFromDragData(e.Data, out series))
            {
                e.Effect = DragDropEffects.Copy;
                SetHoverViewerHost(host);
            }
            else
            {
                e.Effect = DragDropEffects.None;
                SetHoverViewerHost(null);
            }
        }

        private void OnViewerHostDragLeave(object sender, EventArgs e)
        {
            SetHoverViewerHost(null);
        }

        private void OnViewerHostDragDrop(object sender, DragEventArgs e)
        {
            SetHoverViewerHost(null);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    LoadPath(files[0]);
                    e.Effect = DragDropEffects.Copy;
                    return;
                }
            }

            var host = sender as Panel;
            DicomSeries series;
            if (host == null || !TryGetSeriesFromDragData(e.Data, out series))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            DicomImageControl targetViewer;
            if (!hostViewers.TryGetValue(host, out targetViewer) || targetViewer == null)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            AssignSeriesToViewer(targetViewer, series);
            e.Effect = DragDropEffects.Copy;
        }

        private void AssignSeriesToViewer(DicomImageControl targetViewer, DicomSeries series)
        {
            if (targetViewer == null || series == null || series.Images == null || series.Images.Count == 0)
                return;

            SaveCurrentImageMeasurements();
            CaptureStateFromWorkingSet(ActiveViewerControl);

            var targetState = GetViewerState(targetViewer);
            targetState.Images.Clear();
            targetState.Images.AddRange(series.Images);
            targetState.ImageIndex = 0;
            targetState.CurrentImagePath = null;
            targetState.CinePlaying = false;
            targetState.CineLoopStart = null;
            targetState.CineLoopEnd = null;
            targetState.AssignedSeries = series;
            targetState.AssignedSyncPlane = series.Plane;

            SetActiveViewer(targetViewer);
            SelectSeries(series, FindSeriesCard(series));
            if (orthoSeriesSyncEnabled)
            {
                ConfigureViewerOrthoSyncAppearance(targetViewer, series.Plane);
                SyncAllOrthoPanelsToCurrentPoint(targetViewer, true);
            }
        }

        private void SyncSecondaryViewers()
        {
            var sourceViewer = ActiveViewerControl;
            foreach (var targetViewer in viewerHosts.Keys)
            {
                if (targetViewer == sourceViewer)
                    continue;

                var changed = false;

                if (targetViewer.Tool != sourceViewer.Tool)
                {
                    targetViewer.Tool = sourceViewer.Tool;
                    changed = true;
                }

                if (targetViewer.MeasurementMode != sourceViewer.MeasurementMode)
                {
                    targetViewer.MeasurementMode = sourceViewer.MeasurementMode;
                    changed = true;
                }

                ApplyViewerCursor(targetViewer);

                if (targetViewer.Inverted != sourceViewer.Inverted)
                {
                    targetViewer.Inverted = sourceViewer.Inverted;
                    changed = true;
                }

                if (syncWindowLevelAcrossPanels || mprModeEnabled)
                {
                    var wlChanged = Math.Abs(targetViewer.WindowCenter - currentAppliedWlCenter) > 0.001f
                        || Math.Abs(targetViewer.WindowWidth - currentAppliedWlWidth) > 0.001f
                        || targetViewer.WindowLevelEnabled != sourceViewer.WindowLevelEnabled;
                    if (wlChanged)
                    {
                        targetViewer.SetWindowLevel(currentAppliedWlCenter, currentAppliedWlWidth, sourceViewer.WindowLevelEnabled);
                        changed = true;
                    }
                }

                if (changed)
                    targetViewer.Invalidate();
            }
        }

        private static Bitmap CreateSafeBitmapCopy(Image source)
        {
            if (source == null)
                return null;

            var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(copy))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
            }
            return copy;
        }

        private void UpdateRenderedImageCache(Image source)
        {
            if (currentRenderedImage != null)
            {
                try { currentRenderedImage.Dispose(); } catch { }
                currentRenderedImage = null;
            }
            if (source != null)
                currentRenderedImage = CreateSafeBitmapCopy(source);
        }

        private void SyncSecondaryViewerImages()
        {
            foreach (var secondaryViewer in secondaryViewers)
            {
                var safeCopy = currentRenderedImage != null ? CreateSafeBitmapCopy(currentRenderedImage) : null;
                secondaryViewer.SetImage(safeCopy, true);
            }
        }

        private void OnViewerCursorPositionChanged(DicomImageControl sourceViewer, PointF? imagePoint)
        {
            if ((mprModeEnabled && currentMprVolume != null) || orthoSeriesSyncEnabled)
                return;

            if (!syncCursorReferenceLines)
                return;

            if (cursor3dModeEnabled)
            {
                if (!cursor3dLockedPoint.HasValue)
                    return;
                foreach (var targetViewer in viewerHosts.Keys)
                    targetViewer.SetReferencePoint(cursor3dLockedPoint);
                return;
            }

            foreach (var targetViewer in viewerHosts.Keys)
            {
                if (targetViewer == sourceViewer)
                {
                    targetViewer.SetReferencePoint(null);
                    continue;
                }

                targetViewer.SetReferencePoint(imagePoint);
            }
        }

        private void Toggle3dCursorMode()
        {
            cursor3dModeEnabled = !cursor3dModeEnabled;
            if (measure3dCursorMenuItem != null)
                measure3dCursorMenuItem.Checked = cursor3dModeEnabled;

            if (mprModeEnabled && currentMprVolume != null)
            {
                UpdateAllMprCrosshairsAndOverlays();
                return;
            }

            if (orthoSeriesSyncEnabled)
            {
                UpdateAllOrthoSyncCrosshairsAndOverlays();
                return;
            }

            if (cursor3dModeEnabled)
            {
                syncCursorReferenceLines = true;
                return;
            }

            cursor3dLockedPoint = null;
            foreach (var panelViewer in viewerHosts.Keys)
                panelViewer.SetReferencePoint(null);
        }

        private void OnViewer3dCursorMouseDown(DicomImageControl sourceViewer, MouseEventArgs e)
        {
            if (mprModeEnabled && currentMprVolume != null)
            {
                if (e.Button == MouseButtons.Left && sourceViewer != null && !sourceViewer.IsMprCrosshairDragActive && (cursor3dModeEnabled || (ModifierKeys & Keys.Control) == Keys.Control))
                {
                    mprCrosshairDragging = true;
                    UpdateMprCrosshairFromScreenPoint(sourceViewer, e.Location);
                }
                return;
            }

            if (orthoSeriesSyncEnabled)
            {
                if (e.Button == MouseButtons.Left && sourceViewer != null && !sourceViewer.IsMprCrosshairDragActive && (cursor3dModeEnabled || (ModifierKeys & Keys.Control) == Keys.Control))
                {
                    orthoSyncCrosshairDragging = true;
                    UpdateOrthoSyncFromScreenPoint(sourceViewer, e.Location);
                }
                return;
            }

            if (!cursor3dModeEnabled || e.Button != MouseButtons.Left || sourceViewer == null)
                return;

            PointF imagePoint;
            if (!sourceViewer.TryGetImagePointAt(e.Location, out imagePoint))
                return;

            cursor3dLockedPoint = imagePoint;
            foreach (var targetViewer in viewerHosts.Keys)
                targetViewer.SetReferencePoint(imagePoint);
        }

        private void BuildInfoPanel()
        {
            infoPanel.Dock = DockStyle.Fill;
            infoPanel.BackColor = Color.FromArgb(30, 30, 30);
            infoPanel.Padding = new Padding(10, 0, 10, 8);
            rightTabControl.Dock = DockStyle.Fill;
            rightTabControl.Appearance = TabAppearance.Normal;
            rightTabControl.Font = new Font("Segoe UI", 8.5f);
            var infoTab = new TabPage("DICOM") { BackColor = Color.FromArgb(30, 30, 30) };
            var measureTab = new TabPage("Измерения") { BackColor = Color.FromArgb(30, 30, 30) };

            var header = new Label { Dock = DockStyle.Top, Height = 36, Text = "ИНФОРМАЦИЯ DICOM", ForeColor = Color.FromArgb(235, 235, 235), BackColor = Color.FromArgb(45, 45, 45), Font = new Font("Segoe UI", 9f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
            infoTab.Controls.Add(header);
            infoTable.Dock = DockStyle.Fill;
            infoTable.AutoScroll = true;
            infoTable.ColumnCount = 2;
            infoTable.ColumnStyles.Clear();
            infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
            infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
            infoTable.Padding = new Padding(0, 8, 0, 0);
            infoTab.Controls.Add(infoTable);

            var measureButtons = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(45, 45, 45), Padding = new Padding(6, 4, 6, 4) };
            var editButton = new Button { Text = "Свойства", Width = 90, Dock = DockStyle.Left };
            var deleteButton = new Button { Text = "Удалить", Width = 90, Dock = DockStyle.Left, Margin = new Padding(6, 0, 0, 0) };
            editButton.Click += delegate
            {
                var id = GetSelectedMeasurementIdFromList();
                if (id.HasValue)
                    OpenMeasurementPropertiesDialog(id.Value);
            };
            deleteButton.Click += delegate
            {
                var id = GetSelectedMeasurementIdFromList();
                if (!id.HasValue)
                    return;
                DeleteMeasurementAndSelectNext(id.Value);
            };
            measureButtons.Controls.Add(deleteButton);
            measureButtons.Controls.Add(editButton);

            measurementsListView.Dock = DockStyle.Fill;
            measurementsListView.BackColor = Color.FromArgb(25, 25, 25);
            measurementsListView.ForeColor = Color.FromArgb(235, 235, 235);
            measurementsListView.FullRowSelect = true;
            measurementsListView.View = View.Details;
            measurementsListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            measurementsListView.Columns.Clear();
            measurementsListView.Columns.Add("Тип", 90);
            measurementsListView.Columns.Add("Значение", 260);
            measurementsListView.MultiSelect = false;
            measurementsListView.DoubleClick += delegate
            {
                var id = GetSelectedMeasurementIdFromList();
                if (id.HasValue)
                    OpenMeasurementPropertiesDialog(id.Value);
            };

            var listMenu = new ContextMenu();
            listMenu.MenuItems.Add(new MenuItem("Свойства измерения...", delegate
            {
                var id = GetSelectedMeasurementIdFromList();
                if (id.HasValue)
                    OpenMeasurementPropertiesDialog(id.Value);
            }));
            listMenu.MenuItems.Add("-");
            listMenu.MenuItems.Add(new MenuItem("Удалить измерение", delegate
            {
                var id = GetSelectedMeasurementIdFromList();
                if (id.HasValue)
                    DeleteMeasurementAndSelectNext(id.Value);
            }));
            listMenu.MenuItems.Add("-");
            listMenu.MenuItems.Add(new MenuItem("Экспорт в CSV...", delegate { ExportMeasurementsToCsv(); }));
            listMenu.MenuItems.Add(new MenuItem("Экспорт в PDF...", delegate { ExportMeasurementsToPdf(); }));
            measurementsListView.ContextMenu = listMenu;

            measureTab.Controls.Add(measurementsListView);
            measureTab.Controls.Add(measureButtons);

            rightTabControl.TabPages.Clear();
            rightTabControl.TabPages.Add(infoTab);
            rightTabControl.TabPages.Add(measureTab);
            infoPanel.Controls.Add(rightTabControl);
            AddInfoRow("Пациент", "patient");
            AddInfoRow("ID", "patientId");
            AddInfoRow("Дата", "studyDate");
            AddInfoRow("Исследование", "studyDescription");
            AddInfoRow("Модальность", "modality");
            AddInfoRow("Серия", "seriesDescription");
            AddInfoRow("№ серии", "seriesNumber");
            AddInfoRow("Изображение", "instanceNumber");
            AddInfoRow("Размер", "dimensions");
            AddInfoRow("Файл", "file");
            UpdateInfoValues("Нет открытого изображения", "—", "—", "—", "—", "—", "—", "—", "—", "—");
            RefreshMeasurementsList();
            infoTable.AutoScrollPosition = Point.Empty;
        }

        private void AddInfoRow(string title, string key)
        {
            var row = infoTable.RowCount++;
            infoTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            infoTable.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(150, 150, 150), Padding = new Padding(0, 4, 4, 4), AutoSize = true }, 0, row);
            var value = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(235, 235, 235), Padding = new Padding(0, 4, 0, 4), AutoSize = true, MaximumSize = new Size(210, 0) };
            infoTable.Controls.Add(value, 1, row);
            infoValues[key] = value;
        }

        private void UpdateInfoValues(string patient, string patientId, string studyDate, string studyDescription, string modality, string seriesDescription, string seriesNumber, string instanceNumber, string dimensions, string file)
        {
            infoValues["patient"].Text = patient;
            infoValues["patientId"].Text = patientId;
            infoValues["studyDate"].Text = studyDate;
            infoValues["studyDescription"].Text = studyDescription;
            infoValues["modality"].Text = modality;
            infoValues["seriesDescription"].Text = seriesDescription;
            infoValues["seriesNumber"].Text = seriesNumber;
            infoValues["instanceNumber"].Text = instanceNumber;
            infoValues["dimensions"].Text = dimensions;
            infoValues["file"].Text = file;
            infoTable.AutoScrollPosition = Point.Empty;
        }

        private static ToolStripButton ToolButton(Image image, string tooltip, EventHandler click)
        {
            var button = new ToolStripButton();
            StyleButton(button, image, tooltip);
            button.Click += click;
            return button;
        }

        private ToolStripButton GhostButton(Image image, string tooltip, string featureName)
        {
            var button = new ToolStripButton();
            StyleButton(button, image, tooltip);
            button.ToolTipText = tooltip + " (скоро)";
            button.ForeColor = Color.FromArgb(110, 110, 110);
            button.Click += delegate { ShowFeatureComingSoon(featureName); };
            return button;
        }

        private ToolStripMenuItem PlaceholderMenuItem(string text, string featureName)
        {
            var item = new ToolStripMenuItem(text + " (скоро)");
            item.ForeColor = Color.FromArgb(110, 110, 110);
            item.Click += delegate { ShowFeatureComingSoon(featureName); };
            return item;
        }

        private void ShowFeatureComingSoon(string featureName)
        {
            var title = string.IsNullOrWhiteSpace(featureName) ? "Этот инструмент" : featureName;
            MessageBox.Show(this, title + " будет доступен в следующем этапе.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void StyleButton(ToolStripButton button, Image image, string tooltip)
        {
            button.Image = image;
            button.Text = string.Empty;
            button.ToolTipText = tooltip;
            button.AccessibleName = tooltip;
            button.DisplayStyle = ToolStripItemDisplayStyle.Image;
            button.AutoSize = false;
            button.Width = 26;
            button.Height = 22;
            button.Margin = new Padding(1, 0, 1, 0);
            button.Padding = new Padding(1);
            button.BackColor = Color.FromArgb(164, 164, 164);
            button.ForeColor = Color.FromArgb(36, 36, 36);
        }

        private string GetInitialDialogDirectory()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(lastLoadedPath))
                {
                    if (Directory.Exists(lastLoadedPath))
                        return lastLoadedPath;
                    if (File.Exists(lastLoadedPath))
                    {
                        var dir = Path.GetDirectoryName(lastLoadedPath);
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                            return dir;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private void OpenFile()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Открыть DICOM или архив",
                Filter = "Все поддерживаемые файлы|DICOMDIR;*.dcm;*.dicom;*.ima;*.zip;*.rar;*.jpg;*.jpeg;*.png|Архивы (*.zip;*.rar)|*.zip;*.rar|DICOM файлы (*.dcm;*.dicom;*.ima;DICOMDIR)|*.dcm;*.dicom;*.ima;DICOMDIR|Изображения (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|Все файлы (*.*)|*.*",
                CheckFileExists = true
            })
            {
                var initialDir = GetInitialDialogDirectory();
                if (!string.IsNullOrWhiteSpace(initialDir))
                    dialog.InitialDirectory = initialDir;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    LoadPath(dialog.FileName);
            }
        }

        private void OpenFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                var initialDir = GetInitialDialogDirectory();
                if (!string.IsNullOrWhiteSpace(initialDir))
                    dialog.SelectedPath = initialDir;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    LoadPath(dialog.SelectedPath);
            }
        }

        private void OpenZipFile()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Открыть ZIP-архив с DICOM",
                Filter = "ZIP архивы (*.zip)|*.zip|Все файлы (*.*)|*.*",
                CheckFileExists = true
            })
            {
                var initialDir = GetInitialDialogDirectory();
                if (!string.IsNullOrWhiteSpace(initialDir))
                    dialog.InitialDirectory = initialDir;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    LoadPath(dialog.FileName);
            }
        }

        private void OpenRarFile()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Открыть RAR-архив с DICOM",
                Filter = "RAR архивы (*.rar)|*.rar|Все файлы (*.*)|*.*",
                CheckFileExists = true
            })
            {
                var initialDir = GetInitialDialogDirectory();
                if (!string.IsNullOrWhiteSpace(initialDir))
                    dialog.InitialDirectory = initialDir;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    LoadPath(dialog.FileName);
            }
        }

        private void OpenDicomDir()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Открыть DICOMDIR",
                Filter = "Файл DICOMDIR|DICOMDIR;DICOMDIR.*|Все файлы (*.*)|*.*",
                CheckFileExists = true
            })
            {
                var initialDir = GetInitialDialogDirectory();
                if (!string.IsNullOrWhiteSpace(initialDir))
                    dialog.InitialDirectory = initialDir;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    LoadPath(dialog.FileName);
                }
            }
        }

        private void ExportCurrentImage()
        {
            var targetViewer = ActiveViewerControl;
            if (targetViewer == null || (images.Count == 0 && (!mprModeEnabled || currentMprVolume == null)))
            {
                MessageBox.Show(this, "Нет открытого изображения для экспорта.", "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            targetViewer.CommitPendingMeasurement();
            SaveCurrentImageMeasurements();

            var defaultBaseName = mprModeEnabled && currentMprVolume != null
                ? string.Format(CultureInfo.InvariantCulture, "MPR-{0}", GetViewerState(targetViewer).AssignedMprPlane ?? MprPlane.Axial)
                : Path.GetFileNameWithoutExtension((currentImagePath ?? "dicom-image").Replace(':', '-'));

            using (var dialog = new SaveFileDialog
            {
                Title = "Экспорт снимка",
                Filter = "PNG изображение (*.png)|*.png|JPEG изображение (*.jpg;*.jpeg)|*.jpg|BMP изображение (*.bmp)|*.bmp",
                FileName = defaultBaseName + ".png"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                    var format = ImageFormat.Png;
                    if (ext == ".jpg" || ext == ".jpeg") format = ImageFormat.Jpeg;
                    else if (ext == ".bmp") format = ImageFormat.Bmp;

                    using (var bitmap = new Bitmap(targetViewer.Width, targetViewer.Height))
                    {
                        targetViewer.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        ExportService.ExportImageToFile(bitmap, dialog.FileName, format);
                    }

                    MessageBox.Show(this, "Изображение успешно экспортировано.", "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Ошибка экспорта: " + ex.Message, "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportMprCollage()
        {
            if (!mprModeEnabled || currentMprVolume == null || viewerLayoutTable == null)
            {
                MessageBox.Show(this, "Включите режим MPR для экспорта коллажа проекций.", "Экспорт коллажа MPR", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (ActiveViewerControl != null)
                ActiveViewerControl.CommitPendingMeasurement();
            SaveCurrentImageMeasurements();

            using (var dialog = new SaveFileDialog
            {
                Title = "Экспорт коллажа MPR (все проекции)",
                Filter = "PNG изображение (*.png)|*.png|JPEG изображение (*.jpg;*.jpeg)|*.jpg|BMP изображение (*.bmp)|*.bmp",
                FileName = "MPR-Collage-2x2.png"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                    var format = ImageFormat.Png;
                    if (ext == ".jpg" || ext == ".jpeg") format = ImageFormat.Jpeg;
                    else if (ext == ".bmp") format = ImageFormat.Bmp;

                    var ordered = GetOrderedViewers();
                    var cellW = Math.Max(480, viewerLayoutTable.Width / 2);
                    var cellH = Math.Max(420, viewerLayoutTable.Height / 2);
                    const int sep = 2;
                    using (var collage = new Bitmap(cellW * 2 + sep, cellH * 2 + sep, PixelFormat.Format24bppRgb))
                    using (var g = Graphics.FromImage(collage))
                    {
                        g.Clear(Color.FromArgb(28, 32, 38));
                        for (int i = 0; i < ordered.Count && i < 4; i++)
                        {
                            var v = ordered[i];
                            if (v == null || v.Width <= 0 || v.Height <= 0)
                                continue;

                            var r = i / 2;
                            var c = i % 2;
                            var destX = c * (cellW + sep);
                            var destY = r * (cellH + sep);
                            using (var panelBmp = new Bitmap(v.Width, v.Height))
                            {
                                v.DrawToBitmap(panelBmp, new Rectangle(0, 0, panelBmp.Width, panelBmp.Height));
                                g.DrawImage(panelBmp, new Rectangle(destX, destY, cellW, cellH));
                            }
                        }

                        ExportService.ExportImageToFile(collage, dialog.FileName, format);
                    }

                    MessageBox.Show(this, "Коллаж MPR (2×2) успешно экспортирован.", "Экспорт коллажа MPR", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Ошибка экспорта коллажа MPR: " + ex.Message, "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportMprSeriesToFolder()
        {
            if (!mprModeEnabled || currentMprVolume == null)
            {
                MessageBox.Show(this, "Включите режим MPR для экспорта серии срезов.", "Экспорт серии MPR", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var targetViewer = ActiveViewerControl ?? viewer;
            var st = GetViewerState(targetViewer);
            var plane = st.AssignedMprPlane ?? MprPlane.Axial;
            var mode = GetViewerMprRenderMode(targetViewer, plane);
            var slab = GetViewerMprSlabThickness(targetViewer, plane);
            var totalSlices = GetMprPlaneSliceCount(plane);

            using (var folderDialog = new FolderBrowserDialog
            {
                Description = string.Format("Выберите папку для экспорта серии {0} ({1} срезов, режим {2})", GetMprPlaneTitle(plane), totalSlices, GetMprRenderModeTitle(mode)),
                ShowNewFolderButton = true
            })
            {
                var initDir = GetInitialDialogDirectory();
                if (!string.IsNullOrWhiteSpace(initDir) && Directory.Exists(initDir))
                    folderDialog.SelectedPath = initDir;

                if (folderDialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
                    return;

                var prevCursor = Cursor;
                Cursor = Cursors.WaitCursor;
                try
                {
                    var outDir = Path.Combine(folderDialog.SelectedPath, string.Format(CultureInfo.InvariantCulture, "MPR_{0}_{1}_{2:yyyyMMdd_HHmmss}", plane, mode, DateTime.Now));
                    Directory.CreateDirectory(outDir);

                    var wc = currentAppliedWlCenter;
                    var ww = currentAppliedWlWidth <= 0f ? currentMprVolume.DefaultWindowWidth : currentAppliedWlWidth;
                    var inverted = targetViewer.Inverted;

                    for (int s = 0; s < totalSlices; s++)
                    {
                        if (s % 10 == 0 || s == totalSlices - 1)
                        {
                            SetStatusText(string.Format("Экспорт серии MPR ({0}): {1} / {2}...", plane, s + 1, totalSlices));
                            Application.DoEvents();
                        }

                        var provider = currentMprVolume.ExtractPlaneProvider(
                            plane,
                            s,
                            mode,
                            slab,
                            mprObliqueAngleDegrees,
                            mprAngleCoronalDegrees,
                            mprAngleSagittalDegrees,
                            mprVoxelX,
                            mprVoxelY,
                            mprVoxelZ);
                        if (provider == null)
                            continue;

                        using (var bmp = provider.RenderBitmap(wc, ww, inverted))
                        {
                            if (bmp == null)
                                continue;
                            var fileName = Path.Combine(outDir, string.Format(CultureInfo.InvariantCulture, "{0}_{1:D4}.png", plane, s + 1));
                            ExportService.ExportImageToFile(bmp, fileName, ImageFormat.Png);
                        }
                    }

                    SetStatusText(string.Format("Экспортировано срезов MPR: {0} в папку {1}", totalSlices, Path.GetFileName(outDir)));
                    MessageBox.Show(
                        this,
                        string.Format("Серия MPR ({0}, {1} срезов) успешно сохранена в папку:\n{2}", GetMprPlaneTitle(plane), totalSlices, outDir),
                        "Экспорт серии MPR",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Ошибка экспорта серии MPR: " + ex.Message, "Экспорт серии MPR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    Cursor = prevCursor ?? Cursors.Default;
                }
            }
        }

        private void ExportMeasurementsToCsv()
        {
            if (ActiveViewerControl != null)
                ActiveViewerControl.CommitPendingMeasurement();
            SaveCurrentImageMeasurements();
            if (measurementsByImagePath.Count == 0)
            {
                MessageBox.Show(this, "Нет измерений для экспорта.", "Экспорт CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new SaveFileDialog
            {
                Title = "Экспорт таблицы измерений в CSV",
                Filter = "CSV файл (*.csv)|*.csv",
                FileName = "DicomVUZB-Measurements.csv"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    ExportService.ExportMeasurementsToCsv(dialog.FileName, measurementsByImagePath);
                    MessageBox.Show(this, "Таблица измерений успешно сохранена в CSV.", "Экспорт CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Ошибка экспорта CSV: " + ex.Message, "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportMeasurementsToPdf()
        {
            if (ActiveViewerControl != null)
                ActiveViewerControl.CommitPendingMeasurement();
            SaveCurrentImageMeasurements();
            if (measurementsByImagePath.Count == 0)
            {
                MessageBox.Show(this, "Нет измерений для экспорта.", "Экспорт PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new SaveFileDialog
            {
                Filter = "PDF документ (*.pdf)|*.pdf",
                FileName = "DicomVUZB-Measurements.pdf",
                Title = "Сохранить отчёт измерений"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    PdfReportService.GenerateReport(
                        dialog.FileName,
                        measurementsByImagePath,
                        (path, items) => CreateMeasurementSnapshot(path, items));

                    MessageBox.Show(this, "PDF со снимками и измерениями успешно сохранён.", "Экспорт PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Ошибка экспорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void SaveViewerLayoutPreset()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(viewerLayoutPresetPath));
                File.WriteAllText(viewerLayoutPresetPath, string.Format(CultureInfo.InvariantCulture, "{0};{1}", currentViewerLayoutRows, currentViewerLayoutColumns));
            }
            catch
            {
            }
        }

        private Tuple<int, int> LoadViewerLayoutPreset()
        {
            try
            {
                if (!File.Exists(viewerLayoutPresetPath))
                    return Tuple.Create(1, 1);

                var raw = File.ReadAllText(viewerLayoutPresetPath).Trim();
                var parts = raw.Split(';');
                int rows;
                int columns;
                if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out rows) && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out columns))
                    return Tuple.Create(Math.Max(1, Math.Min(MaxLayoutGridSize, rows)), Math.Max(1, Math.Min(MaxLayoutGridSize, columns)));

                if (string.Equals(raw, "OneByTwo", StringComparison.OrdinalIgnoreCase))
                    return Tuple.Create(1, 2);
                if (string.Equals(raw, "TwoByTwo", StringComparison.OrdinalIgnoreCase))
                    return Tuple.Create(2, 2);
                return Tuple.Create(1, 1);
            }
            catch
            {
                return Tuple.Create(1, 1);
            }
        }

        private static string BuildMeasurementReportLine(DicomMeasurement measurement)
        {
            return PdfReportService.BuildMeasurementReportLine(measurement);
        }

        private static RectangleF BuildMeasurementRect(PointF p1, PointF p2)
        {
            var x = Math.Min(p1.X, p2.X);
            var y = Math.Min(p1.Y, p2.Y);
            var w = Math.Abs(p2.X - p1.X);
            var h = Math.Abs(p2.Y - p1.Y);
            return new RectangleF(x, y, w, h);
        }

        private Bitmap CreateMeasurementSnapshot(string imagePath, List<DicomMeasurement> measurements)
        {
            Image source = null;
            try
            {
                MprPlane mprPlane;
                int mprSliceIndex;
                if (TryParseMprMeasurementKey(imagePath, out mprPlane, out mprSliceIndex) && currentMprVolume != null)
                {
                    var modeForPlane = mprPlane == MprPlane.MipCoronal
                        ? (currentMprRenderMode == MprRenderMode.MinIp ? MprRenderMode.MinIp : MprRenderMode.Mip)
                        : currentMprRenderMode;
                    var slabForPlane = mprPlane == MprPlane.MipCoronal
                        ? Math.Max(15f, currentMprSlabThicknessMm)
                        : currentMprSlabThicknessMm;
                    var provider = currentMprVolume.ExtractPlaneProvider(
                        mprPlane,
                        mprSliceIndex,
                        modeForPlane,
                        slabForPlane,
                        mprObliqueAngleDegrees,
                        mprAngleCoronalDegrees,
                        mprAngleSagittalDegrees,
                        mprVoxelX,
                        mprVoxelY,
                        mprVoxelZ);
                    if (provider != null)
                    {
                        var wc = currentAppliedWlCenter;
                        var ww = currentAppliedWlWidth <= 0f ? currentMprVolume.DefaultWindowWidth : currentAppliedWlWidth;
                        source = provider.RenderBitmap(wc, ww, false);
                    }
                }
                else if (IsRaster(imagePath))
                {
                    using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var raw = Image.FromStream(stream))
                        source = CreateSafeBitmapCopy(raw);
                }
                else
                {
                    var dataset = DicomFile.Open(imagePath).Dataset;
                    var provider = DicomPixelDataProvider.Create(dataset, 0);
                    if (provider != null)
                    {
                        source = provider.RenderBitmap(provider.DefaultWindowCenter, provider.DefaultWindowWidth, false);
                        if (measurements != null)
                        {
                            foreach (var m in measurements)
                            {
                                if ((m.Mode == MeasurementMode.RoiRectangle || m.Mode == MeasurementMode.RoiCircle)
                                    && (!m.RoiArea.HasValue || !m.RoiMean.HasValue))
                                {
                                    float? area, min, max, mean, sd;
                                    provider.CalculateRoiStatistics(
                                        BuildMeasurementRect(m.P1, m.P2),
                                        m.Mode,
                                        m.PixelSpacingX,
                                        m.PixelSpacingY,
                                        out area,
                                        out min,
                                        out max,
                                        out mean,
                                        out sd);
                                    m.RoiArea = area;
                                    m.RoiMin = min;
                                    m.RoiMax = max;
                                    m.RoiMean = mean;
                                    m.RoiStdDev = sd;
                                    m.IsHounsfieldUnit = provider.IsHounsfieldUnit;
                                }
                            }
                        }
                    }

                    if (source == null)
                    {
                        using (var raw = new DicomImage(dataset).RenderImage().AsClonedBitmap())
                            source = CreateSafeBitmapCopy(raw);
                    }
                }

                var canvas = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(canvas))
                using (var labelBrush = new SolidBrush(Color.FromArgb(250, 250, 220)))
                using (var shadowBrush = new SolidBrush(Color.FromArgb(185, 0, 0, 0)))
                using (var labelBgBrush = new SolidBrush(Color.FromArgb(145, 10, 10, 10)))
                using (var font = new Font("Segoe UI", 10f, FontStyle.Bold))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.DrawImage(source, 0, 0, source.Width, source.Height);

                    if (measurements != null)
                    {
                        foreach (var m in measurements)
                        {
                            var color = m.Color.IsEmpty ? Color.Lime : m.Color;
                            var drawWidth = m.Mode == MeasurementMode.Pencil && m.StrokeWidth > 0f ? m.StrokeWidth : 2.2f;
                            var drawColor = m.Mode == MeasurementMode.Pencil
                                ? Color.FromArgb(Math.Max(1, Math.Min(255, m.StrokeOpacity > 0 ? m.StrokeOpacity : 220)), color)
                                : color;
                            using (var pen = new Pen(drawColor, drawWidth))
                            {
                                switch (m.Mode)
                                {
                                    case MeasurementMode.Angle:
                                        g.DrawLine(pen, m.P1, m.P2);
                                        g.DrawLine(pen, m.P1, m.P3);
                                        break;
                                    case MeasurementMode.Polygon:
                                        var polyPts = m.Points != null && m.Points.Count > 3
                                            ? m.Points
                                            : new List<PointF> { m.P1, m.P2, m.P3 };
                                        for (var pi = 0; pi < polyPts.Count; pi++)
                                            g.DrawLine(pen, polyPts[pi], polyPts[(pi + 1) % polyPts.Count]);
                                        break;
                                    case MeasurementMode.OpenPolyline:
                                        var points = m.Points != null && m.Points.Count >= 2
                                            ? m.Points
                                            : new List<PointF> { m.P1, m.P2, m.P3 };
                                        for (var pi = 1; pi < points.Count; pi++)
                                            g.DrawLine(pen, points[pi - 1], points[pi]);
                                        break;
                                    case MeasurementMode.Spline:
                                        var splinePoints = m.Points != null && m.Points.Count >= 2
                                            ? m.Points
                                            : new List<PointF> { m.P1, m.P2, m.P3 };
                                        pen.StartCap = LineCap.Round;
                                        pen.EndCap = LineCap.Round;
                                        pen.LineJoin = LineJoin.Round;
                                        if (splinePoints.Count >= 3)
                                            g.DrawCurve(pen, splinePoints.ToArray(), 0.45f);
                                        else
                                            g.DrawLine(pen, splinePoints[0], splinePoints[1]);
                                        break;
                                    case MeasurementMode.CobbAngle:
                                        var cobb = m.Points != null && m.Points.Count >= 4
                                            ? m.Points
                                            : new List<PointF> { m.P1, m.P2, m.P3, m.P3 };
                                        g.DrawLine(pen, cobb[0], cobb[1]);
                                        g.DrawLine(pen, cobb[2], cobb[3]);
                                        break;
                                    case MeasurementMode.Deviation:
                                        using (var dashPen = new Pen(Color.FromArgb(160, drawColor), 1.2f) { DashStyle = DashStyle.Dash })
                                        {
                                            g.DrawLine(dashPen, m.P1.X, m.P1.Y, m.P2.X, m.P1.Y);
                                            g.DrawLine(dashPen, m.P2.X, m.P1.Y, m.P2.X, m.P2.Y);
                                        }
                                        g.DrawLine(pen, m.P1, m.P2);
                                        break;
                                    case MeasurementMode.Arrow:
                                    case MeasurementMode.TextAnnotation:
                                        if (Math.Abs(m.P2.X - m.P1.X) > 0.01f || Math.Abs(m.P2.Y - m.P1.Y) > 0.01f)
                                        {
                                            using (var arrowCap = new AdjustableArrowCap(5f, 6f, true))
                                            using (var arrowPen = new Pen(drawColor, drawWidth))
                                            {
                                                arrowPen.StartCap = LineCap.Round;
                                                arrowPen.EndCap = LineCap.Round;
                                                arrowPen.CustomEndCap = arrowCap;
                                                g.DrawLine(arrowPen, m.P1, m.P2);
                                            }
                                        }
                                        break;
                                    case MeasurementMode.Pencil:
                                        var pencilPoints = m.Points != null && m.Points.Count >= 2
                                            ? m.Points
                                            : new List<PointF> { m.P1, m.P2, m.P3 };
                                        pen.StartCap = LineCap.Round;
                                        pen.EndCap = LineCap.Round;
                                        pen.LineJoin = LineJoin.Round;
                                        if (pencilPoints.Count >= 3)
                                            g.DrawCurve(pen, pencilPoints.ToArray(), 0.35f);
                                        else if (pencilPoints.Count == 2)
                                            g.DrawLine(pen, pencilPoints[0], pencilPoints[1]);
                                        break;
                                    case MeasurementMode.RoiRectangle:
                                        g.DrawRectangle(pen, Rectangle.Round(BuildMeasurementRect(m.P1, m.P2)));
                                        break;
                                    case MeasurementMode.RoiCircle:
                                        g.DrawEllipse(pen, BuildMeasurementRect(m.P1, m.P2));
                                        break;
                                    default:
                                        g.DrawLine(pen, m.P1, m.P2);
                                        break;
                                }
                            }

                            var label = BuildMeasurementReportLine(m);
                            var anchor = m.Mode == MeasurementMode.OpenPolyline && m.Points != null && m.Points.Count > 0
                                ? m.Points[m.Points.Count - 1]
                                : m.Mode == MeasurementMode.Spline && m.Points != null && m.Points.Count > 0
                                    ? m.Points[m.Points.Count - 1]
                                : m.Mode == MeasurementMode.TextAnnotation
                                    ? m.P2
                                : m.Mode == MeasurementMode.Pencil && m.Points != null && m.Points.Count > 0
                                    ? m.Points[m.Points.Count - 1]
                                : m.Mode == MeasurementMode.CobbAngle && m.Points != null && m.Points.Count > 3
                                    ? m.Points[3]
                                : m.Mode == MeasurementMode.Polygon
                                    ? (m.Points != null && m.Points.Count > 3 ? m.Points[m.Points.Count - 1] : m.P3)
                                : m.Mode == MeasurementMode.Angle
                                    ? m.P3
                                    : m.P2;
                            var x = anchor.X + 6f;
                            var y = anchor.Y + 6f;
                            if (m.Mode == MeasurementMode.TextAnnotation)
                            {
                                var text = m.TextValue == null ? string.Empty : m.TextValue.Replace("\r", " ").Replace("\n", " ").Trim();
                                if (text.Length > 180)
                                    text = text.Substring(0, 179) + "…";
                                if (string.IsNullOrWhiteSpace(text))
                                    text = "Text";
                                var title = string.IsNullOrWhiteSpace(m.Name) ? "Text" : m.Name.Trim();
                                var prefixed = title + ": " + text;
                                var chunks = new List<string>();
                                var words = prefixed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                var line = string.Empty;
                                foreach (var word in words)
                                {
                                    if (line.Length == 0)
                                    {
                                        line = word;
                                        continue;
                                    }
                                    if (line.Length + 1 + word.Length <= 34)
                                        line += " " + word;
                                    else
                                    {
                                        chunks.Add(line);
                                        line = word;
                                    }
                                }
                                if (line.Length > 0)
                                    chunks.Add(line);
                                var drawText = string.Join("\n", chunks);

                                using (var textFont = new Font("Segoe UI", 10f, FontStyle.Bold))
                                using (var background = new SolidBrush(Color.FromArgb(170, 14, 14, 14)))
                                using (var textBrush = new SolidBrush(m.Color.IsEmpty ? Color.Lime : m.Color))
                                using (var border = new Pen(Color.FromArgb(150, m.Color.IsEmpty ? Color.Lime : m.Color), 1f))
                                {
                                    var maxBoxWidth = Math.Max(80f, Math.Min(240f, source.Width - 12f));
                                    var size = g.MeasureString(drawText, textFont, (int)maxBoxWidth);
                                    x = Math.Max(4f, Math.Min(x, Math.Max(4f, source.Width - size.Width - 6f)));
                                    y = Math.Max(4f, Math.Min(y, Math.Max(4f, source.Height - size.Height - 6f)));
                                    var rect = new RectangleF(x - 4f, y - 3f, size.Width + 8f, size.Height + 6f);
                                    g.FillRectangle(background, rect);
                                    g.DrawRectangle(border, Rectangle.Round(rect));
                                    g.DrawString(drawText, textFont, textBrush, new RectangleF(x, y, Math.Max(size.Width + 4f, maxBoxWidth), size.Height + 4f));
                                }
                            }
                            else
                            {
                                var maxLabelWidth = Math.Max(80f, source.Width - 12f);
                                var labelSize = g.MeasureString(label, font, (int)maxLabelWidth);
                                x = Math.Max(4f, Math.Min(x, Math.Max(4f, source.Width - labelSize.Width - 6f)));
                                y = Math.Max(4f, Math.Min(y, Math.Max(4f, source.Height - labelSize.Height - 6f)));
                                var bgRect = new RectangleF(x - 3f, y - 2f, labelSize.Width + 6f, labelSize.Height + 4f);
                                g.FillRectangle(labelBgBrush, bgRect);
                                var textRect = new RectangleF(x, y, maxLabelWidth, labelSize.Height + 4f);
                                var shadowRect = new RectangleF(x + 1f, y + 1f, maxLabelWidth, labelSize.Height + 4f);
                                g.DrawString(label, font, shadowBrush, shadowRect);
                                g.DrawString(label, font, labelBrush, textRect);
                            }
                        }
                    }
                }

                return canvas;
            }
            finally
            {
                if (source != null)
                    source.Dispose();
            }
        }

        private void PopulateWlPresetItems(ToolStripDropDownItem parent)
        {
            parent.DropDownItems.Clear();
            var undoItem = new ToolStripMenuItem("Undo WL", null, delegate { UndoWl(); }) { ShortcutKeyDisplayString = "Ctrl+Z" };
            var redoItem = new ToolStripMenuItem("Redo WL", null, delegate { RedoWl(); }) { ShortcutKeyDisplayString = "Ctrl+Y" };
            parent.DropDownItems.Add(undoItem);
            parent.DropDownItems.Add(redoItem);
            parent.DropDownItems.Add(new ToolStripSeparator());
            parent.DropDownItems.Add("Авто (Auto)", null, delegate { SetWlPreset("Auto", null, null); });
            parent.DropDownItems.Add("Мягкие ткани (Soft tissue)", null, delegate { SetWlPreset("Soft tissue", 40f, 350f); });
            parent.DropDownItems.Add("Кости (Bone)", null, delegate { SetWlPreset("Bone", 400f, 1800f); });
            parent.DropDownItems.Add("Легкие (Lung)", null, delegate { SetWlPreset("Lung", -600f, 1500f); });
            parent.DropDownItems.Add("Головной мозг (Brain)", null, delegate { SetWlPreset("Brain", 40f, 80f); });
            parent.DropDownItems.Add("Брюшная полость (Abdomen)", null, delegate { SetWlPreset("Abdomen", 40f, 350f); });
            parent.DropDownItems.Add("Средостение (Mediastinum)", null, delegate { SetWlPreset("Mediastinum", 50f, 350f); });
            parent.DropDownItems.Add("Ангиография (Angio)", null, delegate { SetWlPreset("Angio", 300f, 600f); });

            var userPreset = LoadUserPreset();
            if (userPreset.HasValue)
                parent.DropDownItems.Add("Пользовательский пресет", null, delegate { SetWlPreset("User", userPreset.Value.center, userPreset.Value.width); });

            parent.DropDownItems.Add(new ToolStripSeparator());
            parent.DropDownItems.Add("Вручную...", null, delegate { OpenWlManualDialog(); });
            parent.DropDownItems.Add("Сохранить как польз. пресет", null, delegate { SaveCurrentWlAsUserPreset(); });
        }

        private void SetWlPreset(string name, float? center, float? width, bool pushHistory = true)
        {
            if (pushHistory)
            {
                SaveCurrentWlStateForUndo();
                wlRedoStack.Clear();
            }
            wlPresetName = name;
            wlPresetCenter = center;
            wlPresetWidth = width;
            RegisterRecentWlPreset();
            ApplyCurrentWlToViewer();
            RefreshWlPresetCaption();
            if (mprModeEnabled && currentMprVolume != null)
            {
                UpdateAllMprCrosshairsAndOverlays();
                return;
            }
            if (images.Count > 0)
                UpdateViewerOverlay(imageIndex, null);
        }

        private void RegisterRecentWlPreset()
        {
            if (!wlPresetCenter.HasValue || !wlPresetWidth.HasValue)
                return;
            if (string.Equals(wlPresetName, "Auto", StringComparison.OrdinalIgnoreCase))
                return;

            recentWlPresets.RemoveAll(p => string.Equals(p.Name, wlPresetName, StringComparison.OrdinalIgnoreCase)
                                           && p.Center == wlPresetCenter
                                           && p.Width == wlPresetWidth);
            recentWlPresets.Insert(0, new WlState { Name = wlPresetName, Center = wlPresetCenter, Width = wlPresetWidth });
            if (recentWlPresets.Count > 3)
                recentWlPresets.RemoveRange(3, recentWlPresets.Count - 3);
        }

        private void ActivateViewerTool(ViewerTool tool)
        {
            var targetViewer = ActiveViewerControl;
            if (targetViewer != null && tool != ViewerTool.Measure)
                targetViewer.CommitPendingMeasurement();
            targetViewer.Tool = tool;
            ApplyViewerCursor(targetViewer);

            if (panToolButton != null)
                panToolButton.Checked = tool == ViewerTool.Pan;
            if (measureToolsButton != null)
            {
                measureToolsButton.BackColor = tool == ViewerTool.Measure ? Color.FromArgb(186, 186, 186) : Color.FromArgb(164, 164, 164);
                measureToolsButton.ForeColor = Color.FromArgb(36, 36, 36);
            }
            if (wlButton != null)
            {
                wlButton.BackColor = tool == ViewerTool.WindowLevel ? Color.FromArgb(186, 186, 186) : Color.FromArgb(164, 164, 164);
                wlButton.ForeColor = Color.FromArgb(36, 36, 36);
            }

            if (!string.IsNullOrWhiteSpace(statusBaseText))
                SetStatusText(statusBaseText);
            SyncSecondaryViewers();
        }

        private void SetMeasurementMode(MeasurementMode mode)
        {
            if (ActiveViewerControl != null && ActiveViewerControl.MeasurementMode != mode)
                ActiveViewerControl.CommitPendingMeasurement();
            ActiveViewerControl.MeasurementMode = mode;
            ApplyViewerCursor(ActiveViewerControl);
            if (measureDistanceMenuItem != null)
                measureDistanceMenuItem.Checked = mode == MeasurementMode.Distance;
            if (measureAngleMenuItem != null)
                measureAngleMenuItem.Checked = mode == MeasurementMode.Angle;
            if (measureRoiRectMenuItem != null)
                measureRoiRectMenuItem.Checked = mode == MeasurementMode.RoiRectangle;
            if (measureRoiCircleMenuItem != null)
                measureRoiCircleMenuItem.Checked = mode == MeasurementMode.RoiCircle;
            if (measureDeviationMenuItem != null)
                measureDeviationMenuItem.Checked = mode == MeasurementMode.Deviation;
            if (measureArrowMenuItem != null)
                measureArrowMenuItem.Checked = mode == MeasurementMode.Arrow;
            if (measureTextMenuItem != null)
                measureTextMenuItem.Checked = mode == MeasurementMode.TextAnnotation;
            if (measurePencilMenuItem != null)
                measurePencilMenuItem.Checked = mode == MeasurementMode.Pencil;
            if (measurePolygonMenuItem != null)
                measurePolygonMenuItem.Checked = mode == MeasurementMode.Polygon;
            if (measureOpenPolylineMenuItem != null)
                measureOpenPolylineMenuItem.Checked = mode == MeasurementMode.OpenPolyline;
            if (measureSplineMenuItem != null)
                measureSplineMenuItem.Checked = mode == MeasurementMode.Spline;
            if (measureCobbMenuItem != null)
                measureCobbMenuItem.Checked = mode == MeasurementMode.CobbAngle;
            if (measureToolsButton != null)
            {
                switch (mode)
                {
                    case MeasurementMode.Angle:
                        measureToolsButton.ToolTipText = "Измерить угол";
                        break;
                    case MeasurementMode.RoiRectangle:
                        measureToolsButton.ToolTipText = "ROI прямоугольник";
                        break;
                    case MeasurementMode.RoiCircle:
                        measureToolsButton.ToolTipText = "ROI круг";
                        break;
                    case MeasurementMode.Deviation:
                        measureToolsButton.ToolTipText = "Отклонение";
                        break;
                    case MeasurementMode.Arrow:
                        measureToolsButton.ToolTipText = "Стрелка";
                        break;
                    case MeasurementMode.TextAnnotation:
                        measureToolsButton.ToolTipText = "Текст";
                        break;
                    case MeasurementMode.Pencil:
                        measureToolsButton.ToolTipText = "Карандаш";
                        break;
                    case MeasurementMode.Polygon:
                        measureToolsButton.ToolTipText = "Полигон";
                        break;
                    case MeasurementMode.OpenPolyline:
                        measureToolsButton.ToolTipText = "Открытый полигон";
                        break;
                    case MeasurementMode.Spline:
                        measureToolsButton.ToolTipText = "Spline (кривая)";
                        break;
                    case MeasurementMode.CobbAngle:
                        measureToolsButton.ToolTipText = "Угол Кобба";
                        break;
                    default:
                        measureToolsButton.ToolTipText = "Измерить расстояние";
                        break;
                }
            }
            ActivateViewerTool(ViewerTool.Measure);
            SyncSecondaryViewers();
        }

        private string GetToolCaption()
        {
            var targetViewer = ActiveViewerControl;
            if (targetViewer == null)
                return "Pan";
            switch (targetViewer.Tool)
            {
                case ViewerTool.Measure:
                    switch (targetViewer.MeasurementMode)
                    {
                        case MeasurementMode.Angle:
                            return "Angle";
                        case MeasurementMode.RoiRectangle:
                            return "ROI Rect";
                        case MeasurementMode.RoiCircle:
                            return "ROI Circle";
                        case MeasurementMode.Deviation:
                            return "Deviation";
                        case MeasurementMode.Arrow:
                            return "Arrow";
                        case MeasurementMode.TextAnnotation:
                            return "Text";
                        case MeasurementMode.Pencil:
                            return "Pencil";
                        case MeasurementMode.Polygon:
                            return "Polygon";
                        case MeasurementMode.OpenPolyline:
                            return "Open Polyline";
                        case MeasurementMode.Spline:
                            return "Spline";
                        case MeasurementMode.CobbAngle:
                            return "Cobb Angle";
                        default:
                            return "Distance";
                    }
                case ViewerTool.WindowLevel:
                    return "WL";
                default:
                    return "Pan";
            }
        }

        private void SetStatusText(string text)
        {
            statusBaseText = text ?? string.Empty;
            if (status == null)
                return;

            var now = DateTime.UtcNow;
            if ((now - lastStatusUpdateUtc).TotalMilliseconds >= StatusUpdateThrottleMs)
            {
                pendingStatusText = null;
                if (statusUpdateTimer != null && statusUpdateTimer.Enabled)
                    statusUpdateTimer.Stop();
                ApplyStatusText(statusBaseText);
                lastStatusUpdateUtc = now;
                return;
            }

            pendingStatusText = statusBaseText;
            if (statusUpdateTimer != null && !statusUpdateTimer.Enabled)
                statusUpdateTimer.Start();
        }

        private void OnStatusUpdateTimerTick(object sender, EventArgs e)
        {
            if (pendingStatusText == null)
            {
                if (statusUpdateTimer != null)
                    statusUpdateTimer.Stop();
                return;
            }

            ApplyStatusText(pendingStatusText);
            pendingStatusText = null;
            lastStatusUpdateUtc = DateTime.UtcNow;
            if (statusUpdateTimer != null)
                statusUpdateTimer.Stop();
        }

        private void ApplyStatusText(string text)
        {
            if (status == null)
                return;
            status.Text = string.Format("{0} | Tool: {1}", text ?? string.Empty, GetToolCaption());
        }

        private void ResetWlToAuto()
        {
            SetWlPreset("Auto", null, null);
        }

        private void NormalizeWl()
        {
            SetWlPreset("Normalize", 128f, 256f);
        }

        private void OnViewerWindowLevelMenuRequested(Point screenPoint)
        {
            var targetViewer = ActiveViewerControl;
            if (targetViewer.Tool != ViewerTool.WindowLevel)
                return;

            var menu = new ContextMenu();
            menu.MenuItems.Add(new MenuItem("Reset WL (Auto)", delegate { ResetWlToAuto(); }));
            menu.MenuItems.Add(new MenuItem("Normalize (128/256)", delegate { NormalizeWl(); }));
            if (recentWlPresets.Count > 0)
            {
                menu.MenuItems.Add("-");
                foreach (var preset in recentWlPresets)
                {
                    var p = preset;
                    menu.MenuItems.Add(new MenuItem(
                        string.Format("{0} ({1:0.#}/{2:0.#})", p.Name, p.Center ?? 0f, p.Width ?? 0f),
                        delegate { SetWlPreset(p.Name, p.Center, p.Width); }));
                }
            }

            var clientPoint = targetViewer.PointToClient(screenPoint);
            menu.Show(targetViewer, clientPoint);
        }

        private void OnViewerMeasurementContextMenuRequested(Point screenPoint, DicomMeasurement measurement)
        {
            if (measurement == null)
                return;

            var menu = new ContextMenu();
            menu.MenuItems.Add(new MenuItem("Свойства измерения...", delegate { OpenMeasurementPropertiesDialog(measurement.Id); }));
            if (measurement.Mode == MeasurementMode.Pencil)
            {
                menu.MenuItems.Add(new MenuItem("Добавить узел карандаша", delegate
                {
                    PointF imagePoint;
                    if (ActiveViewerControl.TryGetImagePointAt(ActiveViewerControl.PointToClient(screenPoint), out imagePoint)
                        && ActiveViewerControl.InsertPencilPoint(measurement.Id, imagePoint))
                    {
                        SaveCurrentImageMeasurements();
                    }
                }));
                menu.MenuItems.Add(new MenuItem("Удалить ближайший узел", delegate
                {
                    PointF imagePoint;
                    if (ActiveViewerControl.TryGetImagePointAt(ActiveViewerControl.PointToClient(screenPoint), out imagePoint)
                        && ActiveViewerControl.RemovePencilPoint(measurement.Id, imagePoint))
                    {
                        SaveCurrentImageMeasurements();
                    }
                }));
            }
            else if (measurement.Mode == MeasurementMode.Spline)
            {
                menu.MenuItems.Add(new MenuItem("Добавить узел кривой", delegate
                {
                    PointF imagePoint;
                    if (ActiveViewerControl.TryGetImagePointAt(ActiveViewerControl.PointToClient(screenPoint), out imagePoint)
                        && ActiveViewerControl.InsertSplinePoint(measurement.Id, imagePoint))
                    {
                        SaveCurrentImageMeasurements();
                    }
                }));
                menu.MenuItems.Add(new MenuItem("Удалить ближайший узел", delegate
                {
                    PointF imagePoint;
                    if (ActiveViewerControl.TryGetImagePointAt(ActiveViewerControl.PointToClient(screenPoint), out imagePoint)
                        && ActiveViewerControl.RemoveSplinePoint(measurement.Id, imagePoint))
                    {
                        SaveCurrentImageMeasurements();
                    }
                }));
            }
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem("Удалить измерение", delegate
            {
                DeleteMeasurementAndSelectNext(measurement.Id);
            }));
            var targetViewer = ActiveViewerControl;
            var clientPoint = targetViewer.PointToClient(screenPoint);
            menu.Show(targetViewer, clientPoint);
        }

        private void OpenMeasurementPropertiesDialog(Guid measurementId)
        {
            var targetViewer = ActiveViewerControl;
            var measurement = targetViewer.GetMeasurements().FirstOrDefault(m => m.Id == measurementId);
            if (measurement == null)
                return;

            using (var dialog = new Form
            {
                Text = "Свойства измерения",
                StartPosition = FormStartPosition.CenterParent,
                Width = 360,
                Height = measurement.Mode == MeasurementMode.Pencil || measurement.Mode == MeasurementMode.TextAnnotation ? 280 : 200,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            })
            {
                var nameLabel = new Label { Left = 12, Top = 16, Width = 320, Text = "Название:" };
                var defaultName = measurement.Mode == MeasurementMode.Angle
                    ? "Angle"
                    : measurement.Mode == MeasurementMode.Deviation
                        ? "Deviation"
                    : measurement.Mode == MeasurementMode.Arrow
                        ? "Arrow"
                    : measurement.Mode == MeasurementMode.TextAnnotation
                        ? "Text"
                    : measurement.Mode == MeasurementMode.Pencil
                        ? "Pencil"
                    : measurement.Mode == MeasurementMode.Spline
                        ? "Spline"
                    : measurement.Mode == MeasurementMode.Polygon
                        ? "Polygon"
                        : measurement.Mode == MeasurementMode.OpenPolyline
                            ? "Open Polyline"
                            : measurement.Mode == MeasurementMode.CobbAngle
                                ? "Cobb Angle"
                                : "Distance";
                var nameBox = new TextBox { Left = 12, Top = 36, Width = 320, Text = string.IsNullOrWhiteSpace(measurement.Name) ? defaultName : measurement.Name };
                var colorLabel = new Label { Left = 12, Top = 72, Width = 320, Text = "Цвет:" };
                var colorPreview = new Panel { Left = 12, Top = 92, Width = 60, Height = 24, BorderStyle = BorderStyle.FixedSingle, BackColor = measurement.Color.IsEmpty ? Color.Lime : measurement.Color };
                var colorButton = new Button { Left = 84, Top = 91, Width = 120, Height = 26, Text = "Выбрать цвет..." };
                colorButton.Click += delegate
                {
                    using (var colorDialog = new ColorDialog { Color = colorPreview.BackColor, FullOpen = true })
                    {
                        if (colorDialog.ShowDialog(dialog) == DialogResult.OK)
                            colorPreview.BackColor = colorDialog.Color;
                    }
                };

                var strokeLabel = new Label { Left = 12, Top = 124, Width = 320, Text = "Толщина штриха:" };
                var strokeBox = new NumericUpDown
                {
                    Left = 12,
                    Top = 144,
                    Width = 140,
                    DecimalPlaces = 1,
                    Increment = 0.1m,
                    Minimum = 0.2m,
                    Maximum = 20m,
                    Value = (decimal)Math.Max(0.2f, measurement.StrokeWidth > 0f ? measurement.StrokeWidth : 1.2f),
                    Visible = measurement.Mode == MeasurementMode.Pencil
                };
                var opacityLabel = new Label { Left = 168, Top = 124, Width = 164, Text = "Прозрачность (%):", Visible = measurement.Mode == MeasurementMode.Pencil };
                var opacityBox = new NumericUpDown
                {
                    Left = 168,
                    Top = 144,
                    Width = 164,
                    DecimalPlaces = 0,
                    Increment = 1,
                    Minimum = 1,
                    Maximum = 100,
                    Value = Math.Max(1, Math.Min(100, (int)Math.Round(((measurement.StrokeOpacity > 0 ? measurement.StrokeOpacity : 220) / 255f) * 100f))),
                    Visible = measurement.Mode == MeasurementMode.Pencil
                };

                var textLabel = new Label { Left = 12, Top = 124, Width = 320, Text = "Текст:" };
                var textBox = new TextBox
                {
                    Left = 12,
                    Top = 144,
                    Width = 320,
                    Text = measurement.TextValue ?? string.Empty,
                    Visible = measurement.Mode == MeasurementMode.TextAnnotation
                };

                var buttonTop = measurement.Mode == MeasurementMode.Pencil ? 208 : measurement.Mode == MeasurementMode.TextAnnotation ? 208 : 128;
                var ok = new Button { Text = "OK", Left = 176, Width = 75, Top = buttonTop, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Отмена", Left = 257, Width = 75, Top = buttonTop, DialogResult = DialogResult.Cancel };
                dialog.Controls.AddRange(new Control[] { nameLabel, nameBox, colorLabel, colorPreview, colorButton, strokeLabel, strokeBox, opacityLabel, opacityBox, textLabel, textBox, ok, cancel });
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                float? strokeWidth = null;
                int? strokeOpacity = null;
                if (measurement.Mode == MeasurementMode.Pencil)
                {
                    strokeWidth = (float)strokeBox.Value;
                    var opacityPercent = (float)opacityBox.Value;
                    strokeOpacity = Math.Max(1, Math.Min(255, (int)Math.Round(opacityPercent / 100f * 255f)));
                }

                var textValue = measurement.Mode == MeasurementMode.TextAnnotation ? textBox.Text : null;

                if (targetViewer.UpdateMeasurementProperties(measurementId, nameBox.Text, colorPreview.BackColor, strokeWidth, strokeOpacity, textValue))
                    SaveCurrentImageMeasurements();
            }
        }

        private WlState CaptureCurrentWlState()
        {
            return new WlState { Name = wlPresetName, Center = wlPresetCenter, Width = wlPresetWidth };
        }

        private static bool WlStateEquals(WlState a, WlState b)
        {
            if (a == null || b == null)
                return false;
            return string.Equals(a.Name, b.Name, StringComparison.Ordinal)
                && a.Center == b.Center
                && a.Width == b.Width;
        }

        private void SaveCurrentWlStateForUndo()
        {
            var state = CaptureCurrentWlState();
            if (wlUndoStack.Count > 0 && WlStateEquals(wlUndoStack.Peek(), state))
                return;
            wlUndoStack.Push(state);
            while (wlUndoStack.Count > 80)
            {
                var keep = wlUndoStack.Reverse().Skip(1).Reverse().ToArray();
                wlUndoStack.Clear();
                foreach (var item in keep)
                    wlUndoStack.Push(item);
            }
        }

        private void ClearWlHistory()
        {
            wlUndoStack.Clear();
            wlRedoStack.Clear();
            lastWlHistoryCaptureUtc = DateTime.MinValue;
        }

        private void UndoWl()
        {
            if (wlUndoStack.Count == 0)
                return;
            wlRedoStack.Push(CaptureCurrentWlState());
            var state = wlUndoStack.Pop();
            SetWlPreset(state.Name, state.Center, state.Width, false);
        }

        private void RedoWl()
        {
            if (wlRedoStack.Count == 0)
                return;
            wlUndoStack.Push(CaptureCurrentWlState());
            var state = wlRedoStack.Pop();
            SetWlPreset(state.Name, state.Center, state.Width, false);
        }

        private void ApplyCurrentWlToViewer()
        {
            if (wlPresetCenter.HasValue && wlPresetWidth.HasValue)
            {
                currentAppliedWlCenter = wlPresetCenter.Value;
                currentAppliedWlWidth = wlPresetWidth.Value;
                ActiveViewerControl.SetWindowLevel(currentAppliedWlCenter, currentAppliedWlWidth, true);
                SyncSecondaryViewers();
                return;
            }

            if (autoWlCenter.HasValue && autoWlWidth.HasValue)
            {
                currentAppliedWlCenter = autoWlCenter.Value;
                currentAppliedWlWidth = autoWlWidth.Value;
                ActiveViewerControl.SetWindowLevel(currentAppliedWlCenter, currentAppliedWlWidth, false);
                SyncSecondaryViewers();
                return;
            }

            currentAppliedWlCenter = 128f;
            currentAppliedWlWidth = 256f;
            ActiveViewerControl.SetWindowLevel(currentAppliedWlCenter, currentAppliedWlWidth, false);
            SyncSecondaryViewers();
        }

        private void RefreshWlPresetCaption()
        {
            if (wlButton != null)
            {
                var shortName = wlPresetName.Length > 7 ? wlPresetName.Substring(0, 7) : wlPresetName;
                wlButton.Text = "WL " + shortName;
            }

            if (wlMenuItem != null)
                wlMenuItem.Text = string.Format("WL/WW пресет ({0})", wlPresetName);
        }

        private void OpenWlManualDialog()
        {
            var previousName = wlPresetName;
            var previousCenter = wlPresetCenter;
            var previousWidth = wlPresetWidth;
            using (var dialog = new Form { Text = "WL/WW Manual", StartPosition = FormStartPosition.CenterParent, Width = 360, Height = 230, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            {
                var wlLabel = new Label { Left = 12, Top = 12, Width = 320, Text = "WL: 0" };
                var wwLabel = new Label { Left = 12, Top = 82, Width = 320, Text = "WW: 0" };
                var wlTrack = new TrackBar { Left = 12, Top = 32, Width = 320, Minimum = -1500, Maximum = 1500, TickFrequency = 250 };
                var wwTrack = new TrackBar { Left = 12, Top = 102, Width = 320, Minimum = 1, Maximum = 4000, TickFrequency = 250 };
                var ok = new Button { Text = "OK", Left = 176, Width = 75, Top = 162, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Отмена", Left = 257, Width = 75, Top = 162, DialogResult = DialogResult.Cancel };
                dialog.Controls.AddRange(new Control[] { wlLabel, wwLabel, wlTrack, wwTrack, ok, cancel });
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;

                wlTrack.Value = (int)Math.Max(wlTrack.Minimum, Math.Min(wlTrack.Maximum, currentAppliedWlCenter));
                wwTrack.Value = (int)Math.Max(wwTrack.Minimum, Math.Min(wwTrack.Maximum, currentAppliedWlWidth <= 0 ? 256 : currentAppliedWlWidth));

                EventHandler refresh = delegate
                {
                    wlLabel.Text = "WL: " + wlTrack.Value;
                    wwLabel.Text = "WW: " + wwTrack.Value;
                    ActiveViewerControl.SetWindowLevel(wlTrack.Value, wwTrack.Value, true);
                    wlPresetName = "Manual";
                    wlPresetCenter = wlTrack.Value;
                    wlPresetWidth = wwTrack.Value;
                    currentAppliedWlCenter = wlTrack.Value;
                    currentAppliedWlWidth = wwTrack.Value;
                    RefreshWlPresetCaption();
                    UpdateViewerOverlay(imageIndex, null);
                };
                wlTrack.ValueChanged += refresh;
                wwTrack.ValueChanged += refresh;
                refresh(this, EventArgs.Empty);

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    wlPresetName = previousName;
                    wlPresetCenter = previousCenter;
                    wlPresetWidth = previousWidth;
                    ApplyCurrentWlToViewer();
                    RefreshWlPresetCaption();
                    UpdateViewerOverlay(imageIndex, null);
                }
            }
        }

        private void SaveCurrentWlAsUserPreset()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(userPresetPath));
            File.WriteAllText(userPresetPath, string.Format(CultureInfo.InvariantCulture, "{0};{1}", currentAppliedWlCenter, currentAppliedWlWidth));
            if (wlButton != null)
                PopulateWlPresetItems(wlButton);
            if (wlMenuItem != null)
                PopulateWlPresetItems(wlMenuItem);
            MessageBox.Show(this, "User preset сохранён.", "WL/WW", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private (float center, float width)? LoadUserPreset()
        {
            try
            {
                if (!File.Exists(userPresetPath))
                    return null;
                var raw = File.ReadAllText(userPresetPath).Trim();
                var parts = raw.Split(';');
                if (parts.Length != 2)
                    return null;
                float center;
                float width;
                if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out center))
                    return null;
                if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out width))
                    return null;
                if (width <= 0f)
                    return null;
                return (center, width);
            }
            catch
            {
                return null;
            }
        }

        private void CloseActivePanel()
        {
            if (mainSplit == null)
                return;
            if (!mainSplit.Panel1Collapsed)
            {
                mainSplit.Panel1Collapsed = true;
                return;
            }
            if (topBarHost != null && topBarHost.Visible)
            {
                topBarHost.Visible = false;
                return;
            }
            if (status.Visible)
                status.Visible = false;
        }

        private void CloseAllPanels()
        {
            if (mainSplit != null)
                mainSplit.Panel1Collapsed = true;
            if (topBarHost != null)
                topBarHost.Visible = false;
            status.Visible = false;
        }

        private void ToggleSeriesPreviewBar()
        {
            if (mainSplit == null)
                return;
            mainSplit.Panel1Collapsed = !mainSplit.Panel1Collapsed;
        }

        private void ToggleToolbarAndStatusBar()
        {
            var nextVisible = !(topBarHost != null && topBarHost.Visible && status.Visible);
            if (topBarHost != null)
                topBarHost.Visible = nextVisible;
            status.Visible = nextVisible;
        }

        private void ToggleFullScreenMode()
        {
            if (!fullScreenMode)
            {
                savedBorderStyle = FormBorderStyle;
                savedWindowState = WindowState;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
                fullScreenMode = true;
                return;
            }

            FormBorderStyle = savedBorderStyle;
            WindowState = savedWindowState;
            fullScreenMode = false;
        }

        private void ToggleDistractionFreeMode()
        {
            if (!distractionFreeMode)
            {
                savedTopBarVisible = topBarHost != null && topBarHost.Visible;
                savedStatusVisible = status.Visible;
                savedPreviewVisible = mainSplit != null && !mainSplit.Panel1Collapsed;
                if (!fullScreenMode)
                    ToggleFullScreenMode();
                if (topBarHost != null)
                    topBarHost.Visible = false;
                status.Visible = false;
                if (mainSplit != null)
                    mainSplit.Panel1Collapsed = true;
                distractionFreeMode = true;
                return;
            }

            if (topBarHost != null)
                topBarHost.Visible = savedTopBarVisible;
            status.Visible = savedStatusVisible;
            if (mainSplit != null)
                mainSplit.Panel1Collapsed = !savedPreviewVisible;
            if (fullScreenMode)
                ToggleFullScreenMode();
            distractionFreeMode = false;
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.None)
            {
                var textBox = ActiveControl as TextBoxBase;
                if (textBox == null)
                {
                    var id = GetSelectedMeasurementIdFromList() ?? ActiveViewerControl.GetSelectedMeasurementId();
                    if (id.HasValue && DeleteMeasurementAndSelectNext(id.Value))
                    {
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        return;
                    }
                }
            }

            if (e.KeyCode == Keys.Z && e.Modifiers == Keys.Control)
            {
                UndoWl();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Y && e.Modifiers == Keys.Control)
            {
                RedoWl();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.F4 && e.Modifiers == Keys.None)
            {
                OpenFolder();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.R && e.Modifiers == Keys.Control)
            {
                ResetWlToAuto();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.F5 && e.Modifiers == Keys.Control)
            {
                MessageBox.Show(this, "Split series будет добавлено следующим этапом.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.F4 && e.Modifiers == Keys.Control)
            {
                CloseActivePanel();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.F4 && e.Modifiers == Keys.Shift)
            {
                CloseAllPanels();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.F2 && e.Modifiers == (Keys.Control | Keys.Alt))
            {
                ToggleSeriesPreviewBar();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.F3 && e.Modifiers == (Keys.Control | Keys.Alt))
            {
                ToggleToolbarAndStatusBar();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.F && e.Modifiers == Keys.None)
            {
                ToggleFullScreenMode();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Enter && e.Modifiers == (Keys.Shift | Keys.Alt))
            {
                ToggleDistractionFreeMode();
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None)
            {
                ToggleCinePlayback();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.X && e.Modifiers == Keys.None)
            {
                ToggleCineDirection();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.D1 && e.Modifiers == Keys.Control)
            {
                SetCineStep(1);
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.D2 && e.Modifiers == Keys.Control)
            {
                SetCineStep(2);
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.D3 && e.Modifiers == Keys.Control)
            {
                SetCineStep(3);
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.A && e.Modifiers == Keys.Control)
            {
                SetLoopPointA();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.B && e.Modifiers == Keys.Control)
            {
                SetLoopPointB();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.C && e.Modifiers == (Keys.Control | Keys.Shift))
            {
                ClearLoopRange();
                e.Handled = true;
                return;
            }
            if ((e.KeyCode == Keys.Right || e.KeyCode == Keys.Down || e.KeyCode == Keys.PageDown) && e.Modifiers == Keys.None)
            {
                StopCine();
                NavigateSeriesByStep(1);
                e.Handled = true;
                return;
            }
            if ((e.KeyCode == Keys.Left || e.KeyCode == Keys.Up || e.KeyCode == Keys.PageUp) && e.Modifiers == Keys.None)
            {
                StopCine();
                NavigateSeriesByStep(-1);
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Home && e.Modifiers == Keys.None)
            {
                StopCine();
                NavigateToFirstSlice();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.End && e.Modifiers == Keys.None)
            {
                StopCine();
                NavigateToLastSlice();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.F12 && e.Modifiers == Keys.None && ((mprModeEnabled && currentMprVolume != null) || orthoSeriesSyncEnabled))
            {
                ToggleMprCrosshairVisibility();
                e.Handled = true;
                return;
            }
            if (mprModeEnabled && currentMprVolume != null)
            {
                if (e.KeyCode == Keys.OemOpenBrackets)
                {
                    var step = (e.Modifiers & Keys.Shift) == Keys.Shift ? -5f : -2f;
                    AdjustActiveMprPlaneAngle(step);
                    e.Handled = true;
                    return;
                }
                if (e.KeyCode == Keys.OemCloseBrackets)
                {
                    var step = (e.Modifiers & Keys.Shift) == Keys.Shift ? 5f : 2f;
                    AdjustActiveMprPlaneAngle(step);
                    e.Handled = true;
                    return;
                }
            }
            if (e.KeyCode == Keys.Tab && (e.Modifiers == Keys.None || e.Modifiers == Keys.Shift))
            {
                SwitchActiveViewerByTab(e.Modifiers == Keys.Shift);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void LoadPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (RarArchiveService.IsRarFile(path))
            {
                LoadRar(path);
                return;
            }

            if (ZipArchiveService.IsZipFile(path))
            {
                LoadZip(path);
                return;
            }

            // Очищаем предыдущие временные архивы при открытии обычного пути
            CleanupPreviousZipFolders(null);

            try
            {
                ClearWlHistory();
                SetWlPreset("Auto", null, null, false);
                var dicomDirectory = FindDicomDirectory(path);
                if (dicomDirectory != null)
                {
                    LoadStudies(directoryRepository.Load(dicomDirectory));
                    lastLoadedPath = path;
                    SetStatusText("Открыт DICOMDIR: " + Path.GetFileName(dicomDirectory));
                    return;
                }

                var rasterFiles = Directory.Exists(path)
                    ? Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).Where(IsRaster).ToList()
                    : new List<string>();
                if (IsRaster(path) || (rasterFiles.Count > 0 && !Directory.GetFiles(path, "*.dcm", SearchOption.AllDirectories).Any()))
                {
                    if (IsRaster(path))
                        rasterFiles.Add(path);
                    var uniqueRasterFiles = rasterFiles
                        .Where(file => !string.IsNullOrWhiteSpace(file))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var rasterStudies = BuildRasterStudies(path, uniqueRasterFiles);
                    LoadStudies(rasterStudies);
                    lastLoadedPath = path;
                    SetStatusText(string.Format("Растровых стеков: {0}, изображений: {1}",
                        rasterStudies.SelectMany(study => study.Series).Count(),
                        uniqueRasterFiles.Count));
                    return;
                }

                LoadStudies(repository.Load(path));
                lastLoadedPath = path;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Ошибка загрузки", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void LoadZip(string zipPath)
        {
            if (isLoadingZip)
                return;

            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
                return;

            isLoadingZip = true;
            var prevCursor = Cursor.Current;
            Cursor = Cursors.WaitCursor;
            SetStatusText("Распаковка ZIP-архива: " + Path.GetFileName(zipPath) + "...");

            string extractedDir = null;
            try
            {
                var progressHandler = new Action<string>(msg =>
                {
                    if (InvokeRequired)
                        BeginInvoke(new Action(() => SetStatusText(msg)));
                    else
                        SetStatusText(msg);
                });

                var loadResult = await System.Threading.Tasks.Task.Run(() =>
                {
                    extractedDir = ZipArchiveService.ExtractZip(zipPath, progressHandler);
                    progressHandler("Поиск исследований DICOM в архиве...");

                    // 1. Попытка через DICOMDIR
                    var dicomDir = FindDicomDirectory(extractedDir);
                    if (dicomDir != null)
                    {
                        try
                        {
                            var dirStudies = directoryRepository.Load(dicomDir);
                            if (dirStudies != null && dirStudies.Count > 0)
                                return new { Studies = dirStudies, ExtractedDir = extractedDir };
                        }
                        catch
                        {
                            // fallback к обычному сканированию файлов
                        }
                    }

                    // 2. Попытка прямого сканирования DICOM-файлов
                    var dicomStudies = repository.Load(extractedDir);
                    if (dicomStudies != null && dicomStudies.Count > 0)
                        return new { Studies = dicomStudies, ExtractedDir = extractedDir };

                    // 3. Попытка поиска растровых изображений
                    var rasterFiles = Directory.GetFiles(extractedDir, "*.*", SearchOption.AllDirectories)
                        .Where(IsRaster)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (rasterFiles.Count > 0)
                    {
                        var rasterStudies = BuildRasterStudies(extractedDir, rasterFiles);
                        if (rasterStudies != null && rasterStudies.Count > 0)
                            return new { Studies = rasterStudies, ExtractedDir = extractedDir };
                    }

                    return new { Studies = (List<DicomStudy>)null, ExtractedDir = extractedDir };
                });

                if (loadResult.Studies != null && loadResult.Studies.Count > 0)
                {
                    CleanupPreviousZipFolders(extractedDir);
                    activeTempDirectories.Add(extractedDir);

                    ClearWlHistory();
                    SetWlPreset("Auto", null, null, false);
                    LoadStudies(loadResult.Studies);
                    lastLoadedPath = zipPath;

                    var totalSeries = loadResult.Studies.SelectMany(s => s.Series).Count();
                    var totalImages = loadResult.Studies.SelectMany(s => s.Series).SelectMany(ser => ser.Images).Count();
                    SetStatusText(string.Format("Открыт ZIP: {0} ({1} исслед., {2} серий, {3} снимков)",
                        Path.GetFileName(zipPath), loadResult.Studies.Count, totalSeries, totalImages));
                }
                else
                {
                    ZipArchiveService.CleanupDirectory(extractedDir);
                    MessageBox.Show(this,
                        "В архиве \"" + Path.GetFileName(zipPath) + "\" не найдены поддерживаемые DICOM-файлы или медицинские изображения.",
                        "ZIP архив", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    SetStatusText("В архиве не найдены снимки: " + Path.GetFileName(zipPath));
                }
            }
            catch (Exception ex)
            {
                if (extractedDir != null)
                    ZipArchiveService.CleanupDirectory(extractedDir);

                MessageBox.Show(this,
                    "Ошибка при распаковке или чтении ZIP-архива:\n" + ex.Message,
                    "Ошибка ZIP", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatusText("Ошибка открытия ZIP: " + ex.Message);
            }
            finally
            {
                Cursor = prevCursor ?? Cursors.Default;
                isLoadingZip = false;
            }
        }

        private async void LoadRar(string rarPath)
        {
            if (isLoadingRar)
                return;

            if (string.IsNullOrWhiteSpace(rarPath) || !File.Exists(rarPath))
                return;

            isLoadingRar = true;
            var prevCursor = Cursor.Current;
            Cursor = Cursors.WaitCursor;
            SetStatusText("Распаковка RAR-архива: " + Path.GetFileName(rarPath) + "...");

            string extractedDir = null;
            try
            {
                var progressHandler = new Action<string>(msg =>
                {
                    if (InvokeRequired)
                        BeginInvoke(new Action(() => SetStatusText(msg)));
                    else
                        SetStatusText(msg);
                });

                var loadResult = await System.Threading.Tasks.Task.Run(() =>
                {
                    extractedDir = RarArchiveService.ExtractRar(rarPath, progressHandler);
                    progressHandler("Поиск исследований DICOM в архиве...");

                    // 1. Попытка через DICOMDIR
                    var dicomDir = FindDicomDirectory(extractedDir);
                    if (dicomDir != null)
                    {
                        try
                        {
                            var dirStudies = directoryRepository.Load(dicomDir);
                            if (dirStudies != null && dirStudies.Count > 0)
                                return new { Studies = dirStudies, ExtractedDir = extractedDir };
                        }
                        catch
                        {
                            // fallback к обычному сканированию файлов
                        }
                    }

                    // 2. Попытка прямого сканирования DICOM-файлов
                    var dicomStudies = repository.Load(extractedDir);
                    if (dicomStudies != null && dicomStudies.Count > 0)
                        return new { Studies = dicomStudies, ExtractedDir = extractedDir };

                    // 3. Попытка поиска растровых изображений
                    var rasterFiles = Directory.GetFiles(extractedDir, "*.*", SearchOption.AllDirectories)
                        .Where(IsRaster)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (rasterFiles.Count > 0)
                    {
                        var rasterStudies = BuildRasterStudies(extractedDir, rasterFiles);
                        if (rasterStudies != null && rasterStudies.Count > 0)
                            return new { Studies = rasterStudies, ExtractedDir = extractedDir };
                    }

                    return new { Studies = (List<DicomStudy>)null, ExtractedDir = extractedDir };
                });

                if (loadResult.Studies != null && loadResult.Studies.Count > 0)
                {
                    CleanupPreviousZipFolders(extractedDir);
                    activeTempDirectories.Add(extractedDir);

                    ClearWlHistory();
                    SetWlPreset("Auto", null, null, false);
                    LoadStudies(loadResult.Studies);
                    lastLoadedPath = rarPath;

                    var totalSeries = loadResult.Studies.SelectMany(s => s.Series).Count();
                    var totalImages = loadResult.Studies.SelectMany(s => s.Series).SelectMany(ser => ser.Images).Count();
                    SetStatusText(string.Format("Открыт RAR: {0} ({1} исслед., {2} серий, {3} снимков)",
                        Path.GetFileName(rarPath), loadResult.Studies.Count, totalSeries, totalImages));
                }
                else
                {
                    ZipArchiveService.CleanupDirectory(extractedDir);
                    MessageBox.Show(this,
                        "В архиве \"" + Path.GetFileName(rarPath) + "\" не найдены поддерживаемые DICOM-файлы или медицинские изображения.",
                        "RAR архив", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    SetStatusText("В архиве не найдены снимки: " + Path.GetFileName(rarPath));
                }
            }
            catch (Exception ex)
            {
                if (extractedDir != null)
                    ZipArchiveService.CleanupDirectory(extractedDir);

                MessageBox.Show(this,
                    "Ошибка при распаковке или чтении RAR-архива:\n" + ex.Message,
                    "Ошибка RAR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatusText("Ошибка открытия RAR: " + ex.Message);
            }
            finally
            {
                Cursor = prevCursor ?? Cursors.Default;
                isLoadingRar = false;
            }
        }

        private void CleanupPreviousZipFolders(string currentDirToKeep)
        {
            var dirsToRemove = new List<string>();
            foreach (var dir in activeTempDirectories)
            {
                if (!string.Equals(dir, currentDirToKeep, StringComparison.OrdinalIgnoreCase))
                {
                    ZipArchiveService.CleanupDirectory(dir);
                    dirsToRemove.Add(dir);
                }
            }
            foreach (var dir in dirsToRemove)
            {
                activeTempDirectories.Remove(dir);
            }
        }

        private static List<DicomStudy> BuildRasterStudies(string sourcePath, List<string> rasterFiles)
        {
            var studies = new List<DicomStudy>();
            if (rasterFiles == null || rasterFiles.Count == 0)
                return studies;

            var rootPath = sourcePath;
            if (File.Exists(sourcePath))
                rootPath = Path.GetDirectoryName(sourcePath) ?? sourcePath;
            if (string.IsNullOrWhiteSpace(rootPath))
                rootPath = Environment.CurrentDirectory;

            var grouped = rasterFiles
                .GroupBy(file => Path.GetDirectoryName(file) ?? rootPath, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var studyName = Path.GetFileName(rootPath);
            if (string.IsNullOrWhiteSpace(studyName))
                studyName = "Растровые стеки";

            var study = new DicomStudy
            {
                StudyInstanceUid = "raster:" + rootPath,
                PatientName = "Растровый режим",
                PatientId = string.Empty,
                StudyDate = DateTime.Now,
                StudyDescription = studyName
            };

            var seriesNumber = 1;
            foreach (var folderGroup in grouped)
            {
                var orderedFiles = folderGroup
                    .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (orderedFiles.Count == 0)
                    continue;

                var folderName = Path.GetFileName(folderGroup.Key);
                if (string.IsNullOrWhiteSpace(folderName))
                    folderName = "Stack " + seriesNumber;

                var series = new DicomSeries
                {
                    SeriesInstanceUid = "raster:" + folderGroup.Key,
                    SeriesDescription = folderName,
                    SeriesNumber = seriesNumber
                };

                for (var i = 0; i < orderedFiles.Count; i++)
                {
                    series.Images.Add(new DicomImageItem
                    {
                        FilePath = orderedFiles[i],
                        InstanceNumber = i + 1,
                        SopInstanceUid = orderedFiles[i]
                    });
                }

                DicomSpatialHelper.FinalizeSeriesSpatialMetadata(series);
                study.Series.Add(series);
                seriesNumber++;
            }

            if (study.Series.Count > 0)
                studies.Add(study);
            return studies;
        }

        private void LoadStudies(List<DicomStudy> studies)
        {
                ResetAllParametersToDefault();
                currentStudies.Clear();
                if (studies != null)
                {
                    foreach (var st in studies)
                    {
                        if (st.Series != null)
                        {
                            foreach (var ser in st.Series)
                                DicomSpatialHelper.FinalizeSeriesSpatialMetadata(ser);
                        }
                    }
                    currentStudies.AddRange(studies);
                }
                UpdateSyncSeriesButtonState();
                studiesTree.BeginUpdate();
                studiesTree.Nodes.Clear();
                seriesStrip.SuspendLayout();
                ClearSeriesCards();
                selectedSeriesCard = null;
                images.Clear();
                foreach (var study in studies)
                {
                    var studyNode = new TreeNode(study.DisplayName) { Tag = study };
                    foreach (var series in study.Series)
                    {
                        var seriesNode = new TreeNode(series.DisplayName) { Tag = series };
                        foreach (var image in series.Images)
                            seriesNode.Nodes.Add(new TreeNode(image.ToString()) { Tag = image });
                        studyNode.Nodes.Add(seriesNode);
                        var card = CreateSeriesCard(series);
                        seriesStrip.Controls.Add(card);
                    }
                    studiesTree.Nodes.Add(studyNode);
                }
                studiesTree.EndUpdate();
                seriesStrip.ResumeLayout();
                if (studiesTree.Nodes.Count > 0)
                {
                    var firstStudy = studiesTree.Nodes[0];
                    var firstSeries = firstStudy.FirstNode;
                    var firstImage = firstSeries?.FirstNode;
                    firstStudy.Expand();
                    if (firstSeries != null)
                        firstSeries.Expand();
                    studiesTree.SelectedNode = firstImage ?? firstSeries ?? firstStudy;
                    firstStudy.EnsureVisible();
                }
                if (seriesStrip.Controls.Count > 0)
                {
                    var firstCard = seriesStrip.Controls[0] as Panel;
                    var firstSeries = firstCard != null ? firstCard.Tag as DicomSeries : null;
                    SelectSeries(firstSeries, firstCard);
                }
                if (studies.Count > 0)
                {
                    patientHeader.Text = string.IsNullOrWhiteSpace(studies[0].PatientName) ? "Неизвестный пациент" : studies[0].PatientName;
                    studyHeader.Text = string.Format("{0:dd.MM.yyyy HH:mm}\n{1}\nMR: {2} series",
                        studies[0].StudyDate ?? DateTime.MinValue,
                        string.IsNullOrWhiteSpace(studies[0].StudyDescription) ? "Исследование" : studies[0].StudyDescription,
                        studies.SelectMany(s => s.Series).Count());
                }
                SetStatusText(string.Format("Исследований: {0}", studies.Count));
        }

        private Panel CreateSeriesCard(DicomSeries series)
        {
            var card = new Panel
            {
                Width = 158,
                Height = 118,
                BackColor = Color.FromArgb(70, 70, 70),
                Margin = new Padding(6, 4, 6, 4),
                Padding = new Padding(1),
                Tag = series
            };
            var planeTag = DicomSpatialHelper.GetPlaneShortTag(series.Plane);
            var titlePrefix = string.IsNullOrEmpty(planeTag) ? string.Empty : "[" + planeTag + "] ";
            var title = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(230, 230, 230),
                BackColor = Color.FromArgb(88, 88, 88),
                Font = new Font("Segoe UI", 8f),
                Text = titlePrefix + (string.IsNullOrWhiteSpace(series.SeriesDescription) ? "Серия" : series.SeriesDescription)
            };
            var picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = CreateSeriesThumbnail(series)
            };
            var count = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 18,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.White,
                BackColor = Color.Black,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Text = series.Images.Count.ToString()
            };
            card.Controls.Add(picture);
            card.Controls.Add(count);
            card.Controls.Add(title);
            AttachSeriesClick(card, series);
            AttachSeriesClick(title, series);
            AttachSeriesClick(picture, series);
            AttachSeriesClick(count, series);
            AttachSeriesDrag(card, series);
            AttachSeriesDrag(title, series);
            AttachSeriesDrag(picture, series);
            AttachSeriesDrag(count, series);
            return card;
        }

        private void ClearSeriesCards()
        {
            for (var i = seriesStrip.Controls.Count - 1; i >= 0; i--)
            {
                var control = seriesStrip.Controls[i];
                seriesStrip.Controls.RemoveAt(i);
                DisposePreviewControl(control);
            }
        }

        private static void DisposePreviewControl(Control control)
        {
            var pictureBox = control as PictureBox;
            if (pictureBox != null)
            {
                var image = pictureBox.Image;
                pictureBox.Image = null;
                if (image != null)
                    image.Dispose();
            }

            foreach (Control child in control.Controls)
                DisposePreviewControl(child);

            control.Dispose();
        }

        private void AttachSeriesClick(Control control, DicomSeries series)
        {
            control.Click += delegate
            {
                SelectSeries(series, control as Panel ?? control.Parent as Panel);
            };
        }

        private void AttachSeriesDrag(Control control, DicomSeries series)
        {
            control.MouseDown += delegate (object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left)
                    return;
                pendingDragSeries = series;
                seriesDragStartPoint = e.Location;
            };
            control.MouseMove += delegate (object sender, MouseEventArgs e)
            {
                if ((e.Button & MouseButtons.Left) != MouseButtons.Left || pendingDragSeries != series)
                    return;

                var dragSize = SystemInformation.DragSize;
                var dragRect = new Rectangle(
                    seriesDragStartPoint.X - dragSize.Width / 2,
                    seriesDragStartPoint.Y - dragSize.Height / 2,
                    dragSize.Width,
                    dragSize.Height);
                if (dragRect.Contains(e.Location))
                    return;

                var data = new DataObject();
                data.SetData(SeriesDragDataFormat, series);
                data.SetData(typeof(DicomSeries), series);
                control.DoDragDrop(data, DragDropEffects.Copy);
                pendingDragSeries = null;
            };
            control.MouseUp += delegate { pendingDragSeries = null; };
        }

        private void SelectSeries(DicomSeries series, Panel card)
        {
            if (series == null || series.Images.Count == 0)
                return;
            if (mprModeEnabled)
                DisableMprMode(false);
            UpdateSelectedSeriesCard(card);
            var targetViewer = ActiveViewerControl;
            if (targetViewer != null)
            {
                var st = GetViewerState(targetViewer);
                st.AssignedSeries = series;
                st.AssignedSyncPlane = series.Plane;
                targetViewer.Inverted = false;
                targetViewer.ResetView();
            }
            images.Clear();
            images.AddRange(series.Images);
            ShowImage(0);
            if (orthoSeriesSyncEnabled && targetViewer != null)
            {
                ConfigureViewerOrthoSyncAppearance(targetViewer, series.Plane);
                SyncAllOrthoPanelsToCurrentPoint(targetViewer, true);
            }
        }

        private void UpdateSelectedSeriesCard(Panel card)
        {
            if (selectedSeriesCard != null)
                selectedSeriesCard.BackColor = Color.FromArgb(70, 70, 70);
            selectedSeriesCard = card;
            if (selectedSeriesCard != null)
                selectedSeriesCard.BackColor = Color.FromArgb(110, 78, 78);
        }

        private Panel FindSeriesCard(DicomSeries series)
        {
            if (series == null || seriesStrip == null)
                return null;

            foreach (Control control in seriesStrip.Controls)
            {
                var panel = control as Panel;
                var tagged = panel != null ? panel.Tag as DicomSeries : null;
                if (tagged == null)
                    continue;

                if (ReferenceEquals(tagged, series))
                    return panel;
                if (!string.IsNullOrWhiteSpace(tagged.SeriesInstanceUid) &&
                    string.Equals(tagged.SeriesInstanceUid, series.SeriesInstanceUid, StringComparison.OrdinalIgnoreCase))
                    return panel;
            }

            return null;
        }

        private Image CreateSeriesThumbnail(DicomSeries series)
        {
            if (series.Images.Count == 0 || string.IsNullOrWhiteSpace(series.Images[0].FilePath))
                return new Bitmap(96, 86);
            try
            {
                var path = series.Images[0].FilePath;
                if (IsRaster(path))
                {
                    using (var source = Image.FromFile(path))
                        return new Bitmap(source, new Size(96, 86));
                }

                using (var dicom = new DicomImage(DicomFile.Open(path).Dataset).RenderImage().AsClonedBitmap())
                    return new Bitmap(dicom, new Size(96, 86));
            }
            catch
            {
                return new Bitmap(96, 86);
            }
        }

        private static string FindDicomDirectory(string path)
        {
            if (File.Exists(path) && string.Equals(Path.GetFileName(path), "DICOMDIR", StringComparison.OrdinalIgnoreCase))
                return path;
            if (!Directory.Exists(path))
                return null;
            return Directory.GetFiles(path, "DICOMDIR", SearchOption.AllDirectories).FirstOrDefault();
        }

        private void OnTreeSelect(object sender, TreeViewEventArgs e)
        {
            var image = e.Node.Tag as DicomImageItem;
            if (image == null)
            {
                var series = e.Node.Tag as DicomSeries;
                if (series != null && series.Images.Count > 0)
                    image = series.Images[0];
            }
            if (image == null)
                return;
            images.Clear();
            var seriesNode = e.Node.Parent;
            if (seriesNode != null && seriesNode.Tag is DicomSeries)
                images.AddRange(((DicomSeries)seriesNode.Tag).Images);
            else
                images.Add(image);
            ShowImage(Math.Max(0, images.IndexOf(image)));
        }

        private void ShowImage(int index)
        {
            if (index < 0 || index >= images.Count)
                return;

            SaveCurrentImageMeasurements();

            var targetViewer = ActiveViewerControl;

            var path = images[index].FilePath;
            Image loaded = null;
            DicomDataset dataset = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    throw new FileNotFoundException("Файл изображения не найден.", path ?? string.Empty);

                if (IsRaster(path))
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var rawRaster = Image.FromStream(stream))
                    {
                        loaded = CreateSafeBitmapCopy(rawRaster);
                    }
                    targetViewer.PixelDataProvider = null;
                    targetViewer.SetPixelSpacing(0f, 0f);
                    currentWindowLine = "WL: — WW: —";
                    autoWlCenter = null;
                    autoWlWidth = null;
                    UpdateInfoValues("—", "—", "—", "Растровое изображение", Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), "—", "—", (index + 1).ToString(), string.Format("{0} × {1}", loaded.Width, loaded.Height), Path.GetFileName(path));
                }
                else
                {
                    var dicomFile = DicomFile.Open(path);
                    dataset = dicomFile.Dataset;
                    var provider = DicomPixelDataProvider.Create(dataset, 0);
                    targetViewer.PixelDataProvider = provider;
                    SetPixelSpacing(targetViewer, dataset);
                    autoWlCenter = TryParseDicomFloat(GetTag(dataset, DicomTag.WindowCenter)) ?? (provider != null ? (float?)provider.DefaultWindowCenter : null);
                    autoWlWidth = TryParseDicomFloat(GetTag(dataset, DicomTag.WindowWidth)) ?? (provider != null ? (float?)provider.DefaultWindowWidth : null);
                    currentWindowLine = autoWlCenter.HasValue && autoWlWidth.HasValue
                        ? string.Format("WL: {0:0.#} WW: {1:0.#}", autoWlCenter.Value, autoWlWidth.Value)
                        : string.Format("WL: {0} WW: {1}", GetTag(dataset, DicomTag.WindowCenter), GetTag(dataset, DicomTag.WindowWidth));

                    float initialWc = wlPresetCenter ?? autoWlCenter ?? 128f;
                    float initialWw = wlPresetWidth ?? autoWlWidth ?? 256f;

                    if (provider != null)
                    {
                        loaded = provider.RenderBitmap(initialWc, initialWw, targetViewer.Inverted);
                    }
                    if (loaded == null)
                    {
                        using (var rawDicom = new DicomImage(dataset).RenderImage().AsClonedBitmap())
                        {
                            loaded = CreateSafeBitmapCopy(rawDicom);
                        }
                    }
                    UpdateInfoFromDicom(dataset, loaded, path);
                }

                targetViewer.SetImage(loaded, false);
                UpdateRenderedImageCache(loaded);
                loaded = null;
                imageIndex = index;
                currentImagePath = path;
                List<DicomMeasurement> storedMeasurements;
                if (measurementsByImagePath.TryGetValue(path, out storedMeasurements))
                    targetViewer.SetMeasurements(storedMeasurements);
                else
                    targetViewer.SetMeasurements(null);
                RefreshMeasurementsList();
                ApplyCurrentWlToViewer();
                UpdateViewerOverlay(index, dataset);
                UpdateSliceScrollBar(index);
                SetStatusText(string.Format("{0} / {1}: {2}", index + 1, images.Count, Path.GetFileName(path)));
                previousButton.Enabled = index > 0;
                nextButton.Enabled = index < images.Count - 1;
                CaptureStateFromWorkingSet(targetViewer);
            }
            catch (Exception ex)
            {
                if (loaded != null)
                    loaded.Dispose();
                SetStatusText("Ошибка открытия изображения: " + Path.GetFileName(path));
                MessageBox.Show(this, ex.Message, "Ошибка изображения", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnViewerMeasurementsChanged()
        {
            SaveCurrentImageMeasurements();
            RefreshMeasurementsList();
        }

        private void SaveCurrentImageMeasurements()
        {
            if (string.IsNullOrWhiteSpace(currentImagePath))
                return;

            var measurements = ActiveViewerControl.GetMeasurements();
            if (measurements.Count == 0)
                measurementsByImagePath.Remove(currentImagePath);
            else
                measurementsByImagePath[currentImagePath] = measurements;
        }

        private Guid? GetSelectedMeasurementIdFromList()
        {
            if (measurementsListView.SelectedItems.Count == 0)
                return null;
            var item = measurementsListView.SelectedItems[0];
            if (item.Tag is Guid)
                return (Guid)item.Tag;
            return null;
        }

        private void RefreshMeasurementsList(Guid? preferredSelectedId = null)
        {
            if (measurementsListView == null)
                return;

            var selectedId = preferredSelectedId.HasValue ? preferredSelectedId : GetSelectedMeasurementIdFromList();
            measurementsListView.BeginUpdate();
            measurementsListView.Items.Clear();
            var targetViewer = ActiveViewerControl;
            var measurements = targetViewer.GetMeasurements();
            ListViewItem selectedItem = null;
            foreach (var measurement in measurements)
            {
                var title = string.IsNullOrWhiteSpace(measurement.Name)
                    ? measurement.Mode.ToString()
                    : measurement.Name;
                var item = new ListViewItem(title) { Tag = measurement.Id };
                item.SubItems.Add(targetViewer.FormatMeasurement(measurement));
                measurementsListView.Items.Add(item);

                if (selectedId.HasValue && measurement.Id == selectedId.Value)
                    selectedItem = item;
            }

            if (selectedItem != null)
            {
                selectedItem.Selected = true;
                selectedItem.Focused = true;
                selectedItem.EnsureVisible();
            }

            measurementsListView.EndUpdate();
        }

        private bool DeleteMeasurementAndSelectNext(Guid id)
        {
            var targetViewer = ActiveViewerControl;
            var measurements = targetViewer.GetMeasurements();
            var index = measurements.FindIndex(m => m.Id == id);
            if (index < 0)
                return false;

            var deleted = measurements[index];
            Guid? nextId = null;

            for (var i = index + 1; i < measurements.Count; i++)
            {
                if (measurements[i].Mode == deleted.Mode)
                {
                    nextId = measurements[i].Id;
                    break;
                }
            }

            if (!nextId.HasValue)
            {
                for (var i = index - 1; i >= 0; i--)
                {
                    if (measurements[i].Mode == deleted.Mode)
                    {
                        nextId = measurements[i].Id;
                        break;
                    }
                }
            }

            if (!nextId.HasValue)
            {
                if (index + 1 < measurements.Count)
                    nextId = measurements[index + 1].Id;
                else if (index - 1 >= 0)
                    nextId = measurements[index - 1].Id;
            }

            if (!targetViewer.RemoveMeasurement(id))
                return false;

            if (nextId.HasValue)
                targetViewer.SelectMeasurement(nextId.Value);

            SaveCurrentImageMeasurements();
            RefreshMeasurementsList(nextId);
            return true;
        }

        private void UpdateSliceScrollBar(int index)
        {
            updatingSliceScrollBar = true;
            if (images.Count <= 1)
            {
                sliceScrollBar.Visible = false;
                sliceScrollBar.Value = 0;
                updatingSliceScrollBar = false;
                return;
            }

            sliceScrollBar.Visible = true;
            sliceScrollBar.Minimum = 0;
            sliceScrollBar.Maximum = images.Count - 1;
            sliceScrollBar.LargeChange = 1;
            sliceScrollBar.SmallChange = 1;
            sliceScrollBar.Value = Math.Max(sliceScrollBar.Minimum, Math.Min(sliceScrollBar.Maximum, index));
            updatingSliceScrollBar = false;
        }

        private void OnSliceScrollBarValueChanged(object sender, EventArgs e)
        {
            if (updatingSliceScrollBar)
                return;
            if (mprModeEnabled && currentMprVolume != null)
            {
                SelectMprSliceForActiveViewer(sliceScrollBar.Value);
                return;
            }
            SelectImage(sliceScrollBar.Value);
        }

        private void OnViewerSliceScrollRequested(DicomImageControl sourceViewer, int delta)
        {
            if (sourceViewer != null && sourceViewer != ActiveViewerControl)
                SetActiveViewer(sourceViewer);

            if (delta == 0)
                return;
            NavigateSeriesByStep(delta > 0 ? -1 : 1);
        }

        private void OnViewerWindowLevelChanged()
        {
            if ((DateTime.UtcNow - lastWlHistoryCaptureUtc).TotalMilliseconds > 220)
            {
                SaveCurrentWlStateForUndo();
                wlRedoStack.Clear();
                lastWlHistoryCaptureUtc = DateTime.UtcNow;
            }
            wlPresetName = "Manual";
            wlPresetCenter = ActiveViewerControl.WindowCenter;
            wlPresetWidth = ActiveViewerControl.WindowWidth;
            currentAppliedWlCenter = ActiveViewerControl.WindowCenter;
            currentAppliedWlWidth = ActiveViewerControl.WindowWidth;
            RefreshWlPresetCaption();
            if (mprModeEnabled && currentMprVolume != null)
            {
                SyncSecondaryViewers();
                UpdateAllMprCrosshairsAndOverlays();
                return;
            }
            if (images.Count > 0)
                UpdateViewerOverlay(imageIndex, null);
        }

        private void UpdateViewerOverlay(int index, DicomDataset dataset)
        {
            var targetViewer = ActiveViewerControl;
            var imageCounter = string.Format("{0}/{1}", index + 1, Math.Max(1, images.Count));
            var seriesNumber = infoValues["seriesNumber"].Text;
            var instance = infoValues["instanceNumber"].Text;

            targetViewer.OverlayTopLeft = string.Format("Im: {0}\nSe: {1}", imageCounter, string.IsNullOrWhiteSpace(seriesNumber) ? "—" : seriesNumber);
            targetViewer.OverlayTopRight = string.Format("{0}\n{1}\n{2}\n{3}\n{4}",
                infoValues["patient"].Text,
                infoValues["patientId"].Text,
                infoValues["studyDate"].Text,
                infoValues["studyDescription"].Text,
                infoValues["seriesDescription"].Text);

            var modality = infoValues["modality"].Text;
            var dimensions = infoValues["dimensions"].Text;
            var windowLine = targetViewer.WindowLevelEnabled
                ? string.Format("WL: {0:0.#} WW: {1:0.#} [{2}]", targetViewer.WindowCenter, targetViewer.WindowWidth, wlPresetName)
                : currentWindowLine;
            targetViewer.OverlayBottomLeft = string.Format("{0}\n{1}\nImNo: {2}", windowLine, dimensions, instance);
            targetViewer.OverlayBottomRight = string.Format("{0}\n{1}", modality, infoValues["file"].Text);
            targetViewer.Annotation = string.Empty;
            if (orthoSeriesSyncEnabled)
                UpdateOrthoSyncViewerOverlay(targetViewer);
            targetViewer.Invalidate();
            SyncSecondaryViewers();
        }

        private float? TryParseDicomFloat(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw == "—")
                return null;
            var token = raw.Split('\\').FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token))
                return null;
            float value;
            if (float.TryParse(token.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return value;
            if (float.TryParse(token.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return value;
            return null;
        }

        private void SetPixelSpacing(DicomImageControl target, DicomDataset dataset)
        {
            if (target == null)
                return;

            try
            {
                if (dataset != null)
                {
                    if (dataset.Contains(DicomTag.PixelSpacing))
                    {
                        var values = dataset.GetValues<string>(DicomTag.PixelSpacing);
                        double spacingY;
                        double spacingX;
                        if (values != null && values.Length >= 2
                            && double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out spacingY)
                            && double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out spacingX)
                            && spacingY > 0 && spacingX > 0)
                        {
                            target.SetPixelSpacing((float)spacingX, (float)spacingY);
                            return;
                        }
                    }

                    if (dataset.Contains(DicomTag.ImagerPixelSpacing))
                    {
                        var values = dataset.GetValues<string>(DicomTag.ImagerPixelSpacing);
                        double spacingY;
                        double spacingX;
                        if (values != null && values.Length >= 2
                            && double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out spacingY)
                            && double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out spacingX)
                            && spacingY > 0 && spacingX > 0)
                        {
                            target.SetPixelSpacing((float)spacingX, (float)spacingY);
                            return;
                        }
                    }

                    if (dataset.Contains(DicomTag.NominalScannedPixelSpacing))
                    {
                        var values = dataset.GetValues<string>(DicomTag.NominalScannedPixelSpacing);
                        double spacingY;
                        double spacingX;
                        if (values != null && values.Length >= 2
                            && double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out spacingY)
                            && double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out spacingX)
                            && spacingY > 0 && spacingX > 0)
                        {
                            target.SetPixelSpacing((float)spacingX, (float)spacingY);
                            return;
                        }
                    }

                    if (dataset.Contains(DicomTag.PixelAspectRatio))
                    {
                        var values = dataset.GetValues<string>(DicomTag.PixelAspectRatio);
                        int ratioY;
                        int ratioX;
                        if (values != null && values.Length >= 2
                            && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ratioY)
                            && int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ratioX)
                            && ratioY > 0 && ratioX > 0)
                        {
                            target.SetPixelSpacing((float)ratioX, (float)ratioY);
                            return;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            target.SetPixelSpacing(0f, 0f);
        }

        private void UpdateInfoFromDicom(DicomDataset dataset, Image image, string path)
        {
            var rows = GetTagInt(dataset, DicomTag.Rows);
            var columns = GetTagInt(dataset, DicomTag.Columns);
            var date = GetTag(dataset, DicomTag.StudyDate);
            if (date.Length == 8)
                date = string.Format("{0}-{1}-{2}", date.Substring(0, 4), date.Substring(4, 2), date.Substring(6, 2));
            UpdateInfoValues(
                GetTag(dataset, DicomTag.PatientName),
                GetTag(dataset, DicomTag.PatientID),
                date,
                GetTag(dataset, DicomTag.StudyDescription),
                GetTag(dataset, DicomTag.Modality),
                GetTag(dataset, DicomTag.SeriesDescription),
                GetTag(dataset, DicomTag.SeriesNumber),
                GetTag(dataset, DicomTag.InstanceNumber),
                rows > 0 && columns > 0 ? string.Format("{0} × {1}", columns, rows) : string.Format("{0} × {1}", image.Width, image.Height),
                Path.GetFileName(path));
        }

        private static string GetTag(DicomDataset dataset, DicomTag tag)
        {
            if (dataset == null || !dataset.Contains(tag))
                return "—";
            try
            {
                string value;
                if (dataset.TryGetSingleValue(tag, out value) && !string.IsNullOrWhiteSpace(value))
                    return value;
                var raw = dataset.GetString(tag);
                if (!string.IsNullOrWhiteSpace(raw))
                    return raw;
            }
            catch
            {
            }
            return "—";
        }

        private static int GetTagInt(DicomDataset dataset, DicomTag tag)
        {
            int value;
            return dataset.TryGetSingleValue(tag, out value) ? value : 0;
        }

        private void SelectImage(int index)
        {
            if (index >= 0 && index < images.Count)
            {
                ShowImage(index);
                if (orthoSeriesSyncEnabled)
                    OnOrthoPanelSliceScrolled(ActiveViewerControl, index);
            }
        }

        private static bool IsRaster(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png";
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
                LoadPath(files[0]);
        }

        private ToolStripDropDownButton CreateMprToolbarButton()
        {
            var button = new ToolStripDropDownButton(CreateIcon("mpr"))
            {
                AutoSize = false,
                Width = 64,
                Height = 22,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextAlign = ContentAlignment.MiddleRight,
                ImageAlign = ContentAlignment.MiddleLeft,
                Text = "MPR",
                ToolTipText = "Мультипланарная реконструкция (MPR / MIP / MinIP) — Ctrl+M"
            };

            var toggleItem = new ToolStripMenuItem("Включить / выключить MPR (2×2)", null, delegate { ToggleMprMode(); })
            {
                ShortcutKeyDisplayString = "Ctrl+M",
                ShowShortcutKeys = true
            };
            var modeMprItem = new ToolStripMenuItem("Режим: MPR (тонкий срез)", null, delegate { SetMprRenderMode(MprRenderMode.Mpr); });
            var modeMipItem = new ToolStripMenuItem("Режим: MIP (максимальная интенсивность)", null, delegate { SetMprRenderMode(MprRenderMode.Mip); });
            var modeMinIpItem = new ToolStripMenuItem("Режим: MinIP (минимальная интенсивность)", null, delegate { SetMprRenderMode(MprRenderMode.MinIp); });
            var modeAvgItem = new ToolStripMenuItem("Режим: Average (усреднение слоя / Thick MPR)", null, delegate { SetMprRenderMode(MprRenderMode.Average); });

            var slabMenu = new ToolStripMenuItem("Толщина слоя (Slab)");
            var thicknesses = new[] { 1f, 3f, 5f, 10f, 20f, 30f };
            var slabItems = new List<ToolStripMenuItem>();
            foreach (var mm in thicknesses)
            {
                var capturedMm = mm;
                var item = new ToolStripMenuItem(string.Format(CultureInfo.InvariantCulture, "{0:0.#} мм", capturedMm), null, delegate { SetMprSlabThickness(capturedMm); })
                {
                    Tag = capturedMm
                };
                slabItems.Add(item);
                slabMenu.DropDownItems.Add(item);
            }

            var centerCrosshairItem = new ToolStripMenuItem("Центрировать 3D-перекрестие", null, delegate { ResetMprCrosshairToCenter(); });
            var showCrosshairItem = new ToolStripMenuItem("Показывать оси 3D-перекрестия", null, delegate { ToggleMprCrosshairVisibility(); })
            {
                ShortcutKeyDisplayString = "F12"
            };

            var obliqueMenu = new ToolStripMenuItem("Наклон осей (Oblique MPR)");
            var obliqueAngles = new[] { 0f, -45f, -30f, -15f, 15f, 30f, 45f };
            var obliqueItems = new List<ToolStripMenuItem>();
            foreach (var deg in obliqueAngles)
            {
                var capturedDeg = deg;
                var label = Math.Abs(capturedDeg) < 0.05f
                    ? "0° (Ортогонально)"
                    : string.Format(CultureInfo.InvariantCulture, "{0:+0;-0;0}°", capturedDeg);
                var item = new ToolStripMenuItem(label, null, delegate { SetMprObliqueAngle(capturedDeg); })
                {
                    Tag = capturedDeg
                };
                obliqueItems.Add(item);
                obliqueMenu.DropDownItems.Add(item);
            }
            obliqueMenu.DropDownItems.Add(new ToolStripSeparator());
            obliqueMenu.DropDownItems.Add(new ToolStripMenuItem("Повернуть активную ось на -5°", null, delegate { AdjustActiveMprPlaneAngle(-5f); }) { ShortcutKeyDisplayString = "[" });
            obliqueMenu.DropDownItems.Add(new ToolStripMenuItem("Повернуть активную ось на +5°", null, delegate { AdjustActiveMprPlaneAngle(5f); }) { ShortcutKeyDisplayString = "]" });
            obliqueMenu.DropDownItems.Add(new ToolStripSeparator());
            obliqueMenu.DropDownItems.Add(new ToolStripMenuItem("Сбросить все углы наклона (0°)", null, delegate { ResetAllMprAngles(); }));

            var toggleMaximizeItem = new ToolStripMenuItem("Развернуть / свернуть активную проекцию", null, delegate { OnViewerMprDoubleClick(ActiveViewerControl); })
            {
                ShortcutKeyDisplayString = "Двойной клик"
            };
            var exportCollageItem = new ToolStripMenuItem("Экспорт коллажа MPR (все 4 проекции)...", null, delegate { ExportMprCollage(); });
            var exportSeriesItem = new ToolStripMenuItem("Экспорт серии срезов активной проекции в папку...", null, delegate { ExportMprSeriesToFolder(); });

            button.DropDownItems.Add(toggleItem);
            button.DropDownItems.Add(new ToolStripSeparator());
            button.DropDownItems.Add(modeMprItem);
            button.DropDownItems.Add(modeMipItem);
            button.DropDownItems.Add(modeMinIpItem);
            button.DropDownItems.Add(modeAvgItem);
            button.DropDownItems.Add(new ToolStripSeparator());
            button.DropDownItems.Add(slabMenu);
            button.DropDownItems.Add(obliqueMenu);
            button.DropDownItems.Add(new ToolStripSeparator());
            button.DropDownItems.Add(showCrosshairItem);
            button.DropDownItems.Add(centerCrosshairItem);
            button.DropDownItems.Add(toggleMaximizeItem);
            button.DropDownItems.Add(new ToolStripSeparator());
            button.DropDownItems.Add(exportCollageItem);
            button.DropDownItems.Add(exportSeriesItem);

            button.DropDownOpening += delegate
            {
                toggleItem.Checked = mprModeEnabled;
                modeMprItem.Checked = currentMprRenderMode == MprRenderMode.Mpr;
                modeMipItem.Checked = currentMprRenderMode == MprRenderMode.Mip;
                modeMinIpItem.Checked = currentMprRenderMode == MprRenderMode.MinIp;
                modeAvgItem.Checked = currentMprRenderMode == MprRenderMode.Average;
                foreach (var slabItem in slabItems)
                {
                    var val = (float)slabItem.Tag;
                    slabItem.Checked = Math.Abs(currentMprSlabThicknessMm - val) < 0.05f;
                }
                var activePlaneAngle = GetActiveMprPlaneAngle();
                foreach (var obItem in obliqueItems)
                {
                    var val = (float)obItem.Tag;
                    obItem.Checked = Math.Abs(activePlaneAngle - val) < 0.1f;
                }
                showCrosshairItem.Enabled = mprModeEnabled && currentMprVolume != null;
                showCrosshairItem.Checked = mprModeEnabled && mprShowCrosshair;
                centerCrosshairItem.Enabled = mprModeEnabled && currentMprVolume != null;
                toggleMaximizeItem.Enabled = mprModeEnabled && currentMprVolume != null;
                toggleMaximizeItem.Checked = mprMaximizedViewer != null;
                exportCollageItem.Enabled = mprModeEnabled && currentMprVolume != null;
                exportSeriesItem.Enabled = mprModeEnabled && currentMprVolume != null;
            };

            return button;
        }

        private void UpdateMprButtonCaption()
        {
            if (mprToolbarButton == null)
                return;

            if (!mprModeEnabled)
            {
                mprToolbarButton.Text = "MPR";
                mprToolbarButton.BackColor = Color.FromArgb(164, 164, 164);
                return;
            }

            var modeShort = currentMprRenderMode == MprRenderMode.Mip
                ? "MIP"
                : currentMprRenderMode == MprRenderMode.MinIp
                    ? "MinIP"
                    : currentMprRenderMode == MprRenderMode.Average
                        ? "Avg"
                        : "MPR";
            mprToolbarButton.Text = modeShort;
            mprToolbarButton.BackColor = Color.FromArgb(128, 168, 214);
        }

        private void ToggleMprMode()
        {
            if (mprModeEnabled)
                DisableMprMode(true);
            else
                EnableMprMode();
        }

        private void EnableMprMode()
        {
            var sourceImages = images.Count >= 3 ? images.ToList() : null;
            if (sourceImages == null || sourceImages.Count < 3)
            {
                foreach (var study in currentStudies)
                {
                    foreach (var series in study.Series)
                    {
                        if (series.Images != null && series.Images.Count >= 3)
                        {
                            sourceImages = series.Images.ToList();
                            break;
                        }
                    }
                    if (sourceImages != null)
                        break;
                }
            }

            if (sourceImages == null || sourceImages.Count < 3)
            {
                MessageBox.Show(
                    this,
                    "Для построения мультипланарной реконструкции (MPR) требуется серия минимум из 3 срезов.",
                    "MPR реконструкция",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            StopCine();
            SaveCurrentImageMeasurements();

            var previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                SetStatusText("Построение 3D-объёма MPR...");
                Application.DoEvents();

                var volume = MprVolumeService.BuildVolume(sourceImages, null);
                if (volume == null)
                {
                    MessageBox.Show(
                        this,
                        "Не удалось построить 3D-объём из выбранной серии.",
                        "Ошибка MPR",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                currentMprVolume = volume;
                mprModeEnabled = true;
                mprShowCrosshair = true;
                mprObliqueAngleDegrees = 0f;
                mprAngleCoronalDegrees = 0f;
                mprAngleSagittalDegrees = 0f;
                mprMaximizedViewer = null;
                if (mprToggleMenuItem != null)
                    mprToggleMenuItem.Checked = true;
                UpdateMprButtonCaption();

                mprVoxelX = volume.Width / 2;
                mprVoxelY = volume.Height / 2;
                mprVoxelZ = volume.Depth / 2;

                if (!wlPresetCenter.HasValue || !wlPresetWidth.HasValue)
                {
                    autoWlCenter = volume.DefaultWindowCenter;
                    autoWlWidth = volume.DefaultWindowWidth;
                    currentAppliedWlCenter = volume.DefaultWindowCenter;
                    currentAppliedWlWidth = volume.DefaultWindowWidth;
                }

                ApplyViewerLayoutPreset(2, 2, false);
                var orderedViewers = GetOrderedViewers();
                var planes = new[]
                {
                    MprPlane.Axial,
                    MprPlane.Coronal,
                    MprPlane.Sagittal,
                    MprPlane.MipCoronal
                };

                for (var i = 0; i < orderedViewers.Count && i < planes.Length; i++)
                {
                    var v = orderedViewers[i];
                    var st = GetViewerState(v);
                    st.AssignedMprPlane = planes[i];
                    st.AssignedMprRenderMode = planes[i] == MprPlane.MipCoronal ? (MprRenderMode?)MprRenderMode.Mip : null;
                    st.AssignedMprSlabThicknessMm = planes[i] == MprPlane.MipCoronal ? (float?)15f : null;
                    ConfigureViewerMprAppearance(v, planes[i]);
                }

                RefreshAllMprViewers(true);
                SetActiveViewer(viewer);
                UpdateMprScrollBarForActiveViewer();
                UpdateMprDicomInfoPanel();
                SetStatusText(string.Format(
                    "MPR активирован: объём {0}×{1}×{2} (шаг {3:0.##}×{4:0.##}×{5:0.##} мм)",
                    volume.Width,
                    volume.Height,
                    volume.Depth,
                    volume.SpacingX,
                    volume.SpacingY,
                    volume.SpacingZ));
            }
            finally
            {
                Cursor = previousCursor ?? Cursors.Default;
            }
        }

        private void DisableMprMode(bool restoreSingleLayout)
        {
            if (!mprModeEnabled)
                return;

            SaveCurrentImageMeasurements();
            mprModeEnabled = false;
            currentMprVolume = null;
            mprShowCrosshair = true;
            mprObliqueAngleDegrees = 0f;
            mprAngleCoronalDegrees = 0f;
            mprAngleSagittalDegrees = 0f;
            mprMaximizedViewer = null;
            mprCrosshairDragging = false;
            if (mprToggleMenuItem != null)
                mprToggleMenuItem.Checked = false;
            UpdateMprButtonCaption();

            foreach (var v in viewerHosts.Keys)
            {
                var st = GetViewerState(v);
                st.AssignedMprPlane = null;
                st.AssignedMprRenderMode = null;
                st.AssignedMprSlabThicknessMm = null;
                v.SetReferencePoint(null);
                v.ReferenceLineColorHorizontal = null;
                v.ReferenceLineColorVertical = null;
                v.ReferenceLineAngleDegrees = 0f;
                v.ReferenceSlabThicknessMm = 1f;
                v.MprPanelEnabled = false;
                v.MprInteractiveCrosshairEnabled = false;
                v.OrientationTop = "A";
                v.OrientationBottom = "P";
                v.OrientationLeft = "R";
                v.OrientationRight = "L";
            }

            if (restoreSingleLayout)
            {
                ApplyViewerLayoutPreset(1, 1, false);
                SetActiveViewer(viewer);
                if (images.Count > 0)
                    ShowImage(Math.Max(0, Math.Min(imageIndex, images.Count - 1)));
                SetStatusText("Режим MPR выключен");
            }
        }

        private void ConfigureViewerMprAppearance(DicomImageControl targetViewer, MprPlane plane)
        {
            if (targetViewer == null)
                return;

            targetViewer.MprPanelEnabled = true;
            targetViewer.ShowReferenceLines = mprShowCrosshair;
            targetViewer.MprInteractiveCrosshairEnabled = mprShowCrosshair;
            var axialColor = Color.FromArgb(225, 0, 215, 255);     // Голубой (Axial Z)
            var coronalColor = Color.FromArgb(225, 80, 235, 100);  // Зелёный (Coronal Y)
            var sagittalColor = Color.FromArgb(225, 255, 210, 50); // Жёлтый (Sagittal X)

            switch (plane)
            {
                case MprPlane.Axial:
                    targetViewer.OrientationTop = "A";
                    targetViewer.OrientationBottom = "P";
                    targetViewer.OrientationLeft = "R";
                    targetViewer.OrientationRight = "L";
                    targetViewer.ReferenceLineColorHorizontal = coronalColor;
                    targetViewer.ReferenceLineColorVertical = sagittalColor;
                    break;
                case MprPlane.Coronal:
                case MprPlane.MipCoronal:
                    targetViewer.OrientationTop = "S";
                    targetViewer.OrientationBottom = "I";
                    targetViewer.OrientationLeft = "R";
                    targetViewer.OrientationRight = "L";
                    targetViewer.ReferenceLineColorHorizontal = axialColor;
                    targetViewer.ReferenceLineColorVertical = sagittalColor;
                    break;
                case MprPlane.Sagittal:
                    targetViewer.OrientationTop = "S";
                    targetViewer.OrientationBottom = "I";
                    targetViewer.OrientationLeft = "A";
                    targetViewer.OrientationRight = "P";
                    targetViewer.ReferenceLineColorHorizontal = axialColor;
                    targetViewer.ReferenceLineColorVertical = coronalColor;
                    break;
            }
        }

        private MprRenderMode GetViewerMprRenderMode(DicomImageControl targetViewer, MprPlane plane)
        {
            if (targetViewer != null)
            {
                var st = GetViewerState(targetViewer);
                if (st.AssignedMprRenderMode.HasValue)
                    return st.AssignedMprRenderMode.Value;
            }

            if (plane == MprPlane.MipCoronal)
                return currentMprRenderMode == MprRenderMode.MinIp ? MprRenderMode.MinIp : MprRenderMode.Mip;

            return currentMprRenderMode;
        }

        private float GetViewerMprSlabThickness(DicomImageControl targetViewer, MprPlane plane)
        {
            if (targetViewer != null)
            {
                var st = GetViewerState(targetViewer);
                if (st.AssignedMprSlabThicknessMm.HasValue)
                    return st.AssignedMprSlabThicknessMm.Value;
            }

            if (plane == MprPlane.MipCoronal)
                return Math.Max(15f, currentMprSlabThicknessMm);

            return currentMprSlabThicknessMm;
        }

        private void SetViewerMprRenderMode(DicomImageControl targetViewer, MprRenderMode? mode)
        {
            if (!mprModeEnabled || currentMprVolume == null || targetViewer == null)
                return;

            var st = GetViewerState(targetViewer);
            st.AssignedMprRenderMode = mode;
            if (mode.HasValue && (mode.Value == MprRenderMode.Mip || mode.Value == MprRenderMode.MinIp || mode.Value == MprRenderMode.Average))
            {
                var curSlab = GetViewerMprSlabThickness(targetViewer, st.AssignedMprPlane ?? MprPlane.Axial);
                if (curSlab <= 1.05f)
                    st.AssignedMprSlabThicknessMm = 10f;
            }

            var plane = st.AssignedMprPlane ?? MprPlane.Axial;
            RefreshMprPlaneViewer(targetViewer, plane, false);
            UpdateAllMprCrosshairsAndOverlays();
            UpdateMprDicomInfoPanel();
        }

        private void SetViewerMprSlabThickness(DicomImageControl targetViewer, float? thicknessMm)
        {
            if (!mprModeEnabled || currentMprVolume == null || targetViewer == null)
                return;

            var st = GetViewerState(targetViewer);
            st.AssignedMprSlabThicknessMm = thicknessMm.HasValue ? (float?)Math.Max(0.5f, thicknessMm.Value) : null;
            var plane = st.AssignedMprPlane ?? MprPlane.Axial;
            if (thicknessMm.HasValue && thicknessMm.Value > 1.5f && GetViewerMprRenderMode(targetViewer, plane) == MprRenderMode.Mpr)
                st.AssignedMprRenderMode = MprRenderMode.Average;

            RefreshMprPlaneViewer(targetViewer, plane, false);
            UpdateAllMprCrosshairsAndOverlays();
            UpdateMprDicomInfoPanel();
        }

        private void ToggleMprCrosshairVisibility()
        {
            mprShowCrosshair = !mprShowCrosshair;
            if (orthoSeriesSyncEnabled)
            {
                foreach (var v in viewerHosts.Keys)
                {
                    v.ShowReferenceLines = mprShowCrosshair;
                    v.MprInteractiveCrosshairEnabled = mprShowCrosshair;
                    v.Invalidate();
                }

                SetStatusText(mprShowCrosshair ? "Линии 3D-локализатора (Ax/Sag/Cor) показаны" : "Линии 3D-локализатора скрыты (F12 — показать)");
                return;
            }

            if (!mprModeEnabled || currentMprVolume == null)
                return;

            foreach (var v in viewerHosts.Keys)
            {
                var st = GetViewerState(v);
                if (!st.AssignedMprPlane.HasValue)
                    continue;

                v.ShowReferenceLines = mprShowCrosshair;
                v.MprInteractiveCrosshairEnabled = mprShowCrosshair;
                v.Invalidate();
            }

            SetStatusText(mprShowCrosshair ? "Оси 3D-перекрестия MPR показаны" : "Оси 3D-перекрестия MPR скрыты (F12 — показать)");
        }

        private void UpdateMprDicomInfoPanel()
        {
            if (!mprModeEnabled || currentMprVolume == null || infoValues.Count == 0)
                return;

            var activeViewerControl = ActiveViewerControl ?? viewer;
            var st = GetViewerState(activeViewerControl);
            var plane = st.AssignedMprPlane ?? MprPlane.Axial;
            var sliceIdx = GetMprPlaneSliceIndex(plane);
            var sliceTotal = GetMprPlaneSliceCount(plane);
            var mode = GetViewerMprRenderMode(activeViewerControl, plane);
            var slab = GetViewerMprSlabThickness(activeViewerControl, plane);
            var angle = GetMprPlaneAngle(plane);
            var voxelVal = currentMprVolume.GetVoxel(mprVoxelX, mprVoxelY, mprVoxelZ);
            var unit = currentMprVolume.IsHounsfieldUnit ? " HU" : string.Empty;

            var modeDesc = mode == MprRenderMode.Mpr
                ? "MPR"
                : string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.#} мм)", GetMprRenderModeTitle(mode), slab);
            if (Math.Abs(angle) > 0.05f)
                modeDesc += string.Format(CultureInfo.InvariantCulture, ", угол {0:+0.#;-0.#;0}°", angle);

            UpdateInfoValues(
                string.IsNullOrWhiteSpace(currentMprVolume.PatientName) ? "—" : currentMprVolume.PatientName,
                string.IsNullOrWhiteSpace(currentMprVolume.PatientId) ? "—" : currentMprVolume.PatientId,
                string.IsNullOrWhiteSpace(currentMprVolume.StudyDate) ? "—" : currentMprVolume.StudyDate,
                string.IsNullOrWhiteSpace(currentMprVolume.SeriesDescription) ? "3D MPR Реконструкция" : currentMprVolume.SeriesDescription,
                string.Format("{0} (3D MPR)", string.IsNullOrWhiteSpace(currentMprVolume.Modality) ? "DICOM" : currentMprVolume.Modality),
                string.Format("{0} — {1}", GetMprPlaneTitle(plane), modeDesc),
                string.Format("({0}, {1}, {2}) = {3:0.#}{4}", mprVoxelX, mprVoxelY, mprVoxelZ, voxelVal, unit),
                string.Format("{0} / {1}", sliceIdx + 1, sliceTotal),
                string.Format("{0} × {1} × {2}", currentMprVolume.Width, currentMprVolume.Height, currentMprVolume.Depth),
                string.Format(CultureInfo.InvariantCulture, "Шаг: {0:0.##}×{1:0.##}×{2:0.##} мм", currentMprVolume.SpacingX, currentMprVolume.SpacingY, currentMprVolume.SpacingZ));
        }

        private void SetMprRenderMode(MprRenderMode mode)
        {
            currentMprRenderMode = mode;
            if (!mprModeEnabled)
            {
                EnableMprMode();
                return;
            }

            if ((mode == MprRenderMode.Mip || mode == MprRenderMode.MinIp || mode == MprRenderMode.Average) && currentMprSlabThicknessMm <= 1.05f)
                currentMprSlabThicknessMm = 10f;

            foreach (var v in viewerHosts.Keys)
            {
                var st = GetViewerState(v);
                if (st.AssignedMprPlane != MprPlane.MipCoronal)
                    st.AssignedMprRenderMode = null;
            }

            UpdateMprButtonCaption();
            RefreshAllMprViewers(false);
            UpdateMprDicomInfoPanel();
        }

        private void SetMprSlabThickness(float thicknessMm)
        {
            currentMprSlabThicknessMm = Math.Max(0.5f, thicknessMm);
            if (!mprModeEnabled)
            {
                if (thicknessMm > 1.5f && currentMprRenderMode == MprRenderMode.Mpr)
                    currentMprRenderMode = MprRenderMode.Mip;
                EnableMprMode();
                return;
            }

            if (thicknessMm > 1.5f && currentMprRenderMode == MprRenderMode.Mpr)
                currentMprRenderMode = MprRenderMode.Average;

            foreach (var v in viewerHosts.Keys)
            {
                var st = GetViewerState(v);
                if (st.AssignedMprPlane != MprPlane.MipCoronal)
                    st.AssignedMprSlabThicknessMm = null;
            }

            UpdateMprButtonCaption();
            RefreshAllMprViewers(false);
            UpdateMprDicomInfoPanel();
        }

        private void ResetMprCrosshairToCenter()
        {
            if (!mprModeEnabled || currentMprVolume == null)
                return;

            mprVoxelX = currentMprVolume.Width / 2;
            mprVoxelY = currentMprVolume.Height / 2;
            mprVoxelZ = currentMprVolume.Depth / 2;
            mprObliqueAngleDegrees = 0f;
            mprAngleCoronalDegrees = 0f;
            mprAngleSagittalDegrees = 0f;
            RefreshAllMprViewers(false);
            UpdateMprScrollBarForActiveViewer();
        }

        private void ResetAllMprAngles()
        {
            mprObliqueAngleDegrees = 0f;
            mprAngleCoronalDegrees = 0f;
            mprAngleSagittalDegrees = 0f;
            if (!mprModeEnabled)
            {
                EnableMprMode();
                return;
            }
            RefreshAllMprViewers(false);
        }

        private float GetMprPlaneAngle(MprPlane plane)
        {
            switch (plane)
            {
                case MprPlane.Axial:
                    return mprObliqueAngleDegrees;
                case MprPlane.Coronal:
                case MprPlane.MipCoronal:
                    return mprAngleCoronalDegrees;
                case MprPlane.Sagittal:
                    return mprAngleSagittalDegrees;
                default:
                    return 0f;
            }
        }

        private float GetActiveMprPlaneAngle()
        {
            if (ActiveViewerControl != null)
            {
                var st = GetViewerState(ActiveViewerControl);
                if (st.AssignedMprPlane.HasValue)
                    return GetMprPlaneAngle(st.AssignedMprPlane.Value);
            }
            return mprObliqueAngleDegrees;
        }

        private void SetMprPlaneAngle(MprPlane plane, float angleDegrees, bool refreshAll = true)
        {
            var clamped = Math.Max(-89f, Math.Min(89f, angleDegrees));
            var normalized = Math.Abs(clamped) < 0.1f ? 0f : clamped;
            switch (plane)
            {
                case MprPlane.Axial:
                    mprObliqueAngleDegrees = normalized;
                    break;
                case MprPlane.Coronal:
                case MprPlane.MipCoronal:
                    mprAngleCoronalDegrees = normalized;
                    break;
                case MprPlane.Sagittal:
                    mprAngleSagittalDegrees = normalized;
                    break;
            }

            if (refreshAll && mprModeEnabled && currentMprVolume != null)
                RefreshAllMprViewers(false);
        }

        private void AdjustActiveMprPlaneAngle(float stepDegrees)
        {
            var plane = MprPlane.Axial;
            if (ActiveViewerControl != null)
            {
                var st = GetViewerState(ActiveViewerControl);
                if (st.AssignedMprPlane.HasValue)
                    plane = st.AssignedMprPlane.Value;
            }
            SetMprPlaneAngle(plane, GetMprPlaneAngle(plane) + stepDegrees, true);
        }

        private void SetMprObliqueAngle(float angleDegrees)
        {
            var plane = MprPlane.Axial;
            if (ActiveViewerControl != null)
            {
                var st = GetViewerState(ActiveViewerControl);
                if (st.AssignedMprPlane.HasValue)
                    plane = st.AssignedMprPlane.Value;
            }
            SetMprPlaneAngle(plane, angleDegrees, false);
            if (!mprModeEnabled)
            {
                EnableMprMode();
                return;
            }

            RefreshAllMprViewers(false);
        }

        private void OnViewerMprDoubleClick(DicomImageControl sourceViewer)
        {
            if ((!mprModeEnabled || currentMprVolume == null) && !orthoSeriesSyncEnabled)
                return;
            if (sourceViewer == null || viewerLayoutTable == null)
                return;

            if (sourceViewer.Tool == ViewerTool.Measure && (sourceViewer.MeasurementMode == MeasurementMode.Polygon || sourceViewer.MeasurementMode == MeasurementMode.OpenPolyline || sourceViewer.MeasurementMode == MeasurementMode.Spline))
                return;

            Panel targetHost;
            if (!viewerHosts.TryGetValue(sourceViewer, out targetHost) || targetHost == null)
                return;

            viewerLayoutTable.SuspendLayout();
            try
            {
                if (mprMaximizedViewer == sourceViewer)
                {
                    mprMaximizedViewer = null;
                    for (int r = 0; r < viewerLayoutTable.RowCount; r++)
                        viewerLayoutTable.RowStyles[r] = new RowStyle(SizeType.Percent, 100f / Math.Max(1, viewerLayoutTable.RowCount));
                    for (int c = 0; c < viewerLayoutTable.ColumnCount; c++)
                        viewerLayoutTable.ColumnStyles[c] = new ColumnStyle(SizeType.Percent, 100f / Math.Max(1, viewerLayoutTable.ColumnCount));

                    foreach (var pair in viewerHosts)
                        pair.Value.Visible = true;
                }
                else
                {
                    mprMaximizedViewer = sourceViewer;
                    var pos = viewerLayoutTable.GetPositionFromControl(targetHost);
                    for (int r = 0; r < viewerLayoutTable.RowCount; r++)
                        viewerLayoutTable.RowStyles[r] = new RowStyle(SizeType.Percent, r == pos.Row ? 100f : 0f);
                    for (int c = 0; c < viewerLayoutTable.ColumnCount; c++)
                        viewerLayoutTable.ColumnStyles[c] = new ColumnStyle(SizeType.Percent, c == pos.Column ? 100f : 0f);

                    foreach (var pair in viewerHosts)
                        pair.Value.Visible = pair.Key == sourceViewer;
                }
            }
            finally
            {
                viewerLayoutTable.ResumeLayout(true);
            }

            SetActiveViewer(sourceViewer);
            sourceViewer.ResetView();
        }

        private static string GetMprMeasurementKey(MprPlane plane, int sliceIndex)
        {
            return string.Format(CultureInfo.InvariantCulture, "MPR:{0}:{1}", plane, sliceIndex + 1);
        }

        private static bool TryParseMprMeasurementKey(string key, out MprPlane plane, out int sliceIndex)
        {
            plane = MprPlane.Axial;
            sliceIndex = 0;
            if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("MPR:", StringComparison.OrdinalIgnoreCase))
                return false;

            var parts = key.Split(':');
            if (parts.Length < 3)
                return false;

            MprPlane parsedPlane;
            int oneBasedSlice;
            if (!Enum.TryParse(parts[1], true, out parsedPlane) || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out oneBasedSlice))
                return false;

            plane = parsedPlane;
            sliceIndex = Math.Max(0, oneBasedSlice - 1);
            return true;
        }

        private void RefreshAllMprViewers(bool resetZoomAndPan)
        {
            if (!mprModeEnabled || currentMprVolume == null)
                return;

            foreach (var v in viewerHosts.Keys)
            {
                var st = GetViewerState(v);
                if (!st.AssignedMprPlane.HasValue)
                    continue;
                RefreshMprPlaneViewer(v, st.AssignedMprPlane.Value, resetZoomAndPan);
            }

            UpdateAllMprCrosshairsAndOverlays();
        }

        private void RefreshMprPlaneViewer(DicomImageControl targetViewer, MprPlane plane, bool resetZoomAndPan)
        {
            if (targetViewer == null || currentMprVolume == null)
                return;

            var st = GetViewerState(targetViewer);
            if (!string.IsNullOrWhiteSpace(st.CurrentImagePath))
            {
                var existing = targetViewer.GetMeasurements();
                if (existing.Count == 0)
                    measurementsByImagePath.Remove(st.CurrentImagePath);
                else
                    measurementsByImagePath[st.CurrentImagePath] = existing;
            }

            var sliceIndex = GetMprPlaneSliceIndex(plane);
            var mprKey = GetMprMeasurementKey(plane, sliceIndex);
            st.CurrentImagePath = mprKey;
            if (targetViewer == ActiveViewerControl)
                currentImagePath = mprKey;

            var modeForPlane = GetViewerMprRenderMode(targetViewer, plane);
            var slabForPlane = GetViewerMprSlabThickness(targetViewer, plane);

            float spacingX;
            float spacingY;
            currentMprVolume.GetPlanePixelSpacing(plane, out spacingX, out spacingY);
            var provider = currentMprVolume.ExtractPlaneProvider(
                plane,
                sliceIndex,
                modeForPlane,
                slabForPlane,
                mprObliqueAngleDegrees,
                mprAngleCoronalDegrees,
                mprAngleSagittalDegrees,
                mprVoxelX,
                mprVoxelY,
                mprVoxelZ);

            if (provider == null)
                return;

            targetViewer.PixelDataProvider = provider;
            targetViewer.SetPixelSpacing(spacingX, spacingY);

            var wc = currentAppliedWlCenter;
            var ww = currentAppliedWlWidth <= 0f ? currentMprVolume.DefaultWindowWidth : currentAppliedWlWidth;
            var bitmap = provider.RenderBitmap(wc, ww, targetViewer.Inverted);
            targetViewer.SetImage(bitmap, resetZoomAndPan);
            targetViewer.SetWindowLevel(wc, ww, true);

            List<DicomMeasurement> storedMeasurements;
            if (measurementsByImagePath.TryGetValue(mprKey, out storedMeasurements))
                targetViewer.SetMeasurements(storedMeasurements);
            else
                targetViewer.SetMeasurements(null);

            if (targetViewer == ActiveViewerControl && measurementsListView != null)
                RefreshMeasurementsList();
        }

        private int GetMprPlaneSliceIndex(MprPlane plane)
        {
            if (currentMprVolume == null)
                return 0;

            switch (plane)
            {
                case MprPlane.Axial:
                    return Math.Max(0, Math.Min(currentMprVolume.Depth - 1, mprVoxelZ));
                case MprPlane.Coronal:
                case MprPlane.MipCoronal:
                    return Math.Max(0, Math.Min(currentMprVolume.Height - 1, mprVoxelY));
                case MprPlane.Sagittal:
                    return Math.Max(0, Math.Min(currentMprVolume.Width - 1, mprVoxelX));
                default:
                    return 0;
            }
        }

        private int GetMprPlaneSliceCount(MprPlane plane)
        {
            if (currentMprVolume == null)
                return 1;
            return currentMprVolume.GetPlaneSliceCount(plane);
        }

        private void UpdateAllMprCrosshairsAndOverlays()
        {
            if (!mprModeEnabled || currentMprVolume == null)
                return;

            foreach (var v in viewerHosts.Keys)
            {
                var st = GetViewerState(v);
                if (!st.AssignedMprPlane.HasValue)
                    continue;

                var plane = st.AssignedMprPlane.Value;
                PointF crossPoint;
                switch (plane)
                {
                    case MprPlane.Axial:
                        crossPoint = new PointF(mprVoxelX, mprVoxelY);
                        break;
                    case MprPlane.Coronal:
                    case MprPlane.MipCoronal:
                        crossPoint = new PointF(mprVoxelX, mprVoxelZ);
                        break;
                    case MprPlane.Sagittal:
                        crossPoint = new PointF(mprVoxelY, mprVoxelZ);
                        break;
                    default:
                        crossPoint = new PointF(0, 0);
                        break;
                }

                var panelMode = GetViewerMprRenderMode(v, plane);
                var panelSlab = GetViewerMprSlabThickness(v, plane);
                v.ReferenceLineAngleDegrees = GetMprPlaneAngle(plane);
                v.ReferenceSlabThicknessMm = panelMode == MprRenderMode.Mpr ? 1f : panelSlab;
                v.SetReferencePoint(crossPoint);
                UpdateMprViewerOverlay(v, plane);
            }
        }

        private static string GetMprPlaneTitle(MprPlane plane)
        {
            switch (plane)
            {
                case MprPlane.Axial:
                    return "Axial (Аксиальная)";
                case MprPlane.Coronal:
                    return "Coronal (Корональная)";
                case MprPlane.Sagittal:
                    return "Sagittal (Сагиттальная)";
                case MprPlane.MipCoronal:
                    return "Coronal MIP";
                default:
                    return "MPR";
            }
        }

        private static string GetMprRenderModeTitle(MprRenderMode mode)
        {
            switch (mode)
            {
                case MprRenderMode.Mip:
                    return "MIP";
                case MprRenderMode.MinIp:
                    return "MinIP";
                case MprRenderMode.Average:
                    return "Avg";
                default:
                    return "MPR";
            }
        }

        private void UpdateMprViewerOverlay(DicomImageControl targetViewer, MprPlane plane)
        {
            if (targetViewer == null || currentMprVolume == null)
                return;

            var sliceIdx = GetMprPlaneSliceIndex(plane);
            var sliceTotal = GetMprPlaneSliceCount(plane);
            var planeTitle = GetMprPlaneTitle(plane) + " ▾";
            var modeForPlane = GetViewerMprRenderMode(targetViewer, plane);
            var slabForPlane = GetViewerMprSlabThickness(targetViewer, plane);
            var modeCaption = GetMprRenderModeTitle(modeForPlane);
            var slabCaption = modeForPlane == MprRenderMode.Mpr
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.##} мм", currentMprVolume.SpacingZ)
                : string.Format(CultureInfo.InvariantCulture, "Slab: {0:0.#} мм", slabForPlane);
            var planeAngle = GetMprPlaneAngle(plane);
            var hasAnyOblique = Math.Abs(mprObliqueAngleDegrees) > 0.05f || Math.Abs(mprAngleCoronalDegrees) > 0.05f || Math.Abs(mprAngleSagittalDegrees) > 0.05f;
            var obliqueSuffix = Math.Abs(planeAngle) > 0.05f
                ? string.Format(CultureInfo.InvariantCulture, " | Угол {0:+0.#;-0.#;0}°", planeAngle)
                : (hasAnyOblique ? " | Oblique" : string.Empty);

            targetViewer.OverlayTopLeft = string.Format(
                "{0}{5}\nСрез: {1}/{2} | {3} ({4})",
                planeTitle,
                sliceIdx + 1,
                sliceTotal,
                modeCaption,
                slabCaption,
                obliqueSuffix);

            targetViewer.OverlayTopRight = string.Format(
                "{0}\n{1}\n{2}\n{3}",
                currentMprVolume.PatientName,
                currentMprVolume.PatientId,
                currentMprVolume.StudyDate,
                currentMprVolume.SeriesDescription);

            var windowLine = string.Format("WL: {0:0.#} WW: {1:0.#}", targetViewer.WindowCenter, targetViewer.WindowWidth);
            var voxelVal = currentMprVolume.GetVoxel(mprVoxelX, mprVoxelY, mprVoxelZ);
            var unit = currentMprVolume.IsHounsfieldUnit ? " HU" : string.Empty;
            targetViewer.OverlayBottomLeft = string.Format(
                "{0}\n3D: ({1}, {2}, {3}) = {4:0.#}{5}",
                windowLine,
                mprVoxelX,
                mprVoxelY,
                mprVoxelZ,
                voxelVal,
                unit);

            targetViewer.OverlayBottomRight = string.Format(
                "{0}\nШаг: {1:0.##}×{2:0.##}×{3:0.##} мм",
                currentMprVolume.Modality,
                currentMprVolume.SpacingX,
                currentMprVolume.SpacingY,
                currentMprVolume.SpacingZ);
            targetViewer.Invalidate();
        }

        private void UpdateMprScrollBarForActiveViewer()
        {
            if (!mprModeEnabled || currentMprVolume == null || sliceScrollBar == null)
                return;

            var st = GetViewerState(ActiveViewerControl);
            if (!st.AssignedMprPlane.HasValue)
                return;

            var plane = st.AssignedMprPlane.Value;
            var total = Math.Max(1, GetMprPlaneSliceCount(plane));
            var current = Math.Max(0, Math.Min(total - 1, GetMprPlaneSliceIndex(plane)));

            updatingSliceScrollBar = true;
            sliceScrollBar.Visible = total > 1;
            sliceScrollBar.Minimum = 0;
            sliceScrollBar.Maximum = Math.Max(0, total - 1);
            sliceScrollBar.LargeChange = 1;
            sliceScrollBar.SmallChange = 1;
            sliceScrollBar.Value = current;
            updatingSliceScrollBar = false;

            previousButton.Enabled = current > 0;
            nextButton.Enabled = current < total - 1;
            UpdateMprDicomInfoPanel();
        }

        private void NavigateMprByStep(int step)
        {
            if (!mprModeEnabled || currentMprVolume == null || step == 0)
                return;

            var st = GetViewerState(ActiveViewerControl);
            var plane = st.AssignedMprPlane ?? MprPlane.Axial;
            var total = GetMprPlaneSliceCount(plane);
            if (total <= 1)
                return;

            var loopStart = 0;
            var loopEnd = total - 1;
            var current = GetMprPlaneSliceIndex(plane);
            if (TryGetActiveLoopRange(st, total, out loopStart, out loopEnd))
            {
                if (current < loopStart || current > loopEnd)
                {
                    SetMprPlaneSliceIndex(plane, loopStart);
                    current = loopStart;
                }
            }

            var next = current + step;
            if (next < loopStart)
                next = loopEnd;
            else if (next > loopEnd)
                next = loopStart;

            SetMprPlaneSliceIndex(plane, next);
        }

        private void NavigateMprToBoundary(bool first)
        {
            if (!mprModeEnabled || currentMprVolume == null)
                return;

            var st = GetViewerState(ActiveViewerControl);
            var plane = st.AssignedMprPlane ?? MprPlane.Axial;
            var total = GetMprPlaneSliceCount(plane);
            SetMprPlaneSliceIndex(plane, first ? 0 : Math.Max(0, total - 1));
        }

        private void SelectMprSliceForActiveViewer(int sliceIndex)
        {
            if (!mprModeEnabled || currentMprVolume == null)
                return;

            var st = GetViewerState(ActiveViewerControl);
            var plane = st.AssignedMprPlane ?? MprPlane.Axial;
            SetMprPlaneSliceIndex(plane, sliceIndex);
        }

        private void SetMprPlaneSliceIndex(MprPlane plane, int sliceIndex)
        {
            if (currentMprVolume == null)
                return;

            switch (plane)
            {
                case MprPlane.Axial:
                    mprVoxelZ = Math.Max(0, Math.Min(currentMprVolume.Depth - 1, sliceIndex));
                    break;
                case MprPlane.Coronal:
                case MprPlane.MipCoronal:
                    mprVoxelY = Math.Max(0, Math.Min(currentMprVolume.Height - 1, sliceIndex));
                    break;
                case MprPlane.Sagittal:
                    mprVoxelX = Math.Max(0, Math.Min(currentMprVolume.Width - 1, sliceIndex));
                    break;
            }

            foreach (var v in viewerHosts.Keys)
            {
                var st = GetViewerState(v);
                if (!st.AssignedMprPlane.HasValue)
                    continue;

                var vPlane = st.AssignedMprPlane.Value;
                if (vPlane == plane || (plane == MprPlane.Coronal && vPlane == MprPlane.MipCoronal) || (plane == MprPlane.MipCoronal && vPlane == MprPlane.Coronal))
                    RefreshMprPlaneViewer(v, vPlane, false);
            }

            UpdateAllMprCrosshairsAndOverlays();
            UpdateMprScrollBarForActiveViewer();
        }

        private void OnViewerMprMouseMove(DicomImageControl sourceViewer, MouseEventArgs e)
        {
            if (orthoSeriesSyncEnabled && orthoSyncCrosshairDragging)
            {
                if ((e.Button & MouseButtons.Left) != MouseButtons.Left)
                {
                    orthoSyncCrosshairDragging = false;
                    return;
                }

                UpdateOrthoSyncFromScreenPoint(sourceViewer, e.Location);
                return;
            }

            if (!mprModeEnabled || currentMprVolume == null || !mprCrosshairDragging)
                return;

            if ((e.Button & MouseButtons.Left) != MouseButtons.Left)
            {
                mprCrosshairDragging = false;
                return;
            }

            UpdateMprCrosshairFromScreenPoint(sourceViewer, e.Location);
        }

        private void OnViewerMprMouseUp(DicomImageControl sourceViewer, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                mprCrosshairDragging = false;
                orthoSyncCrosshairDragging = false;
            }
        }

        private void OnViewerMprCrosshairMoved(DicomImageControl sourceViewer, PointF imagePoint, bool moveX, bool moveY)
        {
            if (orthoSeriesSyncEnabled)
            {
                UpdateOrthoSyncFromImagePoint(sourceViewer, imagePoint, moveX, moveY);
                return;
            }

            if (!mprModeEnabled || currentMprVolume == null || sourceViewer == null)
                return;

            UpdateMprCrosshairFromImagePoint(sourceViewer, imagePoint, moveX, moveY);
        }

        private void OnViewerMprCrosshairAngleChanged(DicomImageControl sourceViewer, float newAngleDegrees)
        {
            if (!mprModeEnabled || currentMprVolume == null || sourceViewer == null)
                return;

            var st = GetViewerState(sourceViewer);
            if (!st.AssignedMprPlane.HasValue)
                return;

            var sourcePlane = st.AssignedMprPlane.Value;
            SetMprPlaneAngle(sourcePlane, newAngleDegrees, false);

            // Обновляем остальные проекции в реальном времени, а также оверлей текущего окна
            foreach (var v in viewerHosts.Keys)
            {
                var targetState = GetViewerState(v);
                if (!targetState.AssignedMprPlane.HasValue)
                    continue;

                var targetPlane = targetState.AssignedMprPlane.Value;
                if (v != sourceViewer)
                    RefreshMprPlaneViewer(v, targetPlane, false);
            }

            UpdateAllMprCrosshairsAndOverlays();
        }

        private void OnViewerMprSlabThicknessChanged(DicomImageControl sourceViewer, float newSlabThicknessMm)
        {
            if (!mprModeEnabled || currentMprVolume == null || sourceViewer == null)
                return;

            var clamped = Math.Max(1f, Math.Min(100f, newSlabThicknessMm));
            var st = GetViewerState(sourceViewer);
            if (st.AssignedMprSlabThicknessMm.HasValue || st.AssignedMprRenderMode.HasValue)
            {
                st.AssignedMprSlabThicknessMm = clamped;
                var curMode = GetViewerMprRenderMode(sourceViewer, st.AssignedMprPlane ?? MprPlane.Axial);
                if (clamped > 1.3f && curMode == MprRenderMode.Mpr)
                    st.AssignedMprRenderMode = MprRenderMode.Average;
                else if (clamped <= 1.15f && curMode == MprRenderMode.Average)
                    st.AssignedMprRenderMode = MprRenderMode.Mpr;
            }
            else
            {
                currentMprSlabThicknessMm = clamped;
                if (currentMprSlabThicknessMm > 1.3f && currentMprRenderMode == MprRenderMode.Mpr)
                    currentMprRenderMode = MprRenderMode.Average;
                else if (currentMprSlabThicknessMm <= 1.15f && currentMprRenderMode == MprRenderMode.Average)
                    currentMprRenderMode = MprRenderMode.Mpr;
            }

            UpdateMprButtonCaption();
            RefreshAllMprViewers(false);
        }

        private void AssignMprPlaneToViewer(DicomImageControl targetViewer, MprPlane plane)
        {
            if (!mprModeEnabled || currentMprVolume == null || targetViewer == null)
                return;

            var st = GetViewerState(targetViewer);
            st.AssignedMprPlane = plane;
            if (plane == MprPlane.MipCoronal && !st.AssignedMprRenderMode.HasValue)
            {
                st.AssignedMprRenderMode = MprRenderMode.Mip;
                st.AssignedMprSlabThicknessMm = 15f;
            }
            ConfigureViewerMprAppearance(targetViewer, plane);
            RefreshMprPlaneViewer(targetViewer, plane, true);
            UpdateAllMprCrosshairsAndOverlays();
            if (targetViewer == ActiveViewerControl)
                UpdateMprScrollBarForActiveViewer();
        }

        private void OnViewerMprContextMenuRequested(DicomImageControl sourceViewer, Point screenPoint)
        {
            if (orthoSeriesSyncEnabled)
            {
                OnViewerOrthoSyncContextMenuRequested(sourceViewer, screenPoint);
                return;
            }

            if (!mprModeEnabled || currentMprVolume == null || sourceViewer == null)
                return;

            SetActiveViewer(sourceViewer);
            var st = GetViewerState(sourceViewer);
            var currentPlane = st.AssignedMprPlane ?? MprPlane.Axial;
            var currentPanelMode = GetViewerMprRenderMode(sourceViewer, currentPlane);
            var currentPanelSlab = GetViewerMprSlabThickness(sourceViewer, currentPlane);

            var menu = new ContextMenu();

            var planeMenu = new MenuItem("Проекция панели");
            var planes = new[]
            {
                Tuple.Create(MprPlane.Axial, "Axial (Аксиальная)"),
                Tuple.Create(MprPlane.Coronal, "Coronal (Корональная)"),
                Tuple.Create(MprPlane.Sagittal, "Sagittal (Сагиттальная)"),
                Tuple.Create(MprPlane.MipCoronal, "Coronal MIP")
            };
            foreach (var p in planes)
            {
                var capturedPlane = p.Item1;
                var item = new MenuItem(p.Item2, delegate { AssignMprPlaneToViewer(sourceViewer, capturedPlane); })
                {
                    Checked = currentPlane == capturedPlane
                };
                planeMenu.MenuItems.Add(item);
            }
            menu.MenuItems.Add(planeMenu);

            var modeMenu = new MenuItem("Режим этой панели");
            modeMenu.MenuItems.Add(new MenuItem("MPR (Стандартный срез)", delegate { SetViewerMprRenderMode(sourceViewer, MprRenderMode.Mpr); }) { Checked = currentPanelMode == MprRenderMode.Mpr });
            modeMenu.MenuItems.Add(new MenuItem("MIP (Макс. интенсивность)", delegate { SetViewerMprRenderMode(sourceViewer, MprRenderMode.Mip); }) { Checked = currentPanelMode == MprRenderMode.Mip });
            modeMenu.MenuItems.Add(new MenuItem("MinIP (Мин. интенсивность)", delegate { SetViewerMprRenderMode(sourceViewer, MprRenderMode.MinIp); }) { Checked = currentPanelMode == MprRenderMode.MinIp });
            modeMenu.MenuItems.Add(new MenuItem("Average (Средняя / Thick MPR)", delegate { SetViewerMprRenderMode(sourceViewer, MprRenderMode.Average); }) { Checked = currentPanelMode == MprRenderMode.Average });
            modeMenu.MenuItems.Add("-");
            modeMenu.MenuItems.Add(new MenuItem("Применить режим ко всем панелям", delegate { SetMprRenderMode(currentPanelMode); }));
            menu.MenuItems.Add(modeMenu);

            var slabMenu = new MenuItem("Толщина слоя (Slab) панели");
            foreach (var thick in new[] { 1f, 3f, 5f, 10f, 15f, 20f, 30f, 50f })
            {
                var capturedThick = thick;
                var label = capturedThick <= 1.05f ? "Тонкий срез (1 мм)" : string.Format(CultureInfo.InvariantCulture, "{0:0.#} мм", capturedThick);
                slabMenu.MenuItems.Add(new MenuItem(label, delegate { SetViewerMprSlabThickness(sourceViewer, capturedThick); })
                {
                    Checked = Math.Abs(currentPanelSlab - capturedThick) < 0.1f
                });
            }
            slabMenu.MenuItems.Add("-");
            slabMenu.MenuItems.Add(new MenuItem("Применить толщину ко всем панелям", delegate { SetMprSlabThickness(currentPanelSlab); }));
            menu.MenuItems.Add(slabMenu);

            var obliqueMenu = new MenuItem("Угол наклона (Oblique MPR)");
            var curAngle = GetMprPlaneAngle(currentPlane);
            foreach (var deg in new[] { 0f, -45f, -30f, -15f, 15f, 30f, 45f })
            {
                var capturedDeg = deg;
                var label = Math.Abs(capturedDeg) < 0.05f ? "0° (Ортогонально)" : string.Format(CultureInfo.InvariantCulture, "{0:+0;-0;0}°", capturedDeg);
                obliqueMenu.MenuItems.Add(new MenuItem(label, delegate { SetMprPlaneAngle(currentPlane, capturedDeg, true); })
                {
                    Checked = Math.Abs(curAngle - capturedDeg) < 0.1f
                });
            }
            obliqueMenu.MenuItems.Add("-");
            obliqueMenu.MenuItems.Add(new MenuItem("Сбросить все углы наклона (0°)", delegate { ResetAllMprAngles(); }));
            menu.MenuItems.Add(obliqueMenu);

            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem("Показывать оси 3D-перекрестия (F12)", delegate { ToggleMprCrosshairVisibility(); }) { Checked = mprShowCrosshair });
            menu.MenuItems.Add(new MenuItem("Центрировать 3D-перекрестие", delegate { ResetMprCrosshairToCenter(); }));
            menu.MenuItems.Add(new MenuItem(mprMaximizedViewer == sourceViewer ? "Свернуть панель к сетке 2×2" : "Развернуть панель на весь экран", delegate { OnViewerMprDoubleClick(sourceViewer); }));
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem("Экспорт этой проекции...", delegate { ExportCurrentImage(); }));
            menu.MenuItems.Add(new MenuItem("Экспорт коллажа MPR (все 4 проекции)...", delegate { ExportMprCollage(); }));
            menu.MenuItems.Add(new MenuItem("Экспорт серии срезов этой проекции в папку...", delegate { ExportMprSeriesToFolder(); }));

            var clientPoint = sourceViewer.PointToClient(screenPoint);
            menu.Show(sourceViewer, clientPoint);
        }

        private void UpdateMprCrosshairFromScreenPoint(DicomImageControl sourceViewer, Point screenPoint)
        {
            if (!mprModeEnabled || currentMprVolume == null || sourceViewer == null)
                return;

            PointF imagePoint;
            if (!sourceViewer.TryGetImagePointAt(screenPoint, out imagePoint))
                return;

            UpdateMprCrosshairFromImagePoint(sourceViewer, imagePoint, true, true);
        }

        private void UpdateMprCrosshairFromImagePoint(DicomImageControl sourceViewer, PointF imagePoint, bool moveHorizontalCoord, bool moveVerticalCoord)
        {
            if (!mprModeEnabled || currentMprVolume == null || sourceViewer == null)
                return;

            var st = GetViewerState(sourceViewer);
            if (!st.AssignedMprPlane.HasValue)
                return;

            var plane = st.AssignedMprPlane.Value;
            var newX = mprVoxelX;
            var newY = mprVoxelY;
            var newZ = mprVoxelZ;

            switch (plane)
            {
                case MprPlane.Axial:
                    if (moveHorizontalCoord)
                        newX = Math.Max(0, Math.Min(currentMprVolume.Width - 1, (int)Math.Round(imagePoint.X)));
                    if (moveVerticalCoord)
                        newY = Math.Max(0, Math.Min(currentMprVolume.Height - 1, (int)Math.Round(imagePoint.Y)));
                    break;
                case MprPlane.Coronal:
                case MprPlane.MipCoronal:
                    if (moveHorizontalCoord)
                        newX = Math.Max(0, Math.Min(currentMprVolume.Width - 1, (int)Math.Round(imagePoint.X)));
                    if (moveVerticalCoord)
                        newZ = Math.Max(0, Math.Min(currentMprVolume.Depth - 1, (int)Math.Round(imagePoint.Y)));
                    break;
                case MprPlane.Sagittal:
                    if (moveHorizontalCoord)
                        newY = Math.Max(0, Math.Min(currentMprVolume.Height - 1, (int)Math.Round(imagePoint.X)));
                    if (moveVerticalCoord)
                        newZ = Math.Max(0, Math.Min(currentMprVolume.Depth - 1, (int)Math.Round(imagePoint.Y)));
                    break;
            }

            if (newX == mprVoxelX && newY == mprVoxelY && newZ == mprVoxelZ)
                return;

            var xChanged = newX != mprVoxelX;
            var yChanged = newY != mprVoxelY;
            var zChanged = newZ != mprVoxelZ;

            mprVoxelX = newX;
            mprVoxelY = newY;
            mprVoxelZ = newZ;

            foreach (var v in viewerHosts.Keys)
            {
                var targetState = GetViewerState(v);
                if (!targetState.AssignedMprPlane.HasValue)
                    continue;

                var targetPlane = targetState.AssignedMprPlane.Value;
                if (targetPlane == MprPlane.Axial && zChanged)
                    RefreshMprPlaneViewer(v, targetPlane, false);
                else if ((targetPlane == MprPlane.Coronal || targetPlane == MprPlane.MipCoronal) && yChanged)
                    RefreshMprPlaneViewer(v, targetPlane, false);
                else if (targetPlane == MprPlane.Sagittal && xChanged)
                    RefreshMprPlaneViewer(v, targetPlane, false);
            }

            UpdateAllMprCrosshairsAndOverlays();
            UpdateMprScrollBarForActiveViewer();
        }

        // =========================================================================
        // СИНХРОНИЗАЦИЯ АКСИАЛЬНОЙ, САГИТТАЛЬНОЙ И КОРОНАЛЬНОЙ СЕРИЙ (RadiAnt-style)
        // =========================================================================

        private ToolStripDropDownButton CreateSyncSeriesToolbarButton()
        {
            var button = new ToolStripDropDownButton(CreateIcon("sync"))
            {
                AutoSize = false,
                Width = 136,
                Height = 22,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextAlign = ContentAlignment.MiddleRight,
                ImageAlign = ContentAlignment.MiddleLeft,
                Text = "Синхр. Ax/Sag/Cor",
                ToolTipText = "Автоматическая синхронизация Axial / Sagittal / Coronal срезов (Ctrl+Shift+S)\nАвтоматически выбирает соответствующие серии из загруженного исследования.",
                Enabled = false
            };

            var toggleItem = new ToolStripMenuItem("Включить синхронизацию Ax / Sag / Cor (Автовыбор серий)", null, delegate { ToggleOrthoSeriesSync(); })
            {
                ShortcutKeyDisplayString = "Ctrl+Shift+S",
                ShowShortcutKeys = true
            };
            var autoLayout3Item = new ToolStripMenuItem("Разложить Axial + Sagittal + Coronal (1×3)", null, delegate { EnableOrthoSeriesSync(false); });
            var autoLayout2x2Item = new ToolStripMenuItem("Разложить в сетку 2×2", null, delegate { EnableOrthoSeriesSync(true); });
            var showRefLinesItem = new ToolStripMenuItem("Показывать линии пересечения срезов (Localizer)", null, delegate { ToggleMprCrosshairVisibility(); })
            {
                ShortcutKeyDisplayString = "F12"
            };
            var centerSyncItem = new ToolStripMenuItem("Центрировать точку синхронизации", null, delegate { CenterOrthoSyncPoint(); });
            var fallbackMprItem = new ToolStripMenuItem("Построить 3D MPR из текущей серии (если нет отдельных Sag/Cor)", null, delegate { EnableMprMode(); });
            var disableItem = new ToolStripMenuItem("Выключить синхронизацию и вернуть 1×1", null, delegate { DisableOrthoSeriesSync(true); });

            button.DropDownItems.Add(toggleItem);
            button.DropDownItems.Add(new ToolStripSeparator());
            button.DropDownItems.Add(autoLayout3Item);
            button.DropDownItems.Add(autoLayout2x2Item);
            button.DropDownItems.Add(new ToolStripSeparator());
            button.DropDownItems.Add(showRefLinesItem);
            button.DropDownItems.Add(centerSyncItem);
            button.DropDownItems.Add(new ToolStripSeparator());
            button.DropDownItems.Add(fallbackMprItem);
            button.DropDownItems.Add(new ToolStripSeparator());
            button.DropDownItems.Add(disableItem);

            button.DropDownOpening += delegate
            {
                bool hasAx, hasSag, hasCor;
                int totalSeriesCount, maxSlices;
                var canSync = CanSynchronizeLoadedStudy(out hasAx, out hasSag, out hasCor, out totalSeriesCount, out maxSlices);
                toggleItem.Checked = orthoSeriesSyncEnabled;
                toggleItem.Enabled = canSync || orthoSeriesSyncEnabled;
                autoLayout3Item.Enabled = canSync;
                autoLayout2x2Item.Enabled = canSync;
                showRefLinesItem.Checked = mprShowCrosshair;
                centerSyncItem.Enabled = orthoSeriesSyncEnabled;
                fallbackMprItem.Enabled = images.Count >= 3;
                disableItem.Enabled = orthoSeriesSyncEnabled;
            };

            return button;
        }

        private bool CanSynchronizeLoadedStudy(
            out bool hasAxial,
            out bool hasSagittal,
            out bool hasCoronal,
            out int totalSeriesWithImages,
            out int maxSlicesInSeries)
        {
            hasAxial = false;
            hasSagittal = false;
            hasCoronal = false;
            totalSeriesWithImages = 0;
            maxSlicesInSeries = 0;

            if (currentStudies == null || currentStudies.Count == 0)
                return false;

            foreach (var study in currentStudies)
            {
                if (study == null || study.Series == null)
                    continue;

                foreach (var s in study.Series)
                {
                    if (s == null || s.Images == null || s.Images.Count == 0)
                        continue;

                    totalSeriesWithImages++;
                    if (s.Images.Count > maxSlicesInSeries)
                        maxSlicesInSeries = s.Images.Count;

                    DicomSpatialHelper.FinalizeSeriesSpatialMetadata(s);
                    switch (s.Plane)
                    {
                        case AnatomicalPlane.Axial:
                            hasAxial = true;
                            break;
                        case AnatomicalPlane.Sagittal:
                            hasSagittal = true;
                            break;
                        case AnatomicalPlane.Coronal:
                            hasCoronal = true;
                            break;
                    }
                }
            }

            int distinctOrthoPlanes = (hasAxial ? 1 : 0) + (hasSagittal ? 1 : 0) + (hasCoronal ? 1 : 0);
            if (distinctOrthoPlanes >= 2)
                return true;

            if (totalSeriesWithImages >= 2 && maxSlicesInSeries >= 2)
                return true;

            if (maxSlicesInSeries >= 3)
                return true;

            return false;
        }

        private void UpdateSyncSeriesButtonState()
        {
            bool hasAx, hasSag, hasCor;
            int totalSeries, maxSlices;
            var canSync = CanSynchronizeLoadedStudy(out hasAx, out hasSag, out hasCor, out totalSeries, out maxSlices);

            if (syncSeriesToolbarButton != null)
            {
                syncSeriesToolbarButton.Enabled = canSync || orthoSeriesSyncEnabled;

                var planeParts = new List<string>(3);
                if (hasAx) planeParts.Add("Ax");
                if (hasSag) planeParts.Add("Sag");
                if (hasCor) planeParts.Add("Cor");
                var planesBadge = planeParts.Count >= 2 ? string.Join("/", planeParts) : "Ax/Sag/Cor";

                syncSeriesToolbarButton.Text = string.Format("Синхр. {0}", planesBadge);

                if (orthoSeriesSyncEnabled)
                {
                    syncSeriesToolbarButton.BackColor = Color.FromArgb(128, 168, 214);
                }
                else if (canSync && planeParts.Count >= 2)
                {
                    // Подсветка доступности ортогональных серий, как в RadiAnt DICOM
                    syncSeriesToolbarButton.BackColor = Color.FromArgb(146, 178, 206);
                }
                else
                {
                    syncSeriesToolbarButton.BackColor = Color.FromArgb(164, 164, 164);
                }
            }

            if (syncSeriesToggleMenuItem != null)
            {
                syncSeriesToggleMenuItem.Enabled = canSync || orthoSeriesSyncEnabled;
                syncSeriesToggleMenuItem.Checked = orthoSeriesSyncEnabled;
            }
        }

        private void ClearCachedSliceProviders()
        {
            cachedSliceProviders.Clear();
        }

        private DicomPixelDataProvider GetOrCreateCachedSliceProvider(string filePath, out DicomDataset dataset)
        {
            dataset = null;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            DicomPixelDataProvider cached;
            if (cachedSliceProviders.TryGetValue(filePath, out cached) && cached != null)
                return cached;

            try
            {
                var dicomFile = DicomFile.Open(filePath);
                dataset = dicomFile.Dataset;
                var provider = DicomPixelDataProvider.Create(dataset, 0);
                if (provider != null)
                {
                    if (cachedSliceProviders.Count >= 96)
                        cachedSliceProviders.Clear();
                    cachedSliceProviders[filePath] = provider;
                }
                return provider;
            }
            catch
            {
                return null;
            }
        }

        private DicomSeries GetActiveAssignedSeries()
        {
            var active = ActiveViewerControl ?? viewer;
            if (active != null)
            {
                var st = GetViewerState(active);
                if (st.AssignedSeries != null)
                    return st.AssignedSeries;
            }

            if (selectedSeriesCard != null && selectedSeriesCard.Tag is DicomSeries)
                return (DicomSeries)selectedSeriesCard.Tag;

            if (currentStudies != null)
            {
                foreach (var study in currentStudies)
                {
                    if (study != null && study.Series != null && study.Series.Count > 0)
                        return study.Series[0];
                }
            }

            return null;
        }

        private void ToggleOrthoSeriesSync()
        {
            if (orthoSeriesSyncEnabled)
                DisableOrthoSeriesSync(true);
            else
                EnableOrthoSeriesSync(false);
        }

        private void EnableOrthoSeriesSync(bool force2x2Layout = false)
        {
            bool hasAx, hasSag, hasCor;
            int totalSeries, maxSlices;
            if (!CanSynchronizeLoadedStudy(out hasAx, out hasSag, out hasCor, out totalSeries, out maxSlices))
            {
                MessageBox.Show(
                    this,
                    "В текущем исследовании нет подходящих серий для синхронизации Axial / Sagittal / Coronal.",
                    "Синхронизация срезов",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (mprModeEnabled)
                DisableMprMode(false);

            var anchorSeries = GetActiveAssignedSeries();
            DicomSeries axialSeries;
            DicomSeries sagittalSeries;
            DicomSeries coronalSeries;
            FindBestOrthogonalSeriesSet(anchorSeries, out axialSeries, out sagittalSeries, out coronalSeries);

            var selectedList = new List<Tuple<AnatomicalPlane, DicomSeries>>();
            if (axialSeries != null)
                selectedList.Add(Tuple.Create(AnatomicalPlane.Axial, axialSeries));
            if (sagittalSeries != null && !selectedList.Any(t => ReferenceEquals(t.Item2, sagittalSeries)))
                selectedList.Add(Tuple.Create(AnatomicalPlane.Sagittal, sagittalSeries));
            if (coronalSeries != null && !selectedList.Any(t => ReferenceEquals(t.Item2, coronalSeries)))
                selectedList.Add(Tuple.Create(AnatomicalPlane.Coronal, coronalSeries));

            // Если в исследовании только одна объемная серия (например, КТ в Axial без отдельных Sag/Cor серий),
            // автоматически включаем режим 3D MPR (как в RadiAnt), чтобы показать все 3 плоскости и синхронизировать их!
            if (selectedList.Count <= 1)
            {
                if (images.Count >= 3 || maxSlices >= 3)
                {
                    EnableMprMode();
                    SetStatusText("Автоматически построены синхронизированные проекции Axial / Sagittal / Coronal (3D MPR)");
                    return;
                }
            }

            orthoSeriesSyncEnabled = true;
            mprShowCrosshair = true;
            mprMaximizedViewer = null;

            int rows = 1;
            int cols = Math.Max(2, selectedList.Count);
            if (force2x2Layout || selectedList.Count > 3)
            {
                rows = 2;
                cols = 2;
            }

            ApplyViewerLayoutPreset(rows, cols, false);

            var viewers = GetOrderedViewers();
            for (int i = 0; i < viewers.Count; i++)
            {
                var v = viewers[i];
                var st = GetViewerState(v);
                var tuple = i < selectedList.Count ? selectedList[i] : selectedList[0];
                var plane = tuple.Item1;
                var ser = tuple.Item2;
                st.AssignedSeries = ser;
                st.AssignedSyncPlane = plane;
                st.Images.Clear();
                st.Images.AddRange(ser.Images.Select(CloneImageItem));
                st.ImageIndex = Math.Max(0, st.Images.Count / 2);
                ConfigureViewerOrthoSyncAppearance(v, plane);
            }

            if (viewers.Count > 0)
            {
                SetActiveViewer(viewers[0]);
                var activeSt = GetViewerState(viewers[0]);
                images.Clear();
                images.AddRange(activeSt.Images.Select(CloneImageItem));
                imageIndex = activeSt.ImageIndex;
                if (activeSt.AssignedSeries != null)
                    UpdateSelectedSeriesCard(FindSeriesCard(activeSt.AssignedSeries));
            }

            // Отрисовываем начальные срезы во всех панелях
            foreach (var v in viewers)
            {
                var st = GetViewerState(v);
                if (st.Images.Count > 0)
                    RenderViewerPanelSlice(v, st.ImageIndex, true);
            }

            CenterOrthoSyncPoint();
            UpdateSyncSeriesButtonState();

            var summaryNames = selectedList.Select(x => string.Format("{0}: {1}", DicomSpatialHelper.GetPlaneShortTag(x.Item1), x.Item2.SeriesDescription)).ToArray();
            SetStatusText("Синхронизация срезов активна (" + string.Join(" | ", summaryNames) + ")");
        }

        private void DisableOrthoSeriesSync(bool restoreSingleLayout)
        {
            if (!orthoSeriesSyncEnabled)
                return;

            orthoSeriesSyncEnabled = false;
            orthoSyncCrosshairDragging = false;
            orthoSyncHasPatient3DPoint = false;
            mprMaximizedViewer = null;

            foreach (var v in viewerHosts.Keys)
            {
                var st = GetViewerState(v);
                st.AssignedSyncPlane = AnatomicalPlane.Unknown;
                v.MprPanelEnabled = false;
                v.MprInteractiveCrosshairEnabled = false;
                v.ShowReferenceLines = cursor3dModeEnabled;
                v.ReferenceLineColorHorizontal = null;
                v.ReferenceLineColorVertical = null;
                v.SetReferencePoint(null);
            }

            if (restoreSingleLayout)
            {
                ApplyViewerLayoutPreset(1, 1, false);
                SetActiveViewer(viewer);
                if (images.Count > 0)
                {
                    var safeIdx = Math.Max(0, Math.Min(images.Count - 1, imageIndex));
                    ShowImage(safeIdx);
                }
            }
            else
            {
                if (images.Count > 0)
                    UpdateViewerOverlay(Math.Max(0, Math.Min(images.Count - 1, imageIndex)), null);
            }

            UpdateSyncSeriesButtonState();
            SetStatusText("Синхронизация Axial / Sagittal / Coronal отключена");
        }

        private void FindBestOrthogonalSeriesSet(
            DicomSeries anchorSeries,
            out DicomSeries axialSeries,
            out DicomSeries sagittalSeries,
            out DicomSeries coronalSeries)
        {
            axialSeries = null;
            sagittalSeries = null;
            coronalSeries = null;

            var allSeries = new List<DicomSeries>();
            if (currentStudies != null)
            {
                // Сначала ищем в том же исследовании, которому принадлежит текущая серия
                DicomStudy preferredStudy = null;
                if (anchorSeries != null)
                    preferredStudy = currentStudies.FirstOrDefault(st => st.Series != null && st.Series.Contains(anchorSeries));

                if (preferredStudy != null && preferredStudy.Series != null)
                    allSeries.AddRange(preferredStudy.Series.Where(s => s != null && s.Images != null && s.Images.Count > 0));

                foreach (var st in currentStudies)
                {
                    if (st == null || st.Series == null || ReferenceEquals(st, preferredStudy))
                        continue;
                    allSeries.AddRange(st.Series.Where(s => s != null && s.Images != null && s.Images.Count > 0));
                }
            }

            if (allSeries.Count == 0)
                return;

            foreach (var s in allSeries)
                DicomSpatialHelper.FinalizeSeriesSpatialMetadata(s);

            var anchorForUid = anchorSeries != null ? anchorSeries.FrameOfReferenceUid : null;

            // Если текущая активная серия уже является одной из ортогональных плоскостей — закрепляем её
            if (anchorSeries != null && anchorSeries.Images != null && anchorSeries.Images.Count > 0)
            {
                switch (anchorSeries.Plane)
                {
                    case AnatomicalPlane.Axial:
                        axialSeries = anchorSeries;
                        break;
                    case AnatomicalPlane.Sagittal:
                        sagittalSeries = anchorSeries;
                        break;
                    case AnatomicalPlane.Coronal:
                        coronalSeries = anchorSeries;
                        break;
                }
            }

            Func<AnatomicalPlane, DicomSeries> pickBestForPlane = targetPlane =>
            {
                var candidates = allSeries.Where(s => s.Plane == targetPlane).ToList();
                if (candidates.Count == 0)
                    return null;

                return candidates
                    .OrderByDescending(s => !string.IsNullOrWhiteSpace(anchorForUid) && string.Equals(s.FrameOfReferenceUid, anchorForUid, StringComparison.Ordinal))
                    .ThenByDescending(s => s.Images.Count >= 3)
                    .ThenByDescending(s => s.Images.Count)
                    .FirstOrDefault();
            };

            if (axialSeries == null)
                axialSeries = pickBestForPlane(AnatomicalPlane.Axial);
            if (sagittalSeries == null)
                sagittalSeries = pickBestForPlane(AnatomicalPlane.Sagittal);
            if (coronalSeries == null)
                coronalSeries = pickBestForPlane(AnatomicalPlane.Coronal);

            // Если плоскость не определилась по тегам в некоторых сериях, добираем оставшиеся многосрезовые серии
            var assigned = new HashSet<DicomSeries>();
            if (axialSeries != null) assigned.Add(axialSeries);
            if (sagittalSeries != null) assigned.Add(sagittalSeries);
            if (coronalSeries != null) assigned.Add(coronalSeries);

            if (assigned.Count < 2 && allSeries.Count >= 2)
            {
                foreach (var s in allSeries.OrderByDescending(x => x.Images.Count))
                {
                    if (assigned.Contains(s))
                        continue;

                    if (axialSeries == null)
                    {
                        axialSeries = s;
                        assigned.Add(s);
                    }
                    else if (sagittalSeries == null)
                    {
                        sagittalSeries = s;
                        assigned.Add(s);
                    }
                    else if (coronalSeries == null)
                    {
                        coronalSeries = s;
                        assigned.Add(s);
                    }

                    if (assigned.Count >= 3)
                        break;
                }
            }
        }

        private void ConfigureViewerOrthoSyncAppearance(DicomImageControl targetViewer, AnatomicalPlane plane)
        {
            if (targetViewer == null)
                return;

            targetViewer.MprPanelEnabled = true;
            targetViewer.ShowReferenceLines = mprShowCrosshair;
            targetViewer.MprInteractiveCrosshairEnabled = mprShowCrosshair;
            targetViewer.ReferenceLineAngleDegrees = 0f;
            targetViewer.ReferenceSlabThicknessMm = 1f;

            // Цветовая схема осей точно как в RadiAnt DICOM:
            // Axial = Голубой (#00D7FF), Coronal = Зеленый (#50EB64), Sagittal = Желтый (#FFD232)
            var colorAxial = Color.FromArgb(225, 0, 215, 255);
            var colorCoronal = Color.FromArgb(225, 80, 235, 100);
            var colorSagittal = Color.FromArgb(225, 255, 210, 50);

            switch (plane)
            {
                case AnatomicalPlane.Axial:
                    targetViewer.OrientationTop = "A";
                    targetViewer.OrientationBottom = "P";
                    targetViewer.OrientationLeft = "R";
                    targetViewer.OrientationRight = "L";
                    targetViewer.ReferenceLineColorHorizontal = colorCoronal;
                    targetViewer.ReferenceLineColorVertical = colorSagittal;
                    break;
                case AnatomicalPlane.Coronal:
                    targetViewer.OrientationTop = "S";
                    targetViewer.OrientationBottom = "I";
                    targetViewer.OrientationLeft = "R";
                    targetViewer.OrientationRight = "L";
                    targetViewer.ReferenceLineColorHorizontal = colorAxial;
                    targetViewer.ReferenceLineColorVertical = colorSagittal;
                    break;
                case AnatomicalPlane.Sagittal:
                    targetViewer.OrientationTop = "S";
                    targetViewer.OrientationBottom = "I";
                    targetViewer.OrientationLeft = "A";
                    targetViewer.OrientationRight = "P";
                    targetViewer.ReferenceLineColorHorizontal = colorAxial;
                    targetViewer.ReferenceLineColorVertical = colorCoronal;
                    break;
                default:
                    targetViewer.OrientationTop = "A";
                    targetViewer.OrientationBottom = "P";
                    targetViewer.OrientationLeft = "R";
                    targetViewer.OrientationRight = "L";
                    targetViewer.ReferenceLineColorHorizontal = colorCoronal;
                    targetViewer.ReferenceLineColorVertical = colorSagittal;
                    break;
            }
        }

        private void RenderViewerPanelSlice(DicomImageControl targetViewer, int index, bool resetView)
        {
            if (targetViewer == null)
                return;

            var st = GetViewerState(targetViewer);
            var list = st.Images;
            if (list == null || index < 0 || index >= list.Count)
                return;

            if (!string.IsNullOrWhiteSpace(st.CurrentImagePath))
            {
                var existing = targetViewer.GetMeasurements();
                if (existing.Count == 0)
                    measurementsByImagePath.Remove(st.CurrentImagePath);
                else
                    measurementsByImagePath[st.CurrentImagePath] = existing;
            }

            st.ImageIndex = index;
            var item = list[index];
            st.CurrentImagePath = item.FilePath;

            if (targetViewer == ActiveViewerControl)
            {
                images.Clear();
                images.AddRange(list.Select(CloneImageItem));
                imageIndex = index;
                currentImagePath = item.FilePath;
                if (st.AssignedSeries != null)
                    UpdateSelectedSeriesCard(FindSeriesCard(st.AssignedSeries));
            }

            if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
            {
                if (IsRaster(item.FilePath))
                {
                    using (var stream = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var rawRaster = Image.FromStream(stream))
                    {
                        var bmp = CreateSafeBitmapCopy(rawRaster);
                        targetViewer.PixelDataProvider = null;
                        targetViewer.SetPixelSpacing(0f, 0f);
                        targetViewer.SetImage(bmp, resetView);
                    }
                }
                else
                {
                    DicomSpatialHelper.EnsureItemSpatialMetadata(item, st.AssignedSeries != null ? st.AssignedSeries.SeriesDescription : null);
                    DicomDataset ds;
                    var provider = GetOrCreateCachedSliceProvider(item.FilePath, out ds);
                    if (provider != null)
                    {
                        targetViewer.PixelDataProvider = provider;
                        targetViewer.SetPixelSpacing((float)item.PixelSpacingX, (float)item.PixelSpacingY);

                        var wc = wlPresetCenter ?? (currentAppliedWlWidth > 1.01f ? currentAppliedWlCenter : provider.DefaultWindowCenter);
                        var ww = wlPresetWidth ?? (currentAppliedWlWidth > 1.01f ? currentAppliedWlWidth : provider.DefaultWindowWidth);

                        var bmp = provider.RenderBitmap(wc, ww, targetViewer.Inverted);
                        targetViewer.SetImage(bmp, resetView);
                        targetViewer.SetWindowLevel(wc, ww, true);
                    }
                }
            }

            List<DicomMeasurement> storedMeasurements;
            if (!string.IsNullOrWhiteSpace(item.FilePath) && measurementsByImagePath.TryGetValue(item.FilePath, out storedMeasurements))
                targetViewer.SetMeasurements(storedMeasurements);
            else
                targetViewer.SetMeasurements(null);

            if (orthoSeriesSyncEnabled)
                UpdateOrthoSyncViewerOverlay(targetViewer);
            else if (targetViewer == ActiveViewerControl)
                UpdateViewerOverlay(index, null);

            if (targetViewer == ActiveViewerControl)
            {
                UpdateSliceScrollBar(index);
                previousButton.Enabled = index > 0;
                nextButton.Enabled = index < list.Count - 1;
            }
        }

        private void UpdateOrthoSyncViewerOverlay(DicomImageControl targetViewer)
        {
            if (targetViewer == null)
                return;

            var st = GetViewerState(targetViewer);
            var list = st.Images;
            if (list == null || list.Count == 0)
                return;

            var idx = Math.Max(0, Math.Min(list.Count - 1, st.ImageIndex));
            var item = list[idx];
            var plane = st.AssignedSyncPlane != AnatomicalPlane.Unknown
                ? st.AssignedSyncPlane
                : (st.AssignedSeries != null ? st.AssignedSeries.Plane : item.Plane);

            var planeHeader = DicomSpatialHelper.GetPlaneTitle(plane) + " [SYNC] ▾";
            var seriesTitle = st.AssignedSeries != null ? st.AssignedSeries.SeriesDescription : string.Empty;
            var locValue = item.SliceLocation ?? item.ComputedSlicePosition ?? 0.0;

            targetViewer.OverlayTopLeft = string.Format(
                "{0}\nСрез: {1}/{2} | Loc: {3:0.0} мм",
                planeHeader,
                idx + 1,
                list.Count,
                locValue);

            var activeStudy = currentStudies != null ? currentStudies.FirstOrDefault() : null;
            var studyDateStr = activeStudy != null && activeStudy.StudyDate.HasValue
                ? activeStudy.StudyDate.Value.ToString("dd.MM.yyyy")
                : "—";
            targetViewer.OverlayTopRight = string.Format(
                "{0}\n{1}\n{2}\n{3}",
                activeStudy != null && !string.IsNullOrWhiteSpace(activeStudy.PatientName) ? activeStudy.PatientName : "—",
                activeStudy != null && !string.IsNullOrWhiteSpace(activeStudy.PatientId) ? activeStudy.PatientId : "—",
                studyDateStr,
                seriesTitle);

            var windowLine = string.Format("WL: {0:0.#} WW: {1:0.#}", targetViewer.WindowCenter, targetViewer.WindowWidth);
            if (orthoSyncHasPatient3DPoint)
            {
                targetViewer.OverlayBottomLeft = string.Format(
                    "{0}\n3D: X={1:0.0} Y={2:0.0} Z={3:0.0} мм",
                    windowLine,
                    orthoSyncPatientX,
                    orthoSyncPatientY,
                    orthoSyncPatientZ);
            }
            else
            {
                targetViewer.OverlayBottomLeft = windowLine;
            }

            targetViewer.OverlayBottomRight = string.Format(
                "{0} | Толщ: {1:0.##} мм",
                st.AssignedSeries != null && !string.IsNullOrWhiteSpace(st.AssignedSeries.Modality) ? st.AssignedSeries.Modality : "DICOM",
                item.SliceThickness > 0 ? item.SliceThickness : 1.0);

            targetViewer.Invalidate();
        }

        private void CenterOrthoSyncPoint()
        {
            if (!orthoSeriesSyncEnabled)
                return;

            var active = ActiveViewerControl ?? viewer;
            var st = GetViewerState(active);
            if (st.Images != null && st.Images.Count > 0)
            {
                var idx = Math.Max(0, Math.Min(st.Images.Count - 1, st.ImageIndex));
                var item = st.Images[idx];
                DicomSpatialHelper.EnsureItemSpatialMetadata(item);

                var cx = (float)(Math.Max(1, item.Columns > 0 ? item.Columns : 512) * 0.5);
                var cy = (float)(Math.Max(1, item.Rows > 0 ? item.Rows : 512) * 0.5);
                double px, py, pz;
                if (DicomSpatialHelper.TryImagePixelToPatient3D(item, cx, cy, out px, out py, out pz))
                {
                    orthoSyncHasPatient3DPoint = true;
                    orthoSyncPatientX = px;
                    orthoSyncPatientY = py;
                    orthoSyncPatientZ = pz;
                }
            }

            orthoSyncNormX = 0.5f;
            orthoSyncNormY = 0.5f;
            orthoSyncNormZ = 0.5f;

            SyncAllOrthoPanelsToCurrentPoint(null, true);
        }

        private void OnOrthoPanelSliceScrolled(DicomImageControl sourceViewer, int newSliceIndex)
        {
            if (!orthoSeriesSyncEnabled || sourceViewer == null)
                return;

            var st = GetViewerState(sourceViewer);
            if (st.Images == null || st.Images.Count == 0)
                return;

            var clamped = Math.Max(0, Math.Min(st.Images.Count - 1, newSliceIndex));
            var item = st.Images[clamped];
            DicomSpatialHelper.EnsureItemSpatialMetadata(item);

            var plane = st.AssignedSyncPlane != AnatomicalPlane.Unknown ? st.AssignedSyncPlane : item.Plane;
            var normPos = st.Images.Count > 1 ? (float)clamped / (st.Images.Count - 1) : 0.5f;

            switch (plane)
            {
                case AnatomicalPlane.Axial:
                    orthoSyncNormZ = normPos;
                    break;
                case AnatomicalPlane.Coronal:
                    orthoSyncNormY = normPos;
                    break;
                case AnatomicalPlane.Sagittal:
                    orthoSyncNormX = normPos;
                    break;
            }

            if (item.HasSpatialGeometry && item.ImagePositionPatient != null && item.ImageOrientationPatient != null)
            {
                double nx, ny, nz;
                DicomSpatialHelper.ComputeNormal(item.ImageOrientationPatient, out nx, out ny, out nz);

                if (orthoSyncHasPatient3DPoint)
                {
                    // Смещаем 3D-точку вдоль нормали прокручиваемой серии на плоскость нового среза,
                    // сохраняя 2 остальные координаты неизменными
                    var dx = orthoSyncPatientX - item.ImagePositionPatient[0];
                    var dy = orthoSyncPatientY - item.ImagePositionPatient[1];
                    var dz = orthoSyncPatientZ - item.ImagePositionPatient[2];
                    var distToNewPlane = dx * nx + dy * ny + dz * nz;

                    orthoSyncPatientX -= distToNewPlane * nx;
                    orthoSyncPatientY -= distToNewPlane * ny;
                    orthoSyncPatientZ -= distToNewPlane * nz;
                }
                else
                {
                    double px, py, pz;
                    if (DicomSpatialHelper.TryImagePixelToPatient3D(item, (float)(item.Columns * 0.5), (float)(item.Rows * 0.5), out px, out py, out pz))
                    {
                        orthoSyncHasPatient3DPoint = true;
                        orthoSyncPatientX = px;
                        orthoSyncPatientY = py;
                        orthoSyncPatientZ = pz;
                    }
                }
            }

            // При прокрутке колеса мыши на одной панели обновляем положения линий пересечения (Localizers)
            // на всех ортогональных панелях и подписи координат
            UpdateAllOrthoSyncCrosshairsAndOverlays();
        }

        private void UpdateOrthoSyncFromScreenPoint(DicomImageControl sourceViewer, Point screenPoint)
        {
            if (!orthoSeriesSyncEnabled || sourceViewer == null)
                return;

            PointF imagePoint;
            if (!sourceViewer.TryGetImagePointAt(screenPoint, out imagePoint))
                return;

            UpdateOrthoSyncFromImagePoint(sourceViewer, imagePoint, true, true);
        }

        private void UpdateOrthoSyncFromImagePoint(DicomImageControl sourceViewer, PointF imagePoint, bool moveHorizontalCoord, bool moveVerticalCoord)
        {
            if (!orthoSeriesSyncEnabled || sourceViewer == null)
                return;

            var st = GetViewerState(sourceViewer);
            if (st.Images == null || st.Images.Count == 0)
                return;

            var idx = Math.Max(0, Math.Min(st.Images.Count - 1, st.ImageIndex));
            var item = st.Images[idx];
            DicomSpatialHelper.EnsureItemSpatialMetadata(item);

            double cols = Math.Max(1, item.Columns > 0 ? item.Columns : (sourceViewer.PixelDataProvider != null ? sourceViewer.PixelDataProvider.Width : 512));
            double rows = Math.Max(1, item.Rows > 0 ? item.Rows : (sourceViewer.PixelDataProvider != null ? sourceViewer.PixelDataProvider.Height : 512));

            var curRef = sourceViewer.ReferencePoint ?? new PointF((float)(cols * 0.5), (float)(rows * 0.5));
            var targetPixelX = (float)(moveHorizontalCoord ? Math.Max(0.0, Math.Min(cols - 1.0, imagePoint.X)) : curRef.X);
            var targetPixelY = (float)(moveVerticalCoord ? Math.Max(0.0, Math.Min(rows - 1.0, imagePoint.Y)) : curRef.Y);

            var normU = (float)(targetPixelX / Math.Max(1.0, cols - 1.0));
            var normV = (float)(targetPixelY / Math.Max(1.0, rows - 1.0));

            var plane = st.AssignedSyncPlane != AnatomicalPlane.Unknown ? st.AssignedSyncPlane : item.Plane;
            switch (plane)
            {
                case AnatomicalPlane.Axial:
                    if (moveHorizontalCoord) orthoSyncNormX = normU;
                    if (moveVerticalCoord) orthoSyncNormY = normV;
                    break;
                case AnatomicalPlane.Coronal:
                    if (moveHorizontalCoord) orthoSyncNormX = normU;
                    if (moveVerticalCoord) orthoSyncNormZ = normV;
                    break;
                case AnatomicalPlane.Sagittal:
                    if (moveHorizontalCoord) orthoSyncNormY = normU;
                    if (moveVerticalCoord) orthoSyncNormZ = normV;
                    break;
            }

            double px, py, pz;
            if (DicomSpatialHelper.TryImagePixelToPatient3D(item, targetPixelX, targetPixelY, out px, out py, out pz))
            {
                orthoSyncHasPatient3DPoint = true;
                orthoSyncPatientX = px;
                orthoSyncPatientY = py;
                orthoSyncPatientZ = pz;
            }

            SyncAllOrthoPanelsToCurrentPoint(sourceViewer, true);
        }

        private void SyncAllOrthoPanelsToCurrentPoint(DicomImageControl sourceViewer, bool switchSlicesInOrthogonalPanels)
        {
            if (!orthoSeriesSyncEnabled)
                return;

            if (switchSlicesInOrthogonalPanels)
            {
                foreach (var v in viewerHosts.Keys)
                {
                    if (v == sourceViewer)
                        continue;

                    var st = GetViewerState(v);
                    if (st.Images == null || st.Images.Count <= 1)
                        continue;

                    var bestIdx = FindBestMatchingSliceIndex(st, orthoSyncHasPatient3DPoint, orthoSyncPatientX, orthoSyncPatientY, orthoSyncPatientZ);
                    if (bestIdx != st.ImageIndex)
                        RenderViewerPanelSlice(v, bestIdx, false);
                }
            }

            UpdateAllOrthoSyncCrosshairsAndOverlays();
        }

        private int FindBestMatchingSliceIndex(
            ViewerPanelState panelState,
            bool hasPatient3D,
            double patientX,
            double patientY,
            double patientZ)
        {
            var list = panelState.Images;
            if (list == null || list.Count == 0)
                return 0;

            if (hasPatient3D)
            {
                int bestIndex = -1;
                double bestDistance = double.MaxValue;

                for (int i = 0; i < list.Count; i++)
                {
                    var img = list[i];
                    DicomSpatialHelper.EnsureItemSpatialMetadata(img);
                    double dist;
                    float pX, pY;
                    if (DicomSpatialHelper.TryPatient3DToImagePixel(img, patientX, patientY, patientZ, out pX, out pY, out dist))
                    {
                        var absDist = Math.Abs(dist);
                        if (absDist < bestDistance)
                        {
                            bestDistance = absDist;
                            bestIndex = i;
                        }
                    }
                }

                if (bestIndex >= 0)
                    return bestIndex;
            }

            // Fallback по нормализованным координатам [0..1], если теги 3D-позиции отсутствуют
            float normTarget = 0.5f;
            switch (panelState.AssignedSyncPlane)
            {
                case AnatomicalPlane.Axial:
                    normTarget = orthoSyncNormZ;
                    break;
                case AnatomicalPlane.Coronal:
                    normTarget = orthoSyncNormY;
                    break;
                case AnatomicalPlane.Sagittal:
                    normTarget = orthoSyncNormX;
                    break;
            }

            return Math.Max(0, Math.Min(list.Count - 1, (int)Math.Round(normTarget * (list.Count - 1))));
        }

        private void UpdateAllOrthoSyncCrosshairsAndOverlays()
        {
            if (!orthoSeriesSyncEnabled)
                return;

            foreach (var v in viewerHosts.Keys)
            {
                var st = GetViewerState(v);
                if (st.Images == null || st.Images.Count == 0)
                    continue;

                var idx = Math.Max(0, Math.Min(st.Images.Count - 1, st.ImageIndex));
                var item = st.Images[idx];
                DicomSpatialHelper.EnsureItemSpatialMetadata(item);

                double cols = Math.Max(1, item.Columns > 0 ? item.Columns : (v.PixelDataProvider != null ? v.PixelDataProvider.Width : 512));
                double rows = Math.Max(1, item.Rows > 0 ? item.Rows : (v.PixelDataProvider != null ? v.PixelDataProvider.Height : 512));

                PointF refPt;
                float px, py;
                double dist;
                if (orthoSyncHasPatient3DPoint && DicomSpatialHelper.TryPatient3DToImagePixel(item, orthoSyncPatientX, orthoSyncPatientY, orthoSyncPatientZ, out px, out py, out dist))
                {
                    refPt = new PointF(
                        (float)Math.Max(0.0, Math.Min(cols - 1.0, px)),
                        (float)Math.Max(0.0, Math.Min(rows - 1.0, py)));
                }
                else
                {
                    float u = 0.5f;
                    float w = 0.5f;
                    switch (st.AssignedSyncPlane)
                    {
                        case AnatomicalPlane.Axial:
                            u = orthoSyncNormX;
                            w = orthoSyncNormY;
                            break;
                        case AnatomicalPlane.Coronal:
                            u = orthoSyncNormX;
                            w = orthoSyncNormZ;
                            break;
                        case AnatomicalPlane.Sagittal:
                            u = orthoSyncNormY;
                            w = orthoSyncNormZ;
                            break;
                    }
                    refPt = new PointF((float)(u * (cols - 1.0)), (float)(w * (rows - 1.0)));
                }

                v.SetReferencePoint(refPt);
                UpdateOrthoSyncViewerOverlay(v);
            }
        }

        private void AssignSeriesToOrthoSyncViewer(DicomImageControl targetViewer, DicomSeries series)
        {
            if (targetViewer == null || series == null || series.Images == null || series.Images.Count == 0)
                return;

            DicomSpatialHelper.FinalizeSeriesSpatialMetadata(series);
            var st = GetViewerState(targetViewer);
            st.AssignedSeries = series;
            st.AssignedSyncPlane = series.Plane != AnatomicalPlane.Unknown ? series.Plane : st.AssignedSyncPlane;
            st.Images.Clear();
            st.Images.AddRange(series.Images.Select(CloneImageItem));
            st.ImageIndex = FindBestMatchingSliceIndex(st, orthoSyncHasPatient3DPoint, orthoSyncPatientX, orthoSyncPatientY, orthoSyncPatientZ);

            ConfigureViewerOrthoSyncAppearance(targetViewer, st.AssignedSyncPlane);
            RenderViewerPanelSlice(targetViewer, st.ImageIndex, true);
            UpdateAllOrthoSyncCrosshairsAndOverlays();
            UpdateSelectedSeriesCard(FindSeriesCard(series));
        }

        private void OnViewerOrthoSyncContextMenuRequested(DicomImageControl sourceViewer, Point screenPoint)
        {
            if (!orthoSeriesSyncEnabled || sourceViewer == null)
                return;

            SetActiveViewer(sourceViewer);
            var st = GetViewerState(sourceViewer);
            var menu = new ContextMenu();

            var seriesMenu = new MenuItem("Выбрать серию для этой панели");
            if (currentStudies != null)
            {
                foreach (var study in currentStudies)
                {
                    if (study == null || study.Series == null)
                        continue;

                    foreach (var ser in study.Series)
                    {
                        if (ser == null || ser.Images == null || ser.Images.Count == 0)
                            continue;

                        var capturedSeries = ser;
                        var item = new MenuItem(ser.DisplayName, delegate { AssignSeriesToOrthoSyncViewer(sourceViewer, capturedSeries); })
                        {
                            Checked = ReferenceEquals(st.AssignedSeries, capturedSeries)
                        };
                        seriesMenu.MenuItems.Add(item);
                    }
                }
            }
            menu.MenuItems.Add(seriesMenu);

            var planeMenu = new MenuItem("Назначить ориентацию панели");
            foreach (var p in new[] { AnatomicalPlane.Axial, AnatomicalPlane.Sagittal, AnatomicalPlane.Coronal })
            {
                var capturedPlane = p;
                planeMenu.MenuItems.Add(new MenuItem(DicomSpatialHelper.GetPlaneTitle(capturedPlane), delegate
                {
                    st.AssignedSyncPlane = capturedPlane;
                    ConfigureViewerOrthoSyncAppearance(sourceViewer, capturedPlane);
                    UpdateAllOrthoSyncCrosshairsAndOverlays();
                })
                {
                    Checked = st.AssignedSyncPlane == capturedPlane
                });
            }
            menu.MenuItems.Add(planeMenu);

            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem("Автоподбор серий Axial + Sagittal + Coronal (1×3)", delegate { EnableOrthoSeriesSync(false); }));
            menu.MenuItems.Add(new MenuItem("Разложить в сетку 2×2", delegate { EnableOrthoSeriesSync(true); }));
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem("Показывать линии 3D-локализатора (F12)", delegate { ToggleMprCrosshairVisibility(); }) { Checked = mprShowCrosshair });
            menu.MenuItems.Add(new MenuItem("Центрировать 3D-точку синхронизации", delegate { CenterOrthoSyncPoint(); }));
            menu.MenuItems.Add(new MenuItem(mprMaximizedViewer == sourceViewer ? "Свернуть панель обратно к сетке" : "Развернуть эту панель на весь экран", delegate { OnViewerMprDoubleClick(sourceViewer); }));
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem("Выключить синхронизацию серий", delegate { DisableOrthoSeriesSync(true); }));

            var clientPoint = sourceViewer.PointToClient(screenPoint);
            menu.Show(sourceViewer, clientPoint);
        }
    }
}
