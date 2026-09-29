using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

class BatchRenamer : Form
{
    TextBox folderBox, maskBox, findBox, replaceBox, prefixBox, suffixBox;
    ComboBox caseBox;
    CheckBox numberBox;
    NumericUpDown startBox, digitsBox;
    ListView list;
    Label statusLabel;
    Button applyButton, undoButton;

    // Key = current full path, Value = new full path
    List<KeyValuePair<string, string>> plan = new List<KeyValuePair<string, string>>();
    List<KeyValuePair<string, string>> lastUndo;
    bool planValid;

    BatchRenamer(string startFolder)
    {
        Text = "BatchRenamer — массовое переименование";
        ClientSize = new Size(660, 520);
        MinimumSize = new Size(620, 400);
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterScreen;

        AddLabel("Папка:", 15, 18);
        folderBox = AddBox(80, 15, 450, "");
        folderBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var browse = new Button { Text = "Обзор…", Location = new Point(540, 13), Size = new Size(105, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        browse.Click += (s, e) => Browse();
        Controls.Add(browse);

        AddLabel("Маска:", 15, 55);
        maskBox = AddBox(80, 52, 90, "*.*");
        AddLabel("Найти:", 185, 55);
        findBox = AddBox(235, 52, 140, "");
        AddLabel("Заменить на:", 390, 55);
        replaceBox = AddBox(480, 52, 165, "");

        AddLabel("Префикс:", 15, 92);
        prefixBox = AddBox(80, 89, 90, "");
        AddLabel("Суффикс:", 185, 92);
        suffixBox = AddBox(250, 89, 125, "");
        AddLabel("Регистр:", 390, 92);
        caseBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(480, 89), Width = 165 };
        caseBox.Items.AddRange(new object[] { "Без изменений", "строчные", "ЗАГЛАВНЫЕ", "Каждое Слово" });
        caseBox.SelectedIndex = 0;
        caseBox.SelectedIndexChanged += (s, e) => UpdatePreview();
        Controls.Add(caseBox);

        numberBox = new CheckBox { Text = "Добавить номер, начиная с", Location = new Point(15, 127), AutoSize = true };
        numberBox.CheckedChanged += (s, e) => UpdatePreview();
        Controls.Add(numberBox);
        startBox = new NumericUpDown { Location = new Point(215, 125), Width = 70, Maximum = 1000000, Value = 1 };
        startBox.ValueChanged += (s, e) => UpdatePreview();
        Controls.Add(startBox);
        AddLabel("цифр:", 300, 128);
        digitsBox = new NumericUpDown { Location = new Point(345, 125), Width = 50, Minimum = 1, Maximum = 10, Value = 3 };
        digitsBox.ValueChanged += (s, e) => UpdatePreview();
        Controls.Add(digitsBox);

        list = new ListView
        {
            Location = new Point(15, 162), Size = new Size(630, 295), View = View.Details, FullRowSelect = true, GridLines = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        list.Columns.Add("Было", 305);
        list.Columns.Add("Станет", 305);
        Controls.Add(list);

        statusLabel = new Label { Location = new Point(15, 478), AutoSize = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        Controls.Add(statusLabel);
        undoButton = new Button { Text = "Отменить", Location = new Point(400, 470), Size = new Size(115, 36), Enabled = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        undoButton.Click += (s, e) => Undo();
        applyButton = new Button { Text = "Переименовать", Location = new Point(525, 470), Size = new Size(120, 36), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        applyButton.Click += (s, e) => Apply();
        Controls.Add(undoButton);
        Controls.Add(applyButton);

        folderBox.Text = startFolder ?? "";
        UpdatePreview();
    }

    void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true });
    }

    TextBox AddBox(int x, int y, int width, string text)
    {
        var box = new TextBox { Location = new Point(x, y), Width = width, Text = text };
        box.TextChanged += (s, e) => UpdatePreview();
        Controls.Add(box);
        return box;
    }

    void Browse()
    {
        using (var dlg = new FolderBrowserDialog { Description = "Выбери папку с файлами" })
        {
            if (Directory.Exists(folderBox.Text)) dlg.SelectedPath = folderBox.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK) folderBox.Text = dlg.SelectedPath;
        }
    }

    string Transform(string name, int number)
    {
        if (findBox.Text.Length > 0)
            name = Regex.Replace(name, Regex.Escape(findBox.Text), replaceBox.Text.Replace("$", "$$"), RegexOptions.IgnoreCase);
        name = prefixBox.Text + name + suffixBox.Text;
        if (numberBox.Checked)
            name += "_" + number.ToString().PadLeft((int)digitsBox.Value, '0');
        switch (caseBox.SelectedIndex)
        {
            case 1: name = name.ToLower(); break;
            case 2: name = name.ToUpper(); break;
            case 3: name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower()); break;
        }
        return name;
    }

