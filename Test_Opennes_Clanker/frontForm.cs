//770, 820 SIZE!!
using ClosedXML.Excel;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Path = System.IO.Path;


namespace Test_Opennes_Clanker
{

    public partial class frontForm : Form
    {
        private const int MaxLevelColumns = 8; // Liczba kolumn na poziomy zagnieżdżenia (A-H)

        private static long _idCounter = 0; //
        
        //----------------------------
        public int[] mySelectedIndex { get; private set; } = new int[0];
        public string NazwaCsvDir { get; private set; }

        //Loading screen :D
        private Timer rotationTimer = new Timer { Interval = 30 };
        private Image originalImage;
        private float currentAngle = 0f;

        
        public frontForm()
        {
            InitializeComponent();

            this.DoubleBuffered = true;

            rotationTimer.Tick += RotationTimer_Tick;
            pictureBox1.Paint += PictureBox1_Paint;
        }

        private async void btnBrowse(object sender, EventArgs e) //Szukanie sciezki
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Wybierz projekt TIA Portal";
                openFileDialog.Filter = "Projekty TIA Portal (*.ap*)|*.ap*|Wszystkie pliki (*.*)|*.*";
                openFileDialog.FilterIndex = 1;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    // 1. Przypisanie wybranej ścieżki z pliku do TextBoxa i zmiennej
                    string projectPath = openFileDialog.FileName;
                    textBox1.Text = projectPath;

                    // 2. Nazwa pliku CSV z listą zagnieżdżeń
                    NazwaCsvDir = "lista_ster_bloki_DB.csv";

