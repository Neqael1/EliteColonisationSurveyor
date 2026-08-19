using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace EliteColonisationSurveyor.Installer
{
    internal static class Program
    {
        private static readonly string DefaultInstallDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EDDiscovery",
            "DLL");

        [STAThread]
        private static int Main(string[] args)
        {
            string target = DefaultInstallDirectory;
            bool silent = false;

            foreach (string argument in args)
            {
                if (argument.Equals("/silent", StringComparison.OrdinalIgnoreCase))
                    silent = true;
                else if (argument.StartsWith("/target=", StringComparison.OrdinalIgnoreCase))
                    target = argument.Substring("/target=".Length).Trim('"');
            }

            if (silent)
            {
                try
                {
                    Install(target);
                    return 0;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception.Message);
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm(target));
            return 0;
        }

        internal static void Install(string target)
        {
            if (Process.GetProcessesByName("EDDiscovery").Length != 0)
                throw new InvalidOperationException("Close EDDiscovery before installing the plugin.");

            Directory.CreateDirectory(target);
            string stagingDirectory = Path.Combine(target, ".EliteColonisationSurveyor-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);
            try
            {
                // Verify that the complete payload can be extracted before touching an existing install.
                WritePayload(stagingDirectory, "EliteColonisationSurveyor.Core.dll");
                WritePayload(stagingDirectory, "EliteColonisationSurveyor.Plugin.dll");

                RemovePreviousInstall(target);
                MovePayload(stagingDirectory, target, "EliteColonisationSurveyor.Core.dll");
                MovePayload(stagingDirectory, target, "EliteColonisationSurveyor.Plugin.dll");
            }
            finally
            {
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, true);
            }
        }

        private static void RemovePreviousInstall(string target)
        {
            foreach (string file in Directory.GetFiles(target, "EliteColonisationSurveyor*.dll", SearchOption.TopDirectoryOnly))
                File.Delete(file);

            // Releases up to 0.5.0 included this interface assembly. EDDiscovery supplies its
            // own compatible copy, and leaving the old one beside the plugin can prevent load.
            string legacyInterface = Path.Combine(target, "EDDDLLInterfaces.dll");
            if (File.Exists(legacyInterface))
                File.Delete(legacyInterface);
        }

        private static void MovePayload(string sourceDirectory, string target, string fileName)
        {
            File.Move(Path.Combine(sourceDirectory, fileName), Path.Combine(target, fileName));
        }

        private static void WritePayload(string target, string fileName)
        {
            string resourceName = "Payload." + fileName;
            string destination = Path.Combine(target, fileName);

            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (source == null)
                    throw new InvalidOperationException("The installer payload is incomplete: " + fileName);

                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                    source.CopyTo(output);
            }
        }

        private sealed class InstallerForm : Form
        {
            private readonly string target;
            private readonly Button installButton;
            private readonly Label status;

            public InstallerForm(string installTarget)
            {
                target = installTarget;
                Text = "Elite Colonisation Surveyor Installer";
                ClientSize = new Size(520, 205);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;

                var title = new Label
                {
                    AutoSize = true,
                    Font = new Font(Font, FontStyle.Bold),
                    Location = new Point(20, 20),
                    Text = "Install Elite Colonisation Surveyor"
                };
                var description = new Label
                {
                    AutoSize = false,
                    Location = new Point(20, 52),
                    Size = new Size(480, 52),
                    Text = "This installs the EDDiscovery extension for the current Windows user. " +
                           "Please close EDDiscovery before continuing.\r\n\r\nDestination: " + target
                };
                installButton = new Button
                {
                    Location = new Point(315, 145),
                    Size = new Size(88, 32),
                    Text = "Install"
                };
                var cancelButton = new Button
                {
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(412, 145),
                    Size = new Size(88, 32),
                    Text = "Cancel"
                };
                status = new Label
                {
                    AutoSize = false,
                    Location = new Point(20, 116),
                    Size = new Size(480, 24)
                };

                installButton.Click += InstallClicked;
                Controls.AddRange(new Control[] { title, description, status, installButton, cancelButton });
                AcceptButton = installButton;
                CancelButton = cancelButton;
            }

            private void InstallClicked(object sender, EventArgs args)
            {
                installButton.Enabled = false;
                try
                {
                    Install(target);
                    MessageBox.Show(
                        this,
                        "Installation complete. Start EDDiscovery, approve the extension when prompted, " +
                        "then add Colonisation Surveyor from the panel selector.",
                        "Installation complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    Close();
                }
                catch (Exception exception)
                {
                    status.ForeColor = Color.DarkRed;
                    status.Text = exception.Message;
                    installButton.Enabled = true;
                }
            }
        }
    }
}
