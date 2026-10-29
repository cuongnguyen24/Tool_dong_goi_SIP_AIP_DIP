using System;

namespace AXE.PackagingTool.ViewModels;

public class LogEntry
{
	public string Message { get; }

	public LogLevel Level { get; }

	public string Timestamp { get; }

	public string ProfileCode { get; }

	public string DocumentCode { get; }

	public string ExcelRowIndex { get; }

	public string StatusName => Level switch
	{
		LogLevel.Success => "Thành công", 
		LogLevel.Warning => "Cảnh báo", 
		LogLevel.Error => "Lỗi", 
		_ => "Thông tin", 
	};

	public string Foreground => Level switch
	{
		LogLevel.Success => "#27AE60", 
		LogLevel.Warning => "#E67E22", 
		LogLevel.Error => "#E74C3C", 
		_ => "#ECF0F1", 
	};

	public LogEntry(string message, LogLevel level, string profileCode = "", string documentCode = "", string rowIndex = "")
	{
		Message = message;
		Level = level;
		Timestamp = DateTime.Now.ToString("HH:mm:ss");
		ProfileCode = profileCode;
		DocumentCode = documentCode;
		ExcelRowIndex = rowIndex;
	}
}
