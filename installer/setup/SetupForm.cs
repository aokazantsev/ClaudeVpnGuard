using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeVpnGuard
{
    internal sealed class SetupForm : Form
    {
        private readonly Label status = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly CheckBox startup = new CheckBox();
        private readonly CheckBox preset = new CheckBox();
        private readonly Button install = new Button();
        private readonly string presetPath;

        public SetupForm()
        {
            Text = "Установка " + AppIdentity.Name + " " + AppIdentity.Version;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(560, 330);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            string candidate = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), AppSettings.PresetFileName);
            presetPath = File.Exists(candidate) ? candidate : null;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Controls.Add(layout);
            layout.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                Text = "Claude (десктоп, Claude Code, расширения редакторов) сможет выходить в сеть только через VPN. "
                    + "Выключен VPN — у Claude нет сети вообще.\r\n\r\n"
                    + "Программа ставится в " + AppIdentity.DefaultInstallDirectory + ", живёт в трее и сама берёт под защиту новые версии Claude. "
                    + "Удаление — «Параметры → Приложения»: оно снимает все правила."
            });

            preset.AutoSize = true;
            preset.MaximumSize = new Size(520, 0);
            preset.Margin = new Padding(0, 14, 0, 0);
            if (presetPath != null)
            {
                preset.Text = "Взять настройки из " + AppSettings.PresetFileName;
                preset.Checked = !AppSettings.FileExists;
            }
            else
            {
                preset.Text = "Файла " + AppSettings.PresetFileName + " рядом нет — адаптер VPN выберешь после установки";
                preset.Enabled = false;
            }
            layout.Controls.Add(preset);

            startup.AutoSize = true;
            startup.Checked = true;
            startup.Text = "Запускать при входе в Windows (рекомендуется)";
            layout.Controls.Add(startup);

            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(0, 16, 0, 4);
            layout.Controls.Add(progress);
            status.AutoSize = true;
            status.MaximumSize = new Size(520, 0);
            layout.Controls.Add(status);

            install.Text = "Установить";
            install.AutoSize = true;
            install.Anchor = AnchorStyles.Right;
            install.Click += OnInstall;
            layout.Controls.Add(install);
            AcceptButton = install;
        }

        private void OnInstall(object sender, EventArgs e)
        {
            install.Enabled = false;
            startup.Enabled = false;
            preset.Enabled = false;
            var installation = new Installation(presetPath, preset.Checked, startup.Checked, Report);
            var worker = new Thread(() =>
            {
                Exception failure = null;
                try
                {
                    installation.Run();
                }
                catch (Exception error)
                {
                    failure = error;
                }
                BeginInvoke(new Action(() => Finish(installation, failure)));
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void Report(int percent, string message)
        {
            BeginInvoke(new Action(() =>
            {
                progress.Value = Math.Max(0, Math.Min(100, percent));
                status.Text = message;
            }));
        }

        private void Finish(Installation installation, Exception failure)
        {
            if (failure != null)
            {
                status.Text = "Ошибка: " + failure.Message;
                install.Enabled = true;
                MessageBox.Show(this, failure.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string message = AppIdentity.Name + " установлен и запущен — значок-щит в трее.";
            if (installation.Warnings.Count > 0) message += "\r\n\r\n" + string.Join("\r\n", installation.Warnings);
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK,
                installation.Warnings.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            Close();
        }
    }
}
