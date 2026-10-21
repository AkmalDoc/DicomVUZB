using System;
using System.Collections.Generic;
using System.Linq;

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

        public IEnumerable<DicomImageItem> Images
        {
            get { return Series.SelectMany(series => series.Images); }
        }
    }
}
