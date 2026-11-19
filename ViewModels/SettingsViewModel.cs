using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using TaskList.Data;
using TaskList.Models;
using TaskList.Services;

namespace TaskList.ViewModels;

public sealed class SoundSettingViewModel : ObservableObject
{
    private string? _fileName;
    private string _displayName = string.Empty;
    private double _durationSeconds;

    public SoundSettingViewModel(TaskWeight weight, string title, string description)
    {
        Weight = weight;
        Title = title;
        Description = description;
    }

    public TaskWeight Weight { get; }
    public string Title { get; }
    public string Description { get; }
    public string? FileName => _fileName;
    public bool IsCustom => _fileName is not null;
    public string DisplayName => IsCustom ? _displayName : "内置音效";
    public string Detail => IsCustom
        ? $"{_durationSeconds.ToString("0.##", CultureInfo.CurrentCulture)} 秒 · 已复制到应用数据目录"
        : Description;

    public void Apply(string? fileName, string displayName = "", double durationSeconds = 0)
    {
        _fileName = fileName;
        _displayName = displayName;
        _durationSeconds = durationSeconds;
        RaiseAll();
    }
}

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private sealed record ImportedSound(string FileName, string DisplayName, double DurationSeconds);

    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _importCancellation;
    private bool _soundEnabled;
    private CelebrationEffect _selectedEffect;
    private TaskWeight _previewWeight = TaskWeight.Medium;
    private bool _isImporting;
    private bool _isPreviewing;
    private bool _disposed;
    private string _statusMessage = "设置自动保存，下一次完成任务时生效。";

    public SettingsViewModel()
    {
        Sounds = new[]
        {
            new SoundSettingViewModel(TaskWeight.Light, "轻型任务", "内置：轻快的双音提示"),
            new SoundSettingViewModel(TaskWeight.Medium, "中型任务", "内置：明亮的三音和弦"),
            new SoundSettingViewModel(TaskWeight.Heavy, "重型任务", "内置：饱满的庆祝旋律")
        };
        var settings = TaskRepository.GetSettings();
        _soundEnabled = settings.GetValueOrDefault("SoundEnabled", "1") == "1";
        SoundService.Enabled = _soundEnabled;
        var effect = settings.GetValueOrDefault("CelebrationEffect", nameof(CelebrationEffect.Original));
        _selectedEffect = Enum.TryParse<CelebrationEffect>(effect, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : CelebrationEffect.Original;
        CelebrationService.Effect = _selectedEffect;
        foreach (var sound in Sounds) LoadSound(sound, settings.GetValueOrDefault(SoundKey(sound), string.Empty));
        SoundService.PlaybackFailed += OnPlaybackFailed;
        CelebrationService.PlaybackFailed += OnPlaybackFailed;

        ImportSoundCommand = new RelayCommand(async p => await ImportSoundAsync(p), _ => !_disposed && !IsImporting);
        ResetSoundCommand = new RelayCommand(ResetSound, p => !_disposed && !IsImporting && p is SoundSettingViewModel { IsCustom: true });
        PreviewSoundCommand = new RelayCommand(p =>
        {
            if (p is SoundSettingViewModel sound)
            {
                StatusMessage = $"正在试听{sound.Title}：{sound.DisplayName}";
                SoundService.Preview(sound.Weight);
            }
        }, _ => !_disposed && !IsImporting);
        StopSoundCommand = new RelayCommand(() => SoundService.Stop());
        CancelImportCommand = new RelayCommand(() => _importCancellation?.Cancel(), () => IsImporting);
        PreviewEffectCommand = new RelayCommand(async () => await PreviewEffectAsync(), () => !_disposed && !IsPreviewing);
    }

    public IReadOnlyList<SoundSettingViewModel> Sounds { get; }
    public ICommand ImportSoundCommand { get; }
    public ICommand ResetSoundCommand { get; }
    public ICommand PreviewSoundCommand { get; }
    public ICommand StopSoundCommand { get; }
    public ICommand CancelImportCommand { get; }
    public ICommand PreviewEffectCommand { get; }
    public string SoundsDirectory => Path.Combine(Database.DataDirectory, "Sounds");

    public bool SoundEnabled
    {
        get => _soundEnabled;
        set
        {
            if (_soundEnabled == value || _disposed) return;
            try
            {
                TaskRepository.SetSetting("SoundEnabled", value ? "1" : "0");
                SetProperty(ref _soundEnabled, value);
                SoundService.Enabled = value;
                StatusMessage = value ? "完成音效已开启。" : "完成音效已关闭，视觉特效仍会播放。";
            }
            catch (Exception ex) { ReportSaveFailure(ex, nameof(SoundEnabled)); }
        }
    }

    public CelebrationEffect SelectedEffect
    {
        get => _selectedEffect;
        set
        {
            if (_selectedEffect == value || _disposed || !Enum.IsDefined(value)) return;
            try
            {
                TaskRepository.SetSetting("CelebrationEffect", value.ToString());
                SetProperty(ref _selectedEffect, value);
                CelebrationService.Effect = value;
                StatusMessage = value switch
                {
                    CelebrationEffect.Applause => "已选择鼓掌特效。",
                    CelebrationEffect.MinimalCheck => "已选择极简勾选特效。",
                    CelebrationEffect.CompletionStamp => "已选择完成印章特效。",
                    CelebrationEffect.Starlight => "已选择星光汇聚特效。",
                    CelebrationEffect.Fireworks => "已选择礼花绽放特效。",
                    _ => "已选择原有分级特效。"
                };
            }
            catch (Exception ex) { ReportSaveFailure(ex, nameof(SelectedEffect)); }
        }
    }

    public TaskWeight PreviewWeight
    {
        get => _previewWeight;
        set => SetProperty(ref _previewWeight, value);
    }

    public bool IsImporting
    {
        get => _isImporting;
        private set
        {
            if (SetProperty(ref _isImporting, value)) CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsPreviewing
    {
        get => _isPreviewing;
        private set
        {
            if (SetProperty(ref _isPreviewing, value)) CommandManager.InvalidateRequerySuggested();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public void Deactivate()
    {
        _importCancellation?.Cancel();
        SoundService.Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        SoundService.PlaybackFailed -= OnPlaybackFailed;
        CelebrationService.PlaybackFailed -= OnPlaybackFailed;
    }

    private static string SoundKey(SoundSettingViewModel sound) => $"CompletionSound.{sound.Weight}";

    private static bool IsManagedFileName(string fileName)
        => string.Equals(Path.GetExtension(fileName), ".mp3", StringComparison.OrdinalIgnoreCase)
           && string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
           && Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out _);

    private void LoadSound(SoundSettingViewModel sound, string json)
    {
        if (json.Length == 0) return;
        try
        {
            var imported = JsonSerializer.Deserialize<ImportedSound>(json);
            if (imported is null || !IsManagedFileName(imported.FileName) ||
                !double.IsFinite(imported.DurationSeconds) || imported.DurationSeconds <= 0 ||
                imported.DurationSeconds > SoundService.MaxDurationSeconds)
                throw new InvalidOperationException();
            sound.Apply(imported.FileName, imported.DisplayName, imported.DurationSeconds);
            var path = Path.Combine(SoundsDirectory, imported.FileName);
            SoundService.SetCustomSound(sound.Weight, path);
            if (!File.Exists(path)) StatusMessage = $"{sound.Title}的音频文件已丢失，播放时会使用内置音效。";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            StatusMessage = $"{sound.Title}的音频配置无效，已使用内置音效。";
        }
    }

    private async Task ImportSoundAsync(object? parameter)
    {
        if (parameter is not SoundSettingViewModel sound || IsImporting || _disposed) return;
        string? destination = null;
        bool committed = false;
        IsImporting = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _importCancellation = cancellation;
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = $"选择{sound.Title}的完成音效（不超过 10 秒）",
                Filter = "MP3 音频 (*.mp3)|*.mp3",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
            if (!string.Equals(Path.GetExtension(dialog.FileName), ".mp3", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请选择 MP3 文件。");

            SoundService.Stop();
            StatusMessage = "正在复制并检查 MP3，原音效配置暂时保持不变…";
            var fileName = $"{Guid.NewGuid():N}.mp3";
            destination = Path.Combine(SoundsDirectory, fileName);
            var target = destination;
            await Task.Run(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(SoundsDirectory);
                File.Copy(dialog.FileName, target, false);
            }, cancellation.Token);
            var duration = await SoundService.ValidateMp3Async(destination, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            var oldFileName = sound.FileName;
            var imported = new ImportedSound(fileName, Path.GetFileName(dialog.FileName), duration.TotalSeconds);
            TaskRepository.SetSetting(SoundKey(sound), JsonSerializer.Serialize(imported));
            committed = true;
            sound.Apply(fileName, imported.DisplayName, imported.DurationSeconds);
            SoundService.SetCustomSound(sound.Weight, destination);
            DeleteManagedFile(oldFileName);
            StatusMessage = $"已导入{sound.Title}音效：{imported.DisplayName}（{duration.TotalSeconds:0.##} 秒）。";
            CommandManager.InvalidateRequerySuggested();
        }
        catch (OperationCanceledException) { StatusMessage = "导入已取消，原音效配置保持不变。"; }
        catch (Exception ex) { StatusMessage = $"导入失败：{ex.Message}"; }
        finally
        {
            if (!committed && destination is not null) DeleteManagedFile(Path.GetFileName(destination));
            _importCancellation = null;
            IsImporting = false;
        }
    }

    private void ResetSound(object? parameter)
    {
        if (parameter is not SoundSettingViewModel sound || IsImporting || _disposed) return;
        try
        {
            var oldFileName = sound.FileName;
            TaskRepository.SetSetting(SoundKey(sound), string.Empty);
            SoundService.SetCustomSound(sound.Weight, null);
            sound.Apply(null);
            DeleteManagedFile(oldFileName);
            StatusMessage = $"{sound.Title}已恢复内置音效。";
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex) { StatusMessage = $"恢复失败：{ex.Message}"; }
    }

    private void DeleteManagedFile(string? fileName)
    {
        if (fileName is null || !IsManagedFileName(fileName) || Sounds.Any(s => s.FileName == fileName)) return;
        try { File.Delete(Path.Combine(SoundsDirectory, fileName)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task PreviewEffectAsync()
    {
        IsPreviewing = true;
        try
        {
            var window = Application.Current.MainWindow;
            await CelebrationService.PlayAsync(PreviewWeight, "为每一次完成喝彩",
                new Point(window.ActualWidth * 0.62, window.ActualHeight * 0.5));
        }
        catch (Exception ex) { StatusMessage = $"特效预览失败：{ex.Message}"; }
        finally { IsPreviewing = false; }
    }

    private void OnPlaybackFailed(string message) => StatusMessage = message;

    private void ReportSaveFailure(Exception exception, string propertyName)
    {
        StatusMessage = $"设置保存失败：{exception.Message}";
        OnPropertyChanged(propertyName);
    }
}
