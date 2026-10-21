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
        // Licznik nadający unikalne ID każdemu węzłowi w pliku CSV (Unique ID counter for each node in the CSV file)
        private static long _idCounter = 0;

        /// <summary>
        /// Skanuje strukturę projektu TIA Portal i zapisuje drzewo obiektów do pliku CSV. (Scans the TIA Portal project structure and exports the object tree to a CSV file.)
        /// </summary>
       
        public static void ExportTiaStructureToCsv(string inputProjectPath, string NazwaCsv)
        {
            if (string.IsNullOrWhiteSpace(inputProjectPath) || !File.Exists(inputProjectPath))
            {
                Console.WriteLine($"[ERROR]: Project file does not exist at path: {inputProjectPath}");
                return;
            }

            _idCounter = 0;
            string outputCsvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NazwaCsv);

            Console.WriteLine($"[INFO] Starting processing file: {inputProjectPath}");

            TiaPortal tiaProcess = null;
            Project mainProject = null;
            bool createdNewInstance = false;

            try
            {
                // Podpięcie pod otwartą instancję TIA Portal (Attach to an active open TIA Portal instance)
                var runningProcesses = TiaPortal.GetProcesses();
                if (runningProcesses.Count > 0)
                {
                    Console.WriteLine("[INFO] Active TIA Portal instance detected. Connecting...");
                    tiaProcess = runningProcesses[0].Attach();
                    mainProject = tiaProcess.Projects.FirstOrDefault();
                }

                // Otwieranie w tle, jeśli TIA Portal nie był uruchomiony (Open in background if TIA Portal was not running)
                if (mainProject == null)
                {
                    Console.WriteLine("[INFO] Opening new TIA Portal background instance...");
                    if (tiaProcess == null)
                    {
                        tiaProcess = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                    }

                    FileInfo projectFile = new FileInfo(inputProjectPath);
                    Console.WriteLine("[INFO] Opening project file...");
                    mainProject = tiaProcess.Projects.Open(projectFile);
                    createdNewInstance = true;
                }

                Console.WriteLine("[INFO] Scanning project structure and exporting to CSV...");

                using (StreamWriter writer = new StreamWriter(outputCsvPath, false, Encoding.UTF8, 65536))
                {
                    writer.WriteLine("Id;ParentId;ProjectName;DeviceName;NodeType;Name;FullPath");
                    ScanDevicesInProject(mainProject, writer);
                    writer.Flush();
                }

                Console.WriteLine("\n[SUCCESS] Data retrieval completed successfully!");
                Console.WriteLine($"[SUCCESS] CSV file saved at: {outputCsvPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[ERROR]: An exception occurred during processing: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                if (createdNewInstance)
                {
                    Console.WriteLine("[INFO] Closing project and TIA instance...");
                    mainProject?.Close();
                    tiaProcess?.Dispose();
                }
            }
        }

        #region Metody Pomocnicze Skanowania TIA / TIA Scanning Helper Methods

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