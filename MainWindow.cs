using System.Text.Json;
using System.Windows;
using SplateClientDesktop.Services;
using SplateClientDesktop.Models;
using SplateClientDesktop.ViewModels;
namespace SplateClientDesktop;
public partial class MainWindow : IDisposable
{
    private readonly MainViewModel _viewModel;
    private System.Windows.Threading.Dispatcher Dispatcher => Application.Current.Dispatcher;
    public Action<string> Send { get; set; } = _ => {};
    public Action<string> WindowCommand { get; set; } = _ => {};

    private readonly System.Collections.Generic.Dictionary<string, TaskCompletionSource<bool>> _confirmations = new();
    private double _lastDownBps = 0.0;
    private double _lastUpBps = 0.0;
    private readonly TrafficHistory _trafficHistory = new(Path.Combine(AppPaths.ConfigRoot, "SplateVPN", "traffic-history.json"));
    private System.Threading.CancellationTokenSource? _speedTestCts;
    private System.Threading.CancellationTokenSource? _aiDnsCts;
    private System.Threading.CancellationTokenSource? _aiCheckCts;
    private readonly System.Threading.CancellationTokenSource _adFilterLifetime = new();
    private string? _settingsRequestId;
    private int _settingsApplyVersion;
    private Task? _settingsApplyTask;
    private readonly System.Collections.Concurrent.ConcurrentQueue<object> _recentConnections = new();

