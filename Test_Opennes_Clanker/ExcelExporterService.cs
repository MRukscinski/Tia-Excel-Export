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

namespace Test_Opennes_Clanker
{
    public static class ExcelExporterService
    {
        private const int MaxLevelColumns = 8; // Liczba kolumn na poziomy zagnieżdżenia (A-H)

        public static void ExportSelectedDbsToExcel(Project project, string excelFilePath, List<(string PlcName, string DbPath)> targets)
        {
            if (targets == null || targets.Count == 0) return;

            // 1. JEDNORAZOWE tworzenie kopii zapasowej przed całym procesem
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
                    Console.WriteLine($"[KOPIA ZAPASOWA] Utworzono kopię: {backupPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OSTRZEŻENIE] Nie udało się utworzyć kopii zapasowej: {ex.Message}");
                }
            }

            using (XLWorkbook workbook = new XLWorkbook(excelFilePath))
            {
                // =========================================================================
                // 2. CZYSZCZENIE SKOROSZYTU PRZED KAŻDYM WYWOŁANIEM
                // =========================================================================
                // ClosedXML wymaga obecności co najmniej 1 arkusza w pliku Excel.
                // Tworzymy bufor tymczasowy, usuwamy stare arkusze, a po wygenerowaniu nowych go kasujemy.
                var tempResetSheet = workbook.Worksheets.Add("Temp_Reset_Sheet");

                var existingSheets = workbook.Worksheets.Where(ws => ws.Name != "Temp_Reset_Sheet").ToList();
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

                // 3. Generowanie nowych arkuszy tylko dla zaznaczonych bloków DB
                foreach (var target in targets)
                {
                    if (!plcMap.TryGetValue(target.PlcName, out PlcSoftware plcSoftware))
                    {
                        Console.WriteLine($"[OSTRZEŻENIE] Nie znaleziono sterownika '{target.PlcName}'. Pomijanie.");
                        continue;
                    }

                    PlcBlock dbBlock = FindBlockByPath(plcSoftware, target.DbPath);
                    if (dbBlock == null)
                    {
                        Console.WriteLine($"[OSTRZEŻENIE] Nie znaleziono bloku pod ścieżką '{target.DbPath}'. Pomijanie.");
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
                    tempResetSheet.Delete(); // Usuwamy bufor, bo Mamy już nowe arkusze
                    workbook.Save();

                    Console.WriteLine("\n==========================================");
                    Console.WriteLine($"SUKCES: Wyczyściono i zaktualizowano plik Excel:\n{excelFilePath}");
                    Console.WriteLine("==========================================");
                }
                else
                {
                    // Jeśli żaden blok nie został poprawnie przetworzony, wycofujemy tymczasowy arkusz
                    tempResetSheet.Delete();
                    Console.WriteLine("\n[INFORMACJA] Nie wygenerowano żadnego arkusza. Plik pozostał bez zmian.");
                }
            }
        }

        /// <summary>
        /// Pomocnicza metoda przetwarzająca pojedynczy blok DB na arkusz w obiekcie workbook.
        /// </summary>
        private static bool ProcessSingleDbToSheet(PlcBlock dbBlock, PlcSoftware plcSoftware, XLWorkbook workbook, string plcName)
        {
            string dbName = dbBlock.Name;
            Console.WriteLine($" -> Przetwarzanie bloku '{dbName}' ({plcName})...");

            // Sprawdzanie spójności / kompilacja
            if (!dbBlock.IsConsistent)
            {
                Console.WriteLine($" -> Blok '{dbName}' jest niespójny. Próba kompilacji PLC...");
                ICompilable compilable = plcSoftware.GetService<ICompilable>();
                if (compilable != null)
                {
                    var result = compilable.Compile();
                    if (result.ErrorCount > 0)
                    {
                        Console.WriteLine($" -> [BŁĄD] Kompilacja nie powiodła się ({result.ErrorCount} błędów). Pomijanie.");
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
                // Eksport DB do XML
                dbBlock.Export(new FileInfo(tempXmlPath), ExportOptions.WithDefaults);

                // Parsowanie XML
                List<DbParameterRow> parameters = ParseDbXml(tempXmlPath);

                string rawSheetName = $"{plcName}_{dbName}";
                string safeSheetName = SanitizeSheetName(rawSheetName);

                // Jeśli arkusz istniał, zastępujemy go
                if (workbook.Worksheets.Contains(safeSheetName))
                {
                    workbook.Worksheets.Worksheet(safeSheetName).Delete();
                }

                IXLWorksheet worksheet = workbook.Worksheets.Add(safeSheetName);

                // Nagłówki poziomów (A - H)
                for (int level = 1; level <= MaxLevelColumns; level++)
                {
                    int levelFromRight = MaxLevelColumns - level + 1;
                    worksheet.Cell(1, level).Value = levelFromRight == 1
                        ? "Nazwa Zmiennej"
                        : $"Poziom -{levelFromRight}";
                }

                // Nagłówki właściwości
                worksheet.Cell(1, MaxLevelColumns + 1).Value = "Typ Danych";
                worksheet.Cell(1, MaxLevelColumns + 2).Value = "Wartość Początkowa";
                worksheet.Cell(1, MaxLevelColumns + 3).Value = "Komentarz";

                // Formatowanie nagłówka
                var headerRange = worksheet.Range(1, 1, 1, MaxLevelColumns + 3);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Wypełnianie danych
                int currentRow = 2;
                foreach (var param in parameters)
                {
                    var path = param.Path;

                    if (path.Count > MaxLevelColumns)
                    {
                        path = path.Skip(path.Count - MaxLevelColumns).ToList();
                    }

                    int startColumn = MaxLevelColumns - path.Count + 1;

                    for (int i = 0; i < path.Count; i++)
                    {
                        worksheet.Cell(currentRow, startColumn + i).Value = path[i];
                    }

                    worksheet.Cell(currentRow, MaxLevelColumns + 1).Value = param.DataType;
                    worksheet.Cell(currentRow, MaxLevelColumns + 2).Value = param.StartValue;
                    worksheet.Cell(currentRow, MaxLevelColumns + 3).Value = param.Comment;

                    currentRow++;
                }

                worksheet.Columns().AdjustToContents();

                if (currentRow > 2)
                {
                    worksheet.RangeUsed().SetAutoFilter();
                }

                Console.WriteLine($" -> Pomyślnie dodano/zaktualizowano arkusz '{safeSheetName}'.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" -> [BŁĄD] Podczas przetwarzania DB w {plcName}: {ex.Message}");
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
    }

    public class DbParameterRow
    {
        public List<string> Path { get; set; } = new List<string>();
        public string DataType { get; set; }
        public string StartValue { get; set; }
        public string Comment { get; set; }
    }
}