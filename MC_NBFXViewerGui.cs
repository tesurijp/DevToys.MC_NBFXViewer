using DevToys.Api;
using DevToys.MC_NBFXViewer;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using static DevToys.Api.GUI;

namespace tesuri.DevToys.MC_NBFXViewer;

[Export(typeof(IGuiTool))]
[Name("MC_NBFXViewer")]
[ToolDisplayInformation(
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uE3A5',
    GroupName = PredefinedCommonToolGroupNames.EncodersDecoders,
    ResourceManagerAssemblyIdentifier = nameof(DevToysPluginsAssemblyIdentifier),
    ResourceManagerBaseName = "tesuri.DevToys.MC_NBFXViewer.MC_NBFXViewer",
    ShortDisplayTitleResourceName = nameof(MC_NBFXViewer.ShortDisplayTitle),
    LongDisplayTitleResourceName = nameof(MC_NBFXViewer.LongDisplayTitle),
    DescriptionResourceName = nameof(MC_NBFXViewer.Description),
    AccessibleNameResourceName = nameof(MC_NBFXViewer.AccessibleName),
    SearchKeywordsResourceName = nameof(MC_NBFXViewer.SearchKeywords)
    )]
internal class MC_NBFXViewerGui : IGuiTool, IDisposable
{
    private readonly DisposableSemaphore semaphore = new();
    private CancellationTokenSource? cancelToken = null;

    private static readonly XmlDictionaryImpl WcfDic = new();

    private enum CompressMode
    {
        None = 0,
        GZip = 1,
        Deflate = 2,
        Auto = 3,
    }

    private static readonly SettingDefinition<CompressMode> Compress = new(
            name: $"{nameof(MC_NBFXViewerGui)}.{nameof(CompressMode)}",
            defaultValue: CompressMode.Auto);

    private static readonly SettingDefinition<bool> Format = new(
            name: $"{nameof(MC_NBFXViewerGui)}.{nameof(Format)}",
            defaultValue: false);

    private static readonly SettingDefinition<bool> WCF = new(
            name: $"{nameof(MC_NBFXViewerGui)}.{nameof(WCF)}",
            defaultValue: false);

    private ISettingsProvider settingsProvider;
    private IFileStorage fileStorage;


    [ImportingConstructor]
    public MC_NBFXViewerGui(ISettingsProvider settingsProvider, IFileStorage fileStorage)
    {
        this.fileStorage = fileStorage;
        this.settingsProvider = settingsProvider;

        var mode = settingsProvider.GetSetting(WCF);
        _ = mode ? WcfToggle.On() : WcfToggle.Off();
        ToggleDic(mode);
    }

    private readonly IUIMultiLineTextInput resultArea = MultiLineTextInput().Title(MC_NBFXViewer.OutputResult).Extendable().ReadOnly();
    private readonly IUIMultiLineTextInput inputArea = MultiLineTextInput().Title(MC_NBFXViewer.InputTitle);
    private readonly IUIDataGrid inputDicArea = DataGrid().Title(MC_NBFXViewer.DictionaryTitle).Extendable();
    private readonly IUIButton addLineButton = Button().Text(MC_NBFXViewer.AddLine);
    private readonly IUISwitch WcfToggle = Switch().OnText(MC_NBFXViewer.SwitchWCFMode).OffText(MC_NBFXViewer.SwitchWCFMode);
    private readonly IUIButton exportDicButton = Button().Icon("FluentSystemIcons", '\uEA43');
    private readonly IUIButton importDicButton = Button().Icon("FluentSystemIcons", '\uE4D9');

