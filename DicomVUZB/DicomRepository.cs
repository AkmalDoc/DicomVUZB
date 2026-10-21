using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Dicom;

namespace DicomVUZB
{
    public sealed class DicomRepository
    {
        public List<DicomStudy> Load(string path)
        {
            var files = File.Exists(path) ? new[] { path } : Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
            var studies = new Dictionary<string, DicomStudy>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                if (!IsSupported(file))
                    continue;

                try
                {
                    var dicomFile = DicomFile.Open(file);
                    var dataset = dicomFile.Dataset;
                    var studyUid = Get(dataset, DicomTag.StudyInstanceUID, file);
                    var seriesUid = Get(dataset, DicomTag.SeriesInstanceUID, file);

                    DicomStudy study;
                    if (!studies.TryGetValue(studyUid, out study))
                    {
                        study = new DicomStudy
                        {
                            StudyInstanceUid = studyUid,
                            PatientName = Get(dataset, DicomTag.PatientName, "Неизвестный пациент"),
                            PatientId = Get(dataset, DicomTag.PatientID, string.Empty),
                            StudyDescription = Get(dataset, DicomTag.StudyDescription, "Исследование"),
                            StudyDate = ParseDate(Get(dataset, DicomTag.StudyDate, string.Empty))
                        };
                        studies.Add(studyUid, study);
                    }

                    var series = study.Series.FirstOrDefault(item => item.SeriesInstanceUid == seriesUid);
                    if (series == null)
                    {
                        series = new DicomSeries
                        {
                            SeriesInstanceUid = seriesUid,
                            SeriesDescription = Get(dataset, DicomTag.SeriesDescription, "Без описания"),
                            SeriesNumber = GetInt(dataset, DicomTag.SeriesNumber)
                        };
                        study.Series.Add(series);
                    }

                    series.Images.Add(new DicomImageItem
                    {
                        FilePath = file,
                        InstanceNumber = GetInt(dataset, DicomTag.InstanceNumber),
                        SopInstanceUid = Get(dataset, DicomTag.SOPInstanceUID, file),
                        SliceLocation = GetDouble(dataset, DicomTag.SliceLocation)
                    });
                }
                catch (DicomException)
                {
                }
                catch (InvalidDataException)
                {
                }
            }

            foreach (var study in studies.Values)
                foreach (var series in study.Series)
                    series.Images.Sort((a, b) => a.InstanceNumber.CompareTo(b.InstanceNumber));

            return studies.Values.ToList();
        }

        private static bool IsSupported(string file)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            return extension == ".dcm" || extension == ".dicom" || string.IsNullOrEmpty(extension);
        }

        private static string Get(DicomDataset dataset, DicomTag tag, string fallback)
        {
            string value;
            return dataset.TryGetSingleValue(tag, out value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
        }

        private static int GetInt(DicomDataset dataset, DicomTag tag)
        {
            int value;
            return dataset.TryGetSingleValue(tag, out value) ? value : 0;
        }

        private static double? GetDouble(DicomDataset dataset, DicomTag tag)
        {
            double value;
            return dataset.TryGetSingleValue(tag, out value) ? (double?)value : null;
        }

        private static DateTime? ParseDate(string value)
        {
            DateTime date;
            return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? (DateTime?)date : null;
        }
    }
}
