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
            var files = File.Exists(path) ? new[] { path } : Directory.GetFiles(path, "*", SearchOption.AllDirectories);
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
                            SeriesNumber = GetInt(dataset, DicomTag.SeriesNumber),
                            Modality = Get(dataset, DicomTag.Modality, string.Empty)
                        };
                        study.Series.Add(series);
                    }

                    var imageItem = new DicomImageItem
                    {
                        FilePath = file,
                        InstanceNumber = GetInt(dataset, DicomTag.InstanceNumber),
                        SopInstanceUid = Get(dataset, DicomTag.SOPInstanceUID, file),
                        SliceLocation = GetDouble(dataset, DicomTag.SliceLocation)
                    };
                    DicomSpatialHelper.PopulateFromDataset(imageItem, dataset, series.SeriesDescription);
                    series.Images.Add(imageItem);
                }
                catch (DicomException)
                {
                }
                catch (InvalidDataException)
                {
                }
                catch (Exception)
                {
                }
            }

            foreach (var study in studies.Values)
            {
                foreach (var series in study.Series)
                {
                    series.Images.Sort((a, b) => a.InstanceNumber.CompareTo(b.InstanceNumber));
                    DicomSpatialHelper.FinalizeSeriesSpatialMetadata(series);
                }
            }

            return studies.Values.ToList();
        }

        private static bool IsSupported(string file)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension == ".dcm" || extension == ".dicom" || extension == ".ima" || string.IsNullOrEmpty(extension))
                return true;

            return HasDicomMagic(file);
        }

        private static bool HasDicomMagic(string file)
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Length < 132)
                    return false;

                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < 132)
                        return false;
                    fs.Seek(128, SeekOrigin.Begin);
                    return fs.ReadByte() == 'D' &&
                           fs.ReadByte() == 'I' &&
                           fs.ReadByte() == 'C' &&
                           fs.ReadByte() == 'M';
                }
            }
            catch
            {
                return false;
            }
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
