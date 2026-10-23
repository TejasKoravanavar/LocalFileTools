using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using ImageMagick;
using LocalFileTools.Core;
using Microsoft.Win32;

namespace LocalFileTools;

public partial class MainWindow : Window
{
    private readonly List<SelectedFile> _files = [];
    private readonly IFileProcessor _processor = new FileProcessor();
    private Operation? _operation;
    private CancellationTokenSource? _cancel;
    private List<PagePlanItem> _pagePlan = [];
    private IReadOnlyList<FileResult> _lastResults = [];
    private int _previewVersion;
    private int _selectionVersion;
    private int _cropOriginalWidth;
    private int _cropOriginalHeight;
    private System.Windows.Point? _cropStart;
    private PagePlanItem? _pageDragItem;
    private SelectedFile? _fileDragItem;
    private System.Windows.Point _pageDragStart;
    private System.Windows.Point _fileDragStart;
    private const string PageDragFormat = "LocalFileTools/PagePlanItem";
    private const string FileDragFormat = "LocalFileTools/SelectedFile";

    public MainWindow()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        AddPaths(args.Where(a => a != "--files"));
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        _selectionVersion++;
        var failures = new List<string>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (_files.Any(f => string.Equals(f.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))) continue;
                _files.Add(FileDetector.Inspect(path));
            }
            catch { failures.Add(Path.GetFileName(path)); }
        }
        FileList.ItemsSource = null;
        FileList.ItemsSource = _files;
        var known = _files.Where(f => f.Kind != FileKind.Unsupported).ToList();
        SelectionSummary.Text = _files.Count == 0 ? "Drop files here or select files" :
            $"{_files.Count} selected · {string.Join(" · ", _files.GroupBy(f => f.Format).Select(g => $"{g.Count()} {g.Key}"))}";
        if (_files.Any(f => f.Kind == FileKind.Unsupported)) SelectionSummary.Text += "\nUnsupported types have no available operation.";
        if (failures.Count > 0) SelectionSummary.Text += $"\nCould not open: {string.Join(", ", failures)}";
        var first = known.FirstOrDefault();
        if (first is not null) OutputBox.Text = Path.Combine(Path.GetDirectoryName(first.Path) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LocalFileTools Output");
        ShowActions();
    }

    private void ShowActions()
    {
        _lastResults = [];
        _operation = null;
        PanelTitle.Text = "What would you like to do?";
        PanelSubtitle.Text = _files.Count == 0 ? "Drop files into this window or select files." : "Only operations compatible with every selected item are shown.";
        ToolSearchPanel.Visibility = Visibility.Visible;
        RenderActions();
        ActionPanel.Visibility = Visibility.Visible;
        OptionsPanel.Visibility = ProgressPanel.Visibility = ResultPanel.Visibility = Visibility.Collapsed;
    }

    private void RenderActions()
    {
        if (ActionPanel is null || ToolSearchBox is null) return;
        ActionPanel.Children.Clear();
        var query = ToolSearchBox.Text.Trim();
        var actions = OperationCatalog.For(_files)
            .Where(a => string.IsNullOrEmpty(query) ||
                a.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Group.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        bool imageSelection = _files.Count > 0 && _files.All(f => f.Kind == FileKind.Image);
        foreach (var group in actions.GroupBy(a => a.Group).OrderBy(g => imageSelection && g.Key == "Edit images" ? 0 : 1))
        {
            ActionPanel.Children.Add(new TextBlock { Text = group.Key, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) });
            var row = new WrapPanel();
            foreach (var action in group)
            {
                var button = new Button { Content = new TextBlock { Text = action.Title + "\n" + action.Description, TextWrapping = TextWrapping.Wrap }, Width = 210, Height = 74, Tag = action.Id };
                button.Click += (_, _) => ShowOptions((Operation)button.Tag);
                row.Children.Add(button);
            }
            ActionPanel.Children.Add(row);
        }
        if (actions.Count == 0)
            ActionPanel.Children.Add(new TextBlock { Text = _files.Count == 0 ? "Add files to see relevant tools." : "No available tool matches this search or selection.", Margin = new Thickness(0, 10, 0, 0) });
    }

    private void ToolSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RenderActions();
    private void ResizeWithCompressionBox_Changed(object sender, RoutedEventArgs e)
    {
        if (SizeFields is not null) SizeFields.IsEnabled = ResizeWithCompressionBox.IsChecked == true;
    }
    private void FormatBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFormatOptions();
    private void ResizeModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateResizeMode();
    private void UpdateResizeMode()
    {
        if (ResizeModeBox is null || PercentPanel is null || SizePanel is null || _operation != Operation.ResizeImages) return;
        bool percent = ComboText(ResizeModeBox) == "Percent";
        PercentPanel.Visibility = percent ? Visibility.Visible : Visibility.Collapsed;
        SizePanel.Visibility = percent ? Visibility.Collapsed : Visibility.Visible;
        if (!percent)
        {
            SizeTitle.Text = ComboText(ResizeModeBox) == "Exact pixels" ? "Exact width × height" : "Maximum width × height";
            SizeHint.Text = ComboText(ResizeModeBox) == "Exact pixels" ? "The image will use both values, even if its proportions change." : "Aspect ratio is preserved. Smaller images are not enlarged.";
            if (ComboText(ResizeModeBox) == "Exact pixels" && HeightBox.Text == "0") HeightBox.Text = "1080";
        }
    }
    private void UpdateFormatOptions()
    {
        if (FormatBox is null || BackgroundPanel is null || QualityPanel is null || _operation != Operation.ConvertImages) return;
        var format = ComboText(FormatBox);
        BackgroundPanel.Visibility = format == "JPG" ? Visibility.Visible : Visibility.Collapsed;
        QualityPanel.Visibility = format == "PNG" ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ShowOptions(Operation operation)
    {
        if (operation is Operation.ImagesToPdf or Operation.MergePdf or Operation.OrganizePdf)
        {
            var version = _selectionVersion;
            var snapshot = _files.ToList();
            ActionPanel.IsEnabled = false;
            PanelSubtitle.Text = "Reading pages…";
            try { _pagePlan = await Task.Run(() => PagePlan.Create(snapshot)); }
            catch { MessageBox.Show(this, "Could not read the selected PDF pages. The file may be damaged or encrypted.", "Cannot arrange pages"); ShowActions(); return; }
            finally { ActionPanel.IsEnabled = true; }
            if (version != _selectionVersion) return;
        }
        else _pagePlan = [];
        _operation = operation;
        var label = OperationCatalog.For(_files).First(a => a.Id == operation);
        PanelTitle.Text = label.Title;
        PanelSubtitle.Text = $"{_files.Count} selected · {label.Description}";
        ActionPanel.Visibility = Visibility.Collapsed;
        ToolSearchPanel.Visibility = Visibility.Collapsed;
        OptionsPanel.Visibility = Visibility.Visible;
        PagePlanPanel.Visibility = _pagePlan.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_pagePlan.Count > 0)
        {
            RefreshPagePlan();
            PageList.SelectedIndex = 0;
        }
        FormatPanel.Visibility = operation == Operation.ConvertImages ? Visibility.Visible : Visibility.Collapsed;
        QualityPanel.Visibility = (operation is Operation.CompressImages or Operation.ConvertImages or Operation.PdfToJpg) &&
            !(operation == Operation.CompressImages && _files.All(f => f.Format == "SVG")) ? Visibility.Visible : Visibility.Collapsed;
        SizePanel.Visibility = operation is Operation.CompressImages or Operation.ResizeImages or Operation.PdfToJpg ? Visibility.Visible : Visibility.Collapsed;
        ResizeModePanel.Visibility = operation == Operation.ResizeImages ? Visibility.Visible : Visibility.Collapsed;
        PercentPanel.Visibility = Visibility.Collapsed;
        ResizeWithCompressionBox.Visibility = operation == Operation.CompressImages ? Visibility.Visible : Visibility.Collapsed;
        SizeFields.IsEnabled = operation != Operation.CompressImages || ResizeWithCompressionBox.IsChecked == true;
        SizeTitle.Text = operation == Operation.PdfToJpg ? "JPG maximum width" : "Maximum dimensions (0 keeps original)";
        SizeHint.Text = operation == Operation.PdfToJpg ? "Page aspect ratio is preserved." : "Aspect ratio is preserved. Images are never enlarged.";
        SizeSeparator.Visibility = HeightBox.Visibility = operation == Operation.PdfToJpg ? Visibility.Collapsed : Visibility.Visible;
        UpdateResizeMode();
        ImageCropPanel.Visibility = operation == Operation.CropImages ? Visibility.Visible : Visibility.Collapsed;
        ImageRotatePanel.Visibility = operation == Operation.RotateImages ? Visibility.Visible : Visibility.Collapsed;
        UpscalePanel.Visibility = operation == Operation.UpscaleImages ? Visibility.Visible : Visibility.Collapsed;
        GifPanel.Visibility = operation == Operation.AnimatedGif ? Visibility.Visible : Visibility.Collapsed;
        if (operation == Operation.CropImages)
        {
            ImageCropXBox.Text = ImageCropYBox.Text = ImageCropWidthBox.Text = ImageCropHeightBox.Text = "0";
            _ = LoadCropPreviewAsync();
        }
        PdfOptionsPanel.Visibility = operation is Operation.ImagesToPdf or Operation.MergePdf ? Visibility.Visible : Visibility.Collapsed;
        RangePanel.Visibility = operation is Operation.SplitPdf or Operation.PdfToJpg ? Visibility.Visible : Visibility.Collapsed;
        IndividualPagesBox.Visibility = operation == Operation.SplitPdf ? Visibility.Visible : Visibility.Collapsed;
        BackgroundPanel.Visibility = operation == Operation.ConvertImages ? Visibility.Visible : Visibility.Collapsed;
        UpdateFormatOptions();
        WatermarkPanel.Visibility = operation == Operation.WatermarkPdf ? Visibility.Visible : Visibility.Collapsed;
        CropPanel.Visibility = operation == Operation.CropPdf ? Visibility.Visible : Visibility.Collapsed;
        PasswordPanel.Visibility = operation is Operation.ProtectPdf or Operation.UnlockPdf ? Visibility.Visible : Visibility.Collapsed;
        ConfirmPasswordPanel.Visibility = operation == Operation.ProtectPdf ? Visibility.Visible : Visibility.Collapsed;
        PasswordLabel.Text = operation == Operation.UnlockPdf ? "Current PDF password" : "New PDF password";
        OperationNote.Text = operation switch
        {
            Operation.CompressPdf => "PDF optimization rewrites the document without lowering image quality. It may produce little or no saving; actual sizes are shown afterward.",
            Operation.ConvertImages => "JPG fills transparent pixels with the selected color. Converting an animated image to a still format uses its first frame.",
            Operation.CompressImages => "Creates a new file only when it is smaller; otherwise the original bytes are copied to the output. SVG compression is lossless.",
            Operation.MergePdf => "Drag PDF pages and images into their final order. No original file is changed.",
            Operation.ExtractText => "This extracts existing selectable text. For scanned pages, choose OCR to text instead.",
            Operation.OcrText => "English OCR runs entirely on this computer. The result is a text file; recognition accuracy depends on scan quality.",
            Operation.SearchablePdf => "Creates a searchable PDF locally with English OCR. PDF input pages are rendered as images, so vector text and form fields will not be preserved.",
            Operation.ProtectPdf => "A new AES-128 encrypted PDF is created. Keep your password; it is not stored.",
            Operation.UnlockPdf => "Enter a password you know. A new unencrypted copy is created.",
            Operation.PdfToJpg => "Each selected page becomes a separate JPG file.",
            Operation.CropPdf => "The crop changes the visible page area in a new PDF. Original content outside the crop may still remain embedded; do not use this for redaction.",
            Operation.CropImages => "The preview uses the first selected image. The same pixel rectangle is applied to every file; files too small for it are reported individually.",
            Operation.RotateImages => "Orientation is checked separately for each image before rotation. Animated GIF frames stay animated.",
            Operation.UpscaleImages => "JPG and PNG only. This uses interpolation, not AI detail reconstruction.",
            Operation.AnimatedGif => "A single looping GIF is made from the JPG files in their selected order.",
            Operation.ComparePdf => "Creates one self-contained HTML report with both rendered pages and a visual change percentage. Tiny rendering differences may count as changes.",
            _ => "New files are created in the output folder. Existing names get a numbered suffix."
        };
        RunButton.Content = operation switch
        {
            Operation.ImagesToPdf => "Create PDF", Operation.MergePdf => "Merge PDF", Operation.SplitPdf => "Extract pages",
            Operation.CompressPdf => "Optimize PDF", Operation.ResizeImages => "Resize images",
            Operation.ConvertImages => "Convert images", _ => "Compress images"
        };
        if (operation is Operation.OrganizePdf) RunButton.Content = "Create organized PDF";
        if (operation is Operation.PdfToJpg) RunButton.Content = "Export JPG pages";
        if (operation is Operation.ExtractText) RunButton.Content = "Extract text";
        if (operation is Operation.OcrText) RunButton.Content = "Run OCR";
        if (operation is Operation.SearchablePdf) RunButton.Content = "Create searchable PDF";
        if (operation is Operation.WatermarkPdf) RunButton.Content = "Add watermark";
        if (operation is Operation.PageNumbersPdf) RunButton.Content = "Add page numbers";
        if (operation is Operation.CropPdf) RunButton.Content = "Crop PDF";
        if (operation is Operation.ProtectPdf) RunButton.Content = "Protect PDF";
        if (operation is Operation.UnlockPdf) RunButton.Content = "Unlock PDF";
        if (operation is Operation.CropImages) RunButton.Content = "Crop images";
        if (operation is Operation.RotateImages) RunButton.Content = "Rotate images";
        if (operation is Operation.UpscaleImages) RunButton.Content = "Upscale images";
        if (operation is Operation.AnimatedGif) RunButton.Content = "Create GIF";
        if (operation is Operation.ComparePdf) RunButton.Content = "Compare PDFs";
    }

    private void RefreshPagePlan()
    {
        var selected = PageList.SelectedItem;
        int number = 1;
        foreach (var page in _pagePlan) page.OutputNumber = number++;
        PageList.ItemsSource = null;
        PageList.ItemsSource = _pagePlan;
        if (selected is not null && _pagePlan.Contains((PagePlanItem)selected)) PageList.SelectedItem = selected;
        PageDropLine.Visibility = Visibility.Collapsed;
        PagePlanHint.Text = $"{_pagePlan.Count} pages in the result, in the order shown. Drag any page or image to rearrange it, or merge now. Click a card to preview it; Ctrl+↑/↓ also moves it.";
    }

    private static T? ItemFromEvent<T>(ListBox list, DependencyObject? source) where T : class =>
        source is null ? null : (ItemsControl.ContainerFromElement(list, source) as ListBoxItem)?.DataContext as T;

    private static bool MovedEnough(System.Windows.Point start, System.Windows.Point current) =>
        Math.Abs(start.X - current.X) >= SystemParameters.MinimumHorizontalDragDistance ||
        Math.Abs(start.Y - current.Y) >= SystemParameters.MinimumVerticalDragDistance;

    private static (int index, double lineY) InsertionAt(ListBox list, double y)
    {
        int after = 0;
        double line = 3;
        for (int index = 0; index < list.Items.Count; index++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container || container.ActualHeight <= 0) continue;
            double top = container.TranslatePoint(new System.Windows.Point(0, 0), list).Y;
            if (y < top + container.ActualHeight / 2) return (index, top);
            after = index + 1;
            line = top + container.ActualHeight;
        }
        return (after, line);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }
        return null;
    }

    private static void ScrollWhileDragging(ListBox list, double y)
    {
        var scroll = FindScrollViewer(list);
        if (scroll is null) return;
        if (y < 24) scroll.LineUp();
        else if (y > list.ActualHeight - 24) scroll.LineDown();
    }

    private static void ShowDropLine(Border line, ListBox list, double y)
    {
        line.Width = Math.Max(0, list.ActualWidth - 12);
        Canvas.SetLeft(line, 6);
        Canvas.SetTop(line, Math.Clamp(y - line.Height / 2, 0, Math.Max(0, list.ActualHeight - line.Height)));
        line.Visibility = Visibility.Visible;
    }

    private void PageList_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _pageDragItem = ItemFromEvent<PagePlanItem>(PageList, e.OriginalSource as DependencyObject);
        _pageDragStart = e.GetPosition(PageList);
    }

    private void PageList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _pageDragItem = null; return; }
        if (_pageDragItem is null || !MovedEnough(_pageDragStart, e.GetPosition(PageList))) return;
        var item = _pageDragItem;
        _pageDragItem = null;
        PageList.SelectedItem = item;
        DragDrop.DoDragDrop(PageList, new DataObject(PageDragFormat, item), DragDropEffects.Move);
        PageDropLine.Visibility = Visibility.Collapsed;
    }

    private void PageList_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(PageDragFormat) is not PagePlanItem item || !_pagePlan.Contains(item)) return;
        var point = e.GetPosition(PageList);
        ScrollWhileDragging(PageList, point.Y);
        ShowDropLine(PageDropLine, PageList, InsertionAt(PageList, point.Y).lineY);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void PageList_DragLeave(object sender, System.Windows.DragEventArgs e) => PageDropLine.Visibility = Visibility.Collapsed;

    private void PageList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        PageDropLine.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(PageDragFormat) is not PagePlanItem item || !_pagePlan.Contains(item)) return;
        int target = InsertionAt(PageList, e.GetPosition(PageList).Y).index;
        PagePlan.Move(_pagePlan, item, target);
        RefreshPagePlan();
        PageList.SelectedItem = item;
        PageList.ScrollIntoView(item);
        e.Handled = true;
    }

    private void PageList_KeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || PageList.SelectedItem is not PagePlanItem item) return;
        int direction = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
        if (direction == 0) return;
        int old = _pagePlan.IndexOf(item), next = old + direction;
        if (next < 0 || next >= _pagePlan.Count) return;
        PagePlan.Move(_pagePlan, item, direction < 0 ? next : next + 1);
        RefreshPagePlan();
        PageList.SelectedItem = item;
        PageList.ScrollIntoView(item);
        e.Handled = true;
    }

    private void FileList_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _fileDragItem = ItemFromEvent<SelectedFile>(FileList, e.OriginalSource as DependencyObject);
        _fileDragStart = e.GetPosition(FileList);
    }

    private void FileList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _fileDragItem = null; return; }
        if (_fileDragItem is null || !MovedEnough(_fileDragStart, e.GetPosition(FileList))) return;
        var item = _fileDragItem;
        _fileDragItem = null;
        FileList.SelectedItem = item;
        DragDrop.DoDragDrop(FileList, new DataObject(FileDragFormat, item), DragDropEffects.Move);
        FileDropLine.Visibility = Visibility.Collapsed;
    }

    private void FileList_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(FileDragFormat) is not SelectedFile item || !_files.Contains(item)) return;
        var point = e.GetPosition(FileList);
        ScrollWhileDragging(FileList, point.Y);
        ShowDropLine(FileDropLine, FileList, InsertionAt(FileList, point.Y).lineY);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void FileList_DragLeave(object sender, System.Windows.DragEventArgs e) => FileDropLine.Visibility = Visibility.Collapsed;

    private void FileList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        FileDropLine.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(FileDragFormat) is not SelectedFile item || !_files.Contains(item)) return;
        int target = InsertionAt(FileList, e.GetPosition(FileList).Y).index;
        MoveFile(item, target);
        e.Handled = true;
    }

    private void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || FileList.SelectedItem is not SelectedFile item) return;
        int direction = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
        if (direction == 0) return;
        int old = _files.IndexOf(item), next = old + direction;
        if (next < 0 || next >= _files.Count) return;
        MoveFile(item, direction < 0 ? next : next + 1);
        e.Handled = true;
    }

    private void MoveFile(SelectedFile item, int target)
    {
        int old = _files.IndexOf(item);
        if (old < 0 || target < 0 || target > _files.Count) return;
        _files.RemoveAt(old);
        if (old < target) target--;
        _files.Insert(target, item);
        FileList.Items.Refresh();
        FileList.SelectedItem = item;
        FileList.ScrollIntoView(item);
    }
    private void RotateLeft_Click(object sender, RoutedEventArgs e) => RotatePage(-90);
    private void RotateRight_Click(object sender, RoutedEventArgs e) => RotatePage(90);
    private void RotatePage(int degrees)
    {
        if (PageList.SelectedItem is not PagePlanItem item) return;
        item.Rotation = (item.Rotation + degrees + 360) % 360;
        PageList.Items.Refresh();
        _ = LoadPagePreviewAsync(item);
    }
    private void RemovePage_Click(object sender, RoutedEventArgs e)
    {
        if (PageList.SelectedItem is not PagePlanItem item) return;
        _pagePlan.Remove(item);
        RefreshPagePlan();
        if (_pagePlan.Count > 0) PageList.SelectedIndex = Math.Min(PageList.SelectedIndex, _pagePlan.Count - 1);
    }
    private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageList.SelectedItem is PagePlanItem item) _ = LoadPagePreviewAsync(item);
        else PagePreview.Source = null;
    }
    private async Task LoadPagePreviewAsync(PagePlanItem item)
    {
        int version = ++_previewVersion;
        PagePreview.Source = null;
        try
        {
            var preview = await Task.Run(() =>
            {
                if (!item.IsImage) return PdfRenderService.RenderPage(item.SourcePath, item.SourcePage, 500);
                using var image = new MagickImage(item.SourcePath);
                image.AutoOrient();
                if (item.Rotation != 0) image.Rotate(item.Rotation);
                image.Resize(new MagickGeometry(400, 500));
                using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                return (BitmapSource)bitmap;
            });
            if (version == _previewVersion && PageList.SelectedItem == item)
                PagePreview.Source = !item.IsImage && item.Rotation != 0
                    ? new TransformedBitmap(preview, new RotateTransform(item.Rotation)) : preview;
        }
        catch { if (version == _previewVersion) PagePreview.Source = null; }
    }

    private async Task LoadCropPreviewAsync()
    {
        var file = _files.FirstOrDefault();
        if (file is null) return;
        var version = _selectionVersion;
        CropPreviewLabel.Text = $"Preview: {file.Name}";
        CropPreviewImage.Source = null;
        CropSelection.Visibility = Visibility.Collapsed;
        try
        {
            var preview = await Task.Run(() =>
            {
                using var image = new MagickImage(file.Path);
                image.AutoOrient();
                int width = checked((int)image.Width), height = checked((int)image.Height);
                image.Resize(new MagickGeometry(840, 560));
                using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                return (bitmap, width, height);
            });
            if (version != _selectionVersion || _operation != Operation.CropImages) return;
            CropPreviewImage.Source = preview.bitmap;
            _cropOriginalWidth = preview.width;
            _cropOriginalHeight = preview.height;
            if (ImageCropWidthBox.Text == "0") ImageCropWidthBox.Text = preview.width.ToString();
            if (ImageCropHeightBox.Text == "0") ImageCropHeightBox.Text = preview.height.ToString();
        }
        catch { CropPreviewLabel.Text = $"Preview unavailable: {file.Name}. Enter pixel coordinates below."; }
    }

    private System.Windows.Rect CropImageBounds()
    {
        double scale = Math.Min(420.0 / _cropOriginalWidth, 280.0 / _cropOriginalHeight);
        double width = _cropOriginalWidth * scale, height = _cropOriginalHeight * scale;
        return new((420 - width) / 2, (280 - height) / 2, width, height);
    }

    private System.Windows.Point ClampCropPoint(System.Windows.Point point)
    {
        var bounds = CropImageBounds();
        return new(Math.Clamp(point.X, bounds.Left, bounds.Right), Math.Clamp(point.Y, bounds.Top, bounds.Bottom));
    }

    private void CropPreview_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_cropOriginalWidth < 1 || _cropOriginalHeight < 1) return;
        _cropStart = ClampCropPoint(e.GetPosition(CropPreviewCanvas));
        CropPreviewCanvas.CaptureMouse();
        CropSelection.Visibility = Visibility.Visible;
        CropSelection.Width = CropSelection.Height = 0;
        Canvas.SetLeft(CropSelection, _cropStart.Value.X);
        Canvas.SetTop(CropSelection, _cropStart.Value.Y);
    }

    private void CropPreview_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_cropStart is null) return;
        var point = ClampCropPoint(e.GetPosition(CropPreviewCanvas));
        double x = Math.Min(_cropStart.Value.X, point.X), y = Math.Min(_cropStart.Value.Y, point.Y);
        Canvas.SetLeft(CropSelection, x); Canvas.SetTop(CropSelection, y);
        CropSelection.Width = Math.Abs(point.X - _cropStart.Value.X);
        CropSelection.Height = Math.Abs(point.Y - _cropStart.Value.Y);
    }

    private void CropPreview_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_cropStart is null) return;
        CropPreview_MouseMove(sender, e);
        var bounds = CropImageBounds();
        if (CropSelection.Width >= 3 && CropSelection.Height >= 3)
        {
            double scaleX = _cropOriginalWidth / bounds.Width, scaleY = _cropOriginalHeight / bounds.Height;
            int x = (int)Math.Round((Canvas.GetLeft(CropSelection) - bounds.Left) * scaleX);
            int y = (int)Math.Round((Canvas.GetTop(CropSelection) - bounds.Top) * scaleY);
            ImageCropXBox.Text = Math.Clamp(x, 0, _cropOriginalWidth - 1).ToString();
            ImageCropYBox.Text = Math.Clamp(y, 0, _cropOriginalHeight - 1).ToString();
            ImageCropWidthBox.Text = Math.Min(_cropOriginalWidth - x, (int)Math.Round(CropSelection.Width * scaleX)).ToString();
            ImageCropHeightBox.Text = Math.Min(_cropOriginalHeight - y, (int)Math.Round(CropSelection.Height * scaleY)).ToString();
        }
        _cropStart = null;
        CropPreviewCanvas.ReleaseMouseCapture();
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is null) return;
        if (PagePlanPanel.Visibility == Visibility.Visible)
        {
            if (_pagePlan.Count == 0) { MessageBox.Show(this, "Keep at least one page in the result.", "Check pages"); return; }
        }
        if (_operation == Operation.WatermarkPdf && string.IsNullOrWhiteSpace(WatermarkBox.Text))
        { MessageBox.Show(this, "Enter the watermark text.", "Check options"); return; }
        if (_operation is Operation.ProtectPdf or Operation.UnlockPdf && string.IsNullOrEmpty(PasswordBox.Password))
        { MessageBox.Show(this, "Enter the PDF password.", "Check options"); return; }
        if (_operation == Operation.ProtectPdf && PasswordBox.Password != ConfirmPasswordBox.Password)
        { MessageBox.Show(this, "The two passwords do not match.", "Check options"); return; }
        int cropLeft = 0, cropTop = 0, cropRight = 0, cropBottom = 0;
        if (CropPanel.Visibility == Visibility.Visible &&
            (!int.TryParse(CropLeftBox.Text, out cropLeft) || !int.TryParse(CropTopBox.Text, out cropTop) ||
             !int.TryParse(CropRightBox.Text, out cropRight) || !int.TryParse(CropBottomBox.Text, out cropBottom) ||
             new[] { cropLeft, cropTop, cropRight, cropBottom }.Any(n => n < 0)))
        { MessageBox.Show(this, "Enter non-negative whole numbers for crop margins.", "Check options"); return; }
        int width = 0, height = 0;
        if (SizePanel.Visibility == Visibility.Visible &&
            (!int.TryParse(WidthBox.Text, out width) || width < 0 ||
             (HeightBox.Visibility == Visibility.Visible && (!int.TryParse(HeightBox.Text, out height) || height < 0))))
        { MessageBox.Show(this, "Enter non-negative whole numbers for dimensions.", "Check options"); return; }
        int percent = 50;
        if (PercentPanel.Visibility == Visibility.Visible && (!int.TryParse(PercentBox.Text, out percent) || percent is < 1 or > 1000))
        { MessageBox.Show(this, "Enter a percentage from 1 to 1000.", "Check options"); return; }
        int imageX = 0, imageY = 0, imageWidth = 0, imageHeight = 0;
        if (ImageCropPanel.Visibility == Visibility.Visible &&
            (!int.TryParse(ImageCropXBox.Text, out imageX) || !int.TryParse(ImageCropYBox.Text, out imageY) ||
             !int.TryParse(ImageCropWidthBox.Text, out imageWidth) || !int.TryParse(ImageCropHeightBox.Text, out imageHeight) ||
             imageX < 0 || imageY < 0 || imageWidth < 1 || imageHeight < 1))
        { MessageBox.Show(this, "Enter a crop rectangle with non-negative X/Y and positive width/height.", "Check options"); return; }
        int gifDelay = 500;
        if (GifPanel.Visibility == Visibility.Visible && (!int.TryParse(GifDelayBox.Text, out gifDelay) || gifDelay is < 20 or > 10000))
        { MessageBox.Show(this, "Enter a frame delay from 20 to 10000 ms.", "Check options"); return; }
        string directory;
        try { directory = Path.GetFullPath(OutputBox.Text); }
        catch { MessageBox.Show(this, "Choose a valid output folder.", "Check options"); return; }
        var margin = MarginBox.SelectedIndex switch { 0 => 0, 2 => 32, 3 => 48, _ => 16 };
        var options = new JobOptions(directory, ComboText(FormatBox), (int)QualitySlider.Value,
            SizePanel.Visibility == Visibility.Visible && (_operation != Operation.CompressImages || ResizeWithCompressionBox.IsChecked == true) ? width : 0,
            SizePanel.Visibility == Visibility.Visible && _operation != Operation.PdfToJpg && (_operation != Operation.CompressImages || ResizeWithCompressionBox.IsChecked == true) ? height : 0,
            ComboText(PageSizeBox), margin, RangeBox.Text, ComboText(BackgroundBox),
            _pagePlan.Count > 0 ? _pagePlan.ToList() : null, WatermarkBox.Text, PasswordBox.Password,
            IndividualPagesBox.IsChecked == true && _operation == Operation.SplitPdf,
            cropLeft, cropTop, cropRight, cropBottom,
            ComboText(ResizeModeBox), percent, imageX, imageY, imageWidth, imageHeight,
            int.Parse(((ComboBoxItem)RotateDegreesBox.SelectedItem).Tag.ToString()!), ComboText(RotationFilterBox),
            gifDelay, int.Parse(((ComboBoxItem)UpscaleFactorBox.SelectedItem).Tag.ToString()!));
        if (_operation is Operation.SplitPdf or Operation.PdfToJpg)
        {
            try
            {
                foreach (var file in _files)
                {
                    using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(file.Path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
                    FileProcessor.ParsePages(options.PageRange, pdf.PageCount);
                }
            }
            catch { MessageBox.Show(this, "Enter a valid page range for this PDF.", "Check options"); return; }
        }
        OptionsPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressBar.Value = 0;
        ProgressText.Text = "Preparing…";
        _cancel = new CancellationTokenSource();
        var progress = new Progress<JobProgress>(p => { ProgressBar.Value = p.Total == 0 ? 0 : 100.0 * p.Completed / p.Total; ProgressText.Text = $"Processing {p.Completed} / {p.Total}: {p.CurrentFile}"; });
        try
        {
            var result = await _processor.RunAsync(_operation.Value, _files, options, progress, _cancel.Token);
            _lastResults = result;
            ResultSummary.Text = $"{result.Count(r => r.Success)} succeeded · {result.Count(r => !r.Success)} failed";
            ResultsList.ItemsSource = result.Select(r => r.Success ? $"✓ {Path.GetFileName(r.Output)}  ({FormatBytes(r.OutputBytes)})" : $"✕ {Path.GetFileName(r.Source)}: {r.Error}").ToList();
            ProgressPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            ResultSummary.Text = "Operation cancelled. Any completed outputs remain in the output folder.";
            ResultsList.ItemsSource = null;
            ProgressPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            ResultSummary.Text = "Could not complete the operation. Check the output folder and try again.";
            ResultsList.ItemsSource = null;
            ProgressPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
        }
        finally { PasswordBox.Clear(); ConfirmPasswordBox.Clear(); _cancel.Dispose(); _cancel = null; }
    }

    private static string ComboText(System.Windows.Controls.ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
    private static string FormatBytes(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576.0:0.0} MB" : $"{bytes / 1024.0:0.0} KB";
    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancel?.Cancel();
    private void Back_Click(object sender, RoutedEventArgs e) => ShowActions();
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not SelectedFile file) return;
        _files.Remove(file);
        FileList.Items.Refresh();
        ShowActions();
        SelectionSummary.Text = _files.Count == 0 ? "Drop files here or select files" : $"{_files.Count} selected";
    }
    private void Clear_Click(object sender, RoutedEventArgs e) { _files.Clear(); AddPaths([]); }
    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Select files" };
        if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames);
    }
    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose output folder" };
        if (dialog.ShowDialog(this) == true) OutputBox.Text = dialog.FolderName;
    }
    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(OutputBox.Text)) Process.Start(new ProcessStartInfo(OutputBox.Text) { UseShellExecute = true });
    }
    private void ResultsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        int index = ResultsList.SelectedIndex;
        if (index < 0 || index >= _lastResults.Count || !_lastResults[index].Success) return;
        var path = _lastResults[index].Output;
        if (path is null || !File.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { MessageBox.Show(this, "Windows could not open this file. Use Open output folder to find it.", "Open result"); }
    }
    private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityLabel is not null) QualityLabel.Text = ((int)e.NewValue).ToString();
    }
    private void Window_DragOver(object sender, System.Windows.DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
    }
}
