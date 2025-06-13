extern alias DrawingAlias;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.CommandLine;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using LibBundledGGPK3;
using LibGGPK3;
using Microsoft.Win32;
using PoeBench;
using InstalledFontCollection = DrawingAlias::System.Drawing.Text.InstalledFontCollection;

[SuppressMessage("Interoperability", "CA1416:验证平台兼容性")]
public partial class Program
{
    private static JsonSerializerOptions JsonSerializerOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private static readonly List<string> UiSettingPaths =
    [
        "metadata/ui/uisettings.xml",
        "metadata/ui/uisettings.console.xml",
        "metadata/ui/uisettings.tencent.xml",
        "metadata/ui/uisettings.traditional chinese.xml",
    ];

    private static readonly string MinimapVisibilityPixelPath = "shaders/minimap_visibility_pixel.hlsl";

    private static readonly string CameraZoomNodePath = "metadata/characters/character.ot";

    [GeneratedRegex("fonts=\".*?\"")]
    private static partial Regex FontsRegex();

    [GeneratedRegex("typeface=\".*?\"")]
    private static partial Regex TypefaceRegex();

    [GeneratedRegex("size=\"(\\d+)\"")]
    private static partial Regex FontSizeRegex();

    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("POE Bench");

        // 获取安装的字体
        var getInstalledFontsCommand = new Command("get-installed-fonts", "Get installed fonts");
        rootCommand.Add(getInstalledFontsCommand);
        getInstalledFontsCommand.SetHandler(() =>
        {
            Console.WriteLine(JsonSerializer.Serialize(GetInstalledFonts(), JsonSerializerOptions));
        });

        // 获取游戏安装路径
        var getGameInstallPathCommand = new Command("get-game-install-path", "Get game install path");
        var platformOption = new Option<string>(aliases: ["--platform"], description: "Game platform")
            { IsRequired = true };
        var versionOption = new Option<int>(aliases: ["--version"], description: "Game version") { IsRequired = true };
        getGameInstallPathCommand.Add(platformOption);
        getGameInstallPathCommand.Add(versionOption);
        getGameInstallPathCommand.SetHandler(
            (platform, version) => { Console.WriteLine(GetGameInstallPath(platform, version)); }, platformOption,
            versionOption);
        rootCommand.Add(getGameInstallPathCommand);

        // 打补丁、更换字体等
        var patchCommand = new Command("patch", "Patch GGPK");
        var pathOption = new Option<FileInfo>(aliases: ["--path", "-p"], description: "Path to GGPK/Index file")
            { IsRequired = true };
        var patchOption = new Option<FileInfo[]>(aliases: ["--patch-file", "-pf"], description: "Path to patch file");
        var fontOption = new Option<string>(aliases: ["--font"], description: "Change in-game font");
        var fontSizeAdjustOption = new Option<int?>(aliases: ["--font-size-delta"],
            description:
            "Relative font size adjustment (positive values increase size, negative values decrease size)");
        var removeMinimapFogOption = new Option<bool?>(aliases: ["--remove-minimap-fog"],
            description: "whether remove minimap fog");
        var cameraZoomOption =
            new Option<float?>(aliases: ["--camera-zoom"], description: "change camera zoom");
        patchCommand.Add(pathOption);
        patchCommand.Add(patchOption);
        patchCommand.Add(fontOption);
        patchCommand.Add(fontSizeAdjustOption);
        patchCommand.Add(removeMinimapFogOption);
        patchCommand.Add(cameraZoomOption);
        rootCommand.Add(patchCommand);

