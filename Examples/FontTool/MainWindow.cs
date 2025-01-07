using System;
using System.Drawing.Text;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;
using Eto.Drawing;
using Eto.Forms;
using LibBundledGGPK3;
using LibGGPK3;
using OpenFileDialog = Eto.Forms.OpenFileDialog;

namespace FontTool;

public sealed partial class MainWindow : Form
{
    private const int DefaultSpacing = 10;
    private const string DefaultFont = "霞鹜文楷";

    private readonly List<string> Fonts = [];
    private readonly List<string> UiSettingPaths = [
        "metadata/ui/uisettings.xml",
        "metadata/ui/uisettings.console.xml",
    ];

    private readonly TextBox pathTextBox;
    private readonly ListBox fontsListBox;
    private readonly NumericStepper fontSizeStepper;
    private readonly Button confirmButton;
    
    [GeneratedRegex("fonts=\".*?\"")]
    private static partial Regex FontsRegex();
    
    [GeneratedRegex("typeface=\".*?\"")]
    private static partial Regex TypefaceRegex();
    
    [GeneratedRegex("size=\"(\\d+)\"")]
    private static partial Regex FontSizeRegex();

    public MainWindow()
    {
        Title = "POE2字体修改工具";
        Size = new Size(450, 428);
        Resizable = false;

        // 获取屏幕的工作区大小
        var screen = Screen.WorkingArea;
        // 计算窗口的位置，使其居中显示
        this.Location = new Point((int)(screen.Width - this.Width) / 2, (int)(screen.Height - this.Height) / 2);

        // 获取系统字体
        var installedFontCollection = new InstalledFontCollection();
        var fontFamilies = installedFontCollection.Families;

        foreach (var fontFamily in fontFamilies)
        {
            var name = fontFamily.GetName(0);
            // var eng = fontFamily.GetName(1033);
            Fonts.Add(name);
        }

        var mainLayout = new DynamicLayout();
        mainLayout.BeginVertical(new Padding(10), new Size(DefaultSpacing, DefaultSpacing), true, true);
        mainLayout.BeginVertical(spacing: new Size(DefaultSpacing, 0));
        mainLayout.BeginHorizontal(false);
        pathTextBox = new TextBox
        {
            ReadOnly = true,
            PlaceholderText = "选择 PathOfExile2/Content.ggpk"
        };
        mainLayout.Add(pathTextBox, xscale: true, yscale: false);
        var selectGgpkButton = new Button
        {
            Text = "选择"
        };
        selectGgpkButton.Click += (s, e) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 PathOfExile2/Content.ggpk",
                Filters = { new FileFilter("Content.ggpk", "*.ggpk") },
            };
            var dialogResult = dialog.ShowDialog(this);
            // ReSharper disable once InvertIf
            if (dialogResult == DialogResult.Ok)
            {
                pathTextBox.Text = dialog.FileName;
                fontsListBox?.Focus();
            }
        };
        mainLayout.Add(selectGgpkButton, xscale: false, yscale: false);
        mainLayout.EndHorizontal();
        mainLayout.EndVertical();

        fontsListBox = new ListBox();
        fontsListBox.Height = 300;
        foreach (var font in Fonts)
        {
            fontsListBox.Items.Add(font);
            if (font == DefaultFont)
            {
                fontsListBox.SelectedKey = font;
            }
        }

        mainLayout.Add(fontsListBox);
        mainLayout.AddSpace();
        mainLayout.BeginVertical(spacing: new Size(DefaultSpacing, 0), yscale: false);
        mainLayout.BeginHorizontal(false);
        mainLayout.Add(new Label { Text = "字体大小增减:", VerticalAlignment = VerticalAlignment.Center });
        fontSizeStepper = new NumericStepper()
        {
            MinValue = -10,
            MaxValue = 10,
            DecimalPlaces = 0,
        };
        fontSizeStepper.GotFocus += (s, e) => { fontsListBox.Focus(); };
        mainLayout.Add(fontSizeStepper, xscale: false, yscale: false);
        mainLayout.AddSpace(xscale: true);
        confirmButton = new Button
        {
            Text = "应用"
        };
        confirmButton.Click += (s, e) => { Apply(); };
        mainLayout.Add(confirmButton);
        mainLayout.EndHorizontal();
        mainLayout.EndVertical();

        mainLayout.EndVertical();
        Content = mainLayout;

        var pathOfExile2Folder = GetPathOfExile2Folder();
        if (!string.IsNullOrEmpty(pathOfExile2Folder))
        {
            var ggpkPath = pathOfExile2Folder + "Content.ggpk";
            pathTextBox.Text = ggpkPath;
        }
        selectGgpkButton.Focus();
    }

    private static string? GetPathOfExile2Folder()
    {
        const string foldersKey = @"Software\GrindingGearGames\Path of Exile 2";
        using var key = Registry.CurrentUser.OpenSubKey(foldersKey);
        var value = key?.GetValue("InstallLocation");
        if (value is not string s) return null;
        var exe = value + "PathOfExile.exe";
        return File.Exists(exe) ? s : null;
    }

    private string GetPath()
    {
        return pathTextBox.Text;
    }

    private string? GetSelectedFont()
    {
        return fontsListBox.SelectedValue?.ToString();
    }

    private int GetFontSizeDiff()
    {
        return (int)fontSizeStepper.Value;
    }

    // ReSharper disable once AsyncVoidMethod
    private async void Apply()
    {
        var path = GetPath();
        if (path.Trim().Length == 0)
        {
            MessageBox.Show(this, "请选择 PathOfExile2/Content.ggpk", "错误", MessageBoxType.Error);
            return;
        }

        var selectedFont = GetSelectedFont();
        if (selectedFont == null)
        {
            MessageBox.Show(this, "请选择要进行替换的字体", "错误", MessageBoxType.Error);
            return;
        }
        var fontSizeDiff = GetFontSizeDiff();
        MessageBox.Show(this, "1. 此工具开源免费，基于aianlinb的LibGGPK3项目\n2. 替换字体时请保持客户端和其他补丁工具关闭，否则会报错\n3. 每次更新游戏或者打游戏补丁之后都需要重新替换字体\n4. 任何修改游戏文件的行为都有可能导致封号，用别怕，怕别用", "提示");

        confirmButton.Enabled = false;
        BundledGGPK? ggpk = null;
        var failed = await Task.Run(() =>
        {
            ggpk = new BundledGGPK(path, false);
            return ggpk.Index.ParsePaths();
        });

        var readOnlyDictionary = ggpk?.Index.Files;
        if (readOnlyDictionary != null)
        {
            var encoding = Encoding.GetEncoding("utf-16le");
            foreach (var (key, fileRecord) in readOnlyDictionary)
            {
                var fileRecordPath = fileRecord.Path;
                if (string.IsNullOrEmpty(fileRecordPath) || !UiSettingPaths.Contains(fileRecordPath)) continue;
                var bytes = fileRecord.Read().ToArray();
                var fileContent = encoding.GetString(bytes);
                var lines = fileContent.Split("\r\n");
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (line.Trim().StartsWith("<Font") && line.Contains("typeface"))
                    {
                        line = TypefaceRegex().Replace(line, $"typeface=\"{selectedFont}\"");
                        if (fontSizeDiff != 0)
                        {
                            var fontSizeStr = FontSizeRegex().Match(line).Groups[1].Value;
                            if (int.TryParse(fontSizeStr, out var fontSize))
                            {
                                fontSize += fontSizeDiff;
                                line = FontSizeRegex().Replace(line, $"size=\"{fontSize}\"");
                            }
                        }
                    } else if (line.Trim().StartsWith("<FallbackFont"))
                    {
                        if (line.Contains("ranges=\"CJK\""))
                        {
                            line = FontsRegex().Replace(line, $"fonts=\"{selectedFont},Noto Sans CJK TC,Spoqa Han Sans Neo,Simsun,PMinglu,Gulim,MS UI Gothic,Microsoft JhengHei\"");
                        } else if (line.Contains("ranges=\"Any\""))
                        {
                            line = FontsRegex().Replace(line, $"fonts=\"{selectedFont},Noto Sans CJK TC,Microsoft Sans Serif,Arial,MS UI Gothic,Nirmala UI,Gautami,Microsoft Himalaya,Lao UI,Mangal,Shruti,Euphemia,Gadugi,Marlett,Webdings,Wingdings\"");
                        }
                    }
                    lines[i] = line;
                }
                var newFileContent = string.Join("\r\n", lines);
                var outBytes = encoding.GetBytes(newFileContent);
                fileRecord.Write(outBytes);
            }
        }
        ggpk.Index.Save();
        ggpk.Dispose();
        MessageBox.Show(this, "替换完成", "提示", MessageBoxButtons.OK);
        confirmButton.Enabled = true;
    }

    
}