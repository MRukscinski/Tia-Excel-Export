using ClosedXML.Excel;
using DocumentFormat.OpenXml.ExtendedProperties;
using DocumentFormat.OpenXml.Vml;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Path = System.IO.Path;


namespace TiaDbToExcelExporter
{
    internal class Program_
    {
        private const int MaxLevelColumns = 8; // Liczba kolumn na poziomy zagnieżdżenia (A-H)

        private static long _idCounter = 0; //                                                                         --------------- WKLEJONE 1 ---------------
        [STAThread]
        private static void Main(string[] args)
        {
            
            
            
            
            
            
            //                                                                                                          v --------------- WKLEJONE --------------- v
            //ustalenie miejsca lokalizacji pliku csv listy zagnieżdzenia folderów w program blocks
            string NazwaCsvDir = "lista_ster_bloki_DB.csv";

            // Rejestracja resolvera assembly dla Siemens.Engineering (TIA V20)
            // Musi pozostać w Main przed wywołaniem funkcji korzystającej z bibliotek Siemensa
            AppDomain.CurrentDomain.AssemblyResolve += MyResolver;

            // Przykładowa ścieżka pobrana z argumentu wiersza poleceń lub wpisana na sztywno
            string projectPath = args.Length >= 1 ? args[0].Trim('"') : @"C:\Users\user\Desktop\Ternium\C.HDVU Ternium_V20\C.HDVU Ternium_V20.ap20";

            // Wywołanie głównej funkcji
            ExportTiaStructureToCsv(projectPath,NazwaCsvDir);

            long[] WybraneIdBlokowDB = new long[] { 1576, 2854, 5040 }; // Podaj ID bloków DB z pliku CSV


            //                                                                                                         ^ --------------- WKLEJONE --------------- ^






            // === KONFIGURACJA ŚCIEŻEK ===
            string excelFilePath = @"C:\Users\user\Desktop\Generator.xlsm";

            
            // string targetPlcName = "CPC";
            // string targetDbPath = "Program blocks/Casting platform/2151_LCM Ladle cover manipulator/DBInst/LCM_DBInst02";
            // string csvFilePath = "C:\\Users\\user\\Desktop\\lista_ster_bloki_DB.csv";



            // === KONFIGURACJA DANEGO EKSPORTU ===
            TiaPortal tiaPortal = null;
            Project project = null;
            bool shouldDisposeTia = false;
            
            try
            {
                Console.WriteLine("=== Rozpoczynanie eksportu parametrów z TIA Portal ===");

                if (!File.Exists(excelFilePath))
                {
                    Console.WriteLine($"[BŁĄD] Nie znaleziono pliku Excel (.xlsm): {excelFilePath}");
                    return;
                }

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





                //                                                                             v --------------- WKLEJONE --------------- v
                // 2.                                                                                     Wywołanie funkcji eksportu

                // zdefiniowanie tabeli string i przypisanie jej wartosci pliku o "NazwaCsvDir" który znajduje się w tym smamym folderze co .exe
                string[] lines = File.ReadAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NazwaCsvDir));

                for (int i = 1; i < WybraneIdBlokowDB.Length; i++)
                {
                    string line = lines[WybraneIdBlokowDB[i]];
                    string[] columns = line.Split(';');

                    string targetPlcName = columns[3];
                    string targetDbPath = columns[6].Substring(columns[2].Length + columns[3].Length + 2);

                    ExportDbToExcel(project, excelFilePath, targetPlcName, targetDbPath);
                }
                //                                                                              ^ --------------- WKLEJONE --------------- ^





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







        //                                                                                                          v --------------- WKLEJONE --------------- v
        public static void ExportTiaStructureToCsv(string inputProjectPath, string NazwaCsv)
        {
            if (string.IsNullOrWhiteSpace(inputProjectPath) || !File.Exists(inputProjectPath))
            {
                Console.WriteLine($"[BŁĄD]: Plik projektu nie istnieje pod ścieżką: {inputProjectPath}");
                return;
            }

            // Reset licznika ID przy każdym wywołaniu funkcji
            _idCounter = 0;

            string outputCsvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NazwaCsv);

            Console.WriteLine($"[INFO] Rozpoczynanie przetwarzania pliku: {inputProjectPath}");
            Console.WriteLine("[INFO] Otwieranie instancji TIA Portal w tle... Proszę czekać.");

