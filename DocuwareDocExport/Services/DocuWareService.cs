using DocuWare.Platform.ServerClient;
using DocuwareDocExport.Models;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DocuwareDocExport.Services
{
    public class DocuWareService
    {
        public List<(string Id, string Name)> GetAccessibleFileCabinets(string url, string username, string password)
        {
            var conn = ServiceConnection.Create(new Uri(url), username, password);
            var org = conn.Organizations[0];

            var cabinets = org.GetFileCabinetsFromFilecabinetsRelation().FileCabinet;

            return cabinets
                .Where(c => !c.IsBasket)
                .Select(c => (c.Id, c.Name))
                .OrderBy(c => c.Name)
                .ToList();
        }

        public List<string> GetIndexFieldNames(string url, string username, string password, string cabinetId)
        {
            var conn = ServiceConnection.Create(new Uri(url), username, password);
            var cabinet = conn.GetFileCabinet(cabinetId).GetFileCabinetFromSelfRelation();
            var fields = cabinet.Fields ?? new List<FileCabinetField>();

            return fields
                .Select(f => f.DBFieldName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        private List<Document> GetAllDocumentsFromQuery(Dialog dialog, DialogExpression query)
        {
            var allDocs = new List<Document>();

            var result = dialog.GetDocumentsResult(query);

            if (result?.Items != null)
            {
                allDocs.AddRange(result.Items.Cast<Document>());
            }

            while (!string.IsNullOrWhiteSpace(result?.NextRelationLink))
            {
                result = result.GetDocumentsQueryResultFromNextRelation();

                if (result?.Items != null)
                {
                    allDocs.AddRange(result.Items.Cast<Document>());
                }
            }

            return allDocs;
        }

        public List<string> GetDocumentKeysForCabinet(
            string url,
            string username,
            string password,
            string cabinetId,
            string fromDate,
            string toDate,
            string? selectedFieldName,
            string? selectedFieldValue,
            string? titleKeyword
        )
        {
            var conn = ServiceConnection.Create(new Uri(url), username, password);
            var cabinet = conn.GetFileCabinet(cabinetId);
            var dialog = cabinet.GetDialogFromCustomSearchRelation();

            var conditions = new List<DialogExpressionCondition>
            {
                DialogExpressionCondition.Create("DWSTOREDATETIME", fromDate, toDate)
            };

            if (!string.IsNullOrWhiteSpace(selectedFieldName) &&
                !string.IsNullOrWhiteSpace(selectedFieldValue))
            {
                conditions.Add(DialogExpressionCondition.Create(selectedFieldName, selectedFieldValue));
            }

            var query = new DialogExpression
            {
                Operation = DialogExpressionOperation.And,
                Condition = conditions
            };

            var docs = GetAllDocumentsFromQuery(dialog, query);

            if (!string.IsNullOrWhiteSpace(titleKeyword))
            {
                docs = docs
                    .Where(d => (d.Title ?? string.Empty)
                    .Contains(titleKeyword, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return docs
                .Select(d => $"{cabinet.Id}|{d.Id}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<string> GetFieldValuesForCabinet(
            string url,
            string username,
            string password,
            string cabinetId,
            string fieldName,
            string fromDate,
            string toDate)
        {
            var conn = ServiceConnection.Create(new Uri(url), username, password);
            var cabinet = conn.GetFileCabinet(cabinetId);
            var dialog = cabinet.GetDialogFromCustomSearchRelation();

            var query = new DialogExpression
            {
                Operation = DialogExpressionOperation.And,
                Condition = new List<DialogExpressionCondition>
                {
                    DialogExpressionCondition.Create("DWSTOREDATETIME", fromDate, toDate)
                }
            };

            var docs = GetAllDocumentsFromQuery(dialog, query);

            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var doc in docs)
            {
                try
                {
                    var response = doc.GetDocumentIndexFieldsFromFieldsRelationAsync()
                        .GetAwaiter()
                        .GetResult();

                    var indexFields = response.Content;
                    var match = indexFields.Field.FirstOrDefault(f =>
                        !string.IsNullOrWhiteSpace(f.FieldName) &&
                        f.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

                    var value = match?.Item?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value.Trim());
                    }
                }
                catch
                {
                }
            }

            return values.OrderBy(v => v).ToList();
        }

        public List<DocumentPickItem> GetDocumentsForCabinet(
            string url,
            string username,
            string password,
            string cabinetId,
            string fromDate,
            string toDate,
            string? selectedFieldName,
            string? selectedFieldValue,
            string? titleKeyword
        )
        {
            var conn = ServiceConnection.Create(new Uri(url), username, password);
            var cabinet = conn.GetFileCabinet(cabinetId);
            var dialog = cabinet.GetDialogFromCustomSearchRelation();

            var conditions = new List<DialogExpressionCondition>
            {
                DialogExpressionCondition.Create("DWSTOREDATETIME", fromDate, toDate)
            };

            if (!string.IsNullOrWhiteSpace(selectedFieldName) &&
                !string.IsNullOrWhiteSpace(selectedFieldValue))
            {
                conditions.Add(DialogExpressionCondition.Create(selectedFieldName, selectedFieldValue));
            }

            var query = new DialogExpression
            {
                Operation = DialogExpressionOperation.And,
                Condition = conditions
            };

            var docs = GetAllDocumentsFromQuery(dialog, query);

            if (!string.IsNullOrWhiteSpace(titleKeyword))
            {
                docs = docs
                    .Where(d => (d.Title ?? string.Empty)
                    .Contains(titleKeyword, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var output = new List<DocumentPickItem>();

            foreach (var d in docs)
            {
                var rowField = BuildRowFieldForDocument(d);

                output.Add(new DocumentPickItem
                {
                    CabinetId = cabinet.Id,
                    CabinetName = cabinet.Name,
                    DocumentId = d.Id,
                    Title = d.Title ?? $"Unnamed Document with id {d.Id}",
                    StoredAt = d.LastModified.ToString("yyyy-MM-dd HH:mm"),
                    RowFieldLabel = rowField.Name,
                    RowFieldValue = rowField.Value
                });
            }

            return output;
        }

        public async Task DownloadSelectedDocumentsAsync(
            string url,
            string username,
            string password,
            List<string> selectedKeys,
            string baseDirectory,
            string? mainFolderName,
            string? baseFolderFieldName,
            List<FolderLevelOption>? folderLevels,
            Dictionary<string, string> customSubfolderNames
        )
        {
            var conn = ServiceConnection.Create(new Uri(url), username, password);

            var parsed = selectedKeys
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(k =>
                {
                    var parts = k.Split('|');
                    return new { CabinetId = parts[0], DocId = int.Parse(parts[1]) };
                })
                .GroupBy(x => x.CabinetId)
                .ToList();

            foreach (var group in parsed)
            {
                var cabinet = conn.GetFileCabinet(group.Key);

                foreach (var item in group)
                {
                    var document = await conn.GetFromDocumentForDocumentAsync(item.DocId, cabinet.Id);
                    document = await document.Content.GetDocumentFromSelfRelationAsync();

                    string mainFolder = string.IsNullOrWhiteSpace(mainFolderName)
                        ? MakeSafeName(cabinet.Name)
                        : MakeSafeName(mainFolderName);

                    var rootFolder = Path.Combine(baseDirectory, mainFolder);

                    if (!string.IsNullOrWhiteSpace(baseFolderFieldName))
                    {
                        var baseFieldValue = await GetFieldValueAsync(document, baseFolderFieldName);
                        if (!string.IsNullOrWhiteSpace(baseFieldValue))
                        {
                            rootFolder = Path.Combine(rootFolder, MakeSafeName(baseFieldValue));
                        }
                    }

                    var nestedSubfolderPath = await GetNestedSubfolderPathAsync(document, folderLevels);

                    string parentFolder = rootFolder;
                    if (!string.IsNullOrWhiteSpace(nestedSubfolderPath))
                    {
                        parentFolder = Path.Combine(rootFolder, nestedSubfolderPath);
                    }

                    Directory.CreateDirectory(parentFolder);

                    string key = $"{cabinet.Id}|{item.DocId}";

                    string finalDocumentFolderName;
                    if (customSubfolderNames.TryGetValue(key, out var typedName) &&
                        !string.IsNullOrWhiteSpace(typedName))
                    {
                        finalDocumentFolderName = MakeSafeName(typedName);
                    }
                    else
                    {
                        finalDocumentFolderName = item.DocId.ToString();
                    }

                    string uniqueDocFolder = GetUniqueDirectoryPath(parentFolder, finalDocumentFolderName);
                    Directory.CreateDirectory(uniqueDocFolder);

                    await DownloadDocumentContentAsync(document, item.DocId, uniqueDocFolder);
                    await UpdateDocumentStatus(conn, cabinet.Id, item.DocId);
                }
            }
        }

        private DocumentPreviewField BuildRowFieldForDocument(Document document)
        {
            try
            {
                var response = document.GetDocumentIndexFieldsFromFieldsRelationAsync()
                    .GetAwaiter()
                    .GetResult();

                var indexFields = response.Content;

                var technicalFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "DWDOCID",
                    "DWSTOREDATETIME",
                    "DWSTOREDATE",
                    "DWDOCSIZE",
                    "DWDOCTYPE",
                    "DWEXTENSION",
                    "DWDISKNO",
                    "DWPAGECOUNT",
                    "DWDOCNAME",
                    "DWSECTIONCOUNT",
                    "DWMODDATETIME",
                    "DWMODDATE",
                    "DWVERSION",
                    "DWFILESIZE",
                    "DWSTATUS",
                    "DWCREATEDATETIME",
                    "DWCREATEDATE",
                    "DWSYSTEMFLAGS",
                    "DWFORMAT"
                };

                var preferred = indexFields.Field.FirstOrDefault(f =>
                    !string.IsNullOrWhiteSpace(f.FieldName) &&
                    f.FieldName.Equals("DOCUMENT_TYPE", StringComparison.OrdinalIgnoreCase));

                if (preferred != null)
                {
                    return new DocumentPreviewField
                    {
                        Name = "DOCUMENT_TYPE",
                        Value = string.IsNullOrWhiteSpace(preferred.Item?.ToString()) ? "-" : preferred.Item!.ToString()!
                    };
                }

                var fallback = indexFields.Field.FirstOrDefault(f =>
                    !string.IsNullOrWhiteSpace(f.FieldName) &&
                    !technicalFields.Contains(f.FieldName));

                if (fallback != null)
                {
                    return new DocumentPreviewField
                    {
                        Name = fallback.FieldName,
                        Value = string.IsNullOrWhiteSpace(fallback.Item?.ToString()) ? "-" : fallback.Item!.ToString()!
                    };
                }
            }
            catch
            {
            }

            return new DocumentPreviewField
            {
                Name = "Type",
                Value = "-"
            };
        }

        private async Task<string?> GetFieldValueAsync(Document document, string? fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                return null;

            try
            {
                var response = await document.GetDocumentIndexFieldsFromFieldsRelationAsync();
                var indexFields = response.Content;

                var match = indexFields.Field.FirstOrDefault(f =>
                    !string.IsNullOrWhiteSpace(f.FieldName) &&
                    f.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

                var value = match?.Item?.ToString();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> GetNestedSubfolderPathAsync(Document document, List<FolderLevelOption>? folderLevels)
        {
            if (folderLevels == null || !folderLevels.Any())
                return string.Empty;

            try
            {
                var response = await document.GetDocumentIndexFieldsFromFieldsRelationAsync();
                var indexFields = response.Content;

                var folderParts = new List<string>();

                foreach (var level in folderLevels)
                {
                    if (level == null)
                        continue;

                    string? part = null;

                    if (!string.IsNullOrWhiteSpace(level.FieldName))
                    {
                        var match = indexFields.Field.FirstOrDefault(f =>
                            !string.IsNullOrWhiteSpace(f.FieldName) &&
                            f.FieldName.Equals(level.FieldName, StringComparison.OrdinalIgnoreCase));

                        var fieldValue = match?.Item?.ToString();
                        if (!string.IsNullOrWhiteSpace(fieldValue))
                        {
                            part = fieldValue.Trim();
                        }
                    }

                    if (string.IsNullOrWhiteSpace(part) && !string.IsNullOrWhiteSpace(level.CustomText))
                    {
                        part = level.CustomText.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(part))
                    {
                        folderParts.Add(MakeSafeName(part));
                    }
                }

                if (!folderParts.Any())
                    return string.Empty;

                return Path.Combine(folderParts.ToArray());
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task DownloadDocumentContentAsync(
            Document document,
            int docID,
            string outputFolder)
        {
            bool containsXmlFile = false;
            int sectionCounter = 1;

            foreach (var section in document.Sections)
            {
                var fullSection = await section.GetSectionFromSelfRelationAsync();

                var download = await fullSection.Content
                    .PostToFileDownloadRelationForStreamAsync(
                        new FileDownload { TargetFileType = FileDownloadType.Auto });

                string fileName =
                    download.ContentHeaders.ContentDisposition?.FileName
                    ?? $"Doc_{docID}_Section_{sectionCounter}.dat";

                fileName = CleanDownloadedFileName(fileName);

                string finalName = $"{docID}_{fileName}";
                string filePath = GetUniqueFilePath(outputFolder, finalName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await download.Content.CopyToAsync(stream);
                }

                if (Path.GetExtension(fileName).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                    containsXmlFile = true;

                sectionCounter++;
            }

            if (!containsXmlFile)
                await GenerateXmlMetadataAsync(document, docID, outputFolder);
        }

        private async Task GenerateXmlMetadataAsync(
            Document document,
            int docID,
            string outputFolder)
        {
            var response = await document.GetDocumentIndexFieldsFromFieldsRelationAsync();
            var indexFields = response.Content;

            var elements = new List<XElement>();

            foreach (var field in indexFields.Field)
            {
                var rawName = field.FieldName;
                if (string.IsNullOrWhiteSpace(rawName))
                    continue;

                string cleanName = MakeSafeXmlElementName(rawName);
                string value = field.Item?.ToString() ?? string.Empty;

                elements.Add(new XElement(cleanName, value));
            }

            var xml = new XElement("Document",
                new XAttribute("Id", docID),
                elements
            );

            var xDoc = new XDocument(xml);

            string xmlPath = GetUniqueFilePath(outputFolder, $"{docID}_metadata.xml");
            xDoc.Save(xmlPath);
        }

        private string MakeSafeXmlElementName(string rawName)
        {
            var cleaned = rawName.Trim();

            cleaned = cleaned.Replace(" ", "_");
            cleaned = Regex.Replace(cleaned, @"[^A-Za-z0-9_\-]", "_");

            if (string.IsNullOrWhiteSpace(cleaned))
                cleaned = "Field";

            if (!char.IsLetter(cleaned[0]) && cleaned[0] != '_')
                cleaned = $"Field_{cleaned}";

            return cleaned;
        }

        private async Task UpdateDocumentStatus(
            ServiceConnection connection,
            string fileCabinetId,
            int documentId)
        {
            var fileCabinet = connection.GetFileCabinet(fileCabinetId);

            var query = new DialogExpression
            {
                Operation = DialogExpressionOperation.And,
                Condition = new List<DialogExpressionCondition>
                {
                    DialogExpressionCondition.Create("DWDOCID", documentId.ToString())
                }
            };

            var dialog = fileCabinet.GetDialogFromCustomSearchRelation();
            var result = dialog.GetDocumentsResult(query);

            if (result?.Items == null || result.Items.Count == 0)
                return;

            var document = result.Items[0];

            var fields = new DocumentIndexFields
            {
                Field = new List<DocumentIndexField>
                {
                    DocumentIndexField.Create("STATUS", "Downloaded")
                }
            };

            await document.PutToFieldsRelationForDocumentIndexFieldsAsync(fields);
        }

        private string CleanDownloadedFileName(string fileName)
        {
            fileName = fileName.Trim().Trim('"');

            if (fileName.Contains("\\") || fileName.Contains("/"))
            {
                fileName = Path.GetFileName(fileName);
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "document.dat";
            }

            return fileName;
        }

        private string GetUniqueFilePath(string folder, string fileName)
        {
            fileName = MakeSafeName(fileName);

            string fullPath = Path.Combine(folder, fileName);

            if (!System.IO.File.Exists(fullPath))
                return fullPath;

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            int counter = 1;

            while (true)
            {
                string candidate = Path.Combine(folder, $"{fileNameWithoutExtension}_{counter}{extension}");
                if (!System.IO.File.Exists(candidate))
                    return candidate;

                counter++;
            }
        }

        private string GetUniqueDirectoryPath(string parentFolder, string folderName)
        {
            folderName = MakeSafeName(folderName);

            string fullPath = Path.Combine(parentFolder, folderName);

            if (!Directory.Exists(fullPath))
                return fullPath;

            int counter = 1;
            while (true)
            {
                string candidate = Path.Combine(parentFolder, $"{folderName}_{counter}");
                if (!Directory.Exists(candidate))
                    return candidate;

                counter++;
            }
        }

        private string MakeSafeName(string value)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            return value.Trim();
        }
    }
}