namespace DiskBackupRestoreApp
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.ListBox lstDisks;
        private System.Windows.Forms.Button btnRefreshDisks;
        private System.Windows.Forms.Button btnBackup;
        private System.Windows.Forms.Button btnRestore;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblProgress;
        private System.Windows.Forms.TextBox txtBackupFile;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            lstDisks = new ListBox();
            btnRefreshDisks = new Button();
            btnBackup = new Button();
            btnRestore = new Button();
            btnStop = new Button();
            progressBar = new ProgressBar();
            lblProgress = new Label();
            txtBackupFile = new TextBox();
            label2 = new Label();
            linkLabel1 = new LinkLabel();
            SuspendLayout();
            // 
            // lstDisks
            // 
            resources.ApplyResources(lstDisks, "lstDisks");
            lstDisks.FormattingEnabled = true;
            lstDisks.Name = "lstDisks";
            // 
            // btnRefreshDisks
            // 
            resources.ApplyResources(btnRefreshDisks, "btnRefreshDisks");
            btnRefreshDisks.Name = "btnRefreshDisks";
            btnRefreshDisks.UseVisualStyleBackColor = true;
            btnRefreshDisks.Click += btnRefreshDisks_Click;
            // 
            // btnBackup
            // 
            resources.ApplyResources(btnBackup, "btnBackup");
            btnBackup.Name = "btnBackup";
            btnBackup.UseVisualStyleBackColor = true;
            btnBackup.Click += btnBackup_Click;
            // 
            // btnRestore
            // 
            resources.ApplyResources(btnRestore, "btnRestore");
            btnRestore.Name = "btnRestore";
            btnRestore.UseVisualStyleBackColor = true;
            btnRestore.Click += btnRestore_Click;
            // 
            // btnStop
            // 
            resources.ApplyResources(btnStop, "btnStop");
            btnStop.Name = "btnStop";
            btnStop.UseVisualStyleBackColor = true;
            btnStop.Click += btnStop_Click;
            // 
            // progressBar
            // 
            resources.ApplyResources(progressBar, "progressBar");
            progressBar.Name = "progressBar";
            // 
            // lblProgress
            // 
            resources.ApplyResources(lblProgress, "lblProgress");
            lblProgress.Name = "lblProgress";
            // 
            // txtBackupFile
            // 
            resources.ApplyResources(txtBackupFile, "txtBackupFile");
            txtBackupFile.Name = "txtBackupFile";
            // 
            // label2
            // 
            resources.ApplyResources(label2, "label2");
            label2.Name = "label2";
            // 
            // linkLabel1
            // 
            resources.ApplyResources(linkLabel1, "linkLabel1");
            linkLabel1.Name = "linkLabel1";
            linkLabel1.TabStop = true;
            linkLabel1.LinkClicked += linkLabel1_LinkClicked;
            // 
            // Form1
            // 
            resources.ApplyResources(this, "$this");
            BackColor = SystemColors.ActiveCaption;
            Controls.Add(linkLabel1);
            Controls.Add(label2);
            Controls.Add(txtBackupFile);
            Controls.Add(lblProgress);
            Controls.Add(progressBar);
            Controls.Add(btnStop);
            Controls.Add(btnRestore);
            Controls.Add(btnBackup);
            Controls.Add(btnRefreshDisks);
            Controls.Add(lstDisks);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Name = "Form1";
            SizeGripStyle = SizeGripStyle.Hide;
            ResumeLayout(false);
            PerformLayout();
        }

        private Label label2;
        private LinkLabel linkLabel1;
    }
}
