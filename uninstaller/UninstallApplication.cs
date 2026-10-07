using System.Diagnostics;
using System.Runtime.InteropServices;
using BigBlueFish.Setup;

namespace BigBlueFish.Uninstaller;

internal static class UninstallApplication
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string? destination, int flags);

    internal static Form CreateUninstallForm(string target)
    {
            var form = new Form { Text = "卸载蓝色大肥鱼", ClientSize = new Size(480, 210),
                StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, Font = new Font("Microsoft YaHei UI", 10),
                AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 3, Padding = new Padding(24) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label { Text = "准备卸载蓝色大肥鱼。勾选下方选项，可以保留模型和设置，方便以后重新安装。",
                AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 18) };
            var data = new CheckBox { Text = "保留模型和设置", Checked = false, AutoSize = true, Margin = new Padding(0, 0, 0, 22) };
            var button = new Button { Text = "卸载", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(110, 38), Padding = new Padding(12, 4, 12, 4), Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            button.Click += (_, _) =>
            {
                try
                {
                var temporary = Path.Combine(Path.GetTempPath(), "BigBlueFish-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Environment.ProcessPath!, temporary);
                var start = new ProcessStartInfo(temporary) { UseShellExecute = true, WorkingDirectory = Path.GetTempPath() };
                start.ArgumentList.Add("--uninstall-worker");
                start.ArgumentList.Add(target);
                if (!data.Checked) start.ArgumentList.Add("--remove-data");
                Process.Start(start);
                form.Close();
                }
                catch (Exception error)
                {
                    MessageBox.Show(form, error.Message, "蓝色大肥鱼卸载未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            layout.Controls.Add(label, 0, 0);
            layout.Controls.Add(data, 0, 1);
            layout.Controls.Add(button, 0, 2);
            form.Controls.Add(layout);
            return form;
    }

    internal static int Run(string[] args)
    {
        try
        {
            bool worker = args.Length > 0 && args[0] == "--uninstall-worker";
            if (args.Length > 0 && (args.Length < 2 || args[0] is not ("--uninstall" or "--uninstall-worker")))
                throw new ArgumentException("未提供安装目录。");
            var target = InstallationLifecycle.ValidateInstallation(args.Length == 0
                ? Path.GetDirectoryName(Environment.ProcessPath!)! : args[1]);
            if (worker)
            {
                InstallationLifecycle.Uninstall(target, args.Contains("--remove-data"));
                MoveFileEx(Environment.ProcessPath!, null, 4);
                MessageBox.Show("蓝色大肥鱼已卸载。", "蓝色大肥鱼", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            using var form = CreateUninstallForm(target);
            Application.Run(form);
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "蓝色大肥鱼卸载未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
