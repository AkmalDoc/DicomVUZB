using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dicom;
using Dicom.Imaging;

namespace DicomVUZB
{
    public sealed class DicomImageItem
    {
        public string FilePath { get; set; }
        public int InstanceNumber { get; set; }
        public string SopInstanceUid { get; set; }
        public double? SliceLocation { get; set; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(FilePath) ? "Изображение" : System.IO.Path.GetFileName(FilePath);
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
        public List<DicomImageItem> Images { get; private set; }

        public string DisplayName
        {
            get
            {
                var description = string.IsNullOrWhiteSpace(SeriesDescription) ? "Без описания" : SeriesDescription;
                return string.Format("{0}. {1} ({2})", SeriesNumber, description, Images.Count);
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

    public partial class Form1 : Form
    {
        private readonly DicomRepository repository = new DicomRepository();
        private readonly DicomDirectoryRepository directoryRepository = new DicomDirectoryRepository();
        private readonly TreeView studiesTree = new TreeView();
        private readonly DicomImageControl viewer = new DicomImageControl();
        private readonly Label status = new Label();
        private readonly ToolStripButton previousButton = new ToolStripButton("◀");
        private readonly ToolStripButton nextButton = new ToolStripButton("▶");
        private readonly List<DicomImageItem> images = new List<DicomImageItem>();
        private int imageIndex;

        public Form1()
        {
            InitializeComponent();
            BuildInterface();
        }

        private void BuildInterface()
        {
            Text = "DicomVUZB — DICOM Viewer";
            WindowState = FormWindowState.Maximized;
            BackColor = Color.FromArgb(35, 35, 35);
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("Файл");
            file.DropDownItems.Add("Открыть файл...", null, delegate { OpenFile(); });
            file.DropDownItems.Add("Открыть папку...", null, delegate { OpenFolder(); });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("Выход", null, delegate { Close(); });
            var view = new ToolStripMenuItem("Вид");
            view.DropDownItems.Add("Сбросить вид", null, delegate { viewer.ResetView(); });
            view.DropDownItems.Add("Инверсия", null, delegate { viewer.Inverted = !viewer.Inverted; viewer.Invalidate(); });
            view.DropDownItems.Add("Измерение", null, delegate { viewer.Tool = ViewerTool.Measure; });
            view.DropDownItems.Add("Панорамирование", null, delegate { viewer.Tool = ViewerTool.Pan; });
            var advanced = new ToolStripMenuItem("Расширения");
            advanced.DropDownItems.Add("DICOMDIR — подготовлено для следующего этапа", null, delegate { MessageBox.Show("Поддержка DICOMDIR будет добавлена следующим этапом."); });
            advanced.DropDownItems.Add("PACS / MPR / 3D — подготовлено для следующего этапа", null, delegate { MessageBox.Show("PACS, MPR и 3D требуют отдельной реализации геометрии и сетевых настроек."); });
            menu.Items.Add(file);
            menu.Items.Add(view);
            menu.Items.Add(advanced);
            MainMenuStrip = menu;
            Controls.Add(menu);

            var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
            toolbar.Items.Add(new ToolStripButton("Открыть", null, delegate { OpenFile(); }));
            toolbar.Items.Add(new ToolStripButton("Папка", null, delegate { OpenFolder(); }));
            toolbar.Items.Add(new ToolStripSeparator());
            previousButton.Click += delegate { SelectImage(imageIndex - 1); };
            nextButton.Click += delegate { SelectImage(imageIndex + 1); };
            toolbar.Items.Add(previousButton);
            toolbar.Items.Add(nextButton);
            toolbar.Items.Add(new ToolStripButton("100%", null, delegate { viewer.ResetView(); }));
            toolbar.Items.Add(new ToolStripButton("+", null, delegate { viewer.ZoomBy(1.25f); }));
            toolbar.Items.Add(new ToolStripButton("−", null, delegate { viewer.ZoomBy(0.8f); }));
            toolbar.Items.Add(new ToolStripButton("Измерить", null, delegate { viewer.Tool = ViewerTool.Measure; }));
            toolbar.Items.Add(new ToolStripButton("Панорамировать", null, delegate { viewer.Tool = ViewerTool.Pan; }));
            Controls.Add(toolbar);

            studiesTree.Dock = DockStyle.Fill;
            studiesTree.BackColor = Color.FromArgb(48, 48, 48);
            studiesTree.ForeColor = Color.White;
            studiesTree.HideSelection = false;
            studiesTree.AfterSelect += OnTreeSelect;

            viewer.Dock = DockStyle.Fill;
            viewer.MinimumSize = new Size(300, 300);

            status.Text = "Перетащите DICOM-файл или папку сюда";
            status.Dock = DockStyle.Bottom;
            status.Height = 24;
            status.ForeColor = Color.Gainsboro;
            status.Padding = new Padding(8, 4, 0, 0);

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 280, BackColor = Color.FromArgb(35, 35, 35) };
            split.Panel1.Controls.Add(studiesTree);
            split.Panel2.Controls.Add(viewer);
            Controls.Add(split);
            Controls.Add(status);
        }

        private void OpenFile()
        {
            using (var dialog = new OpenFileDialog { Filter = "DICOM и изображения|DICOMDIR;*.dcm;*.dicom;*.jpg;*.jpeg;*.png|Все файлы|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    LoadPath(dialog.FileName);
        }

        private void OpenFolder()
        {
            using (var dialog = new FolderBrowserDialog())
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    LoadPath(dialog.SelectedPath);
        }

        private void LoadPath(string path)
        {
            try
            {
                var dicomDirectory = FindDicomDirectory(path);
                if (dicomDirectory != null)
                {
                    LoadStudies(directoryRepository.Load(dicomDirectory));
                    status.Text = "Открыт DICOMDIR: " + Path.GetFileName(dicomDirectory);
                    return;
                }

                var rasterFiles = Directory.Exists(path)
                    ? Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).Where(IsRaster).ToList()
                    : new List<string>();
                if (IsRaster(path) || (rasterFiles.Count > 0 && !Directory.GetFiles(path, "*.dcm", SearchOption.AllDirectories).Any()))
                {
                    images.Clear();
                    if (IsRaster(path))
                        rasterFiles.Add(path);
                    images.AddRange(rasterFiles.Select((file, index) => new DicomImageItem { FilePath = file, InstanceNumber = index + 1 }));
                    ShowImage(0);
                    studiesTree.Nodes.Clear();
                    status.Text = string.Format("Изображений: {0}", images.Count);
                    return;
                }

                LoadStudies(repository.Load(path));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Ошибка загрузки", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadStudies(List<DicomStudy> studies)
        {
                studiesTree.BeginUpdate();
                studiesTree.Nodes.Clear();
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
                    }
                    studiesTree.Nodes.Add(studyNode);
                }
                studiesTree.EndUpdate();
                if (studiesTree.Nodes.Count > 0)
                    studiesTree.SelectedNode = studiesTree.Nodes[0].FirstNode?.FirstNode ?? studiesTree.Nodes[0];
                status.Text = string.Format("Исследований: {0}", studies.Count);
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
            imageIndex = index;
            var path = images[index].FilePath;
            Image loaded;
            if (IsRaster(path))
                loaded = Image.FromFile(path);
            else
            {
                var dicomFile = DicomFile.Open(path);
                loaded = new DicomImage(dicomFile.Dataset).RenderImage().AsBitmap();
            }
            viewer.SetImage(loaded);
            status.Text = string.Format("{0} / {1}: {2}", index + 1, images.Count, Path.GetFileName(path));
            previousButton.Enabled = index > 0;
            nextButton.Enabled = index < images.Count - 1;
        }

        private void SelectImage(int index)
        {
            if (index >= 0 && index < images.Count)
                ShowImage(index);
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
    }
}
