using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace DocuwareDocExport.Models
{
    public class DocumentPreviewField
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class DocumentPickItem
    {
        public string CabinetId { get; set; } = string.Empty;
        public string CabinetName { get; set; } = string.Empty;
        public int DocumentId { get; set; }

        public string Title { get; set; } = string.Empty;
        public string StoredAt { get; set; } = string.Empty;

        public List<DocumentPreviewField> PreviewFields { get; set; } = new();

        public string? CustomSubfolderName { get; set; }

        public string RowFieldLabel { get; set; } = string.Empty;
        public string RowFieldValue { get; set; } = string.Empty;

        public string Key => $"{CabinetId}|{DocumentId}";
    }

    public class FolderLevelOption
    {
        public string? FieldName { get; set; }
        public string? CustomText { get; set; }
    }

    public class ConfigurationViewModel
    {
        [Required]
        public string Url { get; set; } = string.Empty;

        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        public List<string> SelectedFileCabinetIds { get; set; } = new();
        public List<SelectListItem> FileCabinetOptions { get; set; } = new();

        [Required]
        public string FromDate { get; set; } = string.Empty;

        [Required]
        public string ToDate { get; set; } = string.Empty;

        public string BaseDirectory { get; set; } = string.Empty;

        public string? BaseDirectoryPreset { get; set; }
        public string? CustomBaseDirectory { get; set; }

        public string? SelectedBaseFolderFieldName { get; set; }

        public List<SelectListItem> IndexFieldOptions { get; set; } = new();

        public string? SelectedIndexFieldName { get; set; }
        public string? SelectedIndexFieldValue { get; set; }

        public List<string> FieldValueOptions { get; set; } = new();

        public string? TitleKeyword { get; set; }

        public string? MainFolderName { get; set; }

        public List<FolderLevelOption> FolderLevels { get; set; } = new();

        public List<DocumentPickItem> DocumentOptions { get; set; } = new();
        public List<string> SelectedDocumentKeys { get; set; } = new();

        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalDocumentCount { get; set; } = 0;

        public int TotalPages =>
            PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalDocumentCount / PageSize);

        public string ScrollToStep { get; set; } = string.Empty;
    }
}