                    ToggleLoadingAnimation();
                    try
                    {
                        // . Uruchamiamy ciężki eksport na osobnym wątku (UI pozostaje wolny i animacja działa!)
                        await Task.Run(() =>
                        {
                            TiaPortalService.ExportTiaStructureToCsv(projectPath, NazwaCsvDir);
                        });

                        // 5. Ładowanie do drzewka wykonywane w wątku UI po zakończeniu eksportu
                        string fullCsvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NazwaCsvDir);
                        LoadDataBlocksToTree(fullCsvPath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Błąd podczas przetwarzania projektu:\n{ex.Message}",
                                        "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        // 6. Wyłączamy animację ZAWSZE po zakończeniu (nawet jeśli wystąpił błąd)
                        ToggleLoadingAnimation();
                    }

                }
            }
        }
        private void button1_Click(object sender, EventArgs e)
        {
            // 1. Pobieramy ścieżkę z TextBoxa
            string projectPath = textBox1.Text.Trim();

            // 2. Walidacja: sprawdzenie, czy użytkownik w ogóle podał ścieżkę
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                MessageBox.Show("Najpierw wybierz lub wpisz ścieżkę do projektu TIA Portal!",
                                "Brak ścieżki", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 3. Walidacja: sprawdzenie, czy plik projektu faktycznie istnieje na dysku
            if (!File.Exists(projectPath))
            {
                MessageBox.Show($"Wskazany plik projektu nie istnieje:\n{projectPath}",
                                "Błąd ścieżki", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 4. Wywołanie Twojej przykładowej funkcji z przekazaniem ścieżki <<<<<<<<<<<<<<<<<<<<TUTAJ FUNKCJA MICHAŁA>>>>>>>>>>>>>>>>>>>>
            //MojaPrzykładowaFunkcja(projectPath); 
        }
        private void btnLoadCsv_Click(object sender, EventArgs e) //TYMCZASOWE
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Wybierz plik CSV z blockami DB";
                openFileDialog.Filter = "Pliki CSV (*.csv)|*.csv|Wszystkie pliki (*.*)|*.*";
                openFileDialog.FilterIndex = 1;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    textBox2.Text = openFileDialog.FileName; // Przypisanie ścieżki CSV do TextBox
                    LoadDataBlocksToTree(openFileDialog.FileName);
                }
            }
        }

        TiaPortal tiaPortal = null;
        Project project = null;
        bool shouldDisposeTia = false;
        string excelFilePath = @"C:\Users\user\Desktop\Generator.xlsm";
        private async void btnProcessSelected_Click(object sender, EventArgs e)
        {
            // 1. Pobieramy zaznaczone indeksy z drzewka
            mySelectedIndex = GetSelectedCsvRowIndices();

            if (mySelectedIndex == null || mySelectedIndex.Length == 0)
            {
                MessageBox.Show("Nie zaznaczono żadnego elementu w drzewku!",
                                "Ostrzeżenie", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 2. Walidacja nazwy pliku CSV
            if (string.IsNullOrEmpty(NazwaCsvDir))
            {
                NazwaCsvDir = "lista_ster_bloki_DB.csv";
            }

            string csvFullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NazwaCsvDir);
            if (!File.Exists(csvFullPath))
            {
                MessageBox.Show($"Brak pliku CSV pod ścieżką:\n{csvFullPath}", "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 3. Walidacja ścieżki projektu TIA
            string projectPath = textBox1.Text.Trim();
            if (!File.Exists(projectPath))
            {
                MessageBox.Show("Wskazany plik projektu TIA Portal nie istnieje!", "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 4. Odczyt linii pliku CSV i przygotowanie listy do eksportu
            string[] lines = File.ReadAllLines(csvFullPath);
            List<(string PlcName, string DbPath)> targetsToExport = new List<(string, string)>();

            foreach (int csvRowIndex in mySelectedIndex)
            {
                if (csvRowIndex < 0 || csvRowIndex >= lines.Length) continue;

                string line = lines[csvRowIndex];
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] columns = line.Split(';');
                if (columns.Length > 6)
                {
                    string targetPlcName = columns[3].Trim('"').Trim();
                    string fullPathRaw = columns[6].Trim('"').Trim();

                    string projName = columns[2].Trim('"').Trim();
                    string prefix = $"{projName}/{targetPlcName}/";

                    string targetDbPath = fullPathRaw;
                    if (fullPathRaw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        targetDbPath = fullPathRaw.Substring(prefix.Length);
                    }

                    targetsToExport.Add((targetPlcName, targetDbPath));
                }
            }

            if (targetsToExport.Count == 0)
            {
                MessageBox.Show("Nie udało się odczytać ścieżek z zaznaczonych elementów CSV.", "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 5. Start animacji ładowania oraz zablokowanie przycisku
            ToggleLoadingAnimation();
            button2.Enabled = false;

            try
            {
                await Task.Run(() =>
                {
                    // Podłączenie pod istniejącą instancję TIA lub otwarcie nowej w tle
                    var processes = TiaPortal.GetProcesses();
                    if (processes.Count > 0)
                    {
                        tiaPortal = processes[0].Attach();
                        project = tiaPortal.Projects.FirstOrDefault();
                        shouldDisposeTia = false;
                    }
                    else
                    {
                        tiaPortal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                        project = tiaPortal.Projects.Open(new FileInfo(projectPath));
                        shouldDisposeTia = true;
                    }

                    if (project == null)
                    {
                        throw new Exception("Nie udało się pobrać referencji do projektu TIA Portal.");
                    }

                    // Zbiorczy, szybki eksport wybranych bloków DB w pamięci RAM

                    ExportSelectedDbsToExcel(project, excelFilePath, targetsToExport);
                });

                MessageBox.Show("Eksport wybranych bloków zakończony sukcesem!", "Sukces", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"[BŁĄD KRYTYCZNY]: {ex.Message}", "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine($"\n[BŁĄD KRYTYCZNY] {ex.Message}");
            }
            finally
            {
                // Asynchroniczne zamykanie sesji TIA w tle (jeśli była otwierana przez aplikację)
                if (shouldDisposeTia && tiaPortal != null)
                {
                    await Task.Run(() =>
                    {
                        project?.Close();
                        tiaPortal.Dispose();
                        tiaPortal = null;
                        project = null;
                    });
                }

                // Zatrzymanie animacji i odblokowanie przycisku UI
                ToggleLoadingAnimation();
                button2.Enabled = true;
            }
        }


        // TREE VIEW TREE VIEW TREE VIEW TREE VIEW TREE VIEW 
        private void LoadDataBlocksToTree(string csvPath)
        {
            try
            {
                treeViewDB.Nodes.Clear();
                treeViewDB.CheckBoxes = true;

                string[] lines = File.ReadAllLines(csvPath);

                // Pętla od 1 (pomijamy nagłówek w index 0)
                for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] columns = line.Split(';');

                    if (columns.Length > 6)
                    {
                        string fullPath = columns[6].Trim('"').Trim();

                        if (!string.IsNullOrEmpty(fullPath))
                        {
                            string[] pathParts = fullPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

                            // Przekazujemy lineIndex lub ID (np. columns[0]) jako parametr
                            AddPathToTree(treeViewDB.Nodes, pathParts, 0, lineIndex);
                        }
                    }
                }

                treeViewDB.CollapseAll();
                if (treeViewDB.Nodes.Count > 0) // Jeżeli drzewo ma tylko pierwszy węzeł na najwyższym poziomem, to go rozwijamy :)
                {
                    treeViewDB.Nodes[0].Expand();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd podczas budowania drzewa:\n{ex.Message}", "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Metoda rekurencyjna dodająca węzły do drzewa
        private void AddPathToTree(TreeNodeCollection parentNodes, string[] pathParts, int levelIndex, int rowIndex)
        {
            if (levelIndex >= pathParts.Length) return;

            string currentPart = pathParts[levelIndex];

            TreeNode existingNode = parentNodes.Cast<TreeNode>()
                                               .FirstOrDefault(n => n.Text.Equals(currentPart, StringComparison.OrdinalIgnoreCase));

            if (existingNode == null)
            {
                existingNode = new TreeNode(currentPart);
                existingNode.Checked = false;

                // Jeśli jest to liść (ostatni element ścieżki - konkretny DB), przypisujemy indeks wiersza z CSV do Tag
                if (levelIndex == pathParts.Length - 1)
                {
                    existingNode.Tag = rowIndex;
                }

                parentNodes.Add(existingNode);
            }

            AddPathToTree(existingNode.Nodes, pathParts, levelIndex + 1, rowIndex);
        }

        private void GetCheckedNodes(TreeNodeCollection nodes, List<string> checkedNodes)
        {
            foreach (TreeNode node in nodes)
            {
                // Jeśli węzeł nie ma dzieci (jest liściem = DataBlockiem) i jest zaznaczony
                if (node.Nodes.Count == 0 && node.Checked)
                {
                    // node.FullPath zwróci pełną ścieżkę, np. "Stacja_1\Napedy\DB_Motors"
                    checkedNodes.Add(node.FullPath);
                }

                // Przeszukiwanie podfolderów
                GetCheckedNodes(node.Nodes, checkedNodes);
            }
        }

        private void btnSelectAllTree_Click(object sender, EventArgs e)
        {
            SetAllNodesCheckState(treeViewDB.Nodes, true);
        }

        private void button4_Click(object sender, EventArgs e)
        {
            SetAllNodesCheckState(treeViewDB.Nodes, false);
        }
        private void SetAllNodesCheckState(TreeNodeCollection nodes, bool isChecked)
        {
            foreach (TreeNode node in nodes)
            {
                node.Checked = isChecked;

                // Jeśli węzeł ma podfoldery/dzieci, przechodzimy niżej
                if (node.Nodes.Count > 0)
                {
                    SetAllNodesCheckState(node.Nodes, isChecked);
                }
            }
        }

        private bool isUpdatingCheck = false;

        // 1. Zdarzenie wywoływane automatycznie po kliknięciu dowolnego checkboxa w drzewku
        private void treeViewDB_AfterCheck(object sender, TreeViewEventArgs e)
        {
            // Zabezpieczenie przed nieskończoną pętlą rekurencji podczas masowej zmiany
            if (isUpdatingCheck) return;

            isUpdatingCheck = true;

            // Przekazujemy kliknięty węzeł i jego stan (zaznaczony/odznaczony) niżej
            CheckAllChildNodes(e.Node, e.Node.Checked);

            isUpdatingCheck = false;
        }

        // 2. Metoda rekurencyjna, która przechodzi przez wszystkie dzieci i ustawia im taki sam stan
        private void CheckAllChildNodes(TreeNode parentNode, bool isChecked)
        {
            foreach (TreeNode childNode in parentNode.Nodes)
            {
                childNode.Checked = isChecked;

                // Jeśli dziecko ma własne podfoldery, przechodzimy jeszcze niżej
                if (childNode.Nodes.Count > 0)
                {
                    CheckAllChildNodes(childNode, isChecked);
                }
            }
        }

        private int[] GetSelectedCsvRowIndices()
        {
            List<int> selectedIndices = new List<int>();
            CollectCheckedRowIndices(treeViewDB.Nodes, selectedIndices);
            return selectedIndices.ToArray();
        }

        // Pomocnicza funkcja rekurencyjna
        private void CollectCheckedRowIndices(TreeNodeCollection nodes, List<int> indices)
        {
            foreach (TreeNode node in nodes)
            {
                // Jeśli węzeł jest zaznaczony, jest liściem (nie ma dzieci) oraz posiada przypisany Tag
                if (node.Checked && node.Nodes.Count == 0 && node.Tag != null)
                {
                    if (node.Tag is int rowIndex)
                    {
                        indices.Add(rowIndex);
                    }
                }

                // Przechodzimy niżej w strukturze folderów
                if (node.Nodes.Count > 0)
                {
                    CollectCheckedRowIndices(node.Nodes, indices);
                }
            }
        }


        //_______________________________FUNKCJE Z PROGRAMU MICHAŁA__________________
        //--------------------------------------------------------------------------
       

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
        /// <summary>
        /// Główna zoptymalizowana metoda eksportująca wszystkie wybrane bloki DB do jednego pliku Excel.
        /// </summary>
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
    
        private void btnOpenEditor_Click(object sender, EventArgs e)
        {

        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }


        // Metoda inicjalizująca Timer (wywołaj w konstruktorze frontForm())


        // Obsługa pojedynczego "tyknięcia" timera
        private void RotationTimer_Tick(object sender, EventArgs e)
        {
            currentAngle = (currentAngle + 3f) % 360f;
            pictureBox1.Invalidate(); // Wymuszamy przerysowanie w każdej klatce
        }

        private void PictureBox1_Paint(object sender, PaintEventArgs e)
        {
            // Rysujemy obrócony obrazek TYLKO gdy animacja trwa
            if (rotationTimer.Enabled && originalImage != null)
            {
                Graphics g = e.Graphics;

                // Płynne wygładzanie krawędzi
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                // Wyznaczenie środka PictureBoxa
                float centerX = pictureBox1.Width / 2f;
                float centerY = pictureBox1.Height / 2f;

                g.TranslateTransform(centerX, centerY);
                g.RotateTransform(currentAngle);

                // Rysujemy obraz dostosowany rozmiarem do PictureBoxa (wyśrodkowany)
                g.DrawImage(
                    originalImage,
                    -centerX,
                    -centerY,
                    pictureBox1.Width,
                    pictureBox1.Height
                );
            }
        }

        public void ToggleLoadingAnimation()
        {
            // Zabezpieczenie: Pobieramy obrazek z PictureBoxa, jeśli jeszcze tego nie zrobiliśmy
            if (originalImage == null)
            {
                if (pictureBox1.Image == null) return; // Brak obrazu do kręcenia
                originalImage = (Image)pictureBox1.Image.Clone();
            }

            if (rotationTimer.Enabled)
            {
                // === ZATRZYMANIE KRĘCENIA ===
                rotationTimer.Stop();
                currentAngle = 0f;

                // Przywracamy zwykły Image
                pictureBox1.Image = originalImage;
                pictureBox1.Invalidate();
            }
            else
            {
                // === START KRĘCENIA ===
                // Ukrywamy statyczny Image z PictureBoxa, żeby Paint przejął rysowanie
                pictureBox1.Image = null;

                rotationTimer.Start();
                pictureBox1.Invalidate(); // Pierwsze wymuszenie rysowania
            }
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {

        }
    }   
}