    void UpdatePreview()
    {
        plan.Clear();
        planValid = false;
        list.BeginUpdate();
        list.Items.Clear();
        try
        {
            string dir = folderBox.Text.Trim();
            if (!Directory.Exists(dir)) { statusLabel.Text = "Выбери папку с файлами"; return; }

            string[] files;
            try { files = Directory.GetFiles(dir, maskBox.Text.Trim().Length > 0 ? maskBox.Text.Trim() : "*.*"); }
            catch (Exception ex) { statusLabel.Text = "Ошибка: " + ex.Message; return; }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int number = (int)startBox.Value, changed = 0, errors = 0;
            foreach (string file in files)
            {
                string oldName = Path.GetFileName(file);
                string newName = Transform(Path.GetFileNameWithoutExtension(file), number++) + Path.GetExtension(file);
                var item = new ListViewItem(new[] { oldName, newName });

                bool bad = newName.Trim().Length == 0 || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !taken.Add(newName);
                if (bad) { item.ForeColor = Color.Firebrick; errors++; }
                else if (newName != oldName) { item.ForeColor = Color.RoyalBlue; changed++; plan.Add(new KeyValuePair<string, string>(file, Path.Combine(dir, newName))); }
                list.Items.Add(item);
            }

            planValid = errors == 0;
            statusLabel.Text = errors > 0
                ? string.Format("Файлов: {0} · конфликтов имён: {1} (выделены красным)", files.Length, errors)
                : string.Format("Файлов: {0} · будет переименовано: {1}", files.Length, changed);
        }
        finally
        {
            list.EndUpdate();
            applyButton.Enabled = planValid && plan.Count > 0;
        }
    }

    void Apply()
    {
        if (!planValid || plan.Count == 0) return;
        var pairs = new List<KeyValuePair<string, string>>(plan);
        if (RenameAll(pairs))
        {
            lastUndo = pairs.ConvertAll(p => new KeyValuePair<string, string>(p.Value, p.Key));
            undoButton.Enabled = true;
            UpdatePreview();
            statusLabel.Text = "Готово: переименовано файлов — " + pairs.Count;
        }
        else UpdatePreview();
    }

    void Undo()
    {
        if (lastUndo == null) return;
        if (RenameAll(lastUndo))
        {
            int count = lastUndo.Count;
            lastUndo = null;
            undoButton.Enabled = false;
            UpdatePreview();
            statusLabel.Text = "Отменено: возвращено имён — " + count;
        }
    }

    // Two-phase rename (via temp names) so swaps and case-only changes work; rolls back on error.
    bool RenameAll(List<KeyValuePair<string, string>> pairs)
    {
        var temps = new List<string>();
        try
        {
            foreach (var p in pairs)
            {
                string temp = p.Key + ".~ren" + Guid.NewGuid().ToString("N").Substring(0, 8);
                File.Move(p.Key, temp);
                temps.Add(temp);
            }
        }
        catch (Exception ex)
        {
            for (int j = temps.Count - 1; j >= 0; j--) try { File.Move(temps[j], pairs[j].Key); } catch { }
            Fail(ex);
            return false;
        }

        int done = 0;
        try
        {
            for (; done < pairs.Count; done++)
            {
                if (File.Exists(pairs[done].Value))
                    throw new IOException("Файл уже существует: " + Path.GetFileName(pairs[done].Value));
                File.Move(temps[done], pairs[done].Value);
            }
        }
        catch (Exception ex)
        {
            for (int j = 0; j < done; j++) try { File.Move(pairs[j].Value, temps[j]); } catch { }
            for (int j = 0; j < pairs.Count; j++) try { File.Move(temps[j], pairs[j].Key); } catch { }
            Fail(ex);
            return false;
        }
        return true;
    }

    void Fail(Exception ex)
    {
        MessageBox.Show(this, "Не удалось переименовать, изменения откачены.\n\n" + ex.Message, "BatchRenamer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.Run(new BatchRenamer(args.Length > 0 ? args[0] : null));
    }
}
