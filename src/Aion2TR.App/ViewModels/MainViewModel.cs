using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Aion2TR.App.Common;
using Aion2TR.App.Services;
using Aion2TR.V2.RuntimeInstaller;
using Microsoft.Win32;

namespace Aion2TR.App.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly RuntimeInstallerEngine _installerEngine;
    private readonly IProcessDetector _processDetector;
    private readonly AppSettings _settings;

    // View State
    private bool _isBusy;
    private string _busyText = "Hazırlanıyor...";
    private double _progressValue;
    private bool _isIndeterminateProgress = true;

    // Game Detection
    private bool _gameFound;
    private string _gamePath = "Aranıyor...";
    private bool _isGameRunning;

    // Patch Status
    private RuntimeStatusCode _currentStatusCode = RuntimeStatusCode.UNKNOWN;
    private string _statusTitle = "Kontrol ediliyor...";
    private string _statusDescription = "Oyun ve yama durumu taranıyor...";
    private string _statusBadgeColor = "#94A3B8";

    // Primary Action
    private string _primaryButtonText = "Türkçe Yamayı Kur";
    private bool _canInstall;
    private bool _canUpdate;
    private bool _canRemove;
    private bool _canRepair;
    private bool _canLaunch;

    // Notifications / Toasts
    private bool _showNotification;
    private string _notificationMessage = string.Empty;
    private string _notificationType = "Info"; // "Success", "Warning", "Info"

    // Settings Modal
    private bool _showSettingsPopup;

    // Versioning
    public string AppVersion => "1.0.1";
    public string PatchVersion => "1.0.1";
    public string AppVersionText => $"AION2TR v{AppVersion}";
    public string PatchVersionText => $"v{PatchVersion}";

    // Message box delegate for UI testability
    public Func<string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult> ShowMessage { get; set; } =
        (msg, title, btn, img) => MessageBox.Show(msg, title, btn, img);

    // Commands
    public ICommand PrimaryActionCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand RepairCommand { get; }
    public ICommand LaunchGameCommand { get; }
    public ICommand SelectGameFolderCommand { get; }
    public ICommand RefreshStatusCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand DismissNotificationCommand { get; }
    public ICommand OpenGitHubCommand { get; }
    public ICommand MinimizeCommand { get; }
    public ICommand CloseCommand { get; }

    public MainViewModel(RuntimeInstallerEngine? installerEngine = null, IProcessDetector? processDetector = null)
    {
        _settings = UserSettingsService.Load();
        _processDetector = processDetector ?? new SystemProcessDetector();
        _installerEngine = installerEngine ?? new RuntimeInstallerEngine(processDetector: _processDetector);

        // Initialize Commands
        PrimaryActionCommand = new RelayCommand(async _ => await ExecutePrimaryActionAsync(), _ => !IsBusy);
        InstallCommand = new RelayCommand(async _ => await InstallPatchAsync(), _ => !IsBusy && CanInstall);
        UpdateCommand = new RelayCommand(async _ => await UpdatePatchAsync(), _ => !IsBusy && CanUpdate);
        RemoveCommand = new RelayCommand(async _ => await RemovePatchAsync(), _ => !IsBusy && CanRemove);
        RepairCommand = new RelayCommand(async _ => await RepairPatchAsync(), _ => !IsBusy && CanRepair);
        LaunchGameCommand = new RelayCommand(_ => LaunchGame(), _ => !IsBusy && CanLaunch);
        SelectGameFolderCommand = new RelayCommand(async _ => await SelectGameFolderAsync(), _ => !IsBusy);
        RefreshStatusCommand = new RelayCommand(async _ => await RefreshStatusAsync(), _ => !IsBusy);
        OpenLogsCommand = new RelayCommand(_ => AppLogger.OpenLogFile());
        OpenLogFolderCommand = new RelayCommand(_ => AppLogger.OpenLogFolder());
        OpenSettingsCommand = new RelayCommand(_ => ShowSettingsPopup = true);
        CloseSettingsCommand = new RelayCommand(_ => ShowSettingsPopup = false);
        DismissNotificationCommand = new RelayCommand(_ => ShowNotification = false);
        OpenGitHubCommand = new RelayCommand(_ => OpenGitHub());
        MinimizeCommand = new RelayCommand(_ => MinimizeWindow());
        CloseCommand = new RelayCommand(_ => CloseWindow());

        // Initial detection
        _ = InitializeAsync();
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanExecuteActions));
            }
        }
    }

    public string BusyText
    {
        get => _busyText;
        set => SetProperty(ref _busyText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public bool IsIndeterminateProgress
    {
        get => _isIndeterminateProgress;
        set => SetProperty(ref _isIndeterminateProgress, value);
    }

    public bool GameFound
    {
        get => _gameFound;
        set => SetProperty(ref _gameFound, value);
    }

    public string GamePath
    {
        get => _gamePath;
        set => SetProperty(ref _gamePath, value);
    }

    public bool IsGameRunning
    {
        get => _isGameRunning;
        set => SetProperty(ref _isGameRunning, value);
    }

    public string StatusTitle
    {
        get => _statusTitle;
        set => SetProperty(ref _statusTitle, value);
    }

    public string StatusDescription
    {
        get => _statusDescription;
        set => SetProperty(ref _statusDescription, value);
    }

    public string StatusBadgeColor
    {
        get => _statusBadgeColor;
        set => SetProperty(ref _statusBadgeColor, value);
    }

    public string PrimaryButtonText
    {
        get => _primaryButtonText;
        set => SetProperty(ref _primaryButtonText, value);
    }

    public bool CanInstall
    {
        get => _canInstall;
        set => SetProperty(ref _canInstall, value);
    }

    public bool CanUpdate
    {
        get => _canUpdate;
        set => SetProperty(ref _canUpdate, value);
    }

    public bool CanRemove
    {
        get => _canRemove;
        set => SetProperty(ref _canRemove, value);
    }

    public bool CanRepair
    {
        get => _canRepair;
        set => SetProperty(ref _canRepair, value);
    }

    public bool CanLaunch
    {
        get => _canLaunch;
        set => SetProperty(ref _canLaunch, value);
    }

    private string? _detectedSteamAppId;
    public string? DetectedSteamAppId
    {
        get => _detectedSteamAppId;
        set
        {
            if (SetProperty(ref _detectedSteamAppId, value))
            {
                OnPropertyChanged(nameof(CurrentLaunchUri));
            }
        }
    }

    public string? CurrentLaunchUri => !string.IsNullOrEmpty(DetectedSteamAppId)
        ? $"steam://rungameid/{DetectedSteamAppId}"
        : null;

    public string GameStatusText => GameFound ? "AION 2 bulundu" : "AION 2 bulunamadı";
    public string GameLocationActionText => GameFound ? "Konumu Değiştir" : "Oyun Klasörünü Seç";
    public string VersionRowText => $"AION2TR  v{AppVersion}  /  Yama {PatchVersion}";

    public string StatusDeckTitle
    {
        get
        {
            return _currentStatusCode switch
            {
                RuntimeStatusCode.INSTALLED_VALID => "TÜRKÇE YAMA HAZIR",
                RuntimeStatusCode.NOT_INSTALLED => "TÜRKÇE YAMA KURULU DEĞİL",
                RuntimeStatusCode.INSTALLED_MODIFIED => "YENİ YAMA HAZIR",
                RuntimeStatusCode.MIXED_STATE or RuntimeStatusCode.PARTIAL_INSTALL => "ONARIM GEREKİYOR",
                _ => IsGameRunning ? "AION 2 ÇALIŞIYOR" : (GameFound ? "DURUM BELİRSİZ" : "AION 2 BULUNAMADI")
            };
        }
    }

    public string PrimaryButtonTextUpper => PrimaryButtonText?.ToUpperInvariant() ?? "";

    public bool ShowIntroAnimation
    {
        get => _settings.ShowIntroAnimation;
        set
        {
            if (_settings.ShowIntroAnimation != value)
            {
                _settings.ShowIntroAnimation = value;
                UserSettingsService.Save(_settings);
                OnPropertyChanged();
            }
        }
    }

    public bool IsPlayAction => CanLaunch && (PrimaryButtonText == "Oyunu Başlat" || PrimaryButtonTextUpper == "OYUNU BAŞLAT");

    public bool CanExecuteActions => !IsBusy;

    public bool ShowNotification
    {
        get => _showNotification;
        set => SetProperty(ref _showNotification, value);
    }

    public string NotificationMessage
    {
        get => _notificationMessage;
        set => SetProperty(ref _notificationMessage, value);
    }

    public string NotificationType
    {
        get => _notificationType;
        set => SetProperty(ref _notificationType, value);
    }

    public bool ShowSettingsPopup
    {
        get => _showSettingsPopup;
        set => SetProperty(ref _showSettingsPopup, value);
    }

    public async Task InitializeAsync()
    {
        AppLogger.Info("Initializing AION2TR MainViewModel...");

        // 1. Check custom path from settings
        if (!string.IsNullOrEmpty(_settings.CustomGamePath) && _installerEngine.IsValidGameRoot(_settings.CustomGamePath))
        {
            GamePath = _settings.CustomGamePath;
            GameFound = true;
            AppLogger.Info($"Using saved custom game path: {GamePath}");
        }
        else
        {
            // 2. Auto-detect from Steam
            string? detected = _installerEngine.AutoDetectGameRoot();
            if (!string.IsNullOrEmpty(detected) && _installerEngine.IsValidGameRoot(detected))
            {
                GamePath = detected;
                GameFound = true;
                AppLogger.Info($"Auto-detected AION2 at: {GamePath}");
            }
            else
            {
                GamePath = "AION2 otomatik olarak bulunamadı.";
                GameFound = false;
                AppLogger.Warn("AION2 game directory could not be auto-detected.");
            }
        }

        await RefreshStatusAsync();
    }

    public async Task RefreshStatusAsync()
    {
        if (IsBusy) return;

        try
        {
            IsGameRunning = _processDetector.IsGameRunning();

            if (!GameFound || !_installerEngine.IsValidGameRoot(GamePath))
            {
                _currentStatusCode = RuntimeStatusCode.UNKNOWN;
                StatusTitle = "AION2 Bulunamadı";
                StatusDescription = "Türkçe yamayı kurabilmek için lütfen AION2 oyun klasörünü seçin.";
                StatusBadgeColor = "#94A3B8"; // Muted
                PrimaryButtonText = "Oyun Klasörünü Seç";
                CanInstall = false;
                CanUpdate = false;
                CanRemove = false;
                CanRepair = false;
                CanLaunch = false;
                return;
            }

            if (IsGameRunning)
            {
                StatusTitle = "AION2 Şu Anda Çalışıyor";
                StatusDescription = "Yamayı kurmak, güncellemek veya kaldırmak için oyunu kapatın.";
                StatusBadgeColor = "#F59E0B"; // Amber
                PrimaryButtonText = "Tekrar Kontrol Et";
                CanInstall = false;
                CanUpdate = false;
                CanRemove = false;
                CanRepair = false;
                CanLaunch = false;
                return;
            }

            // Read actual physical status from runtime engine
            var status = await Task.Run(() => _installerEngine.GetStatus(GamePath));
            _currentStatusCode = status.Code;
            AppLogger.Info($"Physical game status code: {status.Code} ({status.Message})");

            switch (status.Code)
            {
                case RuntimeStatusCode.NOT_INSTALLED:
                    StatusTitle = "Türkçe Yama Kurulu Değil";
                    StatusDescription = "AION2 için 100% Türkçe yama kurulumu hazır.";
                    StatusBadgeColor = "#38BDF8"; // Sky Blue
                    PrimaryButtonText = "Türkçe Yamayı Kur";
                    CanInstall = true;
                    CanUpdate = false;
                    CanRemove = false;
                    CanRepair = false;
                    CanLaunch = false;
                    break;

                case RuntimeStatusCode.INSTALLED_VALID:
                    StatusTitle = "Türkçe Yama Kurulu";
                    StatusBadgeColor = "#10B981"; // Emerald Green
                    CanInstall = false;
                    CanUpdate = false;
                    CanRemove = true;
                    CanRepair = false;

                    // Dynamically derive trustworthy Steam AppID
                    DetectedSteamAppId = _installerEngine.DetectSteamAppId(GamePath);
                    if (!string.IsNullOrEmpty(DetectedSteamAppId))
                    {
                        CanLaunch = true;
                        PrimaryButtonText = "Oyunu Başlat";
                        StatusDescription = $"Yama sürümü: {PatchVersion} — Oyun Türkçe olarak oynamaya hazır.";
                    }
                    else
                    {
                        CanLaunch = false;
                        PrimaryButtonText = "Durumu Yenile";
                        StatusDescription = $"Yama sürümü: {PatchVersion} — Türkçe yama kurulu. Oyunu Steam kütüphanenizden başlatabilirsiniz.";
                    }
                    break;

                case RuntimeStatusCode.INSTALLED_MODIFIED:
                    StatusTitle = "Yama Güncellemesi Mevcut";
                    StatusDescription = "Yeni bir Türkçe yama sürümü hazır. Tek tıkla güncelleyebilirsiniz.";
                    StatusBadgeColor = "#F59E0B"; // Amber
                    PrimaryButtonText = "Yamayı Güncelle";
                    CanInstall = false;
                    CanUpdate = true;
                    CanRemove = true;
                    CanRepair = false;
                    CanLaunch = false;
                    break;

                case RuntimeStatusCode.MIXED_STATE:
                case RuntimeStatusCode.PARTIAL_INSTALL:
                    StatusTitle = "Yama Dosyalarında Onarım Gerekiyor";
                    StatusDescription = "Oyun dosyalarında tutarsızlık tespit edildi. Otomatik onarım ile orijinal temiz duruma döndürebilirsiniz.";
                    StatusBadgeColor = "#EF4444"; // Rose Red
                    PrimaryButtonText = "Dosyaları Onar";
                    CanInstall = false;
                    CanUpdate = false;
                    CanRemove = false;
                    CanRepair = true;
                    CanLaunch = false;
                    break;

                default:
                    StatusTitle = "Durum Belirsiz";
                    StatusDescription = status.Message;
                    StatusBadgeColor = "#94A3B8";
                    PrimaryButtonText = "Tekrar Kontrol Et";
                    CanInstall = false;
                    CanUpdate = false;
                    CanRemove = false;
                    CanRepair = false;
                    CanLaunch = false;
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Error refreshing status", ex);
            StatusTitle = "Hata Oluştu";
            StatusDescription = "Durum kontrol edilirken beklenmeyen bir hata oluştu.";
            StatusBadgeColor = "#EF4444";
        }
        finally
        {
            OnPropertyChanged(nameof(GameStatusText));
            OnPropertyChanged(nameof(GameLocationActionText));
            OnPropertyChanged(nameof(VersionRowText));
            OnPropertyChanged(nameof(StatusDeckTitle));
            OnPropertyChanged(nameof(PrimaryButtonTextUpper));
            OnPropertyChanged(nameof(IsPlayAction));
        }
    }

    private async Task ExecutePrimaryActionAsync()
    {
        if (!GameFound)
        {
            await SelectGameFolderAsync();
            return;
        }

        if (IsGameRunning)
        {
            await RefreshStatusAsync();
            return;
        }

        if (CanInstall)
        {
            await InstallPatchAsync();
        }
        else if (CanUpdate)
        {
            await UpdatePatchAsync();
        }
        else if (CanRepair)
        {
            await RepairPatchAsync();
        }
        else if (CanLaunch)
        {
            LaunchGame();
        }
        else
        {
            await RefreshStatusAsync();
        }
    }

    public async Task InstallPatchAsync()
    {
        if (IsBusy || !CanInstall) return;

        AppLogger.Info("Initiating patch installation workflow...");
        IsBusy = true;
        BusyText = "Hazırlanıyor...";
        IsIndeterminateProgress = false;
        ProgressValue = 10;

        try
        {
            var preflight = await Task.Run(() => _installerEngine.RunPreflight(GamePath));
            if (!preflight.CanInstall)
            {
                string issueMsg = string.Join("; ", preflight.Issues);
                AppLogger.Warn($"Preflight failed: {issueMsg}");
                ShowToast("Kurulum gereksinimleri karşılanamadı: " + issueMsg, "Warning");
                return;
            }

            var progress = new Progress<(string Message, double Progress)>(update =>
            {
                BusyText = update.Message;
                ProgressValue = update.Progress * 100.0;
            });

            await Task.Run(() => _installerEngine.ExecuteInstall(preflight, progress));

            AppLogger.Info("Patch successfully installed and physically verified on disk.");
            ShowToast("Türkçe yama başarıyla kuruldu!", "Success");
            ShowMessage(
                "AION2 Türkçe yaması başarıyla kuruldu!\n\n" +
                "Artık oyunu doğrudan başlatarak Türkçe oynayabilirsiniz.",
                "Kurulum Tamamlandı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Install failed with automatic rollback", ex);
            ShowToast("Kurulum tamamlanamadı. Oyun dosyaları güvenli şekilde eski durumuna geri döndürüldü.", "Warning");
            ShowMessage(
                "Kurulum tamamlanamadı.\n\n" +
                "Güvenli işlem mekanizması devreye girdi ve oyun dosyalarınız otomatik olarak önceki orijinal durumuna geri döndürüldü.\n\n" +
                $"Ayrıntı: {ex.Message}",
                "Kurulum Başarısız",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
            await RefreshStatusAsync();
        }
    }

    public async Task UpdatePatchAsync()
    {
        if (IsBusy || !CanUpdate) return;

        AppLogger.Info("Initiating patch upgrade workflow...");
        IsBusy = true;
        BusyText = "Güncelleme hazırlanıyor...";
        IsIndeterminateProgress = false;
        ProgressValue = 10;

        try
        {
            var progress = new Progress<(string Message, double Progress)>(update =>
            {
                BusyText = update.Message;
                ProgressValue = update.Progress * 100.0;
            });

            await Task.Run(() => _installerEngine.ExecuteUpgrade(GamePath, progress));

            AppLogger.Info("Patch upgrade completed successfully.");
            ShowToast("Türkçe yama en güncel sürüme yükseltildi!", "Success");
            ShowMessage(
                "Türkçe yama başarıyla güncellendi!\n\n" +
                "Tüm güncel çeviriler oyuna entegre edildi.",
                "Güncelleme Başarılı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Upgrade failed", ex);
            ShowToast("Güncelleme tamamlanamadı. Önceki çalışan yama geri yüklendi.", "Warning");
            ShowMessage(
                "Güncelleme tamamlanamadı.\n\n" +
                "Önceki çalışan yama durumunuz güvenle korundu ve geri yüklendi.\n\n" +
                $"Ayrıntı: {ex.Message}",
                "Güncelleme Başarısız",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
            await RefreshStatusAsync();
        }
    }

    public async Task RemovePatchAsync()
    {
        if (IsBusy || !CanRemove) return;

        var confirm = ShowMessage(
            "Türkçe yamayı kaldırmak istediğinizden emin misiniz?\n\n" +
            "Orijinal temiz oyun dosyaları geri yüklenecektir.",
            "Yamayı Kaldır",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            AppLogger.Info("Patch removal canceled by user.");
            return;
        }

        AppLogger.Info("Initiating patch removal workflow...");
        IsBusy = true;
        BusyText = "Yama kaldırılıyor...";
        IsIndeterminateProgress = false;
        ProgressValue = 10;

        try
        {
            var progress = new Progress<(string Message, double Progress)>(update =>
            {
                BusyText = update.Message;
                ProgressValue = update.Progress * 100.0;
            });

            await Task.Run(() => _installerEngine.ExecuteRollback(GamePath, null, progress));

            AppLogger.Info("Patch removed successfully. Clean official game restored.");
            ShowToast("Türkçe yama kaldırıldı. Orijinal oyun dosyaları geri yüklendi.", "Info");
            ShowMessage(
                "Türkçe yama başarıyla kaldırıldı.\n\n" +
                "Orijinal oyun dosyalarınız eksiksiz olarak geri yüklendi.",
                "Kaldırma Başarılı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Remove patch failed", ex);
            ShowToast("Yama kaldırılırken hata oluştu: " + ex.Message, "Warning");
            ShowMessage(
                $"Yama kaldırılırken bir sorun oluştu:\n{ex.Message}\n\nLütfen 'Dosyaları Onar' seçeneğini deneyin.",
                "Hata",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
            await RefreshStatusAsync();
        }
    }

    public async Task RepairPatchAsync()
    {
        if (IsBusy || !CanRepair) return;

        var confirm = ShowMessage(
            "Yama dosyalarında tutarsızlık tespit edildi.\n\n" +
            "Otomatik onarım ile orijinal temiz oyun dosyaları geri yüklenecektir.\n\n" +
            "Devam etmek istiyor musunuz?",
            "Dosyaları Onar",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        AppLogger.Info("Initiating repair workflow...");
        IsBusy = true;
        BusyText = "Dosyalar onarılıyor...";
        IsIndeterminateProgress = false;
        ProgressValue = 10;

        try
        {
            var progress = new Progress<(string Message, double Progress)>(update =>
            {
                BusyText = update.Message;
                ProgressValue = update.Progress * 100.0;
            });

            await Task.Run(() => _installerEngine.ExecuteRepair(GamePath, progress));

            AppLogger.Info("Repair completed successfully. Clean official state achieved.");
            ShowToast("Onarım tamamlandı. Oyun temiz duruma getirildi.", "Success");
            ShowMessage(
                "Onarım işlemi başarıyla tamamlandı.\n\n" +
                "Oyun temiz resmi duruma getirildi. Artık Türkçe yamayı yeniden güvenle kurabilirsiniz.",
                "Onarım Başarılı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Repair failed", ex);
            ShowToast("Onarım tamamlanamadı: " + ex.Message, "Warning");
            ShowMessage(
                $"Onarım sırasında bir hata oluştu:\n{ex.Message}",
                "Onarım Başarısız",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
            await RefreshStatusAsync();
        }
    }

    public void LaunchGame()
    {
        if (string.IsNullOrEmpty(DetectedSteamAppId))
        {
            AppLogger.Warn("LaunchGame requested but no trustworthy Steam AppID was detected.");
            ShowToast("Steam AppID tespit edilemedi. Lütfen oyunu doğrudan Steam üzerinden başlatın.", "Warning");
            return;
        }

        AppLogger.Info($"User requested game launch for Steam AppID: {DetectedSteamAppId}");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"steam://rungameid/{DetectedSteamAppId}",
                UseShellExecute = true
            });
            ShowToast("AION 2 Steam üzerinden başlatılıyor...", "Info");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to launch via Steam protocol: {ex.Message}. Suggesting manual launch.");
            ShowMessage(
                "Anti-cheat ve oturum doğrulaması için oyunu doğrudan Steam kütüphanenizden başlatmanız önerilir.",
                "Oyunu Başlat",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    public void OpenGitHub()
    {
        string repoUrl = !string.IsNullOrWhiteSpace(_settings.GitHubRepositoryUrl)
            ? _settings.GitHubRepositoryUrl
            : "https://github.com/furkantecir";

        try
        {
            Process.Start(new ProcessStartInfo { FileName = repoUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to open GitHub URL: {ex.Message}");
            ShowToast("GitHub sayfası açılamadı.", "Warning");
        }
    }

    public void MinimizeWindow()
    {
        try
        {
            if (Application.Current?.MainWindow != null)
            {
                Application.Current.MainWindow.WindowState = WindowState.Minimized;
            }
        }
        catch { }
    }

    public void CloseWindow()
    {
        try
        {
            Application.Current?.MainWindow?.Close();
        }
        catch { }
    }

    public async Task SelectGameFolderAsync()
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "AION2 Oyun Klasörünü Seçin"
            };

            if (Directory.Exists(GamePath))
            {
                dialog.InitialDirectory = GamePath;
            }

            if (dialog.ShowDialog() == true)
            {
                string selected = dialog.FolderName;
                if (_installerEngine.IsValidGameRoot(selected))
                {
                    GamePath = selected;
                    GameFound = true;
                    _settings.CustomGamePath = selected;
                    UserSettingsService.Save(_settings);
                    AppLogger.Info($"User selected valid game path: {selected}");
                    ShowToast("AION2 oyun klasörü başarıyla güncellendi.", "Success");
                    await RefreshStatusAsync();
                }
                else
                {
                    AppLogger.Warn($"Invalid game folder selected: {selected}");
                    ShowMessage(
                        "Seçilen klasörde geçerli bir AION2 kurulumu bulunamadı.\n\n" +
                        "Lütfen içinde 'Aion2' ve 'Paks' dizinleri bulunan ana oyun klasörünü seçin.",
                        "Geçersiz Klasör",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Folder picker error", ex);
        }
    }

    private CancellationTokenSource? _toastCts;

    public void ShowToast(string message, string type = "Info")
    {
        NotificationMessage = message;
        NotificationType = type;
        ShowNotification = true;

        _toastCts?.Cancel();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3500, token);
                if (!token.IsCancellationRequested)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            ShowNotification = false;
                        }
                    });
                }
            }
            catch (TaskCanceledException) { }
        });
    }
}