    private void SubscribeToViewModelEvents()
    {
        _viewModel.ConnectionChanging += () => { _speedTestCts?.Cancel(); _aiDnsCts?.Cancel(); _aiCheckCts?.Cancel(); _aiMonitorCts?.Cancel(); };
        _viewModel.SubscriptionsUpdated += SendSubscriptionsToWeb;
        _viewModel.UiNotificationRequested += (title, message, kind) => PostWebMessage("notification", new { title, message, kind });
        _viewModel.ConfirmationRequested = async (title, message) =>
        {
            var id = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _confirmations[id] = completion;
            PostWebMessage("confirmation", new { id, title, message });
            try { return await completion.Task; }
            finally { _confirmations.Remove(id); }
        };
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsConnected))
            {
                if (_viewModel.IsConnected) { _trafficHistory.BeginSession(); while (_recentConnections.TryDequeue(out _)) { } }
                else { _speedTestCts?.Cancel(); try { _trafficHistory.Flush(); } catch (Exception ex) { PostWebMessage("notification", new { title = "Статистика", message = ex.Message, kind = "error" }); } }
            }
            switch (args.PropertyName)
            {
                case nameof(MainViewModel.IsAppUpdateAvailable):
                case nameof(MainViewModel.AppUpdateUrl):
                case nameof(MainViewModel.AppUpdateStatus):
                case nameof(MainViewModel.AppLatestVersion):
                case nameof(MainViewModel.IsCheckingAppUpdate):
                    SendAppUpdateToWeb();
                    break;
                case nameof(MainViewModel.IsConnected):
                case nameof(MainViewModel.IsConnecting):
                case nameof(MainViewModel.StatusText):
                case nameof(MainViewModel.ConnectionDetail):
                case nameof(MainViewModel.SessionDuration):
                case nameof(MainViewModel.EgressIp):
                case nameof(MainViewModel.EgressLocation):
                case nameof(MainViewModel.CountryName):
                case nameof(MainViewModel.CountryFlag):
                case nameof(MainViewModel.ConnectionRtt):
                case nameof(MainViewModel.SelectedServer):
                    SendFullStateToWeb();
                    break;

                case nameof(MainViewModel.Servers):
                    ObserveServers();
                    SendServersToWeb();
                    break;
                case nameof(MainViewModel.Subscriptions):
                    ObserveSubscriptions();
                    SendSubscriptionsToWeb();
                    break;
            }
        };

        ObserveServers();
        ObserveSubscriptions();

        _viewModel.ServersBulkUpdated += () =>
        {
            Dispatcher.InvokeAsync(SendServersToWeb);
            Dispatcher.InvokeAsync(SendSubscriptionsToWeb);
        };

        _viewModel.ServerPingUpdated += node =>
        {
            PostWebMessage("serverPing", new { id = node.Id, ping = node.Ping });
        };
        _viewModel.PingProgressUpdated += (completed, total, busy) =>
            PostWebMessage("pingProgress", new { completed, total, busy });

        _viewModel.LogMessageReceived += line =>
        {
            PostWebMessage("log", line);
            var match = System.Text.RegularExpressions.Regex.Match(line, @"accepted\s+(?<target>\S+).*?\[(?<route>[^\]]+)\]");
            if (match.Success)
            {
                _recentConnections.Enqueue(new { time = DateTime.Now.ToString("HH:mm:ss"), target = match.Groups["target"].Value, route = match.Groups["route"].Value });
                while (_recentConnections.Count > 200) _recentConnections.TryDequeue(out _);
            }
        };
        _viewModel.TrafficMonitor.SessionBytesUpdated += (down, up) =>
        {
            try { _trafficHistory.Record(down, up); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        };

        _viewModel.TrafficMonitor.BytesPerSecUpdated += (downBytes, upBytes) =>
        {
            _lastDownBps = downBytes;
            _lastUpBps = upBytes;

            PostWebMessage("traffic", new
            {
                downloadSpeed = _viewModel.DownloadSpeed,
                uploadSpeed = _viewModel.UploadSpeed,
                downloadBps = _lastDownBps,
                uploadBps = _lastUpBps,
                sessionDownloaded = _viewModel.SessionDownloaded
            });
        };
    }

    private System.Collections.Specialized.INotifyCollectionChanged? _observedServers;
    private System.Collections.Specialized.INotifyCollectionChanged? _observedSubscriptions;
    private void ObserveSubscriptions()
    {
        if (_observedSubscriptions != null) _observedSubscriptions.CollectionChanged -= OnSubscriptionsCollectionChanged;
        _observedSubscriptions = _viewModel.Subscriptions;
        _observedSubscriptions.CollectionChanged += OnSubscriptionsCollectionChanged;
    }
    private void OnSubscriptionsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => SendSubscriptionsToWeb();
    private void ObserveServers()
    {
        if (_observedServers != null) _observedServers.CollectionChanged -= OnServersCollectionChanged;
        _observedServers = _viewModel.Servers;
        _observedServers.CollectionChanged += OnServersCollectionChanged;
    }
    private void OnServersCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => ScheduleSendServersToWeb();

    public void Receive(string message)
    {
        try
        {

            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (!root.TryGetProperty("action", out var actionElem)) return;
            var action = actionElem.GetString();
            if (HandleNetworkTool(action, root)) return;

            switch (action)
            {
                case "ready":
                    SendAppUpdateToWeb();
                    SendFullStateToWeb();
                    SendServersToWeb();
                    SendSubscriptionsToWeb();
                    SendSettingsToWeb();
                    break;

                case "checkAppUpdates":
                    _ = RunWebOperationAsync("appUpdateCheck", _viewModel.CheckForAppUpdatesAsync);
                    break;
                case "openAppRelease":
                    if (UpdateCheckerService.IsReleaseUrl(_viewModel.AppUpdateUrl))
                        _viewModel.OpenUrlCommand.Execute(_viewModel.AppUpdateUrl);
                    break;
                case "trafficHistory":
                    PostWebMessage("trafficHistory", _trafficHistory.Days);
                    break;
                case "clearTrafficHistory":
                    _ = RunWebOperationAsync("clearTrafficHistory", async () =>
                    {
                        if (await _viewModel.ConfirmationRequested!("Очистить статистику", "Удалить историю трафика за все дни?"))
                        { _trafficHistory.Clear(); PostWebMessage("trafficHistory", _trafficHistory.Days); }
                    });
                    break;
                case "recentConnections":
                    PostWebMessage("recentConnections", _recentConnections.ToArray());
                    break;
                case "showWidget":
                    // An owned WPF window is automatically hidden when its owner is minimized.
                    WindowCommand("widget");
                    break;
                case "speedTest":
                    _ = RunWebOperationAsync("speedTest", RunSpeedTestAsync);
                    break;
                case "selectAiDns":
                    _ = RunWebOperationAsync("selectAiDns", SelectAiDnsAsync);
                    break;
                case "cancelAiDns":
                    _aiDnsCts?.Cancel();
                    break;
                case "updateAdFilters":
                    _ = RunWebOperationAsync("updateAdFilters", async () => {
                        var settings = _viewModel.Settings;
                        var ok = await AdFilterService.RefreshAsync(AppContext.BaseDirectory, settings.AdBlockAllowlist, _viewModel.IsConnected ? _viewModel.ActiveHttpPort : null, _adFilterLifetime.Token);
                        SendAdFilterState();
                        if (ok && _viewModel.IsConnected && settings.EnableAdBlock && _viewModel.SelectedServer is { } node) await _viewModel.SwitchServerAsync(node);
                    });
                    break;
                case "cancelSpeedTest":
                    _speedTestCts?.Cancel();
                    break;
                case "copyServer":
                case "shareServer":
                    if (root.TryGetProperty("id", out var shareId))
                    {
                        var node = _viewModel.Servers.FirstOrDefault(s => s.Id == shareId.GetString());
                        if (node == null) break;
                        var link = ServerShare.Link(node);
                        if (action == "copyServer") Clipboard.SetText(link);
                        else PostWebMessage("shareServer", new { id = node.Id, name = node.DisplayName, link, qr = ServerShare.Qr(link) });
                    }
                    break;
                case "exportServers":
                    var exportDialog = new Microsoft.Win32.SaveFileDialog { Filter = "Ссылки VPN (*.txt)|*.txt", FileName = "Splate-servers.txt" };
                    if (exportDialog.ShowDialog(this) == true) File.WriteAllLines(exportDialog.FileName, _viewModel.Servers.Select(s => ServerShare.Link(s)));
                    break;
                case "importLinks":
                    var importDialog = new Microsoft.Win32.OpenFileDialog { Filter = "Списки серверов (*.txt;*.conf)|*.txt;*.conf|Все файлы|*.*" };
                    if (importDialog.ShowDialog(this) == true)
                        _ = RunWebOperationAsync("importLinks", async () => await _viewModel.ProcessAnyLinkAsync(await File.ReadAllTextAsync(importDialog.FileName)));
                    break;
                case "scanQrScreen":
                    _ = RunWebOperationAsync("scanQrScreen", () => _viewModel.ScanQrFromScreenCommand.ExecuteAsync(null));
                    break;
                case "manualServer":
                    _viewModel.CreateServerCommand.Execute(null);
                    break;
                case "updateGeoAssets":
                    _ = RunWebOperationAsync("updateGeoAssets", () => _viewModel.UpdateGeoAssetsCommand.ExecuteAsync(null));
                    break;

                case "restorePresets":
                    _ = RunWebOperationAsync("restorePresets", _viewModel.RestorePresetSubscriptionsAsync);
                    break;
                case "subscriptionOptions":
                case "refreshOneSubscription":
                case "deleteSubscription":
                    if (root.TryGetProperty("id", out var subId))
                    {
                        var sub = _viewModel.Subscriptions.FirstOrDefault(s => s.Id == subId.GetString());
                        if (sub == null) break;
                        if (action == "subscriptionOptions")
                            _viewModel.SetSubscriptionOptions(sub,
                                root.TryGetProperty("enabled", out var enabled) ? enabled.GetBoolean() : null,
                                root.TryGetProperty("autoUpdate", out var auto) ? auto.GetBoolean() : null);
                        else if (action == "refreshOneSubscription")
                            _ = RunWebOperationAsync("subscription:" + sub.Id, () => _viewModel.UpdateSubscriptionCommand.ExecuteAsync(sub));
                        else _ = RunWebOperationAsync("deleteSubscription:" + sub.Id, () => _viewModel.DeleteSubscriptionCommand.ExecuteAsync(sub));
                    }
                    break;
                case "favoriteServer":
                    if (root.TryGetProperty("id", out var favoriteId))
                    {
                        var favorite = _viewModel.Servers.FirstOrDefault(s => s.Id == favoriteId.GetString());
                        if (favorite != null)
                        {
                            favorite.IsFavorite = !favorite.IsFavorite;
                            _viewModel.Storage.SaveServers(_viewModel.Servers.ToList());
                            SendServersToWeb();
                        }
                    }
                    break;
                case "serverPreferences":
                    if (root.TryGetProperty("grouped", out var grouped)) _viewModel.Settings.GroupServers = grouped.GetBoolean();
                    if (root.TryGetProperty("sort", out var sort)) _viewModel.Settings.ServerSort = sort.GetString() ?? "default";
                    if (root.TryGetProperty("filter", out var filter)) _viewModel.Settings.ServerFilter = filter.GetString() ?? "all";
                    if (root.TryGetProperty("source", out var group))
                    {
                        _viewModel.SelectedSubscriptionGroup = group.GetString() ?? "Все";
                        _viewModel.Settings.SelectedSubscriptionGroup = _viewModel.SelectedSubscriptionGroup;
                    }
                    _viewModel.Storage.SaveSettings(_viewModel.Settings);
                    break;

                case "windowAction":
                    if(root.TryGetProperty("command",out var command)) WindowCommand(command.GetString() ?? "");
                    break;

                case "checkAiConnectivity":
                    _ = RunWebOperationAsync("checkAiConnectivity", async () =>
                    {
                        using var cancellation = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
                        _aiCheckCts = cancellation;
                        try { var report = await _viewModel.CheckAiConnectivityAsync(cancellation.Token); PostWebMessage("aiConnectivity", new { message = report }); }
                        catch (OperationCanceledException) { PostWebMessage("aiConnectivity", new { message = "Проверка остановлена или соединение изменилось. VPN продолжает работать." }); }
                        catch (Exception ex) { PostWebMessage("aiConnectivity", new { message = "Ошибка проверки: " + ex.Message }); }
                        finally { _aiCheckCts = null; }
                    });
                    break;
                case "cancelAiConnectivity":
                    _aiCheckCts?.Cancel();
                    break;

                case "connectionDiagnostic":
                    _ = RunWebOperationAsync("connectionDiagnostic", async () =>
                        PostWebMessage("connectionDiagnostic", new { message = await _viewModel.GetConnectionDiagnosticAsync() }));
                    break;
                case "copyConnectionDiagnostic":
                    _ = RunWebOperationAsync("connectionDiagnostic", async () =>
                    {
                        var report = await _viewModel.GetConnectionDiagnosticAsync();
                        Clipboard.SetText(report);
                        PostWebMessage("notification", new { title = "Диагностика", message = "Отчёт скопирован.", kind = "info" });
                    });
                    break;

                case "confirmationResult":
                    if (root.TryGetProperty("id", out var confirmationId) &&
                        _confirmations.TryGetValue(confirmationId.GetString() ?? "", out var pending))
                        pending.TrySetResult(root.TryGetProperty("accepted", out var accepted) && accepted.GetBoolean());
                    break;

                case "toggleConnection":
                    _ = Dispatcher.InvokeAsync(() => _viewModel.ToggleConnectionCommand.Execute(null));
                    break;

                case "selectServer":

                    if (root.TryGetProperty("id", out var idElem))
                    {
                        var serverId = idElem.GetString();
                        var target = _viewModel.Servers.FirstOrDefault(s => s.Id == serverId);
                        if (target != null)
                        {
                            _ = SelectServerFromWebAsync(target);
                        }
                    }
                    break;

                case "pingAll":
                    _ = RunWebOperationAsync("pingAll", _viewModel.PingAllServersInternalAsync);
                    break;
                case "cancelPing":
                    _viewModel.CancelPing();
                    break;

                case "pingServer":
                    if (root.TryGetProperty("id", out var pingIdElem))
                    {
                        var serverId = pingIdElem.GetString();
                        var target = _viewModel.Servers.FirstOrDefault(s => s.Id == serverId);
                        if (target != null)
                        {
                            Dispatcher.Invoke(() => _viewModel.PingSingleServerCommand.Execute(target));
                        }
                    }
                    break;

                case "deleteServer":
                    if (root.TryGetProperty("id", out var delIdElem))
                    {
                        var serverId = delIdElem.GetString();
                        var target = _viewModel.Servers.FirstOrDefault(s => s.Id == serverId);
                        if (target != null)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                _viewModel.DeleteServerCommand.Execute(target);
                                SendServersToWeb();
                            });
                        }
                    }
                    break;

                case "addServer":
                    if (root.TryGetProperty("text", out var textElem))
                    {
                        var linkText = textElem.GetString();
                        var subscriptionName = root.TryGetProperty("subscriptionName", out var subName) ? subName.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(linkText))
                        {
                            _ = RunWebOperationAsync("addServer", async () =>
                            {
                                await _viewModel.ProcessAnyLinkAsync(linkText, subscriptionName: subscriptionName);
                                SendServersToWeb();
                            });
                        }
                    }
                    break;

                case "updateSubscription":
                    _ = RunWebOperationAsync("updateSubscriptions", () => _viewModel.UpdateAllSubscriptionsCommand.ExecuteAsync(null));
                    break;

                case "refreshIp":
                    Dispatcher.Invoke(() => _ = _viewModel.RefreshIpDetailsAsync());
                    break;

                case "setSetting":
                    if (root.TryGetProperty("key", out var keyElem) && root.TryGetProperty("value", out var valElem))
                    {
                        var key = keyElem.GetString();
                        Dispatcher.Invoke(() =>
                        {
                            var previousConnection = ConnectionSettings.Signature(_viewModel.Settings);
                            switch (key)
                            {
                                case "EnableTunMode":
                                    _viewModel.EnableTunMode = valElem.GetBoolean();
                                    break;
                                case "RoutingMode":
                                    _viewModel.SelectedRoutingMode = valElem.GetString() ?? "smart_ru";
                                    break;
                                case "EnableKillSwitch":
                                    _viewModel.EnableKillSwitch = valElem.GetBoolean();
                                    break;
                                case "AutoFallbackFastestServer":
                                    _viewModel.AutoFallbackFastestServer = valElem.GetBoolean();
                                    break;
                            }
                            SendSettingsToWeb();
                            QueueConnectionSettings(previousConnection);
                        });
                    }
                    break;

                case "saveSettings":
                    if (root.TryGetProperty("settings", out var settingsElem))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            var httpPort = settingsElem.TryGetProperty("httpPort", out var hp) ? hp.GetInt32() : _viewModel.Settings.HttpPort;
                            var socksPort = settingsElem.TryGetProperty("socksPort", out var sp) ? sp.GetInt32() : _viewModel.Settings.SocksPort;
                            if (httpPort == socksPort || httpPort is < 1 or > 65535 || socksPort is < 1 or > 65535)
                                throw new ArgumentException("Укажите разные HTTP/SOCKS порты от 1 до 65535.");
                            _settingsRequestId = root.TryGetProperty("settingsRequestId", out var requestId) ? requestId.GetString() : null;
                            var previousConnection = ConnectionSettings.Signature(_viewModel.Settings);
                            ApplyExtendedSettings(settingsElem);
                            _viewModel.Settings.HttpPort = httpPort;
                            _viewModel.Settings.SocksPort = socksPort;
                            if (settingsElem.TryGetProperty("theme", out var theme)) _viewModel.Settings.Theme = theme.GetString() ?? "dark";
                            if (settingsElem.TryGetProperty("customProxyRules", out var pr)) _viewModel.CustomProxyRules = pr.GetString() ?? "";
                            if (settingsElem.TryGetProperty("customDirectRules", out var dr)) _viewModel.CustomDirectRules = dr.GetString() ?? "";
                            if (settingsElem.TryGetProperty("enableSniffing", out var sn)) _viewModel.EnableSniffing = sn.GetBoolean();
                            _viewModel.Settings.EnableAiXboxDns = false;
                            _viewModel.EnableAiBypass = _viewModel.Settings.AiRoutingMode != "off";
                            if (settingsElem.TryGetProperty("enableTunMode", out var tun)) _viewModel.EnableTunMode = tun.GetBoolean();
                            if (settingsElem.TryGetProperty("xhttpModeOverride", out var xhttp)) _viewModel.XhttpModeOverride = xhttp.GetString() ?? "auto";
                            if (settingsElem.TryGetProperty("bufferSize", out var buf) && int.TryParse(buf.GetString(), out var bufSize)) _viewModel.Settings.BufferSizeKb = bufSize;
                            if (settingsElem.TryGetProperty("enableTlsFragment", out var frag)) _viewModel.EnableTlsFragment = frag.GetBoolean();
                            if (settingsElem.TryGetProperty("dnsProvider", out var dns)) _viewModel.SelectedDnsProvider = dns.GetString() ?? "cloudflare";
                            if (settingsElem.TryGetProperty("isFakeDns", out var fake)) _viewModel.IsFakeDns = fake.GetBoolean();
                            if (settingsElem.TryGetProperty("blockQuic", out var quic)) _viewModel.BlockQuic = quic.GetBoolean();
                            if (settingsElem.TryGetProperty("blockIpv6", out var ipv6)) _viewModel.BlockIpv6 = ipv6.GetBoolean();
                            if (settingsElem.TryGetProperty("enableKillSwitch", out var ks)) _viewModel.EnableKillSwitch = ks.GetBoolean();
                            if (settingsElem.TryGetProperty("enableDnsLeakProtection", out var dlp)) _viewModel.EnableDnsLeakProtection = dlp.GetBoolean();
                            if (settingsElem.TryGetProperty("autoStart", out var ast)) _viewModel.AutoStart = ast.GetBoolean();
                            if (settingsElem.TryGetProperty("minimizeToTray", out var mtt)) _viewModel.MinimizeToTray = mtt.GetBoolean();
                            if (settingsElem.TryGetProperty("autoFallbackFastestServer", out var afb)) _viewModel.AutoFallbackFastestServer = afb.GetBoolean();
                            if (settingsElem.TryGetProperty("autoUpdateSubscription", out var aus)) _viewModel.AutoUpdateSubscription = aus.GetBoolean();

                            if (!_viewModel.EnableTunMode || !_viewModel.EnableSniffing) _viewModel.IsFakeDns = false;
                            _viewModel.SaveSettingsCommand.Execute(null);
                            SendSettingsToWeb();
                            QueueConnectionSettings(previousConnection);
                        });
                    }
                    break;

                case "editServer":
                    if (root.TryGetProperty("id", out var editId))
                        _viewModel.EditServerCommand.Execute(_viewModel.Servers.FirstOrDefault(s => s.Id == editId.GetString()));
                    break;
                case "exportConfig":
                    _ = RunWebOperationAsync("exportConfig", () => _viewModel.ExportConfigCommand.ExecuteAsync(null));
                    break;
                case "importConfig":
                    _ = RunWebOperationAsync("importConfig", async () =>
                    {
                        await _viewModel.ImportConfigCommand.ExecuteAsync(null);
                        SendServersToWeb(); SendSettingsToWeb(); SendFullStateToWeb();
                    });
                    break;
                case "scanQr":
                    _ = RunWebOperationAsync("scanQr", () => _viewModel.ScanQrFromFileCommand.ExecuteAsync(null));
                    break;
                case "restartElevated":
                    PostWebMessage("notification", new {title="TUN",message="При подключении TUN система запросит права через polkit.",kind="info"});
                    break;
                case "autoSelectServer":
                    _ = RunWebOperationAsync("autoSelect", () => _viewModel.AutoSelectServerCommand.ExecuteAsync(null));
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IPC Error] {ex.Message}");
            PostWebMessage("settingsError", new { message = ex.Message });
        }
    }

    private readonly HashSet<string> _webOperations = new();
    private async Task RunWebOperationAsync(string operation, Func<Task> work)
    {
        if (!_webOperations.Add(operation)) return;
        PostWebMessage("operation", new { operation, busy = true });
        try { await work(); }
        catch (Exception ex) { PostWebMessage("notification", new { title = "Не удалось выполнить действие", message = ex.Message, kind = "error" }); }
        finally
        {
            _webOperations.Remove(operation);
            PostWebMessage("operation", new { operation, busy = false });
        }
    }

    private void PostWebMessage(string type, object payload)
    {
        var acknowledgement = type is "settings" or "extendedSettings" ? _settingsRequestId : null;
        if (Dispatcher.CheckAccess())
        {
            SendInternal();
        }
        else
        {
            Dispatcher.BeginInvoke(SendInternal);
        }

        void SendInternal() => Send(JsonSerializer.Serialize(new { type, payload, settingsRequestId = acknowledgement }));
    }

    private void SendFullStateToWeb()
    {
        var srv = _viewModel.SelectedServer;
        PostWebMessage("state", new
        {
            isConnected = _viewModel.IsConnected,
            activeTunMode = _viewModel.ActiveTunMode,
            activeAiXboxDns = _viewModel.ActiveAiXboxDns,
            activeAiDns = _viewModel.ActiveAiDns,
            activeAiMode = _viewModel.ActiveAiMode,
            activeRoutingMode = _viewModel.ActiveRoutingMode,
            activeRoutingRegion = _viewModel.ActiveRoutingRegion,
            pendingConnectionSettings = _viewModel.HasPendingConnectionSettings,
            activeAiDnsLabel = _viewModel.ActiveAiDnsLabel,
            isConnecting = _viewModel.IsConnecting,
            statusText = _viewModel.StatusText,
            statusDetail = _viewModel.ConnectionDetail,
            sessionDuration = _viewModel.SessionDuration,
            currentIp = _viewModel.EgressIp,
            currentLocation = _viewModel.EgressLocation,
            rtt = _viewModel.ConnectionRtt,
            selectedServer = srv != null ? new
            {
                id = srv.Id,
                displayName = srv.DisplayName,
                flag = srv.Flag,
                protocol = srv.Protocol,
                transport = srv.Network,
                security = srv.Security,
                subscriptionGroup = srv.SubscriptionGroup,
                ping = srv.Ping
            } : null
        });
    }

    private void SendAppUpdateToWeb() => PostWebMessage("appUpdate", new
    {
        currentVersion = UpdateCheckerService.CurrentVersion,
        available = _viewModel.IsAppUpdateAvailable,
        checking = _viewModel.IsCheckingAppUpdate,
        latestVersion = _viewModel.AppLatestVersion,
        status = _viewModel.AppUpdateStatus
    });

    private System.Threading.CancellationTokenSource? _sendServersDebounceCts;

    private void ScheduleSendServersToWeb()
    {
        _sendServersDebounceCts?.Cancel();
        _sendServersDebounceCts?.Dispose();
        _sendServersDebounceCts = new System.Threading.CancellationTokenSource();
        var token = _sendServersDebounceCts.Token;

        Task.Delay(60, token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && !token.IsCancellationRequested)
            {
                Dispatcher.InvokeAsync(SendServersToWeb);
            }
        }, TaskScheduler.Default);
    }

    private void SendServersToWeb()
    {
        var list = _viewModel.Servers.Select(s => new
        {
            id = s.Id,
            displayName = s.DisplayName,
            address = s.Address,
            port = s.Port,
            protocol = s.Protocol,
            transport = s.Network,
            security = s.Security,
            flag = s.Flag,
            ping = s.Ping,
            subscriptionId = s.SubscriptionId,
            subscriptionGroup = s.SubscriptionGroup
            ,
            isFavorite = s.IsFavorite,
            enabled = _viewModel.IsServerEnabled(s),
            isPreset = PresetSubscriptions.Catalog.Any(p => p.Id == s.SubscriptionId)
        }).ToList();

        PostWebMessage("servers", list);
    }

    private void SendSettingsToWeb()
    {
        NativeThemeService.Apply(_viewModel.Settings.Theme, _viewModel.Settings.GlassTransparency);
        PostWebMessage("smartDnsProviders", SmartDnsProviders.All);
        PostWebMessage("extendedSettings", _viewModel.Settings);
        SendProfiles();
        var endpoints = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("198.18.") && !a.Address.ToString().StartsWith("172.19."))
            .Select(a => $"HTTP {a.Address}:{_viewModel.Settings.HttpPort} · SOCKS5 {a.Address}:{_viewModel.Settings.SocksPort}").ToArray();
        if (!string.IsNullOrEmpty(_viewModel.Settings.LanBindAddress)) endpoints = endpoints.Where(line => line.StartsWith("HTTP " + _viewModel.Settings.LanBindAddress + ":", StringComparison.Ordinal)).ToArray();
        PostWebMessage("lanInterfaces", LanSharing.Interfaces());
        PostWebMessage("lanEndpoints", endpoints);
        PostWebMessage("settings", new
        {
            theme = _viewModel.Settings.Theme,
            httpPort = _viewModel.Settings.HttpPort,
            socksPort = _viewModel.Settings.SocksPort,
            customProxyRules = _viewModel.Settings.CustomProxyRules,
            customDirectRules = _viewModel.Settings.CustomDirectRules,
            enableSniffing = _viewModel.Settings.EnableSniffing,
            enableAiBypass = _viewModel.Settings.EnableAiBypass,
            enableAiXboxDns = _viewModel.Settings.EnableAiXboxDns,
            isAdministrator = TunDeviceManager.IsAdministrator(),
            enableTunMode = _viewModel.Settings.EnableTunMode,
            routingMode = _viewModel.Settings.RoutingMode,
            xhttpModeOverride = _viewModel.Settings.XhttpModeOverride ?? "auto",
            bufferSize = _viewModel.Settings.BufferSizeKb,
            enableTlsFragment = _viewModel.Settings.EnableTlsFragment,
            dnsProvider = _viewModel.Settings.DnsProvider,
            isFakeDns = _viewModel.Settings.IsFakeDns,
            blockQuic = _viewModel.Settings.BlockQuic,
            blockIpv6 = _viewModel.Settings.BlockIpv6,
            enableKillSwitch = _viewModel.Settings.EnableKillSwitch,
            enableDnsLeakProtection = _viewModel.Settings.EnableDnsLeakProtection,
            autoStart = _viewModel.Settings.AutoStart,
            minimizeToTray = _viewModel.Settings.MinimizeToTray,
            autoFallbackFastestServer = _viewModel.Settings.AutoFallbackFastestServer,
            autoUpdateSubscription = _viewModel.Settings.AutoUpdateSubscription
        });
    }

    private void SendSubscriptionsToWeb() => PostWebMessage("subscriptions", _viewModel.Subscriptions.Select(s => new
    {
        id = s.Id,
        name = s.Name,
        url = s.Url,
        enabled = s.Enabled,
        autoUpdate = s.AutoUpdate,
        isPreset = s.IsPreset,
        nodeCount = s.NodeCount,
        isUpdating = s.IsUpdating,
        usage = s.FormattedUsage,
        usagePercentage = s.UsagePercentage,
        expiry = s.FormattedExpiry,
        lastUpdated = s.LastUpdatedDisplay
    }).ToList());

    private async Task SelectServerFromWebAsync(ServerNode node)
    {
        try { await _viewModel.SelectServerAsync(node); }
        catch (Exception ex) { PostWebMessage("notification", new { title = "Выбор сервера", message = ex.Message, kind = "error" }); }
    }

    private async Task SelectAiDnsAsync()
    {
        _aiMonitorCts?.Cancel();
        if (!_viewModel.Settings.EnableAiDns || !_viewModel.Settings.EnableAiBypass || _viewModel.Settings.AiRoutingMode != "dns_ai") throw new InvalidOperationException("Включите SmartDNS для нейросетей и выберите маршрут SmartDNS.");
        _aiDnsCts?.Dispose(); using var lifetime = new System.Threading.CancellationTokenSource(); _aiDnsCts = lifetime;
        try {
            await new AiDnsAutoSelector().RunAsync(state => PostWebMessage("aiDnsSelection", state), async choice => {
                lifetime.Token.ThrowIfCancellationRequested();
                _viewModel.Settings.AiDnsProvider = choice.ProviderKey;
                _viewModel.Settings.AiTlsFragment = choice.TlsFragment;
                _viewModel.SaveSettingsCommand.Execute(null); SendSettingsToWeb();
                if (_viewModel.IsConnected && _viewModel.SelectedServer is { } node) _ = RunWebOperationAsync("applyAiDnsConnection", () => _viewModel.SwitchServerAsync(node));
                await Task.CompletedTask;
                return true;
            }, lifetime.Token, confirmationProbe: (choice, token) => new AiDnsAutoSelector().ProbeAsync(choice, token, GeminiPageVerifier.CheckAsync));
        } catch (OperationCanceledException) { }
        finally { _aiDnsCts = null; }
    }
    private void SendAdFilterState() => PostWebMessage("adFilters", AdFilterService.State);
    private async Task RunAdFilterLoopAsync()
    {
        try {
            while (!_adFilterLifetime.IsCancellationRequested) {
                if (_viewModel.Settings.EnableAdBlock) {
                    try {
                        await AdFilterService.PrepareAsync(AppContext.BaseDirectory, _viewModel.Settings.AdBlockAllowlist, _adFilterLifetime.Token);
                        if (AdFilterService.State.Updated == null || DateTimeOffset.UtcNow - AdFilterService.State.Updated > TimeSpan.FromDays(1))
                            await AdFilterService.RefreshAsync(AppContext.BaseDirectory, _viewModel.Settings.AdBlockAllowlist, _viewModel.IsConnected ? _viewModel.ActiveHttpPort : null, _adFilterLifetime.Token);
                    } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException) { PostWebMessage("notification", new { title = "Фильтры рекламы", message = ex.Message, kind = "error" }); }
                }
                SendAdFilterState(); await Task.Delay(TimeSpan.FromHours(1), _adFilterLifetime.Token);
            }
        } catch (OperationCanceledException) { }
    }
    private async Task RunSpeedTestAsync()
    {
        if (!_viewModel.IsConnected) throw new InvalidOperationException("Сначала подключитесь к VPN.");
        _speedTestCts?.Dispose(); _speedTestCts = new System.Threading.CancellationTokenSource();
        _viewModel.IsSpeedTestRunning = true;
        try
        {
            await new SpeedTestService().RunAsync(_viewModel.ActiveHttpPort, sample => PostWebMessage("speedTest", new
            {
                message = sample.Message,
                mbps = sample.Mbps,
                bytes = sample.Bytes,
                seconds = sample.Seconds,
                httpsPing = sample.HttpsPing,
                done = sample.Done
            }), _speedTestCts.Token);
        }
        catch (OperationCanceledException) { PostWebMessage("speedTest", new { message = _speedTestCts.IsCancellationRequested ? "Тест остановлен." : "Тестовый сервер не ответил вовремя. VPN остаётся включённым.", done = true }); }
        catch (Exception ex) { PostWebMessage("speedTest", new { message = ex.Message, done = true }); }
        finally { _viewModel.IsSpeedTestRunning = false; _speedTestCts.Dispose(); _speedTestCts = null; }
    }

    private void QueueConnectionSettings(string previousConnection)
    {
        SendFullStateToWeb();
        if (previousConnection == ConnectionSettings.Signature(_viewModel.Settings) || (!_viewModel.IsConnected && !_viewModel.IsConnecting)) return;
        ++_settingsApplyVersion;
        if (_settingsApplyTask == null || _settingsApplyTask.IsCompleted)
            _settingsApplyTask = RunWebOperationAsync("applyConnectionSettings", ApplyConnectionSettingsAsync);
    }

    private async Task ApplyConnectionSettingsAsync()
    {
        int version;
        do
        {
            version = _settingsApplyVersion;
            await Task.Delay(400);
            if (version != _settingsApplyVersion) continue;
            if ((!_viewModel.IsConnected && !_viewModel.IsConnecting) || _viewModel.SelectedServer is not { } node) return;
            await _viewModel.SwitchServerAsync(node);
            if (version == _settingsApplyVersion && _viewModel.IsConnected && _viewModel.HasPendingConnectionSettings)
                throw new IOException("Настройки сохранены, но не применены: ядро не удалось перезапустить. Проверьте диагностику соединения.");
        } while (version != _settingsApplyVersion);
        SendFullStateToWeb();
    }

    private void ApplyExtendedSettings(JsonElement data)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_viewModel.Settings))!;
        var previousAi = (settings.AiDnsProvider, settings.AiTlsFragment, settings.EnableAiDns, settings.AiRoutingMode);
        void Bool(string name, Action<bool> set) { if (data.TryGetProperty(name, out var value)) set(value.GetBoolean()); }
        void Str(string name, Action<string> set) { if (data.TryGetProperty(name, out var value)) set(value.GetString() ?? ""); }
        Bool("enableAiDns", v => settings.EnableAiDns = v);
        Bool("aiDnsAutoSwitch", v => settings.AiDnsAutoSwitch = v);
        Str("routingMode", v => settings.RoutingMode = v);
        Str("routingRegion", v => settings.RoutingRegion = RoutingRegions.IsValid(v) ? v : throw new ArgumentException("Неизвестный регион."));
        Bool("setupCompleted", v => settings.SetupCompleted = v);
        Str("aiRoutingMode", v => settings.AiRoutingMode = v);
        Str("customDohUrl", v => settings.CustomDohUrl = v);
        Bool("enableAdBlock", v => settings.EnableAdBlock = v);
        Str("customBlockRules", v => settings.CustomBlockRules = v);
        Bool("enableMux", v => settings.EnableMux = v);
        if (data.TryGetProperty("muxConcurrency", out var mux)) settings.MuxConcurrency = int.Parse(mux.ToString());
        Str("tlsFragmentLength", v => settings.TlsFragmentLength = v);
        Str("tlsFragmentInterval", v => settings.TlsFragmentInterval = v);
        Str("tlsFragmentPackets", v => settings.TlsFragmentPackets = v);
        Bool("upstreamProxyEnabled", v => settings.UpstreamProxyEnabled = v);
        Str("upstreamProxyHost", v => settings.UpstreamProxyHost = v.Trim());
        if (data.TryGetProperty("upstreamProxyPort", out var upstreamPort)) settings.UpstreamProxyPort = int.Parse(upstreamPort.ToString());
        if (settings.UpstreamProxyEnabled) { UpstreamProxy.FromSettings(settings); settings.RoutingMode = "global"; }
        Bool("shareLan", v => settings.ShareLan = v);
        Str("lanBindAddress", v => settings.LanBindAddress = v);
        Bool("lanAuthEnabled", v => settings.LanAuthEnabled = v);
        Str("lanUsername", v => settings.LanUsername = v);
        Str("lanPassword", v => settings.LanPassword = v);
        LanSharing.Validate(settings);
        Bool("startMinimized", v => settings.StartMinimized = v);
        Bool("enableGlobalHotkey", v => settings.EnableGlobalHotkey = v);
        if (settings.MuxConcurrency is < 1 or > 128 || !XrayConfigGenerator.ValidRange(settings.TlsFragmentLength, 1, 65535) ||
            !XrayConfigGenerator.ValidRange(settings.TlsFragmentInterval, 0, 10000) ||
            !(settings.TlsFragmentPackets == "tlshello" || XrayConfigGenerator.ValidRange(settings.TlsFragmentPackets, 1, 100)))
            throw new ArgumentException("Проверьте параллельность Mux и диапазоны фрагментации TLS.");
        XrayConfigGenerator.ValidateDoh(settings.CustomDohUrl);
        if (settings.AiRoutingMode is not ("off" or "vpn" or "dns_ai" or "dns_vpn")) throw new ArgumentException("Неизвестный маршрут ИИ.");
        if (settings.RoutingMode is not ("smart_ru" or "global")) throw new ArgumentException("Неизвестный основной маршрут.");
        if (settings.RoutingMode == "global" && settings.AiRoutingMode != "dns_vpn") settings.AiRoutingMode = "vpn";
        if (data.TryGetProperty("glassTransparency", out var glass)) settings.GlassTransparency = double.Parse(glass.ToString(), System.Globalization.CultureInfo.InvariantCulture);
        Bool("glassBlurEnabled", v => settings.GlassBlurEnabled = v);
        Str("aiDnsProvider", v => settings.AiDnsProvider = v);
        Bool("aiTlsFragment", v => settings.AiTlsFragment = v);
        Str("adBlockAllowlist", v => settings.AdBlockAllowlist = string.Join('\n', AdFilterService.Allowlist(v)));
        if (SmartDnsProviders.Find(settings.AiDnsProvider) == null) throw new ArgumentException("Неизвестный SmartDNS-провайдер.");
        Bool("groupServers", v => settings.GroupServers = v);
        Str("balancerMode", v => settings.BalancerMode = v);
        if (data.TryGetProperty("balancerHealthSeconds", out var balancerHealthSeconds)) settings.BalancerHealthSeconds = int.Parse(balancerHealthSeconds.ToString());
        if (data.TryGetProperty("balancerFailureConfirmations", out var balancerFailureConfirmations)) settings.BalancerFailureConfirmations = int.Parse(balancerFailureConfirmations.ToString());
        if (data.TryGetProperty("balancerOptimizationMinutes", out var balancerOptimizationMinutes)) settings.BalancerOptimizationMinutes = int.Parse(balancerOptimizationMinutes.ToString());
        if (data.TryGetProperty("balancerCooldownSeconds", out var balancerCooldownSeconds)) settings.BalancerCooldownSeconds = int.Parse(balancerCooldownSeconds.ToString());
        if (data.TryGetProperty("balancerImprovementPercent", out var balancerImprovementPercent)) settings.BalancerImprovementPercent = int.Parse(balancerImprovementPercent.ToString());
        if (!double.IsFinite(settings.GlassTransparency) || settings.GlassTransparency is < 0 or > 1 ||
            settings.BalancerMode is not ("balanced" or "failover" or "initial_only") ||
            settings.BalancerHealthSeconds is < 2 or > 30 || settings.BalancerFailureConfirmations is < 2 or > 5 ||
            settings.BalancerOptimizationMinutes is < 5 or > 60 || settings.BalancerCooldownSeconds is < 60 or > 1800 ||
            settings.BalancerImprovementPercent is < 30 or > 80) throw new ArgumentException("Проверьте настройки балансировщика и прозрачности.");
        var target = _viewModel.Settings;
        if (previousAi != (settings.AiDnsProvider, settings.AiTlsFragment, settings.EnableAiDns, settings.AiRoutingMode) ||
            (data.TryGetProperty("enableAiBypass", out var aiEnabled) && aiEnabled.GetBoolean() != target.EnableAiBypass) ||
            (data.TryGetProperty("enableAiXboxDns", out var xboxEnabled) && xboxEnabled.GetBoolean() != target.EnableAiXboxDns)) _aiDnsCts?.Cancel();
        target.AiDnsProvider = settings.AiDnsProvider; target.AiTlsFragment = settings.AiTlsFragment; target.AdBlockAllowlist = settings.AdBlockAllowlist;
        target.AiDnsAutoSwitch = settings.AiDnsAutoSwitch;
        var balancerChanged = target.BalancerMode != settings.BalancerMode || target.BalancerHealthSeconds != settings.BalancerHealthSeconds || target.BalancerFailureConfirmations != settings.BalancerFailureConfirmations || target.BalancerOptimizationMinutes != settings.BalancerOptimizationMinutes || target.BalancerCooldownSeconds != settings.BalancerCooldownSeconds || target.BalancerImprovementPercent != settings.BalancerImprovementPercent;
        target.BalancerHealthSeconds = settings.BalancerHealthSeconds;
        target.BalancerFailureConfirmations = settings.BalancerFailureConfirmations;
        target.BalancerOptimizationMinutes = settings.BalancerOptimizationMinutes;
        target.BalancerCooldownSeconds = settings.BalancerCooldownSeconds;
        target.BalancerImprovementPercent = settings.BalancerImprovementPercent;
        target.BalancerMode = settings.BalancerMode;
        target.GlassTransparency = settings.GlassTransparency;
        target.GlassBlurEnabled = settings.GlassBlurEnabled;
        target.GroupServers = settings.GroupServers;

        target.EnableAiDns = settings.AiRoutingMode is "dns_ai" or "dns_vpn"; target.AiRoutingMode = settings.AiRoutingMode;
        _viewModel.SelectedRoutingMode = settings.RoutingMode;
        target.EnableAiBypass = settings.AiRoutingMode != "off"; target.EnableAiXboxDns = false;
        target.SettingsSchemaVersion = Math.Max(2, target.SettingsSchemaVersion);
        target.CustomDohUrl = settings.CustomDohUrl; target.EnableAdBlock = settings.EnableAdBlock;
        target.CustomBlockRules = settings.CustomBlockRules; target.EnableMux = settings.EnableMux;
        target.MuxConcurrency = settings.MuxConcurrency; target.TlsFragmentLength = settings.TlsFragmentLength;
        target.TlsFragmentInterval = settings.TlsFragmentInterval; target.TlsFragmentPackets = settings.TlsFragmentPackets;
        target.UpstreamProxyEnabled = settings.UpstreamProxyEnabled;
        target.UpstreamProxyHost = settings.UpstreamProxyHost; target.UpstreamProxyPort = settings.UpstreamProxyPort;
        target.ShareLan = settings.ShareLan; target.StartMinimized = settings.StartMinimized;
        target.EnableGlobalHotkey = settings.EnableGlobalHotkey;
        if (data.TryGetProperty("subAutoUpdateInterval", out var interval)) _viewModel.SubAutoUpdateInterval = interval.GetString() ?? "24h";
        if (data.TryGetProperty("pingMode", out var ping)) _viewModel.PingMode = ping.GetString() ?? "http";
        if (balancerChanged) _viewModel.RestartBalancer();
        NativeThemeService.Apply(target.Theme, target.GlassTransparency);
    }
    public MainWindow(MainViewModel viewModel, bool startBackgroundWork = true) { _viewModel=viewModel; SubscribeToViewModelEvents(); if(startBackgroundWork) { _=RunAdFilterLoopAsync(); _=RunAiMonitorLoopAsync(); } }
    public void Dispose() { foreach(var pending in _confirmations.Values.ToArray()) pending.TrySetResult(false); _viewModel.Disconnect(); _speedTestCts?.Cancel(); _aiDnsCts?.Cancel(); _aiCheckCts?.Cancel(); _adFilterLifetime.Cancel(); _trafficHistory.Flush(); }
}
