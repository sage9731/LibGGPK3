using System.Drawing.Text;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Eto.Drawing;
using Eto.Forms;
using LibBundledGGPK3;
using LibGGPK3;
using OpenFileDialog = Eto.Forms.OpenFileDialog;

namespace PoeModTool;

public sealed partial class FontWindow : Form
{
    public enum ClientType
    {
        Tencent_POE1,
        GGG_POE1,
        GGG_POE2,
    };

    private const int DefaultSpacing = 10;
    private const string DefaultFont = "霞鹜文楷";

    private readonly List<string> Fonts = [];

    private readonly List<string> UiSettingPaths =
    [
        "metadata/ui/uisettings.xml",
        "metadata/ui/uisettings.console.xml",
        "metadata/ui/uisettings.tencent.xml",
        "metadata/ui/uisettings.traditional chinese.xml",
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

    public FontWindow()
    {
        Title = "POE字体修改工具";
        Size = new Size(450, 458);
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
            PlaceholderText = "选择 Content.ggpk"
        };
        mainLayout.Add(pathTextBox, xscale: true, yscale: false);
        var selectGgpkButton = new Button
        {
            Text = "手动选择"
        };
        selectGgpkButton.Click += (s, e) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 Content.ggpk",
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

        mainLayout.BeginVertical(spacing: new Size(DefaultSpacing, 0));
        mainLayout.BeginHorizontal(false);
        addGgpkButton("腾讯服", ClientType.Tencent_POE1);
        addGgpkButton("国际服POE1", ClientType.GGG_POE1);
        addGgpkButton("国际服POE2", ClientType.GGG_POE2);
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
        mainLayout.AddCentered(new Label { Text = "字体大小增减:", VerticalAlignment = VerticalAlignment.Center });
        fontSizeStepper = new NumericStepper()
        {
            MinValue = -10,
            MaxValue = 10,
            DecimalPlaces = 0,
        };
        fontSizeStepper.GotFocus += (s, e) => { fontsListBox.Focus(); };
        mainLayout.Add(fontSizeStepper, xscale: false, yscale: false);
        LinkButton linkButton = new LinkButton
        {
            Text = "Visit Github",
        };
        linkButton.Click += (s, e) => { Application.Instance.Open("https://github.com/sage9731/LibGGPK3"); };
        mainLayout.AddCentered(linkButton, xscale: true);
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
        selectGgpkButton.Focus();
        return;

        void addGgpkButton(string label, ClientType clientType)
        {
            var tencentPoe1 = new Button()
            {
                Text = label
            };
            tencentPoe1.Click += (s, e) =>
            {
                GgpkButtonClick(clientType);
            };
            mainLayout.Add(tencentPoe1, xscale: true, yscale: false);
        }
        
        void GgpkButtonClick(ClientType clientType)
        {
            var ggpkPath = GetGgpkPath(clientType);
            if (ggpkPath == null)
            {
                MessageBox.Show(this, "无法获取到此客户端的安装目录，请手动选择", "提示", MessageBoxButtons.OK);
            }
            else
            {
                pathTextBox.Text = ggpkPath;
            }
        }
    }

    private static string? GetGgpkPath(ClientType clientType)
    {
        string foldersKey;
        switch (clientType)
        {
            case ClientType.Tencent_POE1:
                foldersKey = @"Software\Tencent\流放之路";
                break;
            case ClientType.GGG_POE1:
                foldersKey = @"Software\GrindingGearGames\Path of Exile";
                break;
            case ClientType.GGG_POE2:
                foldersKey = @"Software\GrindingGearGames\Path of Exile 2";
                break;
            default:
                return null;
        }

        using var key = Registry.CurrentUser.OpenSubKey(foldersKey);
        var value = clientType == ClientType.Tencent_POE1
            ? key?.GetValue("InstallPath")
            : key?.GetValue("InstallLocation");
        if (value is not string s) return null;
        if (!value.ToString()!.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            value += Path.DirectorySeparatorChar.ToString();
        }

        var ggpk = value + "Content.ggpk";
        return File.Exists(ggpk) ? ggpk : null;
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
            MessageBox.Show(this, "请选择 Content.ggpk 地址", "错误", MessageBoxType.Error);
            return;
        }

        var selectedFont = GetSelectedFont();
        if (selectedFont == null)
        {
            MessageBox.Show(this, "请选择要进行替换的字体", "错误", MessageBoxType.Error);
            return;
        }

        var fontSizeDiff = GetFontSizeDiff();
        MessageBox.Show(this,
            "1. 本工具开源免费\n\n2. 替换字体时请关闭游戏客户端和其他补丁工具，否则会报错\n\n3. 每次更新游戏或者打游戏补丁之后需要重新替换字体\n\n4. 任何修改游戏文件的行为都有可能导致封号，用别怕，怕别用",
            "提示");

        confirmButton.Enabled = false;
        confirmButton.Text = "应用中...";
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
                    }
                    else if (line.Trim().StartsWith("<FallbackFont"))
                    {
                        if (line.Contains("ranges=\"CJK\""))
                        {
                            line = FontsRegex().Replace(line,
                                $"fonts=\"{selectedFont},Noto Sans CJK TC,Spoqa Han Sans Neo,Simsun,PMinglu,Gulim,MS UI Gothic,Microsoft JhengHei\"");
                        }
                        else if (line.Contains("ranges=\"Any\""))
                        {
                            line = FontsRegex().Replace(line,
                                $"fonts=\"{selectedFont},Noto Sans CJK TC,Microsoft Sans Serif,Arial,MS UI Gothic,Nirmala UI,Gautami,Microsoft Himalaya,Lao UI,Mangal,Shruti,Euphemia,Gadugi,Marlett,Webdings,Wingdings\"");
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
        confirmButton.Text = "应用";
    }
}