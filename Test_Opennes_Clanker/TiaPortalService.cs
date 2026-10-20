using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;

namespace Primexcel
{
    public static class TiaPortalService
    {
        // Licznik nadający unikalne ID każdemu węzłowi w pliku CSV
        private static long _idCounter = 0;

        /// <summary>
        /// Skanuje strukturę projektu TIA Portal i zapisuje drzewo obiektów do pliku CSV.
        /// </summary>
        public static void ExportTiaStructureToCsv(string inputProjectPath, string NazwaCsv)
        {
            if (string.IsNullOrWhiteSpace(inputProjectPath) || !File.Exists(inputProjectPath))
            {
                Console.WriteLine($"[BŁĄD]: Plik projektu nie istnieje pod ścieżką: {inputProjectPath}");
                return;
            }

            _idCounter = 0;
            string outputCsvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NazwaCsv);

            Console.WriteLine($"[INFO] Rozpoczynanie przetwarzania pliku: {inputProjectPath}");

            TiaPortal tiaProcess = null;
            Project mainProject = null;
            bool createdNewInstance = false;

            try
            {
                // Podpięcie pod otwartą instancję TIA Portal
                var runningProcesses = TiaPortal.GetProcesses();
                if (runningProcesses.Count > 0)
                {
                    Console.WriteLine("[INFO] Wykryto otwartą instancję TIA Portal. Podłączanie...");
                    tiaProcess = runningProcesses[0].Attach();
                    mainProject = tiaProcess.Projects.FirstOrDefault();
                }

                // Otwieranie w tle, jeśli TIA Portal nie był uruchomiony
                if (mainProject == null)
                {
                    Console.WriteLine("[INFO] Otwieranie nowej instancji TIA Portal w tle...");
                    if (tiaProcess == null)
                    {
                        tiaProcess = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                    }

                    FileInfo projectFile = new FileInfo(inputProjectPath);
                    Console.WriteLine("[INFO] Otwieranie pliku projektu...");
                    mainProject = tiaProcess.Projects.Open(projectFile);
                    createdNewInstance = true;
                }

                Console.WriteLine("[INFO] Skanowanie struktury projektu i zapis do CSV...");

                using (StreamWriter writer = new StreamWriter(outputCsvPath, false, Encoding.UTF8, 65536))
                {
                    writer.WriteLine("Id;ParentId;ProjectName;DeviceName;NodeType;Name;FullPath");
                    ScanDevicesInProject(mainProject, writer);
                    writer.Flush();
                }

                Console.WriteLine("\n[SUKCES] Zakończono pobieranie danych!");
                Console.WriteLine($"[SUKCES] Plik CSV został zapisany: {outputCsvPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[BŁĄD]: Wystąpił wyjątek podczas przetwarzania: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                if (createdNewInstance)
                {
                    Console.WriteLine("[INFO] Zamykanie projektu i instancji TIA...");
                    mainProject?.Close();
                    tiaProcess?.Dispose();
                }
            }
        }

        /// <summary>
        /// Pobiera lub dołącza do instancji TIA Portal.
        /// </summary>
        public static TiaPortal GetOrAttachTiaPortal(string projectPath, out Project project, out bool createdNewInstance)
        {
            var processes = TiaPortal.GetProcesses();
            if (processes.Count > 0)
            {
                var tia = processes[0].Attach();
                project = tia.Projects.FirstOrDefault();
                createdNewInstance = false;
                return tia;
            }
            else
            {
                var tia = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                project = tia.Projects.Open(new FileInfo(projectPath));
                createdNewInstance = true;
                return tia;
            }
        }

        #region Metody Pomocnicze Skanowania TIA

        private static void ScanDevicesInProject(Project project, StreamWriter writer)
        {
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

                    long deviceNodeId = ++_idCounter;
                    string devicePath = $"{projectPath}/{plcName}";
                    WriteCsvLine(writer, deviceNodeId, projectId, project.Name, plcName, "Device", plcName, devicePath);

                    PlcBlockGroup rootBlockGroup = plcSoftware.BlockGroup;
                    long rootFolderId = ++_idCounter;
                    string rootPath = $"{devicePath}/{rootBlockGroup.Name}";

                    WriteCsvLine(writer, rootFolderId, deviceNodeId, project.Name, plcName, "Folder", rootBlockGroup.Name, rootPath);

                    TraverseBlockGroup(rootBlockGroup, project.Name, plcName, rootFolderId, rootPath, writer);
                }
            }
        }

        private static void TraverseBlockGroup(PlcBlockGroup currentGroup, string projectName, string deviceName, long parentFolderId, string currentPath, StreamWriter writer)
        {
            foreach (PlcBlock block in currentGroup.Blocks)
            {
                long blockId = ++_idCounter;
                string blockPath = $"{currentPath}/{block.Name}";
                WriteCsvLine(writer, blockId, parentFolderId, projectName, deviceName, "Block", block.Name, blockPath);
            }

            foreach (PlcBlockUserGroup subGroup in currentGroup.Groups)
            {
                long subFolderId = ++_idCounter;
                string subFolderPath = $"{currentPath}/{subGroup.Name}";
                WriteCsvLine(writer, subFolderId, parentFolderId, projectName, deviceName, "Folder", subGroup.Name, subFolderPath);
                TraverseBlockGroup(subGroup, projectName, deviceName, subFolderId, subFolderPath, writer);
            }
        }

        public static List<PlcSoftware> GetPlcSoftwareFromDevice(Device device)
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
    }
}