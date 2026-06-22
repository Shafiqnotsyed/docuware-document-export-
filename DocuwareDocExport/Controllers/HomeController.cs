using DocuwareDocExport.Models;
using DocuwareDocExport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DocuwareDocExport.Controllers
{
    public class HomeController : Controller
    {
        private readonly DocuWareService _docuWareService;

        public HomeController(DocuWareService docuWareService)
        {
            _docuWareService = docuWareService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var model = new ConfigurationViewModel();
            EnsureFolderLevels(model);
            return View(model);
        }

        [HttpPost]
        public IActionResult LoadCabinets(ConfigurationViewModel model)
        {
            EnsureFolderLevels(model);

            if (string.IsNullOrWhiteSpace(model.Url) ||
                string.IsNullOrWhiteSpace(model.Username) ||
                string.IsNullOrWhiteSpace(model.Password))
            {
                ViewBag.Error = "Please enter URL, Username, and Password first.";
                return View("Index", model);
            }

            try
            {
                var cabinets = _docuWareService.GetAccessibleFileCabinets(
                    model.Url, model.Username, model.Password);

                model.FileCabinetOptions = cabinets
                    .Select(c => new SelectListItem
                    {
                        Value = c.Id,
                        Text = c.Name
                    })
                    .ToList();

                model.ScrollToStep = "step2";
                ViewBag.LoginSuccess = "Login successful. File cabinets loaded.";
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Failed to load file cabinets: {ex.Message}";
            }

            return View("Index", model);
        }

        [HttpPost]
        public IActionResult LoadFields(ConfigurationViewModel model)
        {
            EnsureFolderLevels(model);

            if (!BasicInputsOk(model))
            {
                ViewBag.Error = "Please enter URL, Username, Password, From Date, and To Date first.";
                ReloadCabinetOptions(model);
                return View("Index", model);
            }

            if (model.SelectedFileCabinetIds == null || !model.SelectedFileCabinetIds.Any())
            {
                ViewBag.Error = "Please select at least one file cabinet first.";
                ReloadCabinetOptions(model);
                return View("Index", model);
            }

            try
            {
                LoadIndexFields(model);

                if (!model.IndexFieldOptions.Any())
                {
                    ViewBag.Error = "No index fields were found for the selected cabinet(s).";
                }
                else
                {
                    model.FieldValueOptions = new List<string>();
                    model.SelectedIndexFieldValue = string.Empty;
                    model.ScrollToStep = "step3";
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Failed to load index fields: {ex.Message}";
            }

            ReloadCabinetOptions(model);
            return View("Index", model);
        }

        [HttpPost]
        public IActionResult LoadFieldValues(ConfigurationViewModel model)
        {
            EnsureFolderLevels(model);

            if (!BasicInputsOk(model))
            {
                ViewBag.Error = "Please complete URL, Username, Password, From Date, and To Date.";
                ReloadCabinetOptions(model);
                ReloadFieldOptions(model);
                return View("Index", model);
            }

            if (model.SelectedFileCabinetIds == null || !model.SelectedFileCabinetIds.Any())
            {
                ViewBag.Error = "Please select at least one file cabinet.";
                ReloadCabinetOptions(model);
                ReloadFieldOptions(model);
                return View("Index", model);
            }

            ReloadCabinetOptions(model);
            ReloadFieldOptions(model);

            if (string.IsNullOrWhiteSpace(model.SelectedIndexFieldName))
            {
                model.FieldValueOptions = new List<string>();
                ViewBag.Error = "Please choose an index field first.";
                model.ScrollToStep = "step3";
                return View("Index", model);
            }

            try
            {
                LoadFieldValuesForSelectedField(model);
                model.ScrollToStep = "step3";
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Failed to load field values: {ex.Message}";
            }

            return View("Index", model);
        }

        [HttpPost]
        public IActionResult LoadDocuments(ConfigurationViewModel model)
        {
            EnsureFolderLevels(model);

            if (!BasicInputsOk(model))
            {
                ViewBag.Error = "Please complete URL, Username, Password, From Date, and To Date.";
                ReloadCabinetOptions(model);
                ReloadFieldOptions(model);
                ReloadSelectedFieldValues(model);
                return View("Index", model);
            }

            if (model.SelectedFileCabinetIds == null || !model.SelectedFileCabinetIds.Any())
            {
                ViewBag.Error = "Please select at least one file cabinet.";
                ReloadCabinetOptions(model);
                ReloadFieldOptions(model);
                ReloadSelectedFieldValues(model);
                return View("Index", model);
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(model.SelectedIndexFieldName))
                {
                    ReloadSelectedFieldValues(model);
                }
                else
                {
                    model.FieldValueOptions = new List<string>();
                    model.SelectedIndexFieldValue = string.Empty;
                }

                LoadAllDocuments(model);

                if (!model.DocumentOptions.Any())
                {
                    ViewBag.Error = "No documents matched your search.";
                    model.ScrollToStep = "step3";
                }
                else
                {
                    ViewBag.DocumentsLoaded = "Documents loaded successfully.";
                    model.ScrollToStep = "step5";
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Failed to load documents: {ex.Message}";
            }

            ReloadCabinetOptions(model);
            ReloadFieldOptions(model);
            ReloadSelectedFieldValues(model);
            return View("Index", model);
        }

        [HttpPost]
        public IActionResult ChangePage(ConfigurationViewModel model, int targetPage)
        {
            EnsureFolderLevels(model);

            if (targetPage < 1)
                targetPage = 1;

            model.PageNumber = targetPage;

            ReloadCabinetOptions(model);
            ReloadFieldOptions(model);
            ReloadSelectedFieldValues(model);
            LoadAllDocuments(model);

            model.ScrollToStep = "step5";
            return View("Index", model);
        }

        [HttpPost]
        public async Task<IActionResult> DownloadSelected(ConfigurationViewModel model)
        {
            EnsureFolderLevels(model);

            var resolvedBaseDirectory = ResolveBaseDirectory(model);

            if (!DownloadInputsOk(model, resolvedBaseDirectory))
            {
                ViewBag.DownloadError = "Please choose a preset folder or enter a custom base directory, and select at least one document.";
                ReloadCabinetOptions(model);
                ReloadFieldOptions(model);
                ReloadSelectedFieldValues(model);
                LoadAllDocuments(model);
                model.ScrollToStep = "step4";
                return View("Index", model);
            }

            try
            {
                model.BaseDirectory = resolvedBaseDirectory!;
                var customNames = BuildCustomNameDictionary(model);

                await _docuWareService.DownloadSelectedDocumentsAsync(
                    model.Url,
                    model.Username,
                    model.Password,
                    model.SelectedDocumentKeys,
                    model.BaseDirectory,
                    model.MainFolderName,
                    model.SelectedBaseFolderFieldName,
                    model.FolderLevels,
                    customNames
                );

                ViewBag.DownloadSuccess = $"Selected documents downloaded successfully to: {model.BaseDirectory}";
            }
            catch (Exception ex)
            {
                ViewBag.DownloadError = ex.Message;
            }

            ReloadCabinetOptions(model);
            ReloadFieldOptions(model);
            ReloadSelectedFieldValues(model);
            LoadAllDocuments(model);
            model.ScrollToStep = "step5";

            return View("Index", model);
        }

        [HttpPost]
        public async Task<IActionResult> DownloadAll(ConfigurationViewModel model)
        {
            EnsureFolderLevels(model);

            var resolvedBaseDirectory = ResolveBaseDirectory(model);

            if (!BasicInputsOk(model) || string.IsNullOrWhiteSpace(resolvedBaseDirectory))
            {
                ViewBag.DownloadError = "Please complete URL, Username, Password, From Date, To Date, and choose a preset folder or custom base directory.";
                ReloadCabinetOptions(model);
                ReloadFieldOptions(model);
                ReloadSelectedFieldValues(model);
                LoadAllDocuments(model);
                model.ScrollToStep = "step4";
                return View("Index", model);
            }

            if (model.SelectedFileCabinetIds == null || !model.SelectedFileCabinetIds.Any())
            {
                ViewBag.DownloadError = "Please select at least one file cabinet.";
                ReloadCabinetOptions(model);
                ReloadFieldOptions(model);
                ReloadSelectedFieldValues(model);
                LoadAllDocuments(model);
                model.ScrollToStep = "step5";
                return View("Index", model);
            }

            try
            {
                model.BaseDirectory = resolvedBaseDirectory;
                var allKeys = new List<string>();

                foreach (var cabinetId in model.SelectedFileCabinetIds)
                {
                    var keys = _docuWareService.GetDocumentKeysForCabinet(
                        model.Url,
                        model.Username,
                        model.Password,
                        cabinetId,
                        model.FromDate,
                        model.ToDate,
                        model.SelectedIndexFieldName,
                        model.SelectedIndexFieldValue,
                        model.TitleKeyword
                    );

                    allKeys.AddRange(keys);
                }

                allKeys = allKeys
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!allKeys.Any())
                {
                    ViewBag.DownloadError = "No documents matched your current filters.";
                }
                else
                {
                    var customNames = BuildCustomNameDictionary(model);

                    await _docuWareService.DownloadSelectedDocumentsAsync(
                        model.Url,
                        model.Username,
                        model.Password,
                        allKeys,
                        model.BaseDirectory,
                        model.MainFolderName,
                        model.SelectedBaseFolderFieldName,
                        model.FolderLevels,
                        customNames
                    );

                    ViewBag.DownloadSuccess = $"All matched documents ({allKeys.Count}) downloaded successfully to: {model.BaseDirectory}";
                }
            }
            catch (Exception ex)
            {
                ViewBag.DownloadError = ex.Message;
            }

            ReloadCabinetOptions(model);
            ReloadFieldOptions(model);
            ReloadSelectedFieldValues(model);
            LoadAllDocuments(model);
            model.ScrollToStep = "step5";

            return View("Index", model);
        }

        private void EnsureFolderLevels(ConfigurationViewModel model)
        {
            if (model.FolderLevels == null)
            {
                model.FolderLevels = new List<FolderLevelOption>();
            }

            if (!model.FolderLevels.Any())
            {
                model.FolderLevels.Add(new FolderLevelOption());
            }
        }

        private Dictionary<string, string> BuildCustomNameDictionary(ConfigurationViewModel model)
        {
            var customNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (model.DocumentOptions != null)
            {
                foreach (var doc in model.DocumentOptions)
                {
                    var key = $"{doc.CabinetId}|{doc.DocumentId}";
                    if (!string.IsNullOrWhiteSpace(doc.CustomSubfolderName))
                    {
                        customNames[key] = doc.CustomSubfolderName.Trim();
                    }
                }
            }

            return customNames;
        }

        private bool BasicInputsOk(ConfigurationViewModel model)
        {
            return !string.IsNullOrWhiteSpace(model.Url)
                && !string.IsNullOrWhiteSpace(model.Username)
                && !string.IsNullOrWhiteSpace(model.Password)
                && !string.IsNullOrWhiteSpace(model.FromDate)
                && !string.IsNullOrWhiteSpace(model.ToDate);
        }

        private bool DownloadInputsOk(ConfigurationViewModel model, string? resolvedBaseDirectory)
        {
            return BasicInputsOk(model)
                && !string.IsNullOrWhiteSpace(resolvedBaseDirectory)
                && model.SelectedDocumentKeys != null
                && model.SelectedDocumentKeys.Any();
        }

        private string? ResolveBaseDirectory(ConfigurationViewModel model)
        {
            if (!string.IsNullOrWhiteSpace(model.CustomBaseDirectory))
            {
                return model.CustomBaseDirectory.Trim();
            }

            if (string.IsNullOrWhiteSpace(model.BaseDirectoryPreset))
            {
                return null;
            }

            return model.BaseDirectoryPreset switch
            {
                "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Downloads" => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads"),
                _ => null
            };
        }

        private void LoadIndexFields(ConfigurationViewModel model)
        {
            var allFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var cabinetId in model.SelectedFileCabinetIds)
            {
                var fields = _docuWareService.GetIndexFieldNames(
                    model.Url,
                    model.Username,
                    model.Password,
                    cabinetId);

                foreach (var field in fields)
                {
                    allFields.Add(field);
                }
            }

            model.IndexFieldOptions = allFields
                .OrderBy(f => f)
                .Select(f => new SelectListItem
                {
                    Value = f,
                    Text = f,
                    Selected = model.SelectedIndexFieldName == f
                })
                .ToList();
        }

        private void LoadFieldValuesForSelectedField(ConfigurationViewModel model)
        {
            model.FieldValueOptions = new List<string>();

            if (string.IsNullOrWhiteSpace(model.SelectedIndexFieldName))
                return;

            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var cabinetId in model.SelectedFileCabinetIds)
            {
                var cabinetValues = _docuWareService.GetFieldValuesForCabinet(
                    model.Url,
                    model.Username,
                    model.Password,
                    cabinetId,
                    model.SelectedIndexFieldName,
                    model.FromDate,
                    model.ToDate
                );

                foreach (var value in cabinetValues)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Add(value.Trim());
                }
            }

            model.FieldValueOptions = values
                .OrderBy(v => v)
                .ToList();
        }

        private void LoadAllDocuments(ConfigurationViewModel model)
        {
            var existingCustomNames = model.DocumentOptions?
                .ToDictionary(
                    d => $"{d.CabinetId}|{d.DocumentId}",
                    d => d.CustomSubfolderName ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var allDocuments = new List<DocumentPickItem>();

            foreach (var cabinetId in model.SelectedFileCabinetIds)
            {
                var docs = _docuWareService.GetDocumentsForCabinet(
                    model.Url,
                    model.Username,
                    model.Password,
                    cabinetId,
                    model.FromDate,
                    model.ToDate,
                    model.SelectedIndexFieldName,
                    model.SelectedIndexFieldValue,
                    model.TitleKeyword
                );

                foreach (var doc in docs)
                {
                    var key = $"{doc.CabinetId}|{doc.DocumentId}";
                    if (existingCustomNames.TryGetValue(key, out var customName))
                    {
                        doc.CustomSubfolderName = customName;
                    }
                }

                allDocuments.AddRange(docs);
            }

            model.TotalDocumentCount = allDocuments.Count;

            if (model.PageSize <= 0)
                model.PageSize = 25;

            if (model.PageNumber <= 0)
                model.PageNumber = 1;

            var skip = (model.PageNumber - 1) * model.PageSize;

            model.DocumentOptions = allDocuments
                .Skip(skip)
                .Take(model.PageSize)
                .ToList();
        }

        private void ReloadCabinetOptions(ConfigurationViewModel model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.Url) ||
                    string.IsNullOrWhiteSpace(model.Username) ||
                    string.IsNullOrWhiteSpace(model.Password))
                    return;

                var cabinets = _docuWareService.GetAccessibleFileCabinets(
                    model.Url,
                    model.Username,
                    model.Password);

                model.FileCabinetOptions = cabinets
                    .Select(c => new SelectListItem
                    {
                        Value = c.Id,
                        Text = c.Name,
                        Selected = model.SelectedFileCabinetIds.Contains(c.Id)
                    })
                    .ToList();
            }
            catch
            {
            }
        }

        private void ReloadFieldOptions(ConfigurationViewModel model)
        {
            try
            {
                if (model.SelectedFileCabinetIds == null || !model.SelectedFileCabinetIds.Any())
                    return;

                LoadIndexFields(model);
            }
            catch
            {
            }
        }

        private void ReloadSelectedFieldValues(ConfigurationViewModel model)
        {
            try
            {
                if (model.SelectedFileCabinetIds == null || !model.SelectedFileCabinetIds.Any())
                    return;

                if (string.IsNullOrWhiteSpace(model.SelectedIndexFieldName))
                {
                    model.FieldValueOptions = new List<string>();
                    return;
                }

                LoadFieldValuesForSelectedField(model);
            }
            catch
            {
            }
        }
    }
}