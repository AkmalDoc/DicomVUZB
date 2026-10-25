using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dicom;
using Dicom.Media;

namespace DicomVUZB
{
    public sealed class DicomDirectoryRepository
    {
        public List<DicomStudy> Load(string dicomDirectoryPath)
        {
            var directory = DicomDirectory.Open(dicomDirectoryPath);
            var studies = new List<DicomStudy>();

            foreach (var patientRecord in directory.RootDirectoryRecordCollection)
            {
                if (!IsRecord(patientRecord, "PATIENT"))
                    continue;

                foreach (var studyRecord in patientRecord.LowerLevelDirectoryRecordCollection)
                {
                    if (!IsRecord(studyRecord, "STUDY"))
                        continue;

                    var study = new DicomStudy
                    {
                        StudyInstanceUid = Get(studyRecord, DicomTag.StudyInstanceUID, Guid.NewGuid().ToString()),
                        PatientName = Get(patientRecord, DicomTag.PatientName, "Неизвестный пациент"),
                        PatientId = Get(patientRecord, DicomTag.PatientID, string.Empty),
                        StudyDescription = Get(studyRecord, DicomTag.StudyDescription, "Исследование")
                    };
                    AddStudy(studies, study);

                    foreach (var seriesRecord in studyRecord.LowerLevelDirectoryRecordCollection)
                    {
                        if (!IsRecord(seriesRecord, "SERIES"))
                            continue;

                        var series = new DicomSeries
                        {
                            SeriesInstanceUid = Get(seriesRecord, DicomTag.SeriesInstanceUID, Guid.NewGuid().ToString()),
                            SeriesDescription = Get(seriesRecord, DicomTag.SeriesDescription, "Без описания"),
                            SeriesNumber = GetInt(seriesRecord, DicomTag.SeriesNumber),
                            Modality = Get(seriesRecord, DicomTag.Modality, string.Empty)
                        };
                        study.Series.Add(series);

                        foreach (var imageRecord in seriesRecord.LowerLevelDirectoryRecordCollection)
                        {
                            if (!IsRecord(imageRecord, "IMAGE"))
                                continue;

                            var relativePath = GetFileId(imageRecord);
                            var fullPath = ResolvePath(Path.GetDirectoryName(dicomDirectoryPath), relativePath);
                            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                                continue;

                            series.Images.Add(new DicomImageItem
                            {
                                FilePath = fullPath,
                                InstanceNumber = GetInt(imageRecord, DicomTag.InstanceNumber),
                                SopInstanceUid = Get(imageRecord, DicomTag.ReferencedSOPInstanceUIDInFile, fullPath)
                            });
                        }
                        series.Images.Sort((a, b) => a.InstanceNumber.CompareTo(b.InstanceNumber));
                        DicomSpatialHelper.FinalizeSeriesSpatialMetadata(series);
                    }
                }
            }

            return studies.Where(study => study.Series.Any(series => series.Images.Count > 0)).ToList();
        }

        private static void AddStudy(List<DicomStudy> studies, DicomStudy study)
        {
            var existing = studies.FirstOrDefault(item => item.StudyInstanceUid == study.StudyInstanceUid);
            if (existing == null)
                studies.Add(study);
        }

        private static bool IsRecord(DicomDirectoryRecord record, string type)
        {
            return record != null && string.Equals(record.DirectoryRecordType, type, StringComparison.OrdinalIgnoreCase);
        }

        private static string Get(DicomDirectoryRecord record, DicomTag tag, string fallback)
        {
            string value;
            return record.TryGetSingleValue(tag, out value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
        }

        private static int GetInt(DicomDirectoryRecord record, DicomTag tag)
        {
            int value;
            return record.TryGetSingleValue(tag, out value) ? value : 0;
        }

        private static string GetFileId(DicomDirectoryRecord record)
        {
            try
            {
                var parts = record.GetValues<string>(DicomTag.ReferencedFileID);
                return parts == null ? null : Path.Combine(parts);
            }
            catch (DicomDataException)
            {
                return null;
            }
        }

        private static string ResolvePath(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;
            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(root, normalized));
        }
    }
}
