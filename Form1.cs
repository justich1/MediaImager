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
using System.Threading.Tasks;
using System.Collections.Concurrent;

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
                if (!string.IsNullOrEmpty(sizeInBytes) && long.TryParse(sizeInBytes, out long sizeBytes))
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
                    int bufferSize = 256 * 1024; // Velikost bufferu snÌûena na 256 KB
                    long totalRead = 0;
                    long totalSize = diskStream.Length;

                    var emptyBuffer = new byte[bufferSize];
                    var semaphore = new SemaphoreSlim(1, 1); // AsynchronnÌ semafor

                    stopwatch.Start(); // ZaË·tek mÏ¯enÌ Ëasu z·lohov·nÌ

                    while (totalRead < totalSize)
                    {
                        if (token.IsCancellationRequested)
                        {
                            MessageBox.Show("Z·lohov·nÌ bylo zruöeno.");
                            return;
                        }

                        byte[] buffer = new byte[bufferSize];
                        int bytesRead = await diskStream.ReadAsync(buffer, 0, bufferSize, token);
                        if (bytesRead == 0) break;

                        bool isEmpty = buffer.All(b => b == 0);

                        await semaphore.WaitAsync(); // PoËk· na semafor
                        try
                        {
                            if (isEmpty)
                            {
                                // Pokud je buffer pr·zdn˝, zapÌöeme pr·zdn˝ blok
                                await compressedStream.WriteAsync(emptyBuffer, 0, bufferSize, token);
                            }
                            else
                            {
                                // Pokud buffer nenÌ pr·zdn˝, zapÌöeme skuteËn· data
                                await compressedStream.WriteAsync(buffer, 0, bytesRead, token);
                            }
                        }
                        finally
                        {
                            semaphore.Release(); // UvolnÌ semafor
                        }

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
                btnBackup.Enabled = true;
                btnRestore.Enabled = true;
                btnRefreshDisks.Enabled = true;
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

        private async Task RestoreDiskData(string diskInfo, string backupFilePath, CancellationToken token)
        {
            string tempFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_restore_file.bin");

            try
            {
                string devicePath = ExtractDevicePathFromInfo(diskInfo);

                if (string.IsNullOrEmpty(devicePath))
                {
                    throw new ArgumentNullException(nameof(devicePath), "Cesta za¯ÌzenÌ nem˘ûe b˝t null nebo pr·zdn·.");
                }

                string diskNumber = RetrieveDiskNumber(diskInfo);

                if (string.IsNullOrEmpty(diskNumber))
                {
                    throw new ArgumentException("Cesta za¯ÌzenÌ nenÌ spr·vn·.");
                }

                string deviceName = $"\\\\.\\PhysicalDrive{diskNumber}";
                IntPtr hDevice = CreateFile(deviceName, FileAccess.ReadWrite, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, FileAttributes.Normal, IntPtr.Zero);

                if (hDevice == IntPtr.Zero || hDevice == new IntPtr(-1))
                {
                    MessageBox.Show("Nepoda¯ilo se otev¯Ìt za¯ÌzenÌ.");
                    return;
                }

                // NastavenÌ disku jako offline
                if (!SetDiskOffline(hDevice))
                {
                    MessageBox.Show("Nepoda¯ilo se nastavit disk jako offline.");
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
                        // Velikost bufferu: 256 KB
                        int bufferSize = 256 * 1024;
                        long totalRead = 0;
                        long totalSize = backupStream.Length;

                        while (totalRead < totalSize)
                        {
                            if (token.IsCancellationRequested)
                            {
                                MessageBox.Show("Obnova byla zruöena.");
                                return;
                            }

                            byte[] buffer = new byte[bufferSize];
                            int bytesRead = await decompressedStream.ReadAsync(buffer, 0, bufferSize, token);
                            if (bytesRead == 0) break;

                            // ZapÌöeme data do doËasnÈho souboru
                            await tempFileStream.WriteAsync(buffer, 0, bytesRead, token);

                            totalRead += bytesRead;

                            int progress = (int)((double)totalRead / totalSize * 100);
                            UpdateProgressBar(progress, totalSize - totalRead, "DekompresnÌ f·ze");
                            Debug.WriteLine($"DekompresnÌ f·ze: »tenÌ: {totalRead}/{totalSize}, Zb˝v·: {totalSize - totalRead}");
                        }
                    }

                    if (token.IsCancellationRequested)
                    {
                        MessageBox.Show("Obnova byla zruöena.");
                        return;
                    }

                    Debug.WriteLine("ZaË·tek f·ze z·pisu.");

                    // Inicializace promÏnn˝ch pro sledov·nÌ pokroku z·pisu
                    long totalWritten = 0;
                    int writeProgress = 0;

                    using (var tempFileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
                    {
                        while (totalWritten < tempFileStream.Length)
                        {
                            if (token.IsCancellationRequested) return;

                            byte[] buffer = new byte[256 * 1024]; // Velikost bufferu 256 KB
                            int bytesRead = await tempFileStream.ReadAsync(buffer, 0, buffer.Length, token);

                            lock (this)
                            {
                                // Kontrola platnosti parametr˘ p¯ed z·pisem
                                if (hDevice == IntPtr.Zero || hDevice == new IntPtr(-1) || buffer == null || bytesRead <= 0)
                                {
                                    Debug.WriteLine("NeplatnÈ parametry pro z·pis na za¯ÌzenÌ.");
                                    continue;
                                }

                                Debug.WriteLine("Zahajuji z·pis na za¯ÌzenÌ.");
                                WriteToDevice(hDevice, buffer, bytesRead);

                                totalWritten += bytesRead;

                                // Aktualizace progress baru bÏhem z·pisu
                                writeProgress = (int)((double)totalWritten / tempFileStream.Length * 100);
                                UpdateProgressBar(writeProgress, tempFileStream.Length - totalWritten, "F·ze z·pisu");
                                Debug.WriteLine($"F·ze z·pisu: Zaps·no: {totalWritten}/{tempFileStream.Length}, Zb˝v·: {tempFileStream.Length - totalWritten}");
                            }
                        }
                    }

                    await FlushBuffersAsync(hDevice, token);
                    UpdateProgressBar(100, 0, "DokonËenÌ");

                    if (totalWritten != new FileInfo(tempFilePath).Length)
                    {
                        MessageBox.Show("Nesoulad ve velikosti z·pisu, operace obnovy nebyla ˙spÏön·.");
                    }
                }
                finally
                {
                    // NastavenÌ disku zpÏt online
                    if (!SetDiskOnline(hDevice))
                    {
                        MessageBox.Show("Nepoda¯ilo se nastavit disk jako online.");
                    }

                    CloseHandle(hDevice);

                    // Smaûeme doËasn˝ soubor po ˙spÏönÈm z·pisu
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }

                stopwatch.Stop();
                MessageBox.Show($"Obnova byla ˙spÏönÏ dokonËena. Doba trv·nÌ: {stopwatch.Elapsed}");
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show("ZamÌtnut˝ p¯Ìstup p¯i obnovÏ. UjistÏte se, ûe m·te spr·vn· opr·vnÏnÌ.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chyba p¯i obnovÏ: {ex.Message}");
                // Smaûeme doËasn˝ soubor p¯i chybÏ
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            finally
            {
                // ObnovenÌ stavu tlaËÌtek
                Invoke(new Action(() =>
                {
                    btnBackup.Enabled = true;
                    btnRestore.Enabled = true;
                    btnRefreshDisks.Enabled = true;
                }));
            }
        }


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
            uint bytesWritten;
            // P¯i¯aÔte buffer ke spravovanÈmu ukazateli
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr pBuffer = handle.AddrOfPinnedObject(); // ZÌskejte ukazatel na buffer
                if (!WriteFile(hDevice, pBuffer, (uint)bytesToWrite, out bytesWritten, IntPtr.Zero))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"Chyba p¯i z·pisu na za¯ÌzenÌ. KÛd chyby: {errorCode}");
                    return false;
                }

                return bytesWritten == bytesToWrite;
            }
            finally
            {
                handle.Free(); // UvolnÏte buffer z pamÏti
            }
        }


        private async Task FlushBuffersAsync(IntPtr hDevice, CancellationToken token)
        {
            await Task.Run(() =>
            {
                if (!FlushFileBuffers(hDevice))
                {
                    MessageBox.Show("Nepoda¯ilo se vypr·zdnit vyrovn·vacÌ pamÏù.");
                }
            }, token);
        }


        private bool SetDiskOffline(IntPtr hDevice)
        {
            uint bytesReturned;
            SET_DISK_ATTRIBUTES attributes = new SET_DISK_ATTRIBUTES
            {
                Version = (uint)Marshal.SizeOf(typeof(SET_DISK_ATTRIBUTES)),
                Persist = true,
                Attributes = DISK_ATTRIBUTE_OFFLINE,
                AttributesMask = DISK_ATTRIBUTE_OFFLINE,
                Reserved = new uint[4]
            };

            int size = Marshal.SizeOf(attributes);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(attributes, buffer, false);

            bool result = DeviceIoControl(hDevice, IOCTL_DISK_SET_DISK_ATTRIBUTES, buffer, (uint)size, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);

            Marshal.FreeHGlobal(buffer);
            return result;
        }

        private bool SetDiskOnline(IntPtr hDevice)
        {
            uint bytesReturned;
            SET_DISK_ATTRIBUTES attributes = new SET_DISK_ATTRIBUTES
            {
                Version = (uint)Marshal.SizeOf(typeof(SET_DISK_ATTRIBUTES)),
                Persist = true,
                Attributes = 0,
                AttributesMask = DISK_ATTRIBUTE_OFFLINE,
                Reserved = new uint[4]
            };

            int size = Marshal.SizeOf(attributes);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(attributes, buffer, false);

            bool result = DeviceIoControl(hDevice, IOCTL_DISK_SET_DISK_ATTRIBUTES, buffer, (uint)size, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);

            Marshal.FreeHGlobal(buffer);
            return result;
        }



        private void UpdateProgressBar(int progress, long remainingSize, string phase)
        {
            if (progressBar.InvokeRequired)
            {
                progressBar.Invoke(new Action<int, long, string>(UpdateProgressBar), progress, remainingSize, phase);
            }
            else
            {
                progressBar.Value = progress;
                lblProgress.Text = $"{phase}: Hotovo: {progress}%, Zb˝v·: {remainingSize / 1024 / 1024} MB";
            }
        }


        private string ExtractDevicePathFromInfo(string diskInfo)
        {
            var startIndex = diskInfo.IndexOf("Device ID: ") + "Device ID: ".Length;
            var endIndex = diskInfo.IndexOf(", Size:", startIndex);
            return diskInfo.Substring(startIndex, endIndex - startIndex).Trim();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://www.pc-pohotovost.eu");
        }
    }
}
