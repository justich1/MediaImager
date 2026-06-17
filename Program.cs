using System;
using System.Windows.Forms;
using DiskBackupRestoreApp;

namespace SDCardBackupRestore
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}