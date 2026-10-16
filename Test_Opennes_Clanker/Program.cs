using System;
using System.Collections.Generic;
using System.Windows.Forms; //moje 
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
using Test_Opennes_Clanker;
//dupapapeja sra
using WinFormsApp = System.Windows.Forms.Application;

namespace TiaDbToExcelExporter
{
    internal class Program
    {


        private const string TargetDbName = "Parameters_DB";
        private const int MaxLevelColumns = 8; // Liczba kolumn na poziomy zagnieżdżenia (A-H)

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Tutaj podajesz nazwę swojego formularza:
            Application.Run(new frontForm());
       
        
        }
        //komenarz test 
    }
}
/*
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

namespace TiaDbToExcelExporter
{
    internal class Program
    {
        private const string TargetDbName = "Parameters_DB";
        private const int MaxLevelColumns = 8; // Liczba kolumn na poziomy zagnieżdżenia (A-H)

        [STAThread]
        private static void Main(string[] args)
        {
            string projectPath = @"C:\Users\user\Desktop\Ternium\C.HDVU Ternium_V20\C.HDVU Ternium_V20.ap20";
            string excelOutputPath = @"C:\Users\user\Desktop\Exported_Parameters.xlsx";

            TiaPortal tiaPortal = null;
            Project project = null;
            bool shouldDisposeTia = false;

            try
            {
                Console.WriteLine("=== Rozpoczynanie eksportu parametrów z TIA Portal ===");

                // 1. Łączenie z TIA Portal
                var runningProcesses = TiaPortal.GetProcesses();
                if (runningProcesses.Count > 0)
                {
                    Console.WriteLine("Znaleziono uruchomioną instancję TIA Portal. Podłączanie...");
                    TiaPortalProcess process = runningProcesses.FirstOrDefault();
                    tiaPortal = process.Attach();

                    if (tiaPortal.Projects.Count > 0)
                    {
                        project = tiaPortal.Projects.FirstOrDefault();
                        Console.WriteLine($"Podłączono do otwartego projektu: {project.Name}");
                    }
                }

                if (project == null)
                {
                    if (tiaPortal == null)
                    {
                        Console.WriteLine("Uruchamianie nowej instancji TIA Portal V20...");
                        tiaPortal = new TiaPortal(TiaPortalMode.WithUserInterface);
                        shouldDisposeTia = true;
                    }

                    FileInfo projectFile = new FileInfo(projectPath);
                    if (!projectFile.Exists)
                    {
                        Console.WriteLine($"[BŁĄD] Nie znaleziono pliku projektu: {projectPath}");
                        return;
                    }

                    Console.WriteLine($"Otwieranie projektu: {projectPath}");
                    project = tiaPortal.Projects.Open(projectFile);
                }

                // 2. Tworzenie dokumentu Excel
                using (XLWorkbook workbook = new XLWorkbook())
                {
                    bool addedAnySheet = false;

                    foreach (Device device in project.Devices)
                    {
                        List<PlcSoftware> plcList = GetPlcSoftwareFromDevice(device);

                        foreach (PlcSoftware plcSoftware in plcList)
                        {
                            string plcName = plcSoftware.Name;
                            Console.WriteLine($"\nPrzeszukiwanie sterownika: {plcName}");

                            PlcBlock dbBlock = FindBlockByName(plcSoftware.BlockGroup, TargetDbName);

                            if (dbBlock != null)
                            {
                                Console.WriteLine($" -> Znaleziono '{TargetDbName}' w {plcName}.");

                                // Sprawdzanie spójności / kompilacja
                                if (!dbBlock.IsConsistent)
                                {
                                    Console.WriteLine($" -> Blok '{TargetDbName}' jest niespójny. Próba kompilacji PLC...");
                                    ICompilable compilable = plcSoftware.GetService<ICompilable>();
                                    if (compilable != null)
                                    {
                                        var result = compilable.Compile();
                                        if (result.ErrorCount > 0)
                                        {
                                            Console.WriteLine($" -> [BŁĄD] Kompilacja nie powiodła się ({result.ErrorCount} błędów). Pomijanie.");
                                            continue;
                                        }
                                        Console.WriteLine(" -> Kompilacja zakończona sukcesem.");
                                    }
                                    else
                                    {
                                        Console.WriteLine(" -> Nie można pobrać usługi kompilacji. Pomijanie.");
                                        continue;
                                    }
                                }

                                // Eksport DB do XML
                                string tempXmlPath = Path.Combine(Path.GetTempPath(), $"{plcName}_{TargetDbName}.xml");
                                try
                                {
                                    dbBlock.Export(new FileInfo(tempXmlPath), ExportOptions.WithDefaults);

                                    // Parsowanie czystego drzewa XML z bloku DB
                                    List<DbParameterRow> parameters = ParseDbXml(tempXmlPath);

                                    // Dodawanie arkusza
                                    string safeSheetName = SanitizeSheetName(plcName);
                                    IXLWorksheet worksheet = workbook.Worksheets.Add(safeSheetName);

                                    // Nagłówki poziomów (A - H)
                                    for (int level = 1; level <= MaxLevelColumns; level++)
                                    {
                                        int levelFromRight = MaxLevelColumns - level + 1;
                                        worksheet.Cell(1, level).Value = levelFromRight == 1
                                            ? "Nazwa Zmiennej"
                                            : $"Poziom -{levelFromRight - 1}";
                                    }

                                    // Nagłówki właściwości (I, J, K)
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

                                    addedAnySheet = true;
                                    Console.WriteLine($" -> Pomyślnie wygenerowano arkusz '{safeSheetName}' ({parameters.Count} zmiennych).");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($" -> [BŁĄD] Podczas przetwarzania DB w {plcName}: {ex.Message}");
                                }
                                finally
                                {
                                    if (File.Exists(tempXmlPath))
                                    {
                                        File.Delete(tempXmlPath);
                                    }
                                }
                            }
                        }
                    }

                    if (addedAnySheet)
                    {
                        workbook.SaveAs(excelOutputPath);
                        Console.WriteLine($"\n==========================================");
                        Console.WriteLine($"SUKCES: Plik zapisano pod ścieżką:\n{excelOutputPath}");
                        Console.WriteLine($"==========================================");
                    }
                    else
                    {
                        Console.WriteLine($"\n[INFORMACJA] Nie znaleziono spójnego bloku '{TargetDbName}' w projektach.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[BŁĄD KRYTYCZNY] {ex.Message}");
            }
            finally
            {
                if (shouldDisposeTia && tiaPortal != null)
                {
                    tiaPortal.Dispose();
                }
            }

            Console.WriteLine("\nNaciśnij dowolny klawisz, aby zakończyć...");
            Console.ReadKey();
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

        private static PlcBlock FindBlockByName(PlcBlockGroup group, string blockName)
        {
            foreach (PlcBlock block in group.Blocks)
            {
                if (string.Equals(block.Name, blockName, StringComparison.OrdinalIgnoreCase))
                {
                    return block;
                }
            }

            foreach (PlcBlockUserGroup subGroup in group.Groups)
            {
                PlcBlock found = FindBlockByName(subGroup, blockName);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// Parsuje strukturę XML bloku DB precyzyjnie poruszając się po drzewie interfejsu.
        /// </summary>
        private static List<DbParameterRow> ParseDbXml(string xmlFilePath)
        {
            List<DbParameterRow> result = new List<DbParameterRow>();
            XDocument doc = XDocument.Load(xmlFilePath);

            // Szukamy sekcji interfejsu najwyższego poziomu (Interface -> Sections -> Section)
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

        /// <summary>
        /// Precyzyjnie wyciąga wartości zmiennej oraz jej podzmienne (Struct / UDT).
        /// </summary>
        private static void ParseMemberElement(XElement memberElem, List<string> currentPath, List<DbParameterRow> resultList)
        {
            string name = memberElem.Attribute("Name")?.Value ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return;

            List<string> newPath = new List<string>(currentPath) { name };
            string rawDataType = memberElem.Attribute("Datatype")?.Value ?? string.Empty;

            // Wartość początkowa – ściśle z bezpośredniego węzła tej zmiennej
            XElement startValElem = memberElem.Elements().FirstOrDefault(e => e.Name.LocalName == "StartValue");
            string startVal = startValElem?.Value ?? string.Empty;

            // Komentarz – wyłącznie przypisany do bieżącej zmiennej
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

            // Pobieranie dzieci zagnieżdżonych (podzmiennych)
            List<XElement> childMembers = new List<XElement>();

            // 1. Dzieci bezpośrednie
            childMembers.AddRange(memberElem.Elements().Where(e => e.Name.LocalName == "Member"));

            // 2. Dzieci zawarte w strukturach pod-sekcji UDT / Struct (<Sections><Section Name="None"><Member>...)
            var nestedSections = memberElem.Elements().Where(e => e.Name.LocalName == "Sections")
                                           .Elements().Where(e => e.Name.LocalName == "Section");

            foreach (var sec in nestedSections)
            {
                childMembers.AddRange(sec.Elements().Where(e => e.Name.LocalName == "Member"));
            }

            // Rekurencyjne przetworzenie podzmiennych
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
    }

    public class DbParameterRow
    {
        public List<string> Path { get; set; } = new List<string>();
        public string DataType { get; set; }
        public string StartValue { get; set; }
        public string Comment { get; set; }
    }
}
*/
