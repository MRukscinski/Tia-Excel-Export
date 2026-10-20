using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClosedXML.Excel;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;

namespace Primexcel
{
    public static class ExcelExporterService
    {
        private const int MaxLevelColumns = 8; // Liczba kolumn na poziomy zagnieżdżenia (A-H)

        public static void ExportSelectedDbsToExcel(Project project, string excelFilePath, List<(string PlcName, string DbPath)> targets)
        {
            if (targets == null || targets.Count == 0) return;

            // 1. Kopia zapasowa TYLKO wtedy, gdy plik już wcześniej istniał na dysku
            if (File.Exists(excelFilePath))
            {
                try
                {
                    string directory = Path.GetDirectoryName(excelFilePath);
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(excelFilePath);
                    string extension = Path.GetExtension(excelFilePath);
                    string timestamp = DateTime.Now.ToString("dd.MM.yyyy HH-mm");
                    string backupPath = Path.Combine(directory ?? "", $"{fileNameWithoutExt}_{timestamp}{extension}");

                    File.Copy(excelFilePath, backupPath, true);
                    Console.WriteLine($"[BACKUP] Created backup file: {backupPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARNING] Failed to create backup: {ex.Message}");
                }
            }

            // 2. Jeśli plik istnieje – wczytujemy go; jeśli nie istnieje – tworzymy pusty obiekt XLWorkbook
            XLWorkbook workbook = File.Exists(excelFilePath) ? new XLWorkbook(excelFilePath) : new XLWorkbook();

            using (workbook)
            {
                // =========================================================================
                // CZYSZCZENIE Z ZACHOWANIEM LEGENDY
                // =========================================================================
                var tempResetSheet = workbook.Worksheets.Add("Temp_Reset_Sheet");

                // Upewniamy się, że arkusz Legendy istnieje
                EnsureLegendSheetExists(workbook);

                // Usuwamy stare arkusze DB (zostawiamy Temp_Reset_Sheet oraz Legendę)
                var existingSheets = workbook.Worksheets
                    .Where(ws => ws.Name != "Temp_Reset_Sheet" &&
                                 !string.Equals(ws.Name, "Legend", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var sheet in existingSheets)
                {
                    sheet.Delete();
                }
                // =========================================================================

                // Buforowanie sterowników PLC z TIA Portal w pamięci RAM
                var plcMap = new Dictionary<string, PlcSoftware>(StringComparer.OrdinalIgnoreCase);
                foreach (Device device in project.Devices)
                {
                    foreach (PlcSoftware plc in GetPlcSoftwareFromDevice(device))
                    {
                        if (!plcMap.ContainsKey(plc.Name))
                        {
                            plcMap.Add(plc.Name, plc);
                        }
                    }
                }

                bool processedAny = false;

                // 3. Generating new worksheets only for selected DB blocks
                foreach (var target in targets)
                {
                    if (!plcMap.TryGetValue(target.PlcName, out PlcSoftware plcSoftware))
                    {
                        Console.WriteLine($"[WARNING] Controller '{target.PlcName}' not found. Skipping.");
                        continue;
                    }

                    PlcBlock dbBlock = FindBlockByPath(plcSoftware, target.DbPath);
                    if (dbBlock == null)
                    {
                        Console.WriteLine($"[WARNING] Block not found under path '{target.DbPath}'. Skipping.");
                        continue;
                    }

                    if (ProcessSingleDbToSheet(dbBlock, plcSoftware, workbook, target.PlcName))
                    {
                        processedAny = true;
                    }
                }

                // 4. Usunięcie arkusza tymczasowego i zapis skompilowanego pliku
                if (processedAny && workbook.Worksheets.Count > 1)
                {
                    tempResetSheet.Delete();

                    if (workbook.Worksheets.Contains("Legend"))
                    {
                        workbook.Worksheet("Legend").Position = 1;
                    }

                    // Zapis do wybranej ścieżki
                    workbook.SaveAs(excelFilePath);
                    Console.WriteLine($"\nSUCCESS: Saved Excel file at: {excelFilePath}");
                }
                else
                {
                    tempResetSheet.Delete();
                }
            }
        }

        /// <summary>
        /// Pomocnicza metoda przetwarzająca pojedynczy blok DB na arkusz w obiekcie workbook.
        /// </summary>
        private static bool ProcessSingleDbToSheet(PlcBlock dbBlock, PlcSoftware plcSoftware, XLWorkbook workbook, string plcName)
        {
            string dbName = dbBlock.Name;
            Console.WriteLine($" -> Processing block '{dbName}' ({plcName})...");

            // Consistency check / compilation
            if (!dbBlock.IsConsistent)
            {
                Console.WriteLine($" -> Block '{dbName}' is inconsistent. Attempting PLC compilation...");
                ICompilable compilable = plcSoftware.GetService<ICompilable>();
                if (compilable != null)
                {
                    var result = compilable.Compile();
                    if (result.ErrorCount > 0)
                    {
                        Console.WriteLine($" -> [ERROR] Compilation failed ({result.ErrorCount} errors). Skipping.");
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            string tempXmlPath = Path.Combine(Path.GetTempPath(), $"{plcName}_{dbName}.xml");
            try
            {
                // DB export to XML
                dbBlock.Export(new FileInfo(tempXmlPath), ExportOptions.WithDefaults);

                // XML parsing
                List<DbParameterRow> parameters = ParseDbXml(tempXmlPath);

                string rawSheetName = $"{plcName}_{dbName}";
                string safeSheetName = SanitizeSheetName(rawSheetName);

                // Replace worksheet if it already exists
                if (workbook.Worksheets.Contains(safeSheetName))
                {
                    workbook.Worksheets.Worksheet(safeSheetName).Delete();
                }

                IXLWorksheet worksheet = workbook.Worksheets.Add(safeSheetName);

                // =========================================================================
                // 1. TREE LEVEL HEADERS (Columns A - H)
                // =========================================================================
                for (int level = 1; level <= MaxLevelColumns; level++)
                {
                    int levelFromRight = MaxLevelColumns - level + 1;
                    worksheet.Cell(1, level).Value = levelFromRight == 1
                        ? "Variable Name"
                        : $"Level -{levelFromRight}";
                }

                // =========================================================================
                // 2. PROPERTY HEADERS (Columns I, J, K, L)
                // =========================================================================
                worksheet.Cell(1, MaxLevelColumns + 1).Value = "Data Type";
                worksheet.Cell(1, MaxLevelColumns + 2).Value = "Value from Controller";
                worksheet.Cell(1, MaxLevelColumns + 3).Value = "Value to Input"; // <- New editable column
                worksheet.Cell(1, MaxLevelColumns + 4).Value = "Comment";

                // Header formatting (total width: MaxLevelColumns + 4)
                int totalColumns = MaxLevelColumns + 4;
                var headerRange = worksheet.Range(1, 1, 1, totalColumns);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Highlight "Value to Input" column with light blue color for user clarity
                worksheet.Cell(1, MaxLevelColumns + 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");

                // =========================================================================
                // 3. POPULATING DATA
                // =========================================================================
                int currentRow = 2;
                foreach (var param in parameters)
                {
                    var path = param.Path;

                    if (path.Count > MaxLevelColumns)
                    {
                        path = path.Skip(path.Count - MaxLevelColumns).ToList();
                    }

                    int startColumn = MaxLevelColumns - path.Count + 1;

                    // Write tree structure path
                    for (int i = 0; i < path.Count; i++)
                    {
                        worksheet.Cell(currentRow, startColumn + i).Value = path[i];
                    }

                    // Data Type
                    worksheet.Cell(currentRow, MaxLevelColumns + 1).Value = param.DataType;

                    // Value from Controller (current DB value)
                    worksheet.Cell(currentRow, MaxLevelColumns + 2).Value = param.StartValue;

                    // Value to Input (left empty for user input)
                    worksheet.Cell(currentRow, MaxLevelColumns + 3).Value = string.Empty;

                    // Comment
                    worksheet.Cell(currentRow, MaxLevelColumns + 4).Value = param.Comment;

                    currentRow++;
                }

                worksheet.Columns().AdjustToContents();

                if (currentRow > 2)
                {
                    worksheet.RangeUsed().SetAutoFilter();
                }

                Console.WriteLine($" -> Successfully added/updated worksheet '{safeSheetName}'.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" -> [ERROR] While processing DB in {plcName}: {ex.Message}");
                return false;
            }
            finally
            {
                if (File.Exists(tempXmlPath))
                {
                    File.Delete(tempXmlPath);
                }
            }
        }

        #region Metody Pomocnicze

        private static List<PlcSoftware> GetPlcSoftwareFromDevice(Device device)
        {
            List<PlcSoftware> list = new List<PlcSoftware>();
            foreach (DeviceItem item in device.DeviceItems)
            {
                SoftwareContainer container = item.GetService<SoftwareContainer>();
                if (container != null && container.Software is PlcSoftware plcSoftware)
                {
                    list.Add(plcSoftware);
                }
            }
            return list;
        }

        private static PlcBlock FindBlockByPath(PlcSoftware plcSoftware, string dbPath)
        {
            string[] parts = dbPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;

            string blockName = parts[parts.Length - 1];
            PlcBlockGroup currentGroup = plcSoftware.BlockGroup;

            int startIndex = 0;
            if (parts.Length > 0 && string.Equals(parts[0], "Program blocks", StringComparison.OrdinalIgnoreCase))
            {
                startIndex = 1;
            }

            for (int i = startIndex; i < parts.Length - 1; i++)
            {
                string folderName = parts[i];
                PlcBlockUserGroup subGroup = null;

                foreach (PlcBlockUserGroup group in currentGroup.Groups)
                {
                    if (string.Equals(group.Name, folderName, StringComparison.OrdinalIgnoreCase))
                    {
                        subGroup = group;
                        break;
                    }
                }

                if (subGroup == null) return null;
                currentGroup = subGroup;
            }

            foreach (PlcBlock block in currentGroup.Blocks)
            {
                if (string.Equals(block.Name, blockName, StringComparison.OrdinalIgnoreCase))
                {
                    return block;
                }
            }

            return null;
        }

        private static List<DbParameterRow> ParseDbXml(string xmlFilePath)
        {
            List<DbParameterRow> result = new List<DbParameterRow>();
            XDocument doc = XDocument.Load(xmlFilePath);

            var interfaceElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Interface");
            if (interfaceElem == null) return result;

            var topSections = interfaceElem
                .Elements().Where(e => e.Name.LocalName == "Sections")
                .Elements().Where(e => e.Name.LocalName == "Section");

            foreach (var section in topSections)
            {
                var topMembers = section.Elements().Where(e => e.Name.LocalName == "Member");
                foreach (var topMember in topMembers)
                {
                    ParseMemberElement(topMember, new List<string>(), result);
                }
            }

            return result;
        }

        private static void ParseMemberElement(XElement memberElem, List<string> currentPath, List<DbParameterRow> resultList)
        {
            string name = memberElem.Attribute("Name")?.Value ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return;

            List<string> newPath = new List<string>(currentPath) { name };
            string rawDataType = memberElem.Attribute("Datatype")?.Value ?? string.Empty;

            XElement startValElem = memberElem.Elements().FirstOrDefault(e => e.Name.LocalName == "StartValue");
            string startVal = startValElem?.Value ?? string.Empty;

            string comment = string.Empty;
            XElement commentElem = memberElem.Elements().FirstOrDefault(e => e.Name.LocalName == "Comment");
            if (commentElem != null)
            {
                XElement textElem = commentElem.Descendants().FirstOrDefault(e => e.Name.LocalName == "MultiLanguageText");
                comment = textElem?.Value ?? string.Empty;
            }

            resultList.Add(new DbParameterRow
            {
                Path = newPath,
                DataType = rawDataType.Trim('"'),
                StartValue = startVal,
                Comment = comment
            });

            List<XElement> childMembers = new List<XElement>();
            childMembers.AddRange(memberElem.Elements().Where(e => e.Name.LocalName == "Member"));

            var nestedSections = memberElem.Elements().Where(e => e.Name.LocalName == "Sections")
                                                   .Elements().Where(e => e.Name.LocalName == "Section");

            foreach (var sec in nestedSections)
            {
                childMembers.AddRange(sec.Elements().Where(e => e.Name.LocalName == "Member"));
            }

            foreach (var child in childMembers)
            {
                ParseMemberElement(child, newPath, resultList);
            }
        }

        private static string SanitizeSheetName(string rawName)
        {
            string clean = Regex.Replace(rawName, @"[\\/*?:\[\]]", "_");
            if (clean.Length > 31)
            {
                clean = clean.Substring(0, 31);
            }
            return clean;
        }

        #endregion
        #region Metody Pomocnicze - Legenda

        /// <summary>
        /// Tworzy i formatuje arkusz Legendy, jeśli nie istnieje w pliku Excel.
        /// </summary>
        private static void EnsureLegendSheetExists(XLWorkbook workbook)
        {
            string legendSheetName = "Legend";

            if (workbook.Worksheets.Contains(legendSheetName))
            {
                return;
            }

            IXLWorksheet legendWs = workbook.Worksheets.Add(legendSheetName);

            // Title
            legendWs.Cell(1, 1).Value = "LEGEND AND COLUMN DESCRIPTION";
            legendWs.Cell(1, 1).Style.Font.Bold = true;
            legendWs.Cell(1, 1).Style.Font.FontSize = 14;

            // Headers
            legendWs.Cell(3, 1).Value = "Column Name";
            legendWs.Cell(3, 2).Value = "Description / Purpose";

            var headerRange = legendWs.Range(3, 1, 3, 2);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

            // Descriptions
            int row = 4;
            AddLegendRow(legendWs, ref row, "Level -X / Variable Name", "Tree structure path of the member in the Data Block.");
            AddLegendRow(legendWs, ref row, "Data Type", "PLC data type (e.g., BOOL, INT, REAL, STRING).");
            AddLegendRow(legendWs, ref row, "Value from Controller", "Current value read from TIA Portal Data Block.");
            AddLegendRow(legendWs, ref row, "Value to Input", "Editable field (highlighted in light blue) for entering new values to import.");
            AddLegendRow(legendWs, ref row, "Comment", "Tag comment defined in TIA Portal.");

            legendWs.Columns().AdjustToContents();
        }

        private static void AddLegendRow(IXLWorksheet ws, ref int row, string colName, string description)
        {
            ws.Cell(row, 1).Value = colName;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = description;
            row++;
        }

        #endregion
    }


    public class DbParameterRow
    {
        public List<string> Path { get; set; } = new List<string>();
        public string DataType { get; set; }
        public string StartValue { get; set; }
        public string Comment { get; set; }
    }

}