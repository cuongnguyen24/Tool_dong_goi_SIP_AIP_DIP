using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AXE.PackagingTool.Core;
using AXE.PackagingTool.Models;
using Microsoft.Win32;

namespace AXE.PackagingTool.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    // ─── Package type ──────────────────────────────────────────────────────

    public IReadOnlyList<PackageTypeItem> PackageTypes { get; } = new[]
    {
        new PackageTypeItem(PackageType.AIP, "AIP — Gói lưu trữ bảo quản"),
        new PackageTypeItem(PackageType.SIP, "SIP — Gói nộp kho lưu trữ"),
        new PackageTypeItem(PackageType.DIP, "DIP — Gói phục vụ tra cứu"),
    };

    private PackageTypeItem _selectedPackageType;
    public PackageTypeItem SelectedPackageType
    {
        get => _selectedPackageType;
        set { _selectedPackageType = value; OnPropertyChanged(); }
    }

    // ─── Paths ────────────────────────────────────────────────────────────

    private string _excelFilePath = "";
    public string ExcelFilePath
    {
        get => _excelFilePath;
        set { _excelFilePath = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    private string _baseFolderPath = "";
    public string BaseFolderPath
    {
        get => _baseFolderPath;
        set { _baseFolderPath = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    private string _outputFolderPath = "";
    public string OutputFolderPath
    {
        get => _outputFolderPath;
        set { _outputFolderPath = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    // ─── Progress / Status ────────────────────────────────────────────────

    private int _progress;
    public int Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    private string _statusText = "Sẵn sàng.";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    private bool _hasErrors;
    public bool HasErrors
    {
        get => _hasErrors;
        set { _hasErrors = value; OnPropertyChanged(); }
    }

    // ─── Log ──────────────────────────────────────────────────────────────

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    private CancellationTokenSource? _cts;

    // ─── Commands ─────────────────────────────────────────────────────────

    public ICommand DownloadTemplateCommand { get; }
    public ICommand BrowseExcelCommand      { get; }
    public ICommand BrowseBaseFolderCommand { get; }
    public ICommand BrowseOutputCommand     { get; }
    public ICommand ValidateCommand         { get; }
    public ICommand PackageCommand          { get; }
    public ICommand CancelCommand           { get; }
    public ICommand OpenOutputCommand       { get; }
    public ICommand ClearLogCommand         { get; }

    // ─── Constructor ──────────────────────────────────────────────────────

    public MainViewModel()
    {
        _selectedPackageType = PackageTypes[0];

        DownloadTemplateCommand = new RelayCommand(_ => DownloadTemplate());
        BrowseExcelCommand      = new RelayCommand(_ => BrowseExcel());
        BrowseBaseFolderCommand = new RelayCommand(_ => BrowseBaseFolder());
        BrowseOutputCommand     = new RelayCommand(_ => BrowseOutput());
        ValidateCommand  = new RelayCommand(_ => RunValidateAsync(), _ => CanRun());
        PackageCommand   = new RelayCommand(_ => RunPackageAsync(),  _ => CanRun());
        CancelCommand    = new RelayCommand(_ => _cts?.Cancel(),     _ => IsBusy);
        OpenOutputCommand= new RelayCommand(_ => OpenOutput(),
                                            _ => !string.IsNullOrEmpty(OutputFolderPath));
        ClearLogCommand  = new RelayCommand(_ => LogEntries.Clear());
    }

    // ─── Command implementations ──────────────────────────────────────────

    private void DownloadTemplate()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Lưu file template",
            Filter     = "Excel files|*.xlsx",
            FileName   = $"template_{SelectedPackageType.Type.ToString().ToLower()}.xlsx",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            TemplateGenerator.Generate(SelectedPackageType.Type, dlg.FileName);
            Log($"✅ Đã tải template: {dlg.FileName}", LogLevel.Success);
        }
        catch (Exception ex)
        {
            Log($"❌ Lỗi tạo template: {ex.Message}", LogLevel.Error);
        }
    }

    private void BrowseExcel()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Chọn file Excel",
            Filter = "Excel files|*.xlsx;*.xls",
        };
        if (dlg.ShowDialog() == true)
        {
            ExcelFilePath = dlg.FileName;
            Log($"📄 File Excel: {dlg.FileName}", LogLevel.Info);
        }
    }

    private void BrowseBaseFolder()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Chọn thư mục gốc chứa tài liệu",
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            BaseFolderPath = dlg.SelectedPath;
            Log($"📁 Thư mục gốc: {dlg.SelectedPath}", LogLevel.Info);
        }
    }

    private void BrowseOutput()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Chọn thư mục xuất file ZIP",
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            OutputFolderPath = dlg.SelectedPath;
            Log($"📂 Thư mục xuất: {dlg.SelectedPath}", LogLevel.Info);
        }
    }

    private async void RunValidateAsync()
    {
        _cts = new CancellationTokenSource();
        IsBusy   = true;
        HasErrors = false;
        Progress  = 0;
        LogEntries.Clear();

        try
        {
            Log("🔍 Bắt đầu xác thực...", LogLevel.Info);
            var result = await Task.Run(() => ValidateInternal(), _cts.Token);

            HasErrors = result.HasErrors;
            Progress  = 100;

            foreach (var issue in result.Issues)
            {
                var lvl = issue.Severity == "ERROR" ? LogLevel.Error : LogLevel.Warning;
                Log(issue.ToString(), lvl);
            }

            StatusText = result.HasErrors
                ? $"❌ Xác thực thất bại — {result.Issues.Count(i => i.Severity == "ERROR")} lỗi"
                : $"✅ Xác thực thành công — {result.Issues.Count(i => i.Severity == "WARNING")} cảnh báo";
        }
        catch (OperationCanceledException)
        {
            Log("⛔ Đã hủy.", LogLevel.Warning);
            StatusText = "Đã hủy.";
        }
        catch (Exception ex)
        {
            Log($"❌ Lỗi: {ex.Message}", LogLevel.Error);
            StatusText = "Lỗi xác thực.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async void RunPackageAsync()
    {
        _cts = new CancellationTokenSource();
        IsBusy    = true;
        HasErrors = false;
        Progress  = 0;
        LogEntries.Clear();

        var progressReporter = new Progress<(int Percent, string Message)>(p =>
        {
            Progress   = p.Percent;
            StatusText = p.Message;
        });

        try
        {
            Log("▶ Bắt đầu đóng gói...", LogLevel.Info);

            var outputFiles = await PackageInternalAsync(progressReporter, _cts.Token);

            Progress   = 100;
            StatusText = $"✅ Hoàn tất — {outputFiles.Count} file ZIP";

            foreach (var f in outputFiles)
                Log($"📦 Xuất: {f}", LogLevel.Success);
        }
        catch (OperationCanceledException)
        {
            Log("⛔ Đã hủy.", LogLevel.Warning);
            StatusText = "Đã hủy.";
        }
        catch (Exception ex)
        {
            Log($"❌ Lỗi đóng gói: {ex.Message}", LogLevel.Error);
            StatusText = "Lỗi đóng gói.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ─── Core logic (runs on background thread) ───────────────────────────

    private PackagingResult ValidateInternal()
    {
        var reader    = new ExcelReader(m => AppendLog(m, LogLevel.Info));
        var data      = reader.Read(ExcelFilePath, SelectedPackageType.Type);
        var validator = new Validator(BaseFolderPath, m => AppendLog(m, LogLevel.Info));
        var issues    = validator.Validate(data);
        return new PackagingResult { Issues = issues };
    }

    private async Task<List<string>> PackageInternalAsync(
        IProgress<(int, string)> progress, CancellationToken ct)
    {
        var reader    = new ExcelReader(m => AppendLog(m, LogLevel.Info));
        var data      = reader.Read(ExcelFilePath, SelectedPackageType.Type);
        var validator = new Validator(BaseFolderPath, m => AppendLog(m, LogLevel.Info));
        var issues    = validator.Validate(data);

        // Resolve file paths trước khi build
        foreach (var fond in data.Fonds)
        {
            var allDocs = fond.CaseFiles.SelectMany(cf => cf.Documents)
                                        .Concat(fond.LooseDocuments);
            foreach (var doc in allDocs)
            {
                doc.ResolvedSourcePath = string.IsNullOrEmpty(doc.Path)
                    ? System.IO.Path.Combine(BaseFolderPath, doc.FileAttachment)
                    : System.IO.Path.Combine(BaseFolderPath, doc.Path);
            }
        }

        if (issues.Any(i => i.Severity == "ERROR"))
        {
            foreach (var err in issues.Where(i => i.Severity == "ERROR"))
                AppendLog(err.ToString(), LogLevel.Error);
            throw new InvalidOperationException(
                $"Có {issues.Count(i => i.Severity == "ERROR")} lỗi. Vui lòng sửa trước khi đóng gói.");
        }

        var builder     = new PackageBuilder(progress, m => AppendLog(m, LogLevel.Info));
        var outputFiles = new List<string>();

        int total = data.Fonds.Count;
        int idx   = 0;

        foreach (var fond in data.Fonds)
        {
            ct.ThrowIfCancellationRequested();
            AppendLog($"📦 Đóng gói phông: {fond.FondCode}", LogLevel.Info);

            var zipPath = await builder.BuildAsync(
                fond, data.PackageType, data.DipXacThuc,
                OutputFolderPath, ct);

            outputFiles.Add(zipPath);
            idx++;
            progress.Report((idx * 100 / total, $"Hoàn thành {idx}/{total} gói"));
        }

        return outputFiles;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private bool CanRun() =>
        !IsBusy
        && !string.IsNullOrEmpty(ExcelFilePath)
        && !string.IsNullOrEmpty(BaseFolderPath)
        && !string.IsNullOrEmpty(OutputFolderPath);

    private void OpenOutput()
    {
        if (!string.IsNullOrEmpty(OutputFolderPath)
            && System.IO.Directory.Exists(OutputFolderPath))
        {
            System.Diagnostics.Process.Start("explorer.exe", OutputFolderPath);
        }
    }

    private void Log(string msg, LogLevel level)
    {
        App.Current.Dispatcher.Invoke(() =>
            LogEntries.Add(new LogEntry(msg, level)));
    }

    private void AppendLog(string msg, LogLevel level) => Log(msg, level);

    // ─── INotifyPropertyChanged ───────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ─── Supporting types ─────────────────────────────────────────────────────

public record PackageTypeItem(PackageType Type, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public enum LogLevel { Info, Success, Warning, Error }

public class LogEntry
{
    public string   Message   { get; }
    public LogLevel Level     { get; }
    public string   Timestamp { get; }
    public string   Foreground => Level switch
    {
        LogLevel.Success => "#27AE60",
        LogLevel.Warning => "#E67E22",
        LogLevel.Error   => "#E74C3C",
        _                => "#ECF0F1",
    };

    public LogEntry(string message, LogLevel level)
    {
        Message   = message;
        Level     = level;
        Timestamp = DateTime.Now.ToString("HH:mm:ss");
    }
}

// ─── RelayCommand ────────────────────────────────────────────────────────

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute    = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter)    => _execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
