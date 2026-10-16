using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DataGridViewAutoFilter;

namespace Test_Opennes_Clanker
{
    public partial class Primeditor : Form
    {
        // Przechowywanie przekazanych danych
        private string csvFilePath;
        private int[] selectedIndices;

        // Przechowywanie struktury danych i filtrowania
        private DataTable dataTable = new DataTable();
        private BindingSource bindingSource = new BindingSource();

        // Domyślny konstruktor dla Visual Studio Designer
        public Primeditor()
        {
            InitializeComponent();
        }

        // Konstruktor przyjmujący parametry z frontForm
        public Primeditor(string csvPath, int[] indices) : this()
        {
            this.csvFilePath = csvPath;
            this.selectedIndices = indices ?? new int[0];

            // WYWOŁUJEMY ŁADOWANIE BEZPOŚREDNIO W KONSTRUKTORZE:
            InitializeGridStructure();
            LoadSelectedCsvData();
        }

        private void Primeditor_Load(object sender, EventArgs e)
        {
            InitializeGridStructure();
            LoadSelectedCsvData();
        }

        // 1. Inicjalizacja kolumn w tabeli DataGridView
        private void InitializeGridStructure()
        {
            dataTable.Columns.Clear();

            // DOKŁADNIE 5 KOLUMN:
            dataTable.Columns.Add("RowIndex", typeof(int));       // 1. ID
            dataTable.Columns.Add("ColD", typeof(string));        // 2. ColD
            dataTable.Columns.Add("ColE", typeof(string));        // 3. ColE
            dataTable.Columns.Add("ColF", typeof(string));        // 4. ColF
            dataTable.Columns.Add("FullPath", typeof(string));    // 5. FullPath

            // Podpięcie pod DataGridView
            bindingSource.DataSource = dataTable;
            dgvEditor.DataSource = bindingSource;

            // Ustawienia wizualne
            dgvEditor.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvEditor.AllowUserToAddRows = false;

            if (dgvEditor.Columns["RowIndex"] != null)
            {
                dgvEditor.Columns["RowIndex"].HeaderText = "ID";
                dgvEditor.Columns["RowIndex"].ReadOnly = true;
                dgvEditor.Columns["RowIndex"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                dgvEditor.Columns["RowIndex"].Width = 50; // Węższa kolumna
            }

            if (dgvEditor.Columns["FullPath"] != null)
            {
                dgvEditor.Columns["FullPath"].ReadOnly = true;
            }
        }

        private void LoadSelectedCsvData()
        {
            if (string.IsNullOrEmpty(csvFilePath) || !File.Exists(csvFilePath))
            {
                MessageBox.Show("Ścieżka do pliku CSV jest nieprawidłowa lub plik nie istnieje.",
                                "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                dataTable.Rows.Clear();
                string[] lines = File.ReadAllLines(csvFilePath);

                HashSet<int> selectedSet = new HashSet<int>(selectedIndices);

                for (int i = 1; i < lines.Length; i++)
                {
                    if (selectedSet.Contains(i))
                    {
                        string line = lines[i];
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        string[] cols = line.Split(';');

                        if (cols.Length < 7)
                        {
                            Array.Resize(ref cols, 7);
                        }

                        // DOKŁADNIE 5 WARTOŚCI (odpowiadających 5 kolumnom wyżej):
                        object[] rowData = new object[]
                        {
                    i,                                           // 1. RowIndex (int)
                    cols[3]?.Trim('"').Trim() ?? string.Empty,   // 2. ColD
                    cols[4]?.Trim('"').Trim() ?? string.Empty,   // 3. ColE
                    cols[5]?.Trim('"').Trim() ?? string.Empty,   // 4. ColF
                    cols[6]?.Trim('"').Trim() ?? string.Empty    // 5. FullPath
                        };

                        dataTable.Rows.Add(rowData);
                    }
                }

                this.Text = $"Primeditor - Wczytano {dataTable.Rows.Count} wybranych elementów";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd podczas odczytu danych:\n{ex.Message}",
                                "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 3. Dynamiczne filtrowanie danych podczas wpisywania tekstu
        private void txtFilter_TextChanged(object sender, EventArgs e)
        {
            string filterText = txtFilter.Text.Trim().Replace("'", "''");

            if (string.IsNullOrEmpty(filterText))
            {
                bindingSource.RemoveFilter();
            }
            else
            {
                // Filtrujemy tylko po kolumnach, które istnieją w tabeli
                bindingSource.Filter = $"FullPath LIKE '%{filterText}%' OR " +
                                       $"ColD LIKE '%{filterText}%' OR " +
                                       $"ColE LIKE '%{filterText}%' OR " +
                                       $"ColF LIKE '%{filterText}%'";
            }
        }

        // 4. Pobieranie zmodyfikowanych danych (np. pod przyciskiem "Zapisz / Eksportuj")
        private void btnSave_Click(object sender, EventArgs e)
        {
            List<string> editedRows = new List<string>();

            foreach (DataRow row in dataTable.Rows)
            {
                int originalRowIndex = Convert.ToInt32(row["RowIndex"]);
                string fullPath = row["FullPath"].ToString();
                string valA = row["ColA"].ToString();

                // Tutaj masz dostęp do zmodyfikowanych wartości
                editedRows.Add($"Wiersz CSV [{originalRowIndex}]: {fullPath} | ColA: {valA}");
            }

            MessageBox.Show($"Gotowe do przetworzenia {editedRows.Count} edytowanych wierszy!",
                            "Sukces", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}