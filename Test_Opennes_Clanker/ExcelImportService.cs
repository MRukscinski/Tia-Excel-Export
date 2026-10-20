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
    public static class ExcelImporterService
    {
        private const int MaxLevelColumns = 8; // Liczba kolumn na poziomy zagnieżdżenia (A-H)

        public static void ImportSelectedDbsFromExcel(Project project, string excelFilePath, List<(string PlcName, string DbPath)> targets)
        {
            if (targets == null || targets.Count == 0) return;

            if (!File.Exists(excelFilePath))
            {
                Console.WriteLine($"[BŁĄD] Plik Excela nie istnieje: {excelFilePath}");
                return;
            }

            using (XLWorkbook workbook = new XLWorkbook(excelFilePath))
            {
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

                bool anyUpdated = false;

                foreach (var target in targets)
                {
                    if (!plcMap.TryGetValue(target.PlcName, out PlcSoftware plcSoftware))
                    {
                        Console.WriteLine($"[OSTRZEŻENIE] Nie znaleziono sterownika '{target.PlcName}'. Pomijanie.");
                        continue;
                    }

                    // Szukanie bloku DB w TIA Portal po podanej ścieżce
                    var blockResult = FindBlockAndParentGroup(plcSoftware, target.DbPath);
                    if (blockResult.Block == null)
                    {
                        Console.WriteLine($"[OSTRZEŻENIE] Nie znaleziono bloku pod ścieżką '{target.DbPath}'. Pomijanie.");
                        continue;
                    }

                    string dbName = blockResult.Block.Name;
                    string rawSheetName = $"{target.PlcName}_{dbName}";
                    string safeSheetName = SanitizeSheetName(rawSheetName);

                    if (!workbook.Worksheets.Contains(safeSheetName))
                    {
                        Console.WriteLine($"[OSTRZEŻENIE] Nie znaleziono arkusza '{safeSheetName}' w pliku Excel. Pomijanie.");
                        continue;
                    }

                    IXLWorksheet worksheet = workbook.Worksheet(safeSheetName);
                    var excelData = ReadSheetData(worksheet);

                    if (ProcessSingleDbImport(blockResult.Block, blockResult.ParentGroup, plcSoftware, target.PlcName, excelData))
                    {
                        anyUpdated = true;
                    }
                }

                if (anyUpdated)
                {
                    Console.WriteLine("\nZapisywanie zmian w projekcie TIA Portal...");
                    project.Save();
                    Console.WriteLine("==========================================");
                    Console.WriteLine("SUKCES: Dane zostały zaimportowane i projekt został zapisany!");
                    Console.WriteLine("==========================================");
                }
                else
                {
                    Console.WriteLine("\n[INFORMACJA] Nie dokonano żadnych zmian w projekcie.");
                }
            }
        }

        private static bool ProcessSingleDbImport(PlcBlock dbBlock, PlcBlockGroup parentGroup, PlcSoftware plcSoftware, string plcName, Dictionary<string, (string ValueToSet, string Comment)> excelData)
        {
            string dbName = dbBlock.Name;
            Console.WriteLine($" -> Importowanie danych do bloku '{dbName}' ({plcName})...");

            string tempXmlPath = Path.Combine(Path.GetTempPath(), $"{plcName}_{dbName}_Import.xml");
            try
            {
                // 1. Eksport obecnie istniejącego bloku DB do tymczasowego XML
                dbBlock.Export(new FileInfo(tempXmlPath), ExportOptions.WithDefaults);

                // 2. Modyfikacja pliku XML na podstawie danych z Excela
                bool modified = ModifyXmlWithExcelData(tempXmlPath, excelData);
                if (!modified)
                {
                    Console.WriteLine($" -> [INFORMACJA] Brak zmian do wprowadzenia dla '{dbName}'.");
                    return false;
                }

                // 3. Ponowny import zmodyfikowanego XML (z opcją Override/Nadpisania)
                parentGroup.Blocks.Import(new FileInfo(tempXmlPath), ImportOptions.Override);
                Console.WriteLine($" -> [SUKCES] Zaimportowano zaktualizowany blok '{dbName}'.");

                // 4. Kompilacja oprogramowania PLC
                Console.WriteLine($" -> Kompilowanie sterownika {plcName}...");
                ICompilable compilable = plcSoftware.GetService<ICompilable>();
                if (compilable != null)
                {
                    var result = compilable.Compile();
                    if (result.ErrorCount > 0)
                    {
                        Console.WriteLine($" -> [OSTRZEŻENIE] Kompilacja wykazała błędy ({result.ErrorCount}).");
                    }
                    else
                    {
                        Console.WriteLine(" -> Kompilacja zakończona sukcesem.");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" -> [BŁĄD] Podczas importu bloku '{dbName}' w {plcName}: {ex.Message}");
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

        private static Dictionary<string, (string ValueToSet, string Comment)> ReadSheetData(IXLWorksheet worksheet)
        {
            var rowsDict = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;

            for (int row = 2; row <= lastRow; row++)
            {
                List<string> pathParts = new List<string>();
                for (int col = 1; col <= MaxLevelColumns; col++)
                {
                    var cellVal = worksheet.Cell(row, col).Value.ToString().Trim();
                    if (!string.IsNullOrEmpty(cellVal))
                    {
                        pathParts.Add(cellVal);
                    }
                }

                if (pathParts.Count > 0)
                {
                    string fullPathKey = string.Join(".", pathParts);

                    // Odczyt obecnej wartości ze sterownika (Kolumna J -> MaxLevelColumns + 2)
                    string valueFromPlc = worksheet.Cell(row, MaxLevelColumns + 2).Value.ToString().Trim();

                    // Odczyt nowej wartości do wpisania (Kolumna K -> MaxLevelColumns + 3)
                    string valueToSet = worksheet.Cell(row, MaxLevelColumns + 3).Value.ToString().Trim();

                    // Odczyt komentarza (Kolumna L -> MaxLevelColumns + 4)
                    string comment = worksheet.Cell(row, MaxLevelColumns + 4).Value.ToString().Trim();

                    // Jeśli użytkownik wpisał nową wartość w kolumnę "Wartość do Wpisania", używamy jej.
                    // Jeśli zostawił puste pole, zachowujemy dotychczasową wartość ze sterownika!
                    string finalValue = !string.IsNullOrEmpty(valueToSet) ? valueToSet : valueFromPlc;

                    rowsDict[fullPathKey] = (finalValue, comment);
                }
            }

            return rowsDict;
        }

        private static bool ModifyXmlWithExcelData(string xmlPath, Dictionary<string, (string ValueToSet, string Comment)> excelRows)
        {
            XDocument doc = XDocument.Load(xmlPath);
            var interfaceElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Interface");
            if (interfaceElem == null) return false;

            var topSections = interfaceElem
                .Elements().Where(e => e.Name.LocalName == "Sections")
                .Elements().Where(e => e.Name.LocalName == "Section");

            foreach (var section in topSections)
            {
                var topMembers = section.Elements().Where(e => e.Name.LocalName == "Member");
                foreach (var topMember in topMembers)
                {
                    UpdateMemberNode(topMember, new List<string>(), excelRows);
                }
            }

            doc.Save(xmlPath);
            return true;
        }

        private static void UpdateMemberNode(XElement memberElem, List<string> currentPath, Dictionary<string, (string ValueToSet, string Comment)> excelRows)
        {
            string name = memberElem.Attribute("Name")?.Value ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return;

            List<string> newPath = new List<string>(currentPath) { name };
            string pathKey = string.Join(".", newPath);

            if (excelRows.TryGetValue(pathKey, out var excelValues))
            {
                XNamespace ns = memberElem.Name.Namespace;

                // 1. Aktualizacja StartValue
                XElement startValElem = memberElem.Elements().FirstOrDefault(e => e.Name.LocalName == "StartValue");
                if (!string.IsNullOrEmpty(excelValues.ValueToSet))
                {
                    if (startValElem == null)
                    {
                        startValElem = new XElement(ns + "StartValue");
                        memberElem.Add(startValElem);
                    }
                    startValElem.Value = excelValues.ValueToSet;
                }

                // 2. Aktualizacja Komentarza
                if (!string.IsNullOrEmpty(excelValues.Comment))
                {
                    XElement commentElem = memberElem.Elements().FirstOrDefault(e => e.Name.LocalName == "Comment");
                    if (commentElem == null)
                    {
                        commentElem = new XElement(ns + "Comment",
                            new XElement(ns + "MultiLanguageText",
                                new XAttribute("Lang", "en-US"),
                                excelValues.Comment));
                        memberElem.Add(commentElem);
                    }
                    else
                    {
                        XElement textElem = commentElem.Descendants().FirstOrDefault(e => e.Name.LocalName == "MultiLanguageText");
                        if (textElem != null)
                        {
                            textElem.Value = excelValues.Comment;
                        }
                    }
                }
            }

            // Przetwarzanie podstruktury (drzewa)
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
                UpdateMemberNode(child, newPath, excelRows);
            }
        }

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

        private static (PlcBlock Block, PlcBlockGroup ParentGroup) FindBlockAndParentGroup(PlcSoftware plcSoftware, string dbPath)
        {
            string[] parts = dbPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return (null, null);

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

                if (subGroup == null) return (null, null);
                currentGroup = subGroup;
            }

            foreach (PlcBlock block in currentGroup.Blocks)
            {
                if (string.Equals(block.Name, blockName, StringComparison.OrdinalIgnoreCase))
                {
                    return (block, currentGroup);
                }
            }

            return (null, null);
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
}