        patchCommand.SetHandler(async (context) =>
        {
            var path = context.ParseResult.GetValueForOption(pathOption)!;
            var patchArray = context.ParseResult.GetValueForOption(patchOption);
            var font = context.ParseResult.GetValueForOption(fontOption);
            var fontSizeAdjust = context.ParseResult.GetValueForOption(fontSizeAdjustOption);
            var removeMinimapFog = context.ParseResult.GetValueForOption(removeMinimapFogOption);
            var cameraZoom = context.ParseResult.GetValueForOption(cameraZoomOption);

            BundledGGPK? ggpk = null;
            LibBundle3.Index index = null;
            try
            {
                Console.WriteLine($"正在读取 {path.FullName}");
                if (path.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                {
                    index = new LibBundle3.Index(path.FullName);
                }
                else
                {
                    ggpk = new BundledGGPK(path.FullName, false);
                    index = ggpk.Index;
                }
                index.ParsePaths();

                if (patchArray != null)
                {
                    foreach (var patch in patchArray)
                    {
                        if (patch is { Exists: true })
                        {
                            Console.WriteLine($"正在安装补丁 {patch.Name}");
                            var zip = ZipFile.OpenRead(patch.FullName);
                            try
                            {
                                var total = zip.Entries.Count(e => !e.FullName.EndsWith('/'));
                                await Task.Run(() =>
                                {
                                    if (zip.Entries.Any(e =>
                                            e.FullName.Equals("Bundles2/_.index.bin",
                                                StringComparison.OrdinalIgnoreCase)))
                                    {
                                        if (ggpk is null)
                                        {
                                            zip.ExtractToDirectory(
                                                Path.GetDirectoryName(Path.GetDirectoryName(path.FullName))!, true);
                                            total = 0;
                                        }
                                        else
                                        {
                                            total -= GGPK.Replace(ggpk.Root, zip.Entries, (fr, p, added) =>
                                            {
                                                Console.WriteLine($"{(added ? "已添加: " : "已替换: ")}{p}");
                                                return false;
                                            }, allowAdd: true);
                                        }
                                    }
                                    else
                                    {
                                        total -= LibBundle3.Index.Replace(index, zip.Entries, (fr, p) =>
                                        {
                                            Console.WriteLine($"已替换: {p}");
                                            return false;
                                        });
                                    }
                                });
                                Console.WriteLine(total > 0 ? $"错误: {total} 个文件应用失败！" : $"补丁 {patch.Name} 应用成功");
                            }
                            finally
                            {
                                zip.Dispose();
                            }
                        }
                        else
                        {
                            Console.WriteLine($"补丁 {patch.FullName} 不存在，已跳过");
                        }
                    }
                }

                var whetherModifyUiSetting = !string.IsNullOrWhiteSpace(font);
                if (whetherModifyUiSetting || removeMinimapFog.HasValue || cameraZoom.HasValue)
                {
                    var readOnlyDictionary = index.Files;
                    foreach (var (key, fileRecord) in readOnlyDictionary)
                    {
                        var fileRecordPath = fileRecord.Path;
                        if (string.IsNullOrEmpty(fileRecordPath)) continue;
                        if ((!UiSettingPaths.Contains(fileRecordPath) || !whetherModifyUiSetting) &&
                            (!MinimapVisibilityPixelPath.Equals(fileRecordPath) || !removeMinimapFog.HasValue) &&
                            (!CameraZoomNodePath.Equals(fileRecordPath) || !cameraZoom.HasValue))
                            continue;
                        if (whetherModifyUiSetting && UiSettingPaths.Contains(fileRecordPath))
                        {
                            Console.WriteLine($"正在应用字体...");
                            var bytes = fileRecord.Read().ToArray();
                            var encoding = Encoding.GetEncoding("utf-16le");
                            var fileContent = encoding.GetString(bytes);

                            var lines = fileContent.Split("\r\n");
                            for (var i = 0; i < lines.Length; i++)
                            {
                                var line = lines[i];
                                if (line.Trim().StartsWith("<Font") && line.Contains("typeface"))
                                {
                                    line = TypefaceRegex().Replace(line, $"typeface=\"{font}\"");
                                    if (fontSizeAdjust.HasValue && fontSizeAdjust != 0)
                                    {
                                        var fontSizeStr = FontSizeRegex().Match(line).Groups[1].Value;
                                        if (int.TryParse(fontSizeStr, out var fontSize))
                                        {
                                            fontSize += fontSizeAdjust ?? 0;
                                            line = FontSizeRegex().Replace(line, $"size=\"{fontSize}\"");
                                        }
                                    }
                                }
                                else if (line.Trim().StartsWith("<FallbackFont"))
                                {
                                    if (line.Contains("ranges=\"CJK\""))
                                    {
                                        line = FontsRegex().Replace(line,
                                            $"fonts=\"{font},Noto Sans CJK TC,Spoqa Han Sans Neo,Simsun,PMinglu,Gulim,MS UI Gothic,Microsoft JhengHei\"");
                                    }
                                    else if (line.Contains("ranges=\"Any\""))
                                    {
                                        line = FontsRegex().Replace(line,
                                            $"fonts=\"{font},Noto Sans CJK TC,Microsoft Sans Serif,Arial,MS UI Gothic,Nirmala UI,Gautami,Microsoft Himalaya,Lao UI,Mangal,Shruti,Euphemia,Gadugi,Marlett,Webdings,Wingdings\"");
                                    }
                                }

                                lines[i] = line;
                            }

                            var newFileContent = string.Join("\r\n", lines);
                            if (!fileContent.Equals(newFileContent))
                            {
                                var outBytes = encoding.GetBytes(newFileContent);
                                fileRecord.Write(outBytes);
                            }
                        }

                        if (removeMinimapFog.HasValue && MinimapVisibilityPixelPath.Equals(fileRecordPath))
                        {
                            Console.WriteLine("正在前往狮眼守望...");
                            var bytes = fileRecord.Read().ToArray();
                            var encoding = Encoding.GetEncoding("utf-8");
                            var fileContent = encoding.GetString(bytes);

                            var lines = fileContent.Split("\r\n");
                            var i = Array.FindIndex(lines, line => line.Contains("if(visibility_reset > 0.5f)")) + 1;
                            if (i > 0)
                            {
                                lines[i] = removeMinimapFog.Value
                                    ? "\t\tres_color = float4(0.17f, 0.0f, 0.0f, 1.0f);"
                                    : "\t\tres_color = float4(0.0f, 0.0f, 0.0f, 1.0f);";
                                var newFileContent = string.Join("\r\n", lines);
                                var outBytes = encoding.GetBytes(newFileContent);
                                fileRecord.Write(outBytes);
                            }
                        }

                        if (cameraZoom.HasValue && CameraZoomNodePath.Equals(fileRecordPath))
                        {
                            Console.WriteLine("正在寻找扎娜...");
                            var bytes = fileRecord.Read().ToArray();
                            var encoding = Encoding.GetEncoding("utf-16le");
                            var fileContent = encoding.GetString(bytes);
                            var lines = new List<string>(fileContent.Split("\r\n"));
                            var i = lines.FindIndex(line => line.Contains("team = 1")) + 1;
                            if (i > 0)
                            {
                                var line = lines[i];
                                if (cameraZoom < 1)
                                {
                                    cameraZoom = 1;
                                }

                                if (cameraZoom > 3)
                                {
                                    cameraZoom = 3;
                                }

                                var script =
                                    $"on_initial_position_set = \"CreateCameraZoomNode(1000000000.0f, 1000000000.0f, {cameraZoom.Value}f);\"";
                                if (line.Contains("CreateCameraZoomNode"))
                                {
                                    lines[i] = script;
                                }
                                else
                                {
                                    lines.Insert(i, script);
                                }

                                var newFileContent = string.Join("\r\n", lines);
                                var outBytes = encoding.GetBytes(newFileContent);
                                fileRecord.Write(outBytes);
                            }
                        }
                    }
                }
                Console.WriteLine("太好了，没有出现bug");
                context.ExitCode = ExitCode.Success;
            }
            catch (Exception e)
            {
                Console.WriteLine("哦吼，出现了一些问题~");
                context.Console.WriteLine(e.Message);
                context.ExitCode = ExitCode.Error;
            }
            finally
            {
                Console.WriteLine($"正在保存 {path.Name}");
                if (ggpk != null)
                {
                    ggpk.Index.Save();
                    ggpk.Dispose();
                }
                else if (index != null)
                {
                    index.Save();
                    index.Dispose();
                }
                Console.WriteLine("执行结束");
            }
        });
        return await rootCommand.InvokeAsync(args);
    }

    static List<string> GetInstalledFonts()
    {
        var installedFontCollection = new InstalledFontCollection();
        var fontFamilies = installedFontCollection.Families;
        return fontFamilies.Select(fontFamily => fontFamily.GetName(0)).ToList();
    }

    static string? GetGameInstallPath(string platform, int version)
    {
        string foldersKey;
        switch (platform)
        {
            case "TENCENT":
                foldersKey = @"Software\Tencent\流放之路";
                break;
            case "GGG":
                foldersKey = version == 1
                    ? @"Software\GrindingGearGames\Path of Exile"
                    : @"Software\GrindingGearGames\Path of Exile 2";
                break;
            default:
                return null;
        }

        using var key = Registry.CurrentUser.OpenSubKey(foldersKey);
        var value = "TENCENT".Equals(platform) ? key?.GetValue("InstallPath") : key?.GetValue("InstallLocation");
        if (value is not string s) return null;
        if (!value.ToString()!.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            value += Path.DirectorySeparatorChar.ToString();
        }

        var ggpk = value + "Content.ggpk";
        if (File.Exists(ggpk))
        {
            return ggpk;
        }

        var indexBin = value + "Bundles2\\_.index.bin";
        return File.Exists(indexBin) ? indexBin : null;
    }
}