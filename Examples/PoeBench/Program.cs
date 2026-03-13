extern alias DrawingAlias;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.CommandLine;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
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
        "metadata/ui/uisettings.tencent.console.xml",
        "metadata/ui/uisettings.traditional chinese.xml",
    ];

    private static readonly string MinimapVisibilityPixelPath = "shaders/minimap_visibility_pixel.hlsl";

    private static readonly string CameraZoomNodePath = "metadata/characters/character.ot";

    [GeneratedRegex(".*environmentsettings/.*\\.env$")]
    private static partial Regex EnvironmentSettingsRegex();

    [GeneratedRegex(".*shaders/bloomcutoff\\\\.hlsl$")]
    private static partial Regex bloomCutOffShadersRegex();

    [GeneratedRegex(".*shaders/bloomgather\\\\.hlsl$")]
    private static partial Regex bloomGatherShadersRegex();

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
        var fontSizeDeltaOption = new Option<int?>(aliases: ["--font-size-delta"],
            description:
            "Relative font size adjustment (positive values increase size, negative values decrease size)");
        var minimapVisibilityOption = new Option<bool?>(aliases: ["--minimap-visibility"],
            description: "set minimap visibility");
        var removeFogOption = new Option<bool?>(aliases: ["--remove-fog"], description: "remove fog");
        var lightUpOption = new Option<float?>(aliases: ["--light-up"], description: "light up the environment");
        var cameraZoomOption =
            new Option<float?>(aliases: ["--camera-zoom"], description: "change camera zoom");
        patchCommand.Add(pathOption);
        patchCommand.Add(patchOption);
        patchCommand.Add(fontOption);
        patchCommand.Add(fontSizeDeltaOption);
        patchCommand.Add(minimapVisibilityOption);
        patchCommand.Add(removeFogOption);
        patchCommand.Add(lightUpOption);
        patchCommand.Add(cameraZoomOption);
        rootCommand.Add(patchCommand);

        patchCommand.SetHandler(async (context) =>
        {
            var path = context.ParseResult.GetValueForOption(pathOption)!;
            var patchArray = context.ParseResult.GetValueForOption(patchOption);
            var font = context.ParseResult.GetValueForOption(fontOption);
            var fontSizeDelta = context.ParseResult.GetValueForOption(fontSizeDeltaOption);
            var minimapVisibility = context.ParseResult.GetValueForOption(minimapVisibilityOption);
            var removeFog = context.ParseResult.GetValueForOption(removeFogOption);
            var lightUp = context.ParseResult.GetValueForOption(lightUpOption);
            var cameraZoom = context.ParseResult.GetValueForOption(cameraZoomOption);

            BundledGGPK? ggpk = null;
            LibBundle3.Index index = null;
            var disposed = false;
            try
            {
                Console.WriteLine($"正在读取 {path.FullName}");
                if (!path.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                {
                    ggpk = await Task.Run(() => new BundledGGPK(path.FullName, false));
                    index = ggpk.Index;
                }

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
                                if (path.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                                {
                                    zip.ExtractToDirectory(
                                        Path.GetDirectoryName(Path.GetDirectoryName(path.FullName))!, true);
                                    total = 0;
                                }
                                else
                                {
                                    if (zip.Entries.Any(e =>
                                            e.FullName.Equals("Bundles2/_.index.bin",
                                                StringComparison.OrdinalIgnoreCase)))
                                    {
                                        total -= GGPK.Replace(ggpk.Root, zip.Entries, (fr, p, added) =>
                                        {
                                            Console.WriteLine($"{(added ? "已添加: " : "已替换: ")}{p}");
                                            return false;
                                        }, allowAdd: true);
                                    }
                                    else
                                    {
                                        total -= LibBundle3.Index.Replace(index, zip.Entries, (fr, p) =>
                                        {
                                            Console.WriteLine($"已替换 {p}");
                                            return false;
                                        });
                                    }
                                }

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

                    disposed = true;
                    ggpk?.Dispose();
                    index?.Dispose();
                }

                var fontIsEmpty = string.IsNullOrWhiteSpace(font);
                var whetherModifyUiSetting = !fontIsEmpty || (fontSizeDelta.HasValue && fontSizeDelta.Value != 0);
                if (whetherModifyUiSetting || minimapVisibility.HasValue || cameraZoom.HasValue || removeFog.HasValue ||
                    lightUp.HasValue)
                {
                    if (disposed)
                    {
                        if (path.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                        {
                            index = await Task.Run(() => new LibBundle3.Index(path.FullName, parsePaths: false));
                        }
                        else
                        {
                            ggpk = await Task.Run(() => new BundledGGPK(path.FullName, false));
                            index = ggpk.Index;
                        }

                        disposed = false;
                    }

                    index.ParsePaths();
                    var readOnlyDictionary = index.Files;
                    foreach (var (key, fileRecord) in readOnlyDictionary)
                    {
                        var fileRecordPath = fileRecord.Path;
                        if (string.IsNullOrEmpty(fileRecordPath)) continue;
                        if (whetherModifyUiSetting && UiSettingPaths.Contains(fileRecordPath))
                        {
                            Console.WriteLine($"正在应用字体到 {fileRecordPath} ...");
                            var bytes = fileRecord.Read().ToArray();
                            var encoding = Encoding.GetEncoding("utf-16le");
                            var fileContent = encoding.GetString(bytes);

                            var lines = fileContent.Split("\r\n");
                            for (var i = 0; i < lines.Length; i++)
                            {
                                var line = lines[i];
                                if (line.Trim().StartsWith("<Font") && line.Contains("typeface"))
                                {
                                    if (!fontIsEmpty)
                                    {
                                        line = TypefaceRegex().Replace(line, $"typeface=\"{font}\"");
                                    }

                                    if (fontSizeDelta.HasValue && fontSizeDelta != 0)
                                    {
                                        var fontSizeStr = FontSizeRegex().Match(line).Groups[1].Value;
                                        if (int.TryParse(fontSizeStr, out var fontSize))
                                        {
                                            fontSize += fontSizeDelta ?? 0;
                                            line = FontSizeRegex().Replace(line, $"size=\"{fontSize}\"");
                                        }
                                    }
                                }
                                else if (!fontIsEmpty && line.Trim().StartsWith("<FallbackFont"))
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

                        if (minimapVisibility.HasValue && MinimapVisibilityPixelPath.Equals(fileRecordPath))
                        {
                            var bytes = fileRecord.Read().ToArray();
                            var encoding = Encoding.GetEncoding("utf-8");
                            var fileContent = encoding.GetString(bytes);
                            if (minimapVisibility.Value)
                            {
                                Console.WriteLine("正在顾全大局...");
                                fileContent = fileContent.Replace("return res_color;",
                                    "return max(res_color.r, 0.15f);");
                            }
                            else
                            {
                                Console.WriteLine("正在目光短浅...");
                                fileContent = Regex.Replace(fileContent, @"return max\(res_color\.r, \d+(\.\d+)?f\);",
                                    "return res_color;");
                            }

                            var outBytes = encoding.GetBytes(fileContent);
                            fileRecord.Write(outBytes);
                        }

                        if ((removeFog.HasValue || lightUp.HasValue) && EnvironmentSettingsRegex().IsMatch(fileRecordPath))
                        {
                            var bytes = fileRecord.Read().ToArray();
                            var encoding = Encoding.GetEncoding("utf-16le");
                            var fileContent = encoding.GetString(bytes);
                            if (removeFog.HasValue)
                            {
                                if (removeFog.Value)
                                {
                                    Console.WriteLine("正在驱散迷雾... " + fileRecordPath);
                                    // 添加 # 号，使用单词边界避免部分匹配
                                    fileContent = Regex.Replace(fileContent, @"(fog|area|water|post_transform)", "#$1#",
                                        RegexOptions.IgnoreCase);
                                }
                                else
                                {
                                    Console.WriteLine("正在步入迷雾... " + fileRecordPath);
                                    // 移除 # 号，同样使用单词边界
                                    fileContent = Regex.Replace(fileContent, @"#+(fog|area|water|post_transform)#+",
                                        "$1",
                                        RegexOptions.IgnoreCase);
                                }
                            }

                            if (lightUp.HasValue)
                            {
                                if (lightUp > 3)
                                {
                                    lightUp = 3;
                                }
                                
                                // 将fileContent转换成JSON对象
                                try
                                {
                                    var bom = "";
                                    if (fileContent.Length > 0 && fileContent[0] == '\uFEFF')
                                    {
                                        bom = "\uFEFF";
                                        fileContent = fileContent.Substring(1);
                                    }
                                    using var jsonDocument = JsonDocument.Parse(fileContent);
                                    var root = jsonDocument.RootElement;

                                    // 检查是否存在 directional_light.multiplier
                                    if (root.TryGetProperty("directional_light", out var directionaLight) &&
                                        directionaLight.ValueKind == JsonValueKind.Object)
                                    {
                                        if (directionaLight.TryGetProperty("multiplier", out var multiplierElement) &&
                                            multiplierElement.ValueKind == JsonValueKind.Number)
                                        {
                                            // 创建可写的JSON对象
                                            var jsonObject = JsonObject.Create(jsonDocument.RootElement.Clone());

                                            // 确保directional_light对象存在
                                            if (!jsonObject.ContainsKey("directional_light"))
                                            {
                                                jsonObject["directional_light"] = new JsonObject();
                                            }

                                            var directionaLightObj = jsonObject["directional_light"].AsObject();

                                            // 如果对象不存在 directional_light.original_multiplier，则进行备份
                                            if (!directionaLightObj.ContainsKey("original_multiplier"))
                                            {
                                                directionaLightObj["original_multiplier"] =
                                                    multiplierElement.GetSingle();
                                            }
                                            // 获取原始乘数值
                                            float originalMultiplier = directionaLightObj["original_multiplier"].GetValue<float>();
                                            // 处理光照调节
                                            if (lightUp > 0)
                                            {
                                                if (lightUp.Value > originalMultiplier)
                                                {
                                                    Console.WriteLine("正在点亮环境..." + fileRecordPath);
                                                    directionaLightObj["multiplier"] = lightUp.Value;
                                                }
                                            }
                                            else if (lightUp <= 0)
                                            {
                                                if (directionaLightObj.ContainsKey("original_multiplier"))
                                                {
                                                    Console.WriteLine("正在复原光亮..." + fileRecordPath);
                                                    directionaLightObj["multiplier"] =
                                                        directionaLightObj["original_multiplier"].GetValue<float>();
                                                }
                                            }

                                            // 将JSON对象转换回字符串，格式化缩进为2个空格
                                            var newJsonContent = jsonObject.ToJsonString(new JsonSerializerOptions
                                            {
                                                WriteIndented = true,
                                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder
                                                    .UnsafeRelaxedJsonEscaping
                                            });
                                            fileContent = bom + newJsonContent;
                                        }
                                    }
                                }
                                catch (JsonException)
                                {
                                    // 如果JSON解析失败，保持原内容不变
                                    Console.WriteLine($"警告: {fileRecordPath} 不是有效的JSON格式，跳过光照调节");
                                }
                            }

                            var outBytes = encoding.GetBytes(fileContent);
                            fileRecord.Write(outBytes);
                        }

                        if (cameraZoom.HasValue && CameraZoomNodePath.Equals(fileRecordPath))
                        {
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

                                Console.WriteLine("正在调整焦距 x" + cameraZoom + "...");

                                var script =
                                    $"on_initial_position_set = {{CreateCameraZoomNode(1000000000.0f, 1000000000.0f, {cameraZoom.Value}f);}}";
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

                    index.Save();
                }

                context.ExitCode = ExitCode.Success;
            }
            catch (Exception e)
            {
                Console.WriteLine("执行过程中出错：");
                context.Console.WriteLine(e.Message);
                context.ExitCode = ExitCode.Error;
            }
            finally
            {
                if (!disposed)
                {
                    ggpk?.Dispose();
                    index?.Dispose();
                }

                Console.WriteLine("执行结束");
            }
        });
        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// 备份文件记录到用户目录下的指定文件夹
    /// </summary>
    /// <param name="fileRecordPath">原文件路径（用于生成MD5文件名）</param>
    /// <param name="fileContent">要备份的文件内容</param>
    public static void BackupFileRecord(string fileRecordPath, string fileContent)
    {
        // 获取备份目录路径
        string backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".poe-bench",
            "backup"
        );

        // 如果目录不存在则创建[8](@ref)
        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        // 计算文件路径的MD5值作为文件名
        string md5FileName = ComputeMD5(fileRecordPath);
        string backupFilePath = Path.Combine(backupDir, md5FileName);

        try
        {
            // 使用UTF-16LE编码将内容写入文件，文件已存在时会自动替换[2,3](@ref)
            File.WriteAllText(backupFilePath, fileContent, Encoding.Unicode);
        }
        catch (Exception ex)
        {
            throw new Exception($"备份文件失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 读取备份的文件记录内容
    /// </summary>
    /// <param name="fileRecordPath">原文件路径（用于生成MD5文件名）</param>
    /// <returns>备份的文件内容，如果备份不存在则返回null</returns>
    public static string GetFileRecordBackup(string fileRecordPath)
    {
        // 获取备份目录路径
        string backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".poe-bench",
            "backup"
        );

        // 计算文件路径的MD5值作为文件名
        string md5FileName = ComputeMD5(fileRecordPath);
        string backupFilePath = Path.Combine(backupDir, md5FileName);

        // 检查备份文件是否存在[7](@ref)
        if (!File.Exists(backupFilePath))
        {
            return null;
        }

        try
        {
            // 使用UTF-16LE编码读取文件内容[3](@ref)
            return File.ReadAllText(backupFilePath, Encoding.Unicode);
        }
        catch (Exception ex)
        {
            throw new Exception($"读取备份文件失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 计算字符串的MD5哈希值
    /// </summary>
    private static string ComputeMD5(string input)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2"));
            }

            return sb.ToString();
        }
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
                if (version == 1)
                {
                    foldersKey = @"Software\Tencent\流放之路";
                }
                else
                {
                    foldersKey = @"Software\Rail\Game2002052";
                }

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