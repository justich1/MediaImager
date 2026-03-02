using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Security.Principal;
using System.Text.RegularExpressions;
// using System.Threading.Tasks; // DuplicitnÌ using
using System.Collections.Concurrent;
// using System.Runtime.InteropServices; // DuplicitnÌ using

namespace DiskBackupRestoreApp
{
    public partial class Form1 : Form
    {
        private CancellationTokenSource _cancellationTokenSource;
        private Task _backupRestoreTask;

        public Form1()
        {
            InitializeComponent();
            LoadDisks();
        }

        // NaËÌst disky
        private void LoadDisks()
        {
            lstDisks.Items.Clear();
            var disks = GetDisks();

            foreach (var disk in disks)
            {
                lstDisks.Items.Add(disk);
            }
        }
        private void btnRefreshDisks_Click(object sender, EventArgs e)
        {
            lstDisks.Items.Clear();
            var disks = GetDisks();

            foreach (var disk in disks)
            {
                lstDisks.Items.Add(disk);
            }
        }

        // V˝bÏr souboru pro z·lohu nebo obnovu
        private string SelectFile(bool isBackup)
        {
            if (isBackup)
            {
                using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.Filter = "Komprimovan˝ obraz disku (*.kod)|*.kod";
                    saveFileDialog.Title = "Vyberte umÌstÏnÌ pro z·lohu";
                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        return saveFileDialog.FileName;
                    }
                }
            }
            else
            {
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = "Komprimovan˝ obraz disku (*.kod)|*.kod";
                    openFileDialog.Title = "Vyberte soubor pro obnovu";
                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        return openFileDialog.FileName;
                    }
                }
            }
            return null;
        }



        // Z·lohov·nÌ
        private void btnBackup_Click(object sender, EventArgs e)
        {
            if (lstDisks.SelectedItem == null)
            {
                MessageBox.Show("Vyberte disk pro z·lohu.", "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string filePath = SelectFile(true);
            if (string.IsNullOrEmpty(filePath))
            {
                MessageBox.Show("Nebyla vybr·na û·dn· cesta pro z·lohu.", "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            txtBackupFile.Text = filePath;
            StartBackup(filePath);
        }

        // Obnova
        private void btnRestore_Click(object sender, EventArgs e)
        {
            if (lstDisks.SelectedItem == null)
            {
                MessageBox.Show("Vyberte disk pro obnovu.", "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string filePath = SelectFile(false);
            if (string.IsNullOrEmpty(filePath))
            {
                MessageBox.Show("Nebyl vybr·n û·dn˝ soubor pro obnovu.", "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            txtBackupFile.Text = filePath;
            StartRestoreProcess(filePath);
        }



        // ZastavenÌ procesu
        private void btnStop_Click(object sender, EventArgs e)
        {
            _cancellationTokenSource?.Cancel();
        }

        // ZÌsk·nÌ disk˘ p¯es WMI
        private List<string> GetDisks()
        {
            List<string> disks = new List<string>();
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");

            foreach (ManagementObject disk in searcher.Get())
            {
                string deviceID = disk["DeviceID"].ToString();
                string model = disk["Model"].ToString();
                string sizeInBytes = disk["Size"]?.ToString();

                string sizeInGB = "Unknown";
                if (!string.IsNullOrEmpty(sizeInBytes) && ulong.TryParse(sizeInBytes, out ulong sizeBytes)) // WMI vracÌ ulong pro 'Size'
                {
                    sizeInGB = $"{sizeBytes / (1024.0 * 1024 * 1024):F2} GB";
                }

                disks.Add($"Model: {model}, Device ID: {deviceID}, Size: {sizeInGB}");
            }

            return disks;
        }

        // Zah·jenÌ z·lohy
        private void StartBackup(string filePath)
        {
            if (lstDisks.SelectedItem == null)
            {
                MessageBox.Show("Vyberte disk pro z·lohu.");
                return;
            }

            string diskInfo = lstDisks.SelectedItem.ToString();
            _cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = _cancellationTokenSource.Token;

            btnBackup.Enabled = false;
            btnRestore.Enabled = false;
            btnRefreshDisks.Enabled = false;

            _backupRestoreTask = Task.Run(() => BackupDisk(diskInfo, filePath, token));
        }

        private async Task BackupDisk(string diskInfo, string backupFilePath, CancellationToken token)
        {
            var stopwatch = new Stopwatch();
            try
            {
                string devicePath = ExtractDevicePathFromInfo(diskInfo);
                using (var diskStream = new FileStream(devicePath, FileMode.Open, FileAccess.Read))
                using (var backupStream = new FileStream(backupFilePath, FileMode.Create, FileAccess.Write))
                using (var compressedStream = new System.IO.Compression.GZipStream(backupStream, System.IO.Compression.CompressionMode.Compress))
                {
                    int bufferSize = 256 * 1024; // Velikost bufferu 256 KB
                    long totalRead = 0;
                    long totalSize = diskStream.Length; // ZÌsk·me skuteËnou velikost disku

                    // OPRAVA 1: Buffer musÌ b˝t definov·n VNÃ smyËky.
                    // Vytv·¯enÌ bufferu uvnit¯ smyËky zp˘sobovalo extrÈmnÌ z·tÏû na GC (Garbage Collector)
                    // a drasticky sniûovalo v˝kon.
                    byte[] buffer = new byte[bufferSize];

                    // OPRAVA 2: OdstranÏna 'isEmpty' logika a 'semaphore'.
                    // Logika 'isEmpty' byla chybn· (kontrolovala cel˝ buffer, nejen 'bytesRead') a
                    // byla zbyteËn· - GZip si s nulov˝mi bloky poradÌ s·m velmi efektivnÏ.
                    // Semaphore zde nebyl pot¯eba.

                    stopwatch.Start(); // ZaË·tek mÏ¯enÌ Ëasu z·lohov·nÌ

                    while (totalRead < totalSize)
                    {
                        if (token.IsCancellationRequested)
                        {
                            MessageBox.Show("Z·lohov·nÌ bylo zruöeno.");
                            return;
                        }

                        // »teme do jiû existujÌcÌho bufferu
                        int bytesRead = await diskStream.ReadAsync(buffer, 0, bufferSize, token);
                        if (bytesRead == 0) break; // Konec streamu

                        // P¯Ìmo zapÌöeme p¯eËten· data (GZip si s nulami poradÌ)
                        await compressedStream.WriteAsync(buffer, 0, bytesRead, token);

                        totalRead += bytesRead;

                        int progress = (int)((double)totalRead / totalSize * 100);
                        UpdateProgressBar(progress, totalSize - totalRead, "Komprese");
                        Debug.WriteLine($"»tenÌ: {totalRead}/{totalSize}, Zb˝v·: {totalSize - totalRead}");
                    }

                    await compressedStream.FlushAsync(token);
                    UpdateProgressBar(100, 0, "DokonËenÌ");
                    stopwatch.Stop(); // Konec mÏ¯enÌ Ëasu z·lohov·nÌ

                    MessageBox.Show($"Z·loha byla ˙spÏönÏ dokonËena. Doba trv·nÌ: {stopwatch.Elapsed}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chyba p¯i z·lohov·nÌ: {ex.Message}");
            }
            finally
            {
                // UI operace musÌ bÏûet v hlavnÌm vl·knÏ
                Invoke(new Action(() => {
                    btnBackup.Enabled = true;
                    btnRestore.Enabled = true;
                    btnRefreshDisks.Enabled = true;
                }));
            }
        }




        // Zah·jenÌ obnovy
        private void StartRestoreProcess(string filePath)
        {
            if (lstDisks.SelectedItem == null)
            {
                MessageBox.Show("Vyberte disk pro obnovu.");
                return;
            }

            string diskInfo = lstDisks.SelectedItem.ToString();
            _cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = _cancellationTokenSource.Token;

            btnBackup.Enabled = false;
            btnRestore.Enabled = false;
            btnRefreshDisks.Enabled = false;

            _backupRestoreTask = Task.Run(() => RestoreDiskData(diskInfo, filePath, token));
        }

        // --- P/Invoke definice pro p¯Ìm˝ p¯Ìstup k disku ---
        // (Z˘st·vajÌ, jak byly, kromÏ GetDiskSize)

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateFile(
            string lpFileName,
            [MarshalAs(UnmanagedType.U4)] FileAccess dwDesiredAccess,
            [MarshalAs(UnmanagedType.U4)] FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            [MarshalAs(UnmanagedType.U4)] FileMode dwCreationDisposition,
            [MarshalAs(UnmanagedType.U4)] FileAttributes dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FlushFileBuffers(IntPtr hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(
            IntPtr hFile,
            IntPtr lpBuffer,
            uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten,
            IntPtr lpOverlapped);

        const uint VOLUME_LOCK = 0x00090018;
        const uint VOLUME_DISMOUNT = 0x00090020;
        const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
        const uint FSCTL_LOCK_VOLUME = 0x00090018;

        [StructLayout(LayoutKind.Sequential)]
        public struct SET_DISK_ATTRIBUTES
        {
            public uint Version;
            public bool Persist;
            public ulong Attributes;
            public ulong AttributesMask;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] Reserved;
        }

        const uint IOCTL_DISK_SET_DISK_ATTRIBUTES = 0x7c0f4;
        const uint IOCTL_DISK_GET_DISK_ATTRIBUTES = 0x700f0;
        const ulong DISK_ATTRIBUTE_OFFLINE = 0x0000000000000001;
        private SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1); // Semafor pro ¯ÌzenÌ p¯Ìstupu k z·pisu

        private async Task RestoreDiskData(string diskInfo, string backupFilePath, CancellationToken token)
        {
            string tempFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_restore_file.bin");

            try
            {
                string devicePath = ExtractDevicePathFromInfo(diskInfo);
                if (string.IsNullOrEmpty(devicePath))
                    throw new ArgumentNullException(nameof(devicePath), "Cesta za¯ÌzenÌ nem˘ûe b˝t null nebo pr·zdn·.");

                // P˘vodnÌ kÛd na RetrieveDiskNumber byl v po¯·dku, ale m˘ûeme to zjednoduöit.
                // devicePath je jiû \\.\PHYSICALDRIVEX
                string deviceName = devicePath; // Nap¯. \\.\PHYSICALDRIVE0

                IntPtr hDevice = CreateFile(deviceName, FileAccess.ReadWrite, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, FileAttributes.Normal, IntPtr.Zero);
                if (hDevice == IntPtr.Zero || hDevice == new IntPtr(-1))
                {
                    int error = Marshal.GetLastWin32Error();
                    MessageBox.Show($"Nepoda¯ilo se otev¯Ìt za¯ÌzenÌ. KÛd chyby: {error}. SpouötÌte program jako administr·tor?");
                    return;
                }

                if (!SetDiskOffline(hDevice))
                {
                    MessageBox.Show("Nepoda¯ilo se nastavit disk jako offline. (Moûn· je pouûÌv·n?)");
                    CloseHandle(hDevice);
                    return;
                }

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                try
                {
                    using (var backupStream = new FileStream(backupFilePath, FileMode.Open, FileAccess.Read))
                    using (var decompressedStream = new System.IO.Compression.GZipStream(backupStream, System.IO.Compression.CompressionMode.Decompress))
                    using (var tempFileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                    {
                        int bufferSize = 256 * 1024;
                        long totalRead = 0;
                        // Nezn·me dekomprimovanou velikost, ale m˘ûeme sledovat komprimovanou
                        long totalSizeCompressed = backupStream.Length;

                        while (true) // »teme, dokud jsou data
                        {
                            if (token.IsCancellationRequested)
                            {
                                MessageBox.Show("Obnova byla zruöena.");
                                return;
                            }

                            byte[] buffer = new byte[bufferSize];
                            int bytesRead = await decompressedStream.ReadAsync(buffer, 0, bufferSize, token);
                            if (bytesRead == 0) break;

                            await tempFileStream.WriteAsync(buffer, 0, bytesRead, token);

                            // Aktualizace progress baru na z·kladÏ pozice v komprimovanÈm souboru
                            totalRead = backupStream.Position;
                            int progress = (int)((double)totalRead / totalSizeCompressed * 100);
                            UpdateProgressBar(progress, totalSizeCompressed - totalRead, "DekompresnÌ f·ze");
                        }
                    }

                    if (token.IsCancellationRequested)
                    {
                        MessageBox.Show("Obnova byla zruöena.");
                        return;
                    }

                    long totalWritten = 0;
                    using (var tempFileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
                    {
                        long totalTempSize = tempFileStream.Length;
                        while (totalWritten < totalTempSize)
                        {
                            if (token.IsCancellationRequested) return;

                            byte[] buffer = new byte[512 * 1024]; // Buffer pro z·pis
                            int bytesRead = await tempFileStream.ReadAsync(buffer, 0, buffer.Length, token);
                            if (bytesRead == 0) break;

                            await _writeLock.WaitAsync(token); // ZajiötÏnÌ bezpeËnÈho p¯Ìstupu k z·pisu
                            try
                            {
                                if (hDevice == IntPtr.Zero || hDevice == new IntPtr(-1))
                                    continue;

                                WriteToDevice(hDevice, buffer, bytesRead);
                                // await FlushBuffersAsync(hDevice, token); // Flush po kaûdÈm z·pisu m˘ûe b˝t pomal˝
                                totalWritten += bytesRead;
                                int progress = (int)((double)totalWritten / totalTempSize * 100);
                                UpdateProgressBar(progress, totalTempSize - totalWritten, "F·ze z·pisu");
                            }
                            finally
                            {
                                _writeLock.Release(); // UvolnÏnÌ z·mku
                            }
                        }
                    }

                    // OPRAVA 3: PouûitÌ novÈ, spolehlivÈ metody GetDiskSizeWMI
                    // Vymaz·nÌ zb˝vajÌcÌho prostoru na disku nulami
                    long diskSize = GetDiskSizeWMI(devicePath); // ZÌsk·nÌ velikosti disku pomocÌ WMI

                    if (totalWritten < diskSize)
                    {
                        long remainingSpace = diskSize - totalWritten;
                        byte[] zeroBuffer = new byte[512 * 1024]; // Buffer pro z·pis nul
                        Array.Clear(zeroBuffer, 0, zeroBuffer.Length); // Jistota, ûe je buffer nulov˝

                        while (remainingSpace > 0)
                        {
                            if (token.IsCancellationRequested) return;

                            int bytesToWrite = (int)Math.Min(remainingSpace, zeroBuffer.Length);
                            await _writeLock.WaitAsync(token);
                            try
                            {
                                WriteToDevice(hDevice, zeroBuffer, bytesToWrite);
                                remainingSpace -= bytesToWrite;
                            }
                            finally
                            {
                                _writeLock.Release();
                            }
                        }
                    }

                    await FlushBuffersAsync(hDevice, token); // Fin·lnÌ flush
                    UpdateProgressBar(100, 0, "DokonËenÌ");

                    if (totalWritten != new FileInfo(tempFilePath).Length)
                        MessageBox.Show("UpozornÏnÌ: Velikost zapsan˝ch dat se liöÌ od velikosti temp souboru.");
                }
                finally
                {
                    if (!SetDiskOnline(hDevice))
                        MessageBox.Show("Nepoda¯ilo se nastavit disk jako online.");

                    CloseHandle(hDevice);
                    if (File.Exists(tempFilePath))
                        File.Delete(tempFilePath);
                }

                stopwatch.Stop();
                MessageBox.Show($"Obnova byla ˙spÏönÏ dokonËena. Doba trv·nÌ: {stopwatch.Elapsed}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chyba p¯i obnovÏ: {ex.Message}");
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
            }
            finally
            {
                Invoke(new Action(() => {
                    btnBackup.Enabled = true;
                    btnRestore.Enabled = true;
                    btnRefreshDisks.Enabled = true;
                }));
            }
        }

        // OPRAVA 3: NahrazenÌ nespolehlivÈ GetDiskSize (P/Invoke) za spolehlivou WMI metodu
        /// <summary>
        /// ZÌsk· p¯esnou velikost disku v bajtech pomocÌ WMI.
        /// </summary>
        /// <param name="devicePath">Cesta k disku (nap¯. \\.\PHYSICALDRIVE0)</param>
        /// <returns>Velikost disku v bajtech</returns>
        private long GetDiskSizeWMI(string devicePath)
        {
            try
            {
                // WMI dotaz hled· disk podle jeho DeviceID
                // MusÌme escapovat zpÏtn· lomÌtka pro WQL dotaz
                string wmiDevicePath = devicePath.Replace("\\", "\\\\");
                string query = $"SELECT Size FROM Win32_DiskDrive WHERE DeviceID = '{wmiDevicePath}'";
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);

                foreach (ManagementObject disk in searcher.Get())
                {
                    string sizeInBytesStr = disk["Size"]?.ToString();
                    if (!string.IsNullOrEmpty(sizeInBytesStr) && ulong.TryParse(sizeInBytesStr, out ulong sizeBytes))
                    {
                        return (long)sizeBytes;
                    }
                }

                // Pokud se disk nenaöel (coû by se nemÏlo st·t)
                throw new InvalidOperationException($"Nepoda¯ilo se najÌt velikost pro disk: {devicePath}");
            }
            catch (Exception ex)
            {
                // Zalogujeme chybu a vyvol·me v˝jimku, protoûe bez velikosti disku je obnova nebezpeËn·
                Debug.WriteLine($"Kritick· chyba v GetDiskSizeWMI: {ex.Message}");
                throw new InvalidOperationException($"Nepoda¯ilo se zÌskat velikost disku {devicePath} p¯es WMI.", ex);
            }
        }

        // P˘vodnÌ P/Invoke metody pro GetDiskSize byly odstranÏny (IOCTL_DISK_GET_DRIVE_GEOMETRY, DISK_GEOMETRY, atd.)
        // protoûe byly nespolehlivÈ a chybnÏ implementovanÈ.

        private void ExecuteDiskPartCommands(string[] commands)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo();
            processInfo.FileName = "diskpart.exe";
            processInfo.RedirectStandardInput = true;
            processInfo.UseShellExecute = false;
            processInfo.CreateNoWindow = true;

            Process process = new Process();
            process.StartInfo = processInfo;
            process.Start();

            using (StreamWriter writer = process.StandardInput)
            {
                foreach (string command in commands)
                {
                    writer.WriteLine(command);
                }
            }

            process.WaitForExit();
        }


        private string RetrieveDiskNumber(string diskInfo)
        {
            var match = Regex.Match(diskInfo, @"Device ID: \\\\.\\PHYSICALDRIVE(\d+)");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return null;
        }

        private bool WriteToDevice(IntPtr hDevice, byte[] buffer, int bytesToWrite)
        {
            if (buffer == null || buffer.Length == 0 || bytesToWrite <= 0)
            {
                Debug.WriteLine("Neplatn· velikost bufferu pro z·pis.");
                return false;
            }

            // Zarovn·nÌ nenÌ nutnÏ vyûadov·no na ˙rovni WriteFile,
            // ale je dobrÈ si b˝t vÏdom velikosti sektoru.
            // int sectorSize = 512;
            // if (bytesToWrite % sectorSize != 0)
            // {
            //     Debug.WriteLine("Varov·nÌ: Velikost z·pisu nenÌ zarovn·na na velikost sektoru!");
            // }

            uint bytesWritten;
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr pBuffer = handle.AddrOfPinnedObject();
                if (!WriteFile(hDevice, pBuffer, (uint)bytesToWrite, out bytesWritten, IntPtr.Zero))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"Chyba p¯i z·pisu na za¯ÌzenÌ. KÛd chyby: {errorCode}");
                    return false;
                }
            }
            finally
            {
                handle.Free();
            }

            // FlushFileBuffers(hDevice); // Vol·nÌ po kaûdÈm z·pisu je pomalÈ, vol·me na konci
            return bytesWritten == bytesToWrite;
        }

        private async Task FlushBuffersAsync(IntPtr hDevice, CancellationToken token)
        {
            await Task.Run(() =>
            {
                if (!FlushFileBuffers(hDevice))
                {
                    Debug.WriteLine("Chyba: Nepoda¯ilo se vypr·zdnit vyrovn·vacÌ pamÏù.");
                }
            }, token);
        }

        private bool SetDiskOffline(IntPtr hDevice)
        {
            return SetDiskAttribute(hDevice, DISK_ATTRIBUTE_OFFLINE);
        }

        private bool SetDiskOnline(IntPtr hDevice)
        {
            return SetDiskAttribute(hDevice, 0);
        }

        private bool SetDiskAttribute(IntPtr hDevice, ulong attribute)
        {
            uint bytesReturned;
            SET_DISK_ATTRIBUTES attributes = new SET_DISK_ATTRIBUTES
            {
                Version = (uint)Marshal.SizeOf(typeof(SET_DISK_ATTRIBUTES)),
                Persist = true,
                Attributes = attribute,
                AttributesMask = DISK_ATTRIBUTE_OFFLINE,
                Reserved = new uint[4]
            };

            int size = Marshal.SizeOf(attributes);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(attributes, buffer, false);
                return DeviceIoControl(hDevice, IOCTL_DISK_SET_DISK_ATTRIBUTES, buffer, (uint)size, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void UpdateProgressBar(int progress, long remainingSize, string phase)
        {
            if (progressBar.InvokeRequired)
            {
                progressBar.Invoke(new Action<int, long, string>(UpdateProgressBar), progress, remainingSize, phase);
            }
            else
            {
                // Ochrana proti hodnot·m mimo rozsah 0-100
                if (progress < 0) progress = 0;
                if (progress > 100) progress = 100;

                progressBar.Value = progress;

                // Zobrazujeme zb˝vajÌcÌ velikost v MB
                long remainingMB = remainingSize / 1024 / 1024;
                lblProgress.Text = $"{phase}: Hotovo: {progress}%, Zb˝v·: {remainingMB} MB";
            }
        }

        private string ExtractDevicePathFromInfo(string diskInfo)
        {
            // OËek·van˝ form·t: "Model: ..., Device ID: \\.\PHYSICALDRIVEX, Size: ... GB"
            var startIndex = diskInfo.IndexOf("Device ID: ") + "Device ID: ".Length;
            if (startIndex < "Device ID: ".Length) return null; // Pojistka

            var endIndex = diskInfo.IndexOf(", Size:", startIndex);
            if (endIndex == -1) return null; // Pojistka

            return diskInfo.Substring(startIndex, endIndex - startIndex).Trim();
        }


        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            // Otev¯enÌ odkazu ve v˝chozÌm prohlÌûeËi
            try
            {
                Process.Start(new ProcessStartInfo("https://www.pc-pohotovost.eu") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nepoda¯ilo se otev¯Ìt odkaz: {ex.Message}");
            }
        }
    }
}
