//770, 820 SIZE!!
using ClosedXML.Excel;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.Multiuser;
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


namespace Primexcel
{

    public partial class frontForm : Form
    {
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

        private async void btnBrowse(object sender, EventArgs e)
        {
            string selectedPath = string.Empty;

            // 1. Dajemy użytkownikowi wybór: plik projektu (.ap* / .als*) lub folder multiprojektu
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Select TIA Portal project or multi-project";
                openFileDialog.Filter = "All TIA Projects (*.ap*;*.als*)|*.ap*;*.als*|" +
                                        "Single Projects (*.ap*)|*.ap*|" +
                                        "Multi-Projects (*.als*)|*.als*|" +
                                        "All files (*.*)|*.*";
                openFileDialog.FilterIndex = 1;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    selectedPath = openFileDialog.FileName;
                }
                else
                {
                    return; // Użytkownik anulował wybór
                }
            }

            textBox1.Text = selectedPath;
            NazwaCsvDir = "lista_ster_bloki_DB.csv";

            ToggleLoadingAnimation();
            searchBtn.Enabled = false;

            try
            {
                // 2. Eksport struktury w tle bez zamrażania interfejsu
                await Task.Run(() =>
                {
                    TiaPortalService.ExportTiaStructureToCsv(selectedPath, NazwaCsvDir);
                });

                // 3. Wczytanie wygenerowanego CSV do drzewka
                string fullCsvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NazwaCsvDir);
                if (File.Exists(fullCsvPath))
                {
                    LoadDataBlocksToTree(fullCsvPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during TIA Portal detection:\n{ex.Message}",
                        "Openness Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ToggleLoadingAnimation();
                searchBtn.Enabled = true;
            }
        }
        private async void button1_Click(object sender, EventArgs e)
        {
            button1.Enabled = false;

            try
            {
                string detectedProjectPath = string.Empty;
                string projectName = string.Empty;

                await Task.Run(() =>
                {
                    var runningProcesses = TiaPortal.GetProcesses();

                    if (runningProcesses.Count == 0)
                    {
                        throw new Exception("Nie znaleziono uruchomionej instancji TIA Portal!");
                    }

                    TiaPortal runningTiaProcess = runningProcesses[0].Attach();

                    // 1. Sprawdzamy standardowy projekt Single-User
                    Project singleUserProject = runningTiaProcess.Projects.FirstOrDefault();

                    if (singleUserProject != null)
                    {
                        detectedProjectPath = singleUserProject.Path.FullName;
                        projectName = singleUserProject.Name;
                    }
                    // 2. Jeśli brak Single-User, sprawdzamy otwartą sesję Multiuser
                    else if (runningTiaProcess.LocalSessions.Count > 0)
                    {
                        var localSession = runningTiaProcess.LocalSessions.FirstOrDefault();
                        if (localSession != null && localSession.Project != null)
                        {
                            // Właściwe wyciągnięcie obiektu MultiuserProject
                            var multiuserProject = localSession.Project;

                            detectedProjectPath = multiuserProject.Path.FullName;
                            projectName = multiuserProject.Name;
                        }
                    }

                    if (string.IsNullOrEmpty(detectedProjectPath))
                    {
                        throw new Exception("Brak otwartego projektu lub sesji lokalnej w wykrytej instancji TIA Portal.");
                    }
                });

                // Wpisywanie danych do kontrolerów UI
                textBox1.Text = detectedProjectPath;

                MessageBox.Show($"Pomyślnie połączono z projektem TIA Portal:\n{projectName}\n\nŚcieżka: {detectedProjectPath}",
                                "TIA Openness Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                NazwaCsvDir = "lista_ster_bloki_DB.csv";
                ToggleLoadingAnimation();

                // 4. Skanowanie i generowanie pliku CSV
                await Task.Run(() =>
                {
                    TiaPortalService.ExportTiaStructureToCsv(detectedProjectPath, NazwaCsvDir);
                });

                string fullCsvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NazwaCsvDir);
                if (File.Exists(fullCsvPath))
                {
                    LoadDataBlocksToTree(fullCsvPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd podczas detekcji TIA Portal:\n{ex.Message}",
                                "Openness Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ToggleLoadingAnimation();
                button1.Enabled = true;
            }
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
        private async void btnProcessSelected_Click(object sender, EventArgs e)
        {
            mySelectedIndex = GetSelectedCsvRowIndices();

            if (mySelectedIndex == null || mySelectedIndex.Length == 0)
            {
                MessageBox.Show("Select at least one DB block from the tree view!",
                                "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 1. Zabezpieczenie przed wartością null dla pliku CSV
            if (string.IsNullOrEmpty(NazwaCsvDir))
            {
                NazwaCsvDir = "lista_ster_bloki_DB.csv";
            }

            // 2. Konfiguracja SaveFileDialog z domyślnym rozszerzeniem .xlsm
            string selectedExcelPath = string.Empty;
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Title = "Select directory and filename for Excel export";
                saveFileDialog.Filter = "Excel Macro-Enabled Workbook (*.xlsm)|*.xlsm|Excel Workbook (*.xlsx)|*.xlsx|All Files (*.*)|*.*";
                saveFileDialog.FilterIndex = 1;
                saveFileDialog.DefaultExt = "xlsm";
                saveFileDialog.RestoreDirectory = true;

                string rawProjectName = Path.GetFileNameWithoutExtension(textBox1.Text.Trim());
                string safeProjectName = !string.IsNullOrWhiteSpace(rawProjectName) ? rawProjectName : "Export_DB";
                saveFileDialog.FileName = $"{safeProjectName}_DB_{DateTime.Now:dd.MM.yyyy_HH-mm}.xlsm";

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    selectedExcelPath = saveFileDialog.FileName;
                }
                else
                {
                    return; // Użytkownik anulował wybór
                }
            }

            // 3. Sprawdzenie istniejących plików
            string csvFullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NazwaCsvDir);
            if (!File.Exists(csvFullPath))
            {
                MessageBox.Show($"CSV map file not found at:\n{csvFullPath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string projectPath = textBox1.Text.Trim();
            if (!File.Exists(projectPath) && !Directory.Exists(projectPath))
            {
                MessageBox.Show("Specified TIA Portal project file does not exist!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 4. Pobranie targetów z serwisu
            var targetsToExport = ExcelExporterService.BuildTargetsFromCsv(csvFullPath, mySelectedIndex);
            if (targetsToExport.Count == 0)
            {
                MessageBox.Show("Failed to parse paths from selected CSV rows.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 5. Wykonanie zadania w tle
            ToggleLoadingAnimation();
            button2.Enabled = false;

            try
            {
                await Task.Run(() =>
                {
                    var processes = TiaPortal.GetProcesses();
                    object targetProject = null;

                    if (processes.Count > 0)
                    {
                        tiaPortal = processes[0].Attach();

                        // 1. Sprawdzamy projekt Single-User
                        targetProject = tiaPortal.Projects.FirstOrDefault();

                        // 2. Jeśli brak Single-User, pobieramy projekt z sesji Multiuser
                        if (targetProject == null && tiaPortal.LocalSessions.Count > 0)
                        {
                            var localSession = tiaPortal.LocalSessions.FirstOrDefault();
                            if (localSession != null)
                            {
                                targetProject = localSession.Project; // Zwraca MultiuserProject
                            }
                        }

                        shouldDisposeTia = false;
                    }
                    else
                    {
                        tiaPortal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                        targetProject = tiaPortal.Projects.Open(new FileInfo(projectPath));
                        shouldDisposeTia = true;
                    }

                    if (targetProject == null)
                    {
                        throw new Exception("Unable to acquire TIA Portal project reference (neither Single-User project nor Multiuser LocalSession was found).");
                    }

                    // Eksport do pliku Excel z przekazaniem obiektu jako dynamic / object
                    ExcelExporterService.ExportSelectedDbsToExcel(targetProject, selectedExcelPath, targetsToExport);
                });

                MessageBox.Show($"Selected DB blocks exported successfully!\n\nFile saved at:\n{selectedExcelPath}",
                                "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"[CRITICAL ERROR]: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
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
                MessageBox.Show($"Error while building tree structure:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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


        #region Assembly Resolver //CZY TO MA JESZCZE SENS? 0 - REFERENCJI
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


        private async void btnImportSelected_Click(object sender, EventArgs e)
        {
            // 1. Wybór pliku Excel przez użytkownika
            string excelFilePath = string.Empty;

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Select Excel File to Import";
                openFileDialog.Filter = "Excel Files (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|All Files (*.*)|*.*";
                openFileDialog.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    excelFilePath = openFileDialog.FileName;
                }
                else
                {
                    return; // Użytkownik anulował
                }
            }

            // 2. Pobranie zaznaczonych elementów z drzewka
            mySelectedIndex = GetSelectedCsvRowIndices();

            if (mySelectedIndex == null || mySelectedIndex.Length == 0)
            {
                MessageBox.Show("No items selected in the tree view!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 3. Walidacja pliku CSV z mapą zagnieżdżeń
            if (string.IsNullOrEmpty(NazwaCsvDir))
            {
                NazwaCsvDir = "lista_ster_bloki_DB.csv";
            }

            string csvFullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NazwaCsvDir);
            if (!File.Exists(csvFullPath))
            {
                MessageBox.Show($"CSV file not found at:\n{csvFullPath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 4. Odczyt linii z CSV i wyciągnięcie celów do importu
            string[] lines = File.ReadAllLines(csvFullPath);
            List<(string PlcName, string DbPath)> targetsToImport = new List<(string, string)>();

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

                    targetsToImport.Add((targetPlcName, targetDbPath));
                }
            }

            if (targetsToImport.Count == 0)
            {
                MessageBox.Show("Failed to parse paths from selected CSV rows.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 5. Wykonanie importu w tle (Task.Run) z blokadą interfejsu
            ToggleLoadingAnimation();
            button2.Enabled = false;

            try
            {
                await Task.Run(() =>
                {
                    var processes = TiaPortal.GetProcesses();
                    object targetProject = null;

                    if (processes.Count > 0)
                    {
                        tiaPortal = processes[0].Attach();

                        // 1. Sprawdzamy projekt Single-User
                        targetProject = tiaPortal.Projects.FirstOrDefault();

                        // 2. Jeśli brak Single-User, pobieramy projekt z sesji Multiuser
                        if (targetProject == null && tiaPortal.LocalSessions.Count > 0)
                        {
                            var localSession = tiaPortal.LocalSessions.FirstOrDefault();
                            if (localSession != null)
                            {
                                targetProject = localSession.Project;
                            }
                        }

                        shouldDisposeTia = false;
                    }
                    else
                    {
                        string projectPath = textBox1.Text.Trim();
                        tiaPortal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                        targetProject = tiaPortal.Projects.Open(new FileInfo(projectPath));
                        shouldDisposeTia = true;
                    }

                    if (targetProject == null)
                    {
                        throw new Exception("Unable to acquire TIA Portal project reference (neither Single-User project nor Multiuser LocalSession was found).");
                    }

                    // Wywołanie usługi importu z Excela do TIA Portal z obiektem typu dynamic
                    ExcelImporterService.ImportSelectedDbsFromExcel(targetProject, excelFilePath, targetsToImport);
                });

                MessageBox.Show("Selected DB blocks imported successfully from Excel!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"[CRITICAL ERROR]: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
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

                ToggleLoadingAnimation();
                button2.Enabled = true;
            }
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

        private void groupBox2_Enter(object sender, EventArgs e)
        {

        }

        private void groupBox1_Enter(object sender, EventArgs e)
        {

        }
    }   
}
