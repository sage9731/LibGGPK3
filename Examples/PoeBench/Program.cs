extern alias DrawingAlias;


using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.CommandLine;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using LibBundledGGPK3;
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

    [GeneratedRegex("fonts=\".*?\"")]
    private static partial Regex FontsRegex();

    [GeneratedRegex("typeface=\".*?\"")]
    private static partial Regex TypefaceRegex();

    [GeneratedRegex("size=\"(\\d+)\"")]
    private static partial Regex FontSizeRegex();

    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("POE Bench");

        var getInstalledFontsCommand = new Command("get-installed-fonts", "Get installed fonts");
        rootCommand.Add(getInstalledFontsCommand);
        getInstalledFontsCommand.SetHandler(() =>
        {
            Console.WriteLine(JsonSerializer.Serialize(GetInstalledFonts(), JsonSerializerOptions));
        });

        var patchGgpkCommand = new Command("patch-ggpk", "Patch GGPK");
        var ggpkPathOption = new Option<FileInfo>(aliases: ["--ggpk", "-g"], description: "Path to GGPK file")
            { IsRequired = true };
        var patchOption = new Option<FileInfo>(aliases: ["--patch", "-p"], description: "Path to patch file");
        var fontOption = new Option<string>(aliases: ["--font", "-f"], description: "Change in-game font");
        var fontSizeAdjustOption = new Option<int?>(aliases: ["--font-size-delta"],
            description:
            "Relative font size adjustment (positive values increase size, negative values decrease size)");
        var removeMinimapFogOption = new Option<bool?>(aliases: ["--remove-minimap-fog"],
            description: "whether remove minimap fog");
        var cameraDistanceOption =
            new Option<float?>(aliases: ["--camera-distance"], description: "change camera distance");
        patchGgpkCommand.Add(ggpkPathOption);
        patchGgpkCommand.Add(patchOption);
        patchGgpkCommand.Add(fontOption);
        patchGgpkCommand.Add(fontSizeAdjustOption);
        patchGgpkCommand.Add(removeMinimapFogOption);
        patchGgpkCommand.Add(cameraDistanceOption);
        rootCommand.Add(patchGgpkCommand);

        patchGgpkCommand.SetHandler(async (context) =>
        {
            var ggpkPath = context.ParseResult.GetValueForOption(ggpkPathOption)!;
            var patch = context.ParseResult.GetValueForOption(patchOption);
            var font = context.ParseResult.GetValueForOption(fontOption);
            var fontSizeAdjust = context.ParseResult.GetValueForOption(fontSizeAdjustOption);
            var removeMinimapFog = context.ParseResult.GetValueForOption(removeMinimapFogOption);
            var cameraDistance = context.ParseResult.GetValueForOption(cameraDistanceOption);

            BundledGGPK? ggpk = null;
            try
            {
                await Task.Run(() =>
                {
                    ggpk = new BundledGGPK(ggpkPath.FullName, false);
                    return ggpk.Index.ParsePaths();
                });
                if (ggpk == null)
                {
                    context.ExitCode = ExitCode.ReadGgpkError;
                    return;
                }

                var whetherModifyUiSetting = !string.IsNullOrWhiteSpace(font);
                if (whetherModifyUiSetting || removeMinimapFog.HasValue || cameraDistance.HasValue)
                {
                    var readOnlyDictionary = ggpk.Index.Files;
                    foreach (var (key, fileRecord) in readOnlyDictionary)
                    {
                        var fileRecordPath = fileRecord.Path;
                        if (string.IsNullOrEmpty(fileRecordPath)) continue;
                        if ((!UiSettingPaths.Contains(fileRecordPath) || !whetherModifyUiSetting) &&
                            (!MinimapVisibilityPixelPath.Equals(fileRecordPath) || !removeMinimapFog.HasValue))
                            continue;
                        if (whetherModifyUiSetting && UiSettingPaths.Contains(fileRecordPath))
                        {
                            // change font
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
                            var bytes = fileRecord.Read().ToArray();
                            var encoding = Encoding.GetEncoding("utf-8");
                            var fileContent = encoding.GetString(bytes);
                            
                            var lines = fileContent.Split("\r\n");
                            var index = Array.FindIndex(lines, line => line.Contains("if(visibility_reset > 0.5f)")) + 1;
                            if (index > 0)
                            {
                                lines[index] = removeMinimapFog.Value
                                    ? "\t\tres_color = float4(0.17f, 0.0f, 0.0f, 1.0f);"
                                    : "\t\tres_color = float4(0.0f, 0.0f, 0.0f, 1.0f);";
                                var newFileContent = string.Join("\r\n", lines);
                                var outBytes = encoding.GetBytes(newFileContent);
                                fileRecord.Write(outBytes);
                            }
                        }
                    }
                }

                context.ExitCode = ExitCode.Success;
            }
            catch (Exception e)
            {
                context.Console.WriteLine(e.Message);
                context.ExitCode = ExitCode.Error;
            }
            finally
            {
                if (ggpk != null)
                {
                    ggpk.Index.Save();
                    ggpk.Dispose();
                }
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
}