    public UIToolView View => new(isScrollable: false,
        SplitGrid().Vertical()
        .LeftPaneLength(new UIGridLength(1, UIGridUnitType.Fraction))
        .WithLeftPaneChild(
            SplitGrid().Horizontal()
            .TopPaneLength(new UIGridLength(1, UIGridUnitType.Fraction))
            .WithTopPaneChild(
                inputArea.OnTextChanged(DataChanged)
            )
            .BottomPaneLength(new UIGridLength(4, UIGridUnitType.Fraction))
            .WithBottomPaneChild(
                SplitGrid().Horizontal()
                .WithTopPaneChild(
                    Stack().Vertical().WithChildren(
                        Setting()
                            .Title(MC_NBFXViewer.SettingsCompressModeTitle)
                            .Handle(
                                settingsProvider,
                                Compress,
                                CompressModeChange,
                                    Item(MC_NBFXViewer.CompressModeAuto, CompressMode.Auto),
                                    Item(MC_NBFXViewer.CompressModeGZip, CompressMode.GZip),
                                    Item(MC_NBFXViewer.CompressModeDeflate, CompressMode.Deflate),
                                    Item(MC_NBFXViewer.CompressModeNone, CompressMode.None)
                            ),
                        Setting()
                            .Title(MC_NBFXViewer.SettingsFormatTitle)
                            .Handle(
                                settingsProvider,
                                Format,
                                FormatChange)
                    )
                )
                .BottomPaneLength(new UIGridLength(3, UIGridUnitType.Fraction))
                .WithBottomPaneChild(
                    inputDicArea.CommandBarExtraContent(
                        Stack().Horizontal().SmallSpacing().WithChildren(
                            importDicButton.OnClick(ImportDic),
                            exportDicButton.OnClick(ExportDic),
                            WcfToggle.OnToggle(ToggleDic),
                            addLineButton.OnClick(DicNewLine)
                        )
                    )
                )
            )
        )
        .RightPaneLength(new UIGridLength(1, UIGridUnitType.Fraction))
        .WithRightPaneChild(resultArea)
        );


    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
        if (dataTypeName == PredefinedCommonDataTypeNames.Base64Text && parsedData is string data)
        {
            inputArea.Text(data);
        }
    }
    private void CompressModeChange(CompressMode _)
    {
        StartParse();
    }

    private void FormatChange(bool _)
    {
        StartParse();
    }
    private void DicNewLine() => DicNewLine(false, 0, "");
    private void DicNewLine(bool withValue, int key, string value)
    {
        var removeButton = Button().Text(MC_NBFXViewer.RemoveLine).Icon("FluentSystemIcons", '\uE4C5');
        var inputKey = SingleLineTextInput().HideCommandBar();
        var inputValue = SingleLineTextInput().HideCommandBar();
        var row = Row(null, Cell(removeButton), Cell(inputKey), Cell(inputValue));
        removeButton.OnClick(() => inputDicArea.Rows.Remove(row));
        inputDicArea.Rows.Add(row);
        if (withValue)
        {
            inputKey.Text(key.ToString()!);
            inputValue.Text(value);
        }
    }
    private void ToggleDic(bool value)
    {
        settingsProvider.SetSetting(WCF, value);
        inputDicArea.Rows.Clear();
        if (value)
        {
            importDicButton.Disable();
            addLineButton.Disable();
            inputDicArea.WithColumns(MC_NBFXViewer.DictionaryKey, MC_NBFXViewer.DictionaryValue);
            var words = new ServiceModelStringsVersion1();
            for (var i = 0; i < words.Count; i++)
            {
                inputDicArea.Rows.Add(Row(null,
                    Cell(Label().Text(i.ToString()).AlignHorizontally(UIHorizontalAlignment.Right)),
                    Cell(Label().Text(words[i]))));
            }
        }
        else
        {
            importDicButton.Enable();
            addLineButton.Enable();
            inputDicArea.WithColumns("", MC_NBFXViewer.DictionaryKey, MC_NBFXViewer.DictionaryValue);
        }
    }

    public void DataChanged(string _)
    {
        StartParse();
    }

    public void StartParse()
    {
        cancelToken?.Cancel();
        cancelToken?.Dispose();
        cancelToken = new CancellationTokenSource();
        _ = BinaryXMLParseAsync(cancelToken.Token);
    }

    private async Task<byte[]> DecompressIfNeed(byte[] buf, CancellationToken token)
    {
        var mode = settingsProvider.GetSetting(Compress);
        await using var ms = new MemoryStream();
        Func<Stream, Stream> makeStream;

        switch (mode)
        {
            case CompressMode.Auto:
                // gzip header exists
                if (buf.Length > 2 && buf[0] == 0x1f && buf[1] == 0x8b) goto case CompressMode.GZip;
                try
                {
                    // test DeflateStream
                    await using var st = new MemoryStream(buf);
                    await using var zst = new DeflateStream(st, CompressionMode.Decompress, true);
                    await zst.CopyToAsync(ms, token);
                    return ms.ToArray();
                }
                catch
                {
                    goto default; // If exception raised, through to default
                }
            case CompressMode.GZip:
                makeStream = st => new GZipStream(st, CompressionMode.Decompress, true);
                break;
            case CompressMode.Deflate:
                makeStream = st => new DeflateStream(st, CompressionMode.Decompress, true);
                break;
            case CompressMode.None:
            default:
                return buf;
        }

        {
            await using var st = new MemoryStream(buf);
            await using Stream zst = makeStream(st);
            await zst.CopyToAsync(ms, token);
        }
        return ms.ToArray();
    }

    private async Task<string> XmlFormatIfNeed(string xml, CancellationToken token)
    {
        if (settingsProvider.GetSetting(Format))
        {
            token.ThrowIfCancellationRequested();
            var doc = XDocument.Parse(xml);
            token.ThrowIfCancellationRequested();
            await using var sw = new StringWriter();
            doc.Save(sw, SaveOptions.None);
            return sw.ToString();
        }
        return xml;
    }

    static readonly JsonSerializerOptions JsonOpt = new() { IncludeFields = true };

    private async void ExportDic()
    {
        await using var file = await fileStorage.PickSaveFileAsync("*.json");
        if (file != null)
        {

            var NBFXDic = settingsProvider.GetSetting<bool>(WCF) ? WcfDic : MakeDic();
            var list = NBFXDic.GetItems();
            JsonSerializer.Serialize(file, list, JsonOpt);
        }
    }
    private async void ImportDic()
    {
        using var sandbox = await fileStorage.PickOpenFileAsync("*.json");
        if (sandbox != null)
        {
            inputDicArea.Rows.Clear();
            await using var file = await sandbox.GetNewAccessToFileContentAsync(CancellationToken.None) ?? new MemoryStream();
            var result = JsonSerializer.Deserialize<IEnumerable<(int, string)>>(file, JsonOpt) ?? [];
            foreach (var (key, value) in result)
            {
                DicNewLine(true, key, value);
            }
        }
    }

    private XmlDictionaryImpl MakeDic()
    {
        List<(int, string)> candidate = [];
        foreach (var row in inputDicArea.Rows)
        {
            if (row.ElementAt(1).UIElement is IUISingleLineTextInput i1 && row.ElementAt(2).UIElement is IUISingleLineTextInput i2 && int.TryParse(i1.Text, out var key) && i2.Text is string value)
            {
                candidate.Add((key, value));
            }
        }
        return new XmlDictionaryImpl(candidate);
    }

    private async Task BinaryXMLParseAsync(CancellationToken token)
    {
        using (await semaphore.WaitAsync(token))
        {
            await TaskSchedulerAwaiter.SwitchOffMainThreadAsync(token);
            try
            {
                var NBFXDic = settingsProvider.GetSetting<bool>(WCF) ? WcfDic : MakeDic();
                token.ThrowIfCancellationRequested();
                var content = Convert.FromBase64String(inputArea.Text);
                token.ThrowIfCancellationRequested();
                var data = await DecompressIfNeed(content, token);
                token.ThrowIfCancellationRequested();
                using var xr = XmlDictionaryReader.CreateBinaryReader(data, 0, data.Length, NBFXDic, XmlDictionaryReaderQuotas.Max);
                xr.Read();
                var outXml = await xr.ReadOuterXmlAsync();
                token.ThrowIfCancellationRequested();
                var result = await XmlFormatIfNeed(outXml, token);
                resultArea.Text(result).Language("xml");
            }

            catch (Exception e)
            {
                resultArea.Text(e.Message).Language("text");
            }
        }
    }

    public void Dispose()
    {
        cancelToken?.Cancel();
        cancelToken?.Dispose();
        semaphore.Dispose();
    }
}