            try
            {
                using (TiaPortal tiaProcess = new TiaPortal(TiaPortalMode.WithoutUserInterface))
                {
                    FileInfo projectFile = new FileInfo(inputProjectPath);

                    Console.WriteLine("[INFO] Otwieranie projektu...");
                    Project mainProject = tiaProcess.Projects.Open(projectFile);

                    Console.WriteLine("[INFO] Skanowanie struktury projektu i zapis do CSV...");
                    using (StreamWriter writer = new StreamWriter(outputCsvPath, false, Encoding.UTF8))
                    {
                        // Zapis nagłówka kolumn
                        writer.WriteLine("Id;ParentId;ProjectName;DeviceName;NodeType;Name;FullPath");

                        foreach (Project proj in tiaProcess.Projects)
                        {
                            ScanDevicesInProject(proj, writer);
                        }
                    }

                    Console.WriteLine("[INFO] Zamykanie projektu...");
                    mainProject.Close();

                    Console.WriteLine("\n[SUKCES] Zakończono pobieranie danych!");
                    Console.WriteLine($"[SUKCES] Plik CSV został zapisany: {outputCsvPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[BŁĄD]: Wystąpił wyjątek podczas przetwarzania: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        #region TIA Openness Logic
        private static void ScanDevicesInProject(Project project, StreamWriter writer)
        {
            // 1. Węzeł korzenia – Projekt (NodeType: Project)
            long projectId = ++_idCounter;
            string projectPath = project.Name;
            WriteCsvLine(writer, projectId, null, project.Name, "", "Project", project.Name, projectPath);

            List<Device> allDevices = new List<Device>();

            CollectDevices(project.Devices, allDevices);

            foreach (DeviceGroup group in project.DeviceGroups)
            {
                CollectDevicesFromGroup(group, allDevices);
            }

            foreach (Device device in allDevices)
            {
                List<PlcSoftware> plcList = GetPlcSoftwareFromDevice(device);

                foreach (PlcSoftware plcSoftware in plcList)
                {
                    string plcName = plcSoftware.Name;

                    // 2. Węzeł sterownika (NodeType: Device)
                    long deviceNodeId = ++_idCounter;
                    string devicePath = $"{projectPath}/{plcName}";
                    WriteCsvLine(writer, deviceNodeId, projectId, project.Name, plcName, "Device", plcName, devicePath);

                    // 3. Węzeł głównego folderu "Program blocks" (NodeType: Folder)
                    PlcBlockGroup rootBlockGroup = plcSoftware.BlockGroup;
                    long rootFolderId = ++_idCounter;
                    string rootPath = $"{devicePath}/{rootBlockGroup.Name}";

                    WriteCsvLine(writer, rootFolderId, deviceNodeId, project.Name, plcName, "Folder", rootBlockGroup.Name, rootPath);

                    // 4. Rekurencyjne przetwarzanie podfolderów i bloków
                    TraverseBlockGroup(rootBlockGroup, project.Name, plcName, rootFolderId, rootPath, writer);
                }
            }
        }
        private static void TraverseBlockGroup(PlcBlockGroup currentGroup, string projectName, string deviceName, long parentFolderId, string currentPath, StreamWriter writer)
        {
            // A. Bloki w bieżącym katalogu
            foreach (PlcBlock block in currentGroup.Blocks)
            {
                long blockId = ++_idCounter;
                string blockPath = $"{currentPath}/{block.Name}";

                WriteCsvLine(writer, blockId, parentFolderId, projectName, deviceName, "Block", block.Name, blockPath);
            }

            // B. Podfoldery użytkownika
            foreach (PlcBlockUserGroup subGroup in currentGroup.Groups)
            {
                long subFolderId = ++_idCounter;
                string subFolderPath = $"{currentPath}/{subGroup.Name}";

                WriteCsvLine(writer, subFolderId, parentFolderId, projectName, deviceName, "Folder", subGroup.Name, subFolderPath);

                TraverseBlockGroup(subGroup, projectName, deviceName, subFolderId, subFolderPath, writer);
            }
        }
        private static void CollectDevices(DeviceComposition devices, List<Device> resultList)
        {
            foreach (Device dev in devices)
            {
                resultList.Add(dev);
            }
        }
        private static void CollectDevicesFromGroup(DeviceGroup group, List<Device> resultList)
        {
            CollectDevices(group.Devices, resultList);
        }
        #endregion

        #region Helpers
        private static void WriteCsvLine(StreamWriter writer, long id, long? parentId, string projectName, string deviceName, string nodeType, string name, string fullPath)
        {
            string line = string.Join(";",
                id,
                parentId.HasValue ? parentId.Value.ToString() : "",
                EscapeCsvField(projectName),
                EscapeCsvField(deviceName),
                EscapeCsvField(nodeType),
                EscapeCsvField(name),
                EscapeCsvField(fullPath)
            );

            writer.WriteLine(line);
        }
        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";

            if (field.Contains(";") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }
        #endregion

        #region Assembly Resolver
        private static Assembly MyResolver(object sender, ResolveEventArgs args)
        {
            int index = args.Name.IndexOf(',');
            if (index == -1) return null;

            string name = args.Name.Substring(0, index);
            if (name.Equals("Siemens.Engineering", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Siemens.Engineering.Hmi", StringComparison.OrdinalIgnoreCase))
            {
                string systemPath = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.dll";
                if (File.Exists(systemPath))
                {
                    return Assembly.LoadFrom(systemPath);
                }
            }
            return null;
        }

        #endregion
        //                                                                                                          ^ --------------- WKLEJONE --------------- ^









        /// <summary>
        /// Metoda odpowiedzialna za wyszukanie bloku DB pod wskazaną ścieżką i wyeksportowanie go do Excela.
        /// </summary>
        private static void ExportDbToExcel(Project project, string excelFilePath, string targetPlcName, string targetDbPath)
        {
            using (XLWorkbook workbook = new XLWorkbook(excelFilePath))
            {
                bool processedSuccessfully = false;

                foreach (Device device in project.Devices)
                {
                    List<PlcSoftware> plcList = GetPlcSoftwareFromDevice(device);

                    foreach (PlcSoftware plcSoftware in plcList)
                    {
                        string plcName = plcSoftware.Name;

                        // Sprawdzamy, czy to dokładnie ten sterownik, który nas interesuje
                        if (!string.Equals(plcName, targetPlcName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        Console.WriteLine($"\nPrzeszukiwanie sterownika: {plcName} pod kątem ścieżki: {targetDbPath}");

                        // Wyszukiwanie bloku po pełnej ścieżce wewnątrz sterownika
                        PlcBlock dbBlock = FindBlockByPath(plcSoftware, targetDbPath);

                        if (dbBlock != null)
                        {
                            string dbName = dbBlock.Name;
                            Console.WriteLine($" -> Znaleziono blok '{dbName}' pod wskazaną ścieżką.");

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
                            string tempXmlPath = Path.Combine(Path.GetTempPath(), $"{plcName}_{dbName}.xml");
                            try
                            {
                                dbBlock.Export(new FileInfo(tempXmlPath), ExportOptions.WithDefaults);

                                // Parsowanie czystego drzewa XML z bloku DB
                                List<DbParameterRow> parameters = ParseDbXml(tempXmlPath);

                                // Przygotowanie nazwy arkusza jako suma nazwy sterownika oraz ścieżki bloku
                                string rawSheetName = $"{plcName}_{targetDbPath.Substring("Program blocks /".Length)}";
                                string safeSheetName = SanitizeSheetName(rawSheetName);

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

                                processedSuccessfully = true;
                                Console.WriteLine($" -> Pomyślnie dodano arkusz '{safeSheetName}' ({parameters.Count} zmiennych).");
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

                if (processedSuccessfully)
                {
                    workbook.Save();
                    Console.WriteLine($"\n==========================================");
                    Console.WriteLine($"SUKCES: Zaktualizowano plik .xlsm:\n{excelFilePath}");
                    Console.WriteLine($"==========================================");
                }
                else
                {
                    Console.WriteLine($"\n[INFORMACJA] Nie znaleziono bloku pod wskazaną ścieżką '{targetDbPath}' dla sterownika '{targetPlcName}'.");
                }
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

        public class DbParameterRow
        {
            public List<string> Path { get; set; } = new List<string>();
            public string DataType { get; set; }
            public string StartValue { get; set; }
            public string Comment { get; set; }
        }
    }
}