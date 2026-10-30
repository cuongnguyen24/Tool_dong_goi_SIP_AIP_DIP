using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using AXE.PackagingTool.Core;
using AXE.PackagingTool.Models;
using ClosedXML.Excel;
using Microsoft.Win32;

namespace AXE.PackagingTool.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
	private bool _isSipSelected = true;

	private bool _isAipSelected;

	private bool _isDipSelected;

	private string _groupingColumn = "FileCodehs";

	private string _excelFilePath = "";

	private string _baseFolderPath = "";

	private string _outputFolderPath = "";

	private int _progress;

	private string _statusText = "Sẵn sàng.";

	private bool _isBusy;

	private string _packageButtonText = "\ud83d\udce6  Đóng gói";

	private bool _syncCover = true;

	private bool _syncTableOfContents = true;

	private bool _hasErrors;

	private int _totalProfiles;

	private int _successProfiles;

	private int _errorProfiles;

	private int _successRows;

	private int _errorRows;

	private int _packagedProfiles;

	private CancellationTokenSource? _cts;

	private List<ProfileDTO>? _lastProcessedProfiles;

	private static readonly object _logLock = new object();

	public bool IsSipSelected
	{
		get
		{
			return _isSipSelected;
		}
		set
		{
			_isSipSelected = value;
			OnPropertyChanged("IsSipSelected");
		}
	}

	public bool IsAipSelected
	{
		get
		{
			return _isAipSelected;
		}
		set
		{
			_isAipSelected = value;
			OnPropertyChanged("IsAipSelected");
		}
	}

	public bool IsDipSelected
	{
		get
		{
			return _isDipSelected;
		}
		set
		{
			_isDipSelected = value;
			OnPropertyChanged("IsDipSelected");
		}
	}

	public PackageType CurrentPackageType
	{
		get
		{
			if (!IsAipSelected)
			{
				if (!IsDipSelected)
				{
					return PackageType.SIP;
				}
				return PackageType.DIP;
			}
			return PackageType.AIP;
		}
	}

	public string GroupingColumn
	{
		get
		{
			return _groupingColumn;
		}
		set
		{
			_groupingColumn = value;
			OnPropertyChanged("GroupingColumn");
		}
	}

	public string ExcelFilePath
	{
		get
		{
			return _excelFilePath;
		}
		set
		{
			_excelFilePath = value;
			OnPropertyChanged("ExcelFilePath");
			CommandManager.InvalidateRequerySuggested();
		}
	}

	public string BaseFolderPath
	{
		get
		{
			return _baseFolderPath;
		}
		set
		{
			_baseFolderPath = value;
			OnPropertyChanged("BaseFolderPath");
			CommandManager.InvalidateRequerySuggested();
		}
	}

	public string OutputFolderPath
	{
		get
		{
			return _outputFolderPath;
		}
		set
		{
			_outputFolderPath = value;
			OnPropertyChanged("OutputFolderPath");
			CommandManager.InvalidateRequerySuggested();
		}
	}

	public int Progress
	{
		get
		{
			return _progress;
		}
		set
		{
			_progress = value;
			OnPropertyChanged("Progress");
		}
	}

	public string StatusText
	{
		get
		{
			return _statusText;
		}
		set
		{
			_statusText = value;
			OnPropertyChanged("StatusText");
		}
	}

	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		set
		{
			_isBusy = value;
			OnPropertyChanged("IsBusy");
			CommandManager.InvalidateRequerySuggested();
		}
	}

	public string PackageButtonText
	{
		get
		{
			return _packageButtonText;
		}
		set
		{
			_packageButtonText = value;
			OnPropertyChanged("PackageButtonText");
		}
	}

	public bool SyncCover
	{
		get
		{
			return _syncCover;
		}
		set
		{
			_syncCover = value;
			OnPropertyChanged("SyncCover");
		}
	}

	public bool SyncTableOfContents
	{
		get
		{
			return _syncTableOfContents;
		}
		set
		{
			_syncTableOfContents = value;
			OnPropertyChanged("SyncTableOfContents");
		}
	}

	public bool HasErrors
	{
		get
		{
			return _hasErrors;
		}
		set
		{
			_hasErrors = value;
			OnPropertyChanged("HasErrors");
		}
	}

	public int TotalProfiles
	{
		get
		{
			return _totalProfiles;
		}
		set
		{
			_totalProfiles = value;
			OnPropertyChanged("TotalProfiles");
		}
	}

	public int SuccessProfiles
	{
		get
		{
			return _successProfiles;
		}
		set
		{
			_successProfiles = value;
			OnPropertyChanged("SuccessProfiles");
		}
	}

	public int ErrorProfiles
	{
		get
		{
			return _errorProfiles;
		}
		set
		{
			_errorProfiles = value;
			OnPropertyChanged("ErrorProfiles");
		}
	}

	public int SuccessRows
	{
		get
		{
			return _successRows;
		}
		set
		{
			_successRows = value;
			OnPropertyChanged("SuccessRows");
		}
	}

	public int ErrorRows
	{
		get
		{
			return _errorRows;
		}
		set
		{
			_errorRows = value;
			OnPropertyChanged("ErrorRows");
		}
	}

	public int PackagedProfiles
	{
		get
		{
			return _packagedProfiles;
		}
		set
		{
			_packagedProfiles = value;
			OnPropertyChanged("PackagedProfiles");
		}
	}

	public ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();

	public ICollectionView ErrorLogView { get; }

	public ICommand DownloadTemplateCommand { get; }

	public ICommand BrowseExcelCommand { get; }

	public ICommand BrowseBaseFolderCommand { get; }

	public ICommand BrowseOutputCommand { get; }

	public ICommand ValidateCommand { get; }

	public ICommand PackageCommand { get; }

	public ICommand CancelCommand { get; }

	public ICommand OpenOutputCommand { get; }

	public ICommand ClearLogCommand { get; }

	public ICommand ExportExcelCommand { get; }

	public ICommand ExportSuccessExcelCommand { get; }

	public ICommand ExportSummaryCommand { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	public MainViewModel()
	{
		DownloadTemplateCommand = new RelayCommand(delegate
		{
			DownloadTemplate();
		});
		BrowseExcelCommand = new RelayCommand(delegate
		{
			BrowseExcel();
		});
		BrowseBaseFolderCommand = new RelayCommand(delegate
		{
			BrowseBaseFolder();
		});
		BrowseOutputCommand = new RelayCommand(delegate
		{
			BrowseOutput();
		});
		ValidateCommand = new RelayCommand(delegate
		{
			RunValidateAsync();
		}, (object? _) => CanRun());
		PackageCommand = new RelayCommand(delegate
		{
			RunPackageAsync();
		}, (object? _) => CanRun());
		CancelCommand = new RelayCommand(delegate
		{
			_cts?.Cancel();
		}, (object? _) => IsBusy);
		OpenOutputCommand = new RelayCommand(delegate
		{
			OpenOutput();
		}, (object? _) => !string.IsNullOrEmpty(OutputFolderPath));
		ClearLogCommand = new RelayCommand(delegate
		{
			LogEntries.Clear();
		});
		ExportExcelCommand = new RelayCommand(delegate
		{
			ExportLogToExcel();
		}, (object? _) => LogEntries.Any((LogEntry e) => e.Level == LogLevel.Error || e.Level == LogLevel.Warning));
		ExportSuccessExcelCommand = new RelayCommand(delegate
		{
			ExportSuccessLogToExcel();
		}, (object? _) => _lastProcessedProfiles != null && _lastProcessedProfiles.Count > 0);
		ExportSummaryCommand = new RelayCommand(delegate
		{
			ExportSummaryToExcel();
		}, (object? _) => _lastProcessedProfiles != null && _lastProcessedProfiles.Count > 0);
		ErrorLogView = CollectionViewSource.GetDefaultView(LogEntries);
		ErrorLogView.Filter = (object item) => item is LogEntry logEntry && (logEntry.Level == LogLevel.Error || logEntry.Level == LogLevel.Warning);
	}

	private void DownloadTemplate()
	{
		Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
		{
			Title = "Lưu file template",
			Filter = "Excel files|*.xlsx",
			FileName = "template_" + CurrentPackageType.ToString().ToLower() + ".xlsx"
		};
		if (saveFileDialog.ShowDialog() != true)
		{
			return;
		}
		try
		{
			string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MauExcel", "Template.xlsx");
			if (!File.Exists(text))
			{
				Log("❌ Không tìm thấy file mẫu gốc tại: " + text, LogLevel.Error);
				return;
			}
			File.Copy(text, saveFileDialog.FileName, overwrite: true);
			Log("✅ Đã tải template: " + saveFileDialog.FileName, LogLevel.Success);
		}
		catch (Exception ex)
		{
			Log("❌ Lỗi tạo template: " + ex.Message, LogLevel.Error);
		}
	}

	private void ExportLogToExcel()
	{
		Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
		{
			Title = "Xuất file Log",
			Filter = "Excel files|*.xlsx",
			FileName = $"LogLoi_DongGoi_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
		};
		if (saveFileDialog.ShowDialog() != true)
		{
			return;
		}
		try
		{
			using XLWorkbook xLWorkbook = new XLWorkbook();
			IXLWorksheet iXLWorksheet = xLWorkbook.Worksheets.Add("Log_Loi");
			iXLWorksheet.Cell(1, 1).Value = "Thời gian";
			iXLWorksheet.Cell(1, 2).Value = "Dòng số";
			iXLWorksheet.Cell(1, 3).Value = "Mã Hồ Sơ";
			iXLWorksheet.Cell(1, 4).Value = "Mã Tài Liệu";
			iXLWorksheet.Cell(1, 5).Value = "Trạng thái";
			iXLWorksheet.Cell(1, 6).Value = "Chi tiết lỗi";
			IXLRange iXLRange = iXLWorksheet.Range(1, 1, 1, 6);
			iXLRange.Style.Font.Bold = true;
			iXLRange.Style.Fill.BackgroundColor = XLColor.LightGray;
			int num = 2;
			foreach (LogEntry item in LogEntries.Where((LogEntry e) => e.Level == LogLevel.Error || e.Level == LogLevel.Warning))
			{
				iXLWorksheet.Cell(num, 1).Value = item.Timestamp;
				iXLWorksheet.Cell(num, 2).Value = item.ExcelRowIndex;
				iXLWorksheet.Cell(num, 3).Value = item.ProfileCode;
				iXLWorksheet.Cell(num, 4).Value = item.DocumentCode;
				iXLWorksheet.Cell(num, 5).Value = item.StatusName;
				iXLWorksheet.Cell(num, 6).Value = item.Message;
				num++;
			}
			iXLWorksheet.Columns().AdjustToContents();
			xLWorkbook.SaveAs(saveFileDialog.FileName);
			Log("✅ Đã xuất log thành công ra: " + saveFileDialog.FileName, LogLevel.Success);
		}
		catch (Exception ex)
		{
			Log("❌ Lỗi xuất Excel: " + ex.Message, LogLevel.Error);
		}
	}

	private void ExportSuccessLogToExcel()
	{
		if (_lastProcessedProfiles == null || _lastProcessedProfiles.Count == 0)
		{
			Log("❌ Không có dữ liệu để xuất! Vui lòng nạp và kiểm tra dữ liệu trước.", LogLevel.Warning);
			return;
		}
		Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
		{
			Title = "Xuất file Log",
			Filter = "Excel files|*.xlsx",
			FileName = $"LogThanhCong_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
		};
		if (saveFileDialog.ShowDialog() != true)
		{
			return;
		}
		try
		{
			using XLWorkbook xLWorkbook = new XLWorkbook();
			IXLWorksheet iXLWorksheet = xLWorkbook.Worksheets.Add("Log_ThanhCong");
			iXLWorksheet.Cell(1, 1).Value = "Thời gian";
			iXLWorksheet.Cell(1, 2).Value = "STT";
			iXLWorksheet.Cell(1, 3).Value = "Mã Hồ Sơ";
			iXLWorksheet.Cell(1, 4).Value = "Mã Tài Liệu";
			iXLWorksheet.Cell(1, 5).Value = "Path";
			IXLRange iXLRange = iXLWorksheet.Range(1, 1, 1, 5);
			iXLRange.Style.Font.Bold = true;
			iXLRange.Style.Fill.BackgroundColor = XLColor.LightGray;
			int num = 2;
			int num2 = 1;
			string text = DateTime.Now.ToString("HH:mm:ss");
			foreach (ProfileDTO lastProcessedProfile in _lastProcessedProfiles)
			{
				foreach (DocumentDTO item in lastProcessedProfile.Documents.Where((DocumentDTO d) => d.IsPdfValid))
				{
					iXLWorksheet.Cell(num, 1).Value = text;
					iXLWorksheet.Cell(num, 2).Value = num2++;
					iXLWorksheet.Cell(num, 3).Value = lastProcessedProfile.ProfileCode;
					iXLWorksheet.Cell(num, 4).Value = item.DocumentCode;
					iXLWorksheet.Cell(num, 5).Value = item.SourcePdfPath;
					num++;
				}
			}
			iXLWorksheet.Columns().AdjustToContents();
			xLWorkbook.SaveAs(saveFileDialog.FileName);
			Log("✅ Đã xuất log thành công ra: " + saveFileDialog.FileName, LogLevel.Success);
		}
		catch (Exception ex)
		{
			Log("❌ Lỗi xuất Excel: " + ex.Message, LogLevel.Error);
		}
	}

	private void ExportSummaryToExcel()
	{
		if (_lastProcessedProfiles == null || _lastProcessedProfiles.Count == 0)
		{
			return;
		}
		Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
		{
			Title = "Xuất file thống kê",
			Filter = "Excel files|*.xlsx",
			FileName = $"ThongKe_DongGoi_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
		};
		if (saveFileDialog.ShowDialog() != true)
		{
			return;
		}
		try
		{
			using XLWorkbook xLWorkbook = new XLWorkbook();
			IXLWorksheet iXLWorksheet = xLWorkbook.Worksheets.Add("Thong_Ke");
			iXLWorksheet.Cell(1, 1).Value = "STT";
			iXLWorksheet.Cell(1, 2).Value = "Mã Hồ Sơ";
			iXLWorksheet.Cell(1, 3).Value = "SL Tài liệu (trong Excel)";
			iXLWorksheet.Cell(1, 4).Value = "SL Hợp lệ";
			iXLWorksheet.Cell(1, 5).Value = "SL Lỗi";
			iXLWorksheet.Cell(1, 6).Value = "Chi tiết lỗi (Dòng số)";
			IXLRange iXLRange = iXLWorksheet.Range(1, 1, 1, 6);
			iXLRange.Style.Font.Bold = true;
			iXLRange.Style.Fill.BackgroundColor = XLColor.LightGray;
			int num = 2;
			int num2 = 1;
			foreach (ProfileDTO lastProcessedProfile in _lastProcessedProfiles)
			{
				int count = lastProcessedProfile.Documents.Count;
				int num3 = lastProcessedProfile.Documents.Count((DocumentDTO d) => d.IsPdfValid);
				int num4 = count - num3;
				List<int> list = (from d in lastProcessedProfile.Documents
					where !d.IsPdfValid
					select d.ExcelRowIndex).ToList();
				string text = ((list.Count > 0) ? string.Join(", ", list) : "");
				iXLWorksheet.Cell(num, 1).Value = num2++;
				iXLWorksheet.Cell(num, 2).Value = lastProcessedProfile.ProfileCode;
				iXLWorksheet.Cell(num, 3).Value = count;
				iXLWorksheet.Cell(num, 4).Value = num3;
				iXLWorksheet.Cell(num, 5).Value = num4;
				iXLWorksheet.Cell(num, 6).Value = text;
				iXLWorksheet.Cell(num, 4).Style.Font.FontColor = XLColor.DarkGreen;
				if (num4 > 0)
				{
					iXLWorksheet.Cell(num, 5).Style.Font.FontColor = XLColor.DarkRed;
					iXLWorksheet.Cell(num, 6).Style.Font.FontColor = XLColor.DarkRed;
				}
				num++;
			}
			iXLWorksheet.Columns().AdjustToContents();
			xLWorkbook.SaveAs(saveFileDialog.FileName);
			Log("✅ Đã xuất thống kê thành công ra: " + saveFileDialog.FileName, LogLevel.Success);
		}
		catch (Exception ex)
		{
			Log("❌ Lỗi xuất thống kê Excel: " + ex.Message, LogLevel.Error);
		}
	}

	private void BrowseExcel()
	{
		Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
		{
			Title = "Chọn file Excel",
			Filter = "Excel files|*.xlsx;*.xls"
		};
		if (openFileDialog.ShowDialog() == true)
		{
			ExcelFilePath = openFileDialog.FileName;
			Log("\ud83d\udcc4 File Excel: " + openFileDialog.FileName);
		}
	}

	private void BrowseBaseFolder()
	{
		FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
		{
			Description = "Chọn thư mục gốc chứa tài liệu"
		};
		if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
		{
			BaseFolderPath = folderBrowserDialog.SelectedPath;
			Log("\ud83d\udcc1 Thư mục gốc: " + folderBrowserDialog.SelectedPath);
		}
	}

	private void BrowseOutput()
	{
		FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
		{
			Description = "Chọn thư mục xuất file ZIP"
		};
		if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
		{
			OutputFolderPath = folderBrowserDialog.SelectedPath;
			Log("\ud83d\udcc2 Thư mục xuất: " + folderBrowserDialog.SelectedPath);
		}
	}

	private async void RunValidateAsync()
	{
		_cts = new CancellationTokenSource();
		IsBusy = true;
		HasErrors = false;
		Progress = 0;
		LogEntries.Clear();
		try
		{
			Log("\ud83d\udd0d Bắt đầu xác thực...");
			HasErrors = !(await Task.Run(() => ValidateInternal(), _cts.Token));
			Progress = 100;
			StatusText = (HasErrors ? $"❌ Xác thực thất bại — {ErrorProfiles} hồ sơ lỗi" : $"✅ Xác thực thành công — {SuccessProfiles} hồ sơ chuẩn");
		}
		catch (OperationCanceledException)
		{
			Log("⛔ Đã hủy.", LogLevel.Warning);
			StatusText = "Đã hủy.";
		}
		catch (Exception ex2)
		{
			Log("❌ Lỗi: " + ex2.Message, LogLevel.Error);
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
		IsBusy = true;
		PackageButtonText = "⏳ Đang đóng gói...";
		HasErrors = false;
		Progress = 0;
		PackagedProfiles = 0;
		LogEntries.Clear();
		Progress<(int Percent, string Message)> progressReporter = new Progress<(int, string)>(delegate((int Percent, string Message) p)
		{
			Progress = p.Percent;
			StatusText = p.Message;
		});
		try
		{
			Log("▶ Bắt đầu đóng gói...");
			List<string> list = await Task.Run(async () => await PackageInternalAsync(progressReporter, _cts.Token));
			Progress = 100;
			StatusText = $"✅ Hoàn tất — {list.Count} file ZIP";
			foreach (string item in list)
			{
				Log("\ud83d\udce6 Xuất: " + item, LogLevel.Success);
			}
		}
		catch (OperationCanceledException)
		{
			Log("⛔ Đã hủy.", LogLevel.Warning);
			StatusText = "Đã hủy.";
		}
		catch (Exception ex2)
		{
			Log("❌ Lỗi đóng gói: " + ex2, LogLevel.Error);
			StatusText = "Lỗi đóng gói.";
		}
		finally
		{
			IsBusy = false;
			PackageButtonText = "\ud83d\udce6  Đóng gói";
		}
	}

	private bool ValidateInternal()
	{
		List<ProfileDTO> list = new DynamicExcelReaderService(delegate(string m, LogLevel lvl, string p, string d, string r)
		{
			Log(m, lvl, p, d, r);
		}).ReadExcel(ExcelFilePath, GroupingColumn);
		bool result = new DynamicValidator(BaseFolderPath, delegate(string m, LogLevel lvl, string p, string d, string r)
		{
			Log(m, lvl, p, d, r);
		}).Validate(list);
		_lastProcessedProfiles = list;
		System.Windows.Application.Current.Dispatcher.InvokeAsync(delegate
		{
			CommandManager.InvalidateRequerySuggested();
		});
		TotalProfiles = list.Count;
		SuccessProfiles = list.Count((ProfileDTO p) => p.Documents.All((DocumentDTO d) => d.IsPdfValid));
		ErrorProfiles = list.Count((ProfileDTO p) => p.Documents.Any((DocumentDTO d) => !d.IsPdfValid));
		SuccessRows = list.Sum((ProfileDTO p) => p.Documents.Count((DocumentDTO d) => d.IsPdfValid));
		ErrorRows = list.Sum((ProfileDTO p) => p.Documents.Count((DocumentDTO d) => !d.IsPdfValid));
		return result;
	}

	private async Task<List<string>> PackageInternalAsync(IProgress<(int, string)> progress, CancellationToken ct)
	{
		List<ProfileDTO> list = new DynamicExcelReaderService(delegate(string m, LogLevel lvl, string p, string d, string r)
		{
			Log(m, lvl, p, d, r);
		}).ReadExcel(ExcelFilePath, GroupingColumn);
		bool num = new DynamicValidator(BaseFolderPath, delegate(string m, LogLevel lvl, string p, string d, string r)
		{
			Log(m, lvl, p, d, r);
		}).Validate(list);
		_lastProcessedProfiles = list;
		System.Windows.Application.Current.Dispatcher.InvokeAsync(delegate
		{
			CommandManager.InvalidateRequerySuggested();
		});
		TotalProfiles = list.Count;
		SuccessProfiles = list.Count((ProfileDTO p) => p.Documents.All((DocumentDTO d) => d.IsPdfValid));
		ErrorProfiles = list.Count((ProfileDTO p) => p.Documents.Any((DocumentDTO d) => !d.IsPdfValid));
		SuccessRows = list.Sum((ProfileDTO p) => p.Documents.Count((DocumentDTO d) => d.IsPdfValid));
		ErrorRows = list.Sum((ProfileDTO p) => p.Documents.Count((DocumentDTO d) => !d.IsPdfValid));
		if (!num)
		{
			Log("⚠\ufe0f Có một số file PDF không hợp lệ nhưng sẽ tiếp tục đóng gói các file tồn tại.", LogLevel.Warning);
		}
		DynamicPackageBuilder builder = new DynamicPackageBuilder(progress, delegate(string m, LogLevel lvl, string p, string d, string r)
		{
			Log(m, lvl, p, d, r);
		});
		List<string> outputFiles = new List<string>();
		int total = list.Count;
		int idx = 0;
		int packagedCount = 0;
		foreach (ProfileDTO profile in list)
		{
			ct.ThrowIfCancellationRequested();
			if (profile.Documents.Count > 0 && profile.Documents.All((DocumentDTO d) => !d.IsPdfValid))
			{
				Log("⚠\ufe0f Bỏ qua hồ sơ do không có tài liệu hợp lệ nào.", LogLevel.Warning, profile.ProfileCode);
				continue;
			}
			progress.Report((idx * 100 / total, $"Đang xử lý: {profile.ProfileCode} ({idx + 1}/{total})"));
			int submissionOrdinal = packagedCount + 1;
			outputFiles.Add(await Task.Run(() => builder.Build(profile, CurrentPackageType, OutputFolderPath, submissionOrdinal)));
			idx++;
			int currentCount;
			packagedCount = (currentCount = packagedCount + 1);
			System.Windows.Application.Current.Dispatcher.InvokeAsync(delegate
			{
				PackagedProfiles = currentCount;
			});
			progress.Report((idx * 100 / total, $"Hoàn thành {idx}/{total} gói"));
		}
		return outputFiles;
	}

	private bool CanRun()
	{
		if (!IsBusy && !string.IsNullOrEmpty(ExcelFilePath) && !string.IsNullOrEmpty(BaseFolderPath))
		{
			return !string.IsNullOrEmpty(OutputFolderPath);
		}
		return false;
	}

	private void OpenOutput()
	{
		if (!string.IsNullOrEmpty(OutputFolderPath) && Directory.Exists(OutputFolderPath))
		{
			Process.Start("explorer.exe", OutputFolderPath);
		}
	}

	public void Log(string message, LogLevel level = LogLevel.Info, string profileCode = "", string docCode = "", string rowIndex = "")
	{
		LogEntry entry = new LogEntry(message, level, profileCode, docCode, rowIndex);
		if (System.Windows.Application.Current.Dispatcher.CheckAccess())
		{
			LogEntries.Add(entry);
			CommandManager.InvalidateRequerySuggested();
			Task.Run(delegate
			{
				try
				{
					string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
					if (!Directory.Exists(text))
					{
						Directory.CreateDirectory(text);
					}
					string path = $"Log_{DateTime.Now:yyyyMMdd}.txt";
					string path2 = Path.Combine(text, path);
					string contents = $"[{entry.Timestamp}] [{level.ToString().ToUpper()}] " + ((!string.IsNullOrEmpty(profileCode)) ? ("[HS: " + profileCode + "] ") : "") + ((!string.IsNullOrEmpty(docCode)) ? ("[TL: " + docCode + "] ") : "") + ((!string.IsNullOrEmpty(rowIndex)) ? ("[Row: " + rowIndex + "] ") : "") + message + Environment.NewLine;
					lock (_logLock)
					{
						File.AppendAllText(path2, contents);
					}
				}
				catch
				{
				}
			});
		}
		else
		{
			System.Windows.Application.Current.Dispatcher.InvokeAsync(delegate
			{
				Log(message, level, profileCode, docCode, rowIndex);
			});
		}
	}

	private void AppendLog(string msg, LogLevel level)
	{
		Log(msg, level);
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
