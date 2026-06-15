using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using rdpManager.Helpers;
using rdpManager.Views;

namespace rdpManager
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<ConnectionItem> _connections = new ObservableCollection<ConnectionItem>();
        private DispatcherTimer _thumbnailTimer;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private bool _isExplicitExit = false;

        public MainWindow()
        {
            InitializeComponent();

            // 绑定连接列表数据源
            ConnectionCardsControl.ItemsSource = _connections;

            // 加载持久化的连接配置
            LoadConnections();

            // 初始时不选定任何 Tab，默认显示仪表盘
            WorkspaceTabs.SelectedIndex = -1;

            // 启动定时器，每 3 秒刷新一次连接网格的屏幕截图
            _thumbnailTimer = new DispatcherTimer();
            _thumbnailTimer.Interval = TimeSpan.FromSeconds(3);
            _thumbnailTimer.Tick += ThumbnailTimer_Tick;
            _thumbnailTimer.Start();

            // 检测 TermWrap 补丁状态
            RefreshTermWrapStatus();

            // 初始化系统托盘图标
            InitializeNotifyIcon();

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 默认展示仪表盘
            SwitchToView(ViewWorkspaces);
            UpdateNavButtons(NavDashboardBtn);

            // 后台异步预热本地账户列表缓存与应用凭据分配/免密登录系统组策略
            _ = System.Threading.Tasks.Task.Run(() => 
            {
                AccountHelper.GetLocalAccounts(true);
                TermWrapDeployer.ApplyCredentialsDelegationPolicies();
            });
        }

        // ======================= 状态与数据加载 =======================

        private void RefreshTermWrapStatus()
        {
            bool isActive = TermWrapDeployer.IsMultiSessionActive();
            bool isRunning = TermWrapDeployer.IsTermServiceRunning();
            Logger.LogInfo($"检测 TermWrap 补丁状态: {(isActive ? "已激活" : "未激活")}, 远程服务运行状态: {(isRunning ? "正在运行" : "已停止")}");
            if (isActive)
            {
                if (isRunning)
                {
                    StatusDot.Fill = (Brush?)new BrushConverter().ConvertFromString("#0070F3") ?? Brushes.Blue; // Vercel 经典蓝色
                    StatusTxt.Text = "并发会话已激活 (TermWrap)";
                    TermWrapStatusTxt.Text = "已激活 (服务已劫持并运行中)";
                    TermWrapStatusTxt.Foreground = (Brush?)new BrushConverter().ConvertFromString("#0070F3") ?? Brushes.Blue;
                }
                else
                {
                    StatusDot.Fill = (Brush?)new BrushConverter().ConvertFromString("#F5A623") ?? Brushes.Orange; // 警告橙色
                    StatusTxt.Text = "并发会话已激活，但远程服务已停止";
                    TermWrapStatusTxt.Text = "已激活 (但服务当前已停止)";
                    TermWrapStatusTxt.Foreground = (Brush?)new BrushConverter().ConvertFromString("#F5A623") ?? Brushes.Orange;
                }
            }
            else
            {
                if (isRunning)
                {
                    StatusDot.Fill = Brushes.Red;
                    StatusTxt.Text = "并发会话未激活 / 补丁未应用";
                    TermWrapStatusTxt.Text = "未激活 / 默认单会话模式";
                    TermWrapStatusTxt.Foreground = Brushes.Red;
                }
                else
                {
                    StatusDot.Fill = Brushes.Red;
                    StatusTxt.Text = "并发会话未激活，且远程服务已停止";
                    TermWrapStatusTxt.Text = "未激活 (远程服务已停止)";
                    TermWrapStatusTxt.Foreground = Brushes.Red;
                }
            }
        }

        private async void LoadAccountsAsync(bool forceRefresh = false)
        {
            bool showLoading = forceRefresh || !AccountHelper.HasCache();
            if (showLoading) ShowLoading("正在读取账户列表...");
            try
            {
                var accounts = await System.Threading.Tasks.Task.Run(() => AccountHelper.GetLocalAccounts(forceRefresh));
                AccountsDataGrid.ItemsSource = accounts;
            }
            catch (Exception ex)
            {
                Logger.LogError("加载本地账户列表失败", ex);
                MessageBox.Show($"加载账户列表失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (showLoading) HideLoading();
            }
        }

        private void ShowLoading(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LoadingText.Text = message;
                GlobalLoadingOverlay.Visibility = Visibility.Visible;
            });
        }

        private void HideLoading()
        {
            Dispatcher.Invoke(() =>
            {
                GlobalLoadingOverlay.Visibility = Visibility.Collapsed;
            });
        }

        // ======================= 侧边栏导航切换 =======================

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedBtn)
            {
                UpdateNavButtons(clickedBtn);

                if (clickedBtn == NavDashboardBtn)
                {
                    SwitchToView(ViewWorkspaces);
                    WorkspaceTabs.SelectedIndex = -1; // 取消选择所有会话标签以显示仪表盘
                }
                else if (clickedBtn == NavAccountsBtn)
                {
                    SwitchToView(ViewAccounts);
                    LoadAccountsAsync(false);
                }
                else if (clickedBtn == NavSettingsBtn)
                {
                    SwitchToView(ViewSettings);
                    RefreshTermWrapStatus();
                }
            }
        }

        private void SwitchToView(Grid activeView)
        {
            ViewWorkspaces.Visibility = (activeView == ViewWorkspaces) ? Visibility.Visible : Visibility.Collapsed;
            ViewAccounts.Visibility = (activeView == ViewAccounts) ? Visibility.Visible : Visibility.Collapsed;
            ViewSettings.Visibility = (activeView == ViewSettings) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateNavButtons(Button activeBtn)
        {
            Style activeStyle = (Style)FindResource("ActiveSidebarBtnStyle");
            Style normalStyle = (Style)FindResource("SidebarBtnStyle");

            NavDashboardBtn.Style = (activeBtn == NavDashboardBtn) ? activeStyle : normalStyle;
            NavAccountsBtn.Style = (activeBtn == NavAccountsBtn) ? activeStyle : normalStyle;
            NavSettingsBtn.Style = (activeBtn == NavSettingsBtn) ? activeStyle : normalStyle;
        }

        // ======================= 标签页管理与切换 =======================

        private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WorkspaceTabs.SelectedIndex == -1)
            {
                // 显示仪表盘
                DashboardGridView.Visibility = Visibility.Visible;

                // 隐藏所有活跃的会话控件，解决 WinFormsHost 遮挡仪表盘的问题
                foreach (var item in _connections)
                {
                    if (item.RdpControl != null)
                        item.RdpControl.IsHiddenSession = true;
                }

                // 虚无化后台容器以切换展示，但由于仍保留在视觉树，后台依然能够保持渲染与截图
                ActiveRdpContainer.Opacity = 0;
                ActiveRdpContainer.IsHitTestVisible = false;
                ActiveRdpContainer.Visibility = Visibility.Visible;
            }
            else
            {
                // 隐藏仪表盘，显示会话页面
                DashboardGridView.Visibility = Visibility.Collapsed;
                ActiveRdpContainer.Opacity = 1;
                ActiveRdpContainer.IsHitTestVisible = true;
                ActiveRdpContainer.Visibility = Visibility.Visible;

                // 激活对应的 RDP 控制实例，将其余保活会话隐形
                if (WorkspaceTabs.SelectedItem is TabItem selectedTab && selectedTab.Tag is ConnectionItem connItem)
                {
                    foreach (var item in _connections)
                    {
                        if (item.RdpControl != null)
                        {
                            if (item == connItem)
                            {
                                item.RdpControl.IsHiddenSession = false;
                            }
                            else
                            {
                                item.RdpControl.IsHiddenSession = true;
                            }
                        }
                    }
                }
            }
        }

        private void CloseTabToKeepAlive(ConnectionItem connItem)
        {
            Logger.LogInfo($"关闭标签页并保活会话: {connItem.FriendlyName}");
            if (connItem.RdpControl != null && connItem.RdpControl.IsConnected)
            {
                // 虚假断开：将 RDP 控件的透明度调为 0，允许其在后台维持渲染
                connItem.RdpControl.IsHiddenSession = true;
                connItem.StatusText = "后台保活";
                connItem.StatusBrush = Brushes.Green; // 后台运行时状态为绿色
            }

            // 从 Tab 列表中移除对应标签页
            TabItem? tabToRemove = null;
            for (int i = 0; i < WorkspaceTabs.Items.Count; i++)
            {
                if (WorkspaceTabs.Items[i] is TabItem t && t.Tag == connItem)
                {
                    tabToRemove = t;
                    break;
                }
            }

            if (tabToRemove != null)
            {
                WorkspaceTabs.Items.Remove(tabToRemove);
            }

            // 切换回仪表盘或前一个标签页
            if (WorkspaceTabs.Items.Count > 0)
            {
                WorkspaceTabs.SelectedIndex = WorkspaceTabs.Items.Count - 1;
            }
            else
            {
                WorkspaceTabs.SelectedIndex = -1;
                UpdateNavButtons(NavDashboardBtn);
            }
        }

        // ======================= 定时器与卡片截图 =======================

        private void ThumbnailTimer_Tick(object? sender, EventArgs e)
        {
            foreach (var conn in _connections)
            {
                if (conn.RdpControl != null && conn.RdpControl.IsConnected)
                {
                    var thumb = conn.RdpControl.CaptureThumbnail();
                    if (thumb != null)
                    {
                        conn.Thumbnail = thumb;
                        conn.PlaceholderVisibility = Visibility.Collapsed;
                    }
                }
            }
        }

        // ======================= 卡片按钮操作事件 =======================

        private void OpenSessionTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string connId)
            {
                var connItem = _connections.FirstOrDefault(c => c.Id == connId);
                if (connItem == null) return;

                Logger.LogInfo($"开始打开会话 (外部 mstsc): FriendlyName={connItem.FriendlyName}, Server={connItem.Server}");

                try
                {
                    Logger.LogInfo($"向 Windows 凭据管理器注入凭据: Server={connItem.Server}, Username={connItem.Username}");
                    var cmdkeyPsi = new System.Diagnostics.ProcessStartInfo("cmdkey")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        Arguments = $"/generic:TERMSRV/{connItem.Server} /user:{connItem.Username} /pass:\"{connItem.Password}\""
                    };

                    using (var proc = System.Diagnostics.Process.Start(cmdkeyPsi))
                    {
                        proc?.WaitForExit(3000);
                    }

                    Logger.LogInfo($"生成临时 RDP 配置文件并启动外部 mstsc.exe: Server={connItem.Server}, Username={connItem.Username}");
                    
                    // 生成临时 rdp 文件路径，使用 Guid 防止并发或重名冲突
                    string tempRdpPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rdp_{Guid.NewGuid():N}.rdp");
                    
                    var rdpContent = new System.Text.StringBuilder();
                    rdpContent.AppendLine($"full address:s:{connItem.Server}");
                    
                    // 如果是本地账号且未指定域，可以加上 .\ 强制指定为目标机器的本地账户（可选）
                    rdpContent.AppendLine($"username:s:{connItem.Username}");
                    
                    rdpContent.AppendLine("prompt for credentials:i:0"); // 自动使用已缓存凭据，不提示密码输入
                    rdpContent.AppendLine("authentication level:i:0"); // 忽略证书警告 (本地 RPA/127.0.0.2 黄金组合)
                    
                    // 屏幕分辨率与模式配置
                    // 注释掉强制指定 desktopscalefactor，以防 RDP 强制修改远程系统原本的缩放比 (解决影响其他远控如 RustDesk 的问题)
                    // rdpContent.AppendLine($"desktopscalefactor:i:{connItem.DesktopScaleFactor}");
                    
                    if (connItem.DesktopWidth > 0 && connItem.DesktopHeight > 0)
                    {
                        rdpContent.AppendLine("screen mode id:i:1"); // 窗口模式
                        rdpContent.AppendLine($"desktopwidth:i:{connItem.DesktopWidth}");
                        rdpContent.AppendLine($"desktopheight:i:{connItem.DesktopHeight}");
                    }
                    else
                    {
                        rdpContent.AppendLine("screen mode id:i:2"); // 全屏模式
                    }

                    // 引入选项首选项
                    bool enableClipboard = OptClipboardChk.IsChecked == true;
                    bool enableSmartSizing = OptSmartSizingChk.IsChecked == true;
                    bool muteAudio = OptMuteChk.IsChecked == true;
                    bool enableUsb = OptUsbChk.IsChecked == true;

                    rdpContent.AppendLine($"redirectclipboard:i:{(enableClipboard ? 1 : 0)}");
                    rdpContent.AppendLine($"smart sizing:i:{(enableSmartSizing ? 1 : 0)}");
                    rdpContent.AppendLine("dynamic resolution:i:1"); // 允许窗口大小改变时，动态更新远程桌面分辨率
                    rdpContent.AppendLine($"audiomode:i:{(muteAudio ? 2 : 0)}"); // 2 = 不要在本地播放音频 (降低系统负荷)
                    rdpContent.AppendLine($"redirectdrives:i:{(enableUsb ? 1 : 0)}");
                    rdpContent.AppendLine($"redirectsmartcards:i:{(enableUsb ? 1 : 0)}");
                    rdpContent.AppendLine("redirectcomports:i:0");
                    rdpContent.AppendLine("redirectprinters:i:0");

                    // 写入临时文件
                    System.IO.File.WriteAllText(tempRdpPath, rdpContent.ToString(), System.Text.Encoding.UTF8);

                    var mstscPsi = new System.Diagnostics.ProcessStartInfo("mstsc.exe")
                    {
                        UseShellExecute = false,
                        Arguments = $"\"{tempRdpPath}\""
                    };
                    System.Diagnostics.Process.Start(mstscPsi);

                    // 后台异步定时清理临时 RDP 配置文件 (10秒延时非常安全)
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(10000);
                        try
                        {
                            if (System.IO.File.Exists(tempRdpPath))
                            {
                                System.IO.File.Delete(tempRdpPath);
                                Logger.LogInfo($"已自动清理临时 RDP 配置文件: {tempRdpPath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"自动清理临时 RDP 配置文件失败: {ex.Message}");
                        }
                    });

                    // 修改 UI 状态为已连接外部桌面
                    connItem.StatusText = "已打开外部桌面";
                    connItem.StatusBrush = (Brush?)new BrushConverter().ConvertFromString("#0070F3") ?? Brushes.Blue;
                    connItem.ActiveActionsVisibility = Visibility.Visible;
                    connItem.PlaceholderVisibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    Logger.LogError("启动外部远程桌面连接失败", ex);
                    MessageBox.Show($"启动远程桌面失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void KeepAliveDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string connId)
            {
                var connItem = _connections.FirstOrDefault(c => c.Id == connId);
                if (connItem == null) return;

                CloseTabToKeepAlive(connItem);
            }
        }

        private void FullDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string connId)
            {
                var connItem = _connections.FirstOrDefault(c => c.Id == connId);
                if (connItem == null) return;

                var result = MessageBox.Show($"是否确定关闭会话 '{connItem.FriendlyName}' 并清理系统凭据？", "断开并清理凭据", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    Logger.LogInfo($"清理会话 Windows 凭据并重置状态: {connItem.FriendlyName}");
                    try
                    {
                        var cmdkeyPsi = new System.Diagnostics.ProcessStartInfo("cmdkey")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        cmdkeyPsi.ArgumentList.Add($"/delete:TERMSRV/{connItem.Server}");
                        using (var proc = System.Diagnostics.Process.Start(cmdkeyPsi))
                        {
                            proc?.WaitForExit(3000);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"删除凭据失败: {ex.Message}");
                    }

                    connItem.StatusText = "已断开";
                    connItem.StatusBrush = Brushes.Red;
                    connItem.ActiveActionsVisibility = Visibility.Collapsed;
                    connItem.PlaceholderVisibility = Visibility.Visible;
                    connItem.Thumbnail = null;
                }
            }
        }

        // ======================= 新建连接弹窗 =======================

        private async void OpenNewConnection_Click(object sender, RoutedEventArgs e)
        {
            TargetPasswordBox.Password = string.Empty;
            FriendlyNameTxt.Text = string.Empty;

            // 自动检测系统分辨率与缩放比
            try
            {
                int scaleFactor = 100;
                double dpiScale = 1.0;
                var presentationSource = PresentationSource.FromVisual(this);
                if (presentationSource != null)
                {
                    dpiScale = presentationSource.CompositionTarget.TransformToDevice.M11;
                    scaleFactor = (int)Math.Round(dpiScale * 100);
                }
                
                int physWidth = (int)Math.Round(SystemParameters.PrimaryScreenWidth * dpiScale);
                int physHeight = (int)Math.Round(SystemParameters.PrimaryScreenHeight * dpiScale);
                string resStr = $"{physWidth}x{physHeight}";

                bool foundRes = false;
                foreach (ComboBoxItem item in ResolutionCombo.Items)
                {
                    if (item.Tag?.ToString() == resStr)
                    {
                        ResolutionCombo.SelectedItem = item;
                        foundRes = true;
                        break;
                    }
                }
                if (!foundRes)
                {
                    var newItem = new ComboBoxItem { Content = $"本机 ({resStr})", Tag = resStr };
                    ResolutionCombo.Items.Add(newItem);
                    ResolutionCombo.SelectedItem = newItem;
                }

                foreach (ComboBoxItem item in ScaleFactorCombo.Items)
                {
                    if (item.Tag?.ToString() == scaleFactor.ToString())
                    {
                        ScaleFactorCombo.SelectedItem = item;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("自动检测分辨率失败", ex);
            }

            TargetComputerCombo.Items.Clear();
            TargetComputerCombo.Items.Add("127.0.0.2");

            bool showLoading = !AccountHelper.HasCache();
            if (showLoading) ShowLoading("正在读取账户配置...");
            try
            {
                var localAccounts = await System.Threading.Tasks.Task.Run(() => AccountHelper.GetLocalAccounts(false));
                foreach (var acc in localAccounts)
                {
                    TargetComputerCombo.Items.Add(acc.Name);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("读取本地账户列表失败", ex);
            }
            finally
            {
                if (showLoading) HideLoading();
            }

            if (TargetComputerCombo.Items.Count > 0)
            {
                TargetComputerCombo.SelectedIndex = 0;
            }

            NewConnectionOverlay.Visibility = Visibility.Visible;
        }

        private void CloseNewConnection_Click(object sender, RoutedEventArgs e)
        {
            NewConnectionOverlay.Visibility = Visibility.Collapsed;
        }

        private void ResolutionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScaleFactorCombo == null) return;
            if (ResolutionCombo.SelectedItem is ComboBoxItem item && item.Tag is string resStr)
            {
                if (resStr == "0x0")
                {
                    // 自适应窗口，不强制缩放比
                    ScaleFactorCombo.IsEnabled = false;
                    return;
                }

                var parts = resStr.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out int width))
                {
                    // 宽度 >= 1920 (1080p) 则激活缩放比下拉框
                    ScaleFactorCombo.IsEnabled = width >= 1920;
                    if (!ScaleFactorCombo.IsEnabled)
                    {
                        // 非高分屏默认 100%
                        foreach (ComboBoxItem scaleItem in ScaleFactorCombo.Items)
                        {
                            if (scaleItem.Tag?.ToString() == "100")
                            {
                                ScaleFactorCombo.SelectedItem = scaleItem;
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void AddConnectionSubmit_Click(object sender, RoutedEventArgs e)
        {
            string targetText = TargetComputerCombo.Text.Trim();
            string password = TargetPasswordBox.Password;
            string friendlyName = FriendlyNameTxt.Text.Trim();

            if (string.IsNullOrEmpty(targetText))
            {
                MessageBox.Show("请指定目标主机或选择本地隔离账户。");
                return;
            }

            string server = targetText;
            string username = string.Empty;

            if (targetText.Contains("\\"))
            {
                string[] parts = targetText.Split('\\');
                server = parts[0];
                username = parts[1];
            }
            else if (!targetText.Contains(".") && !targetText.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                // 本地多会话连接本机首选 loopback 地址 127.0.0.2，避免单路 IP 拦截
                server = "127.0.0.2";
                username = targetText;
            }
            else
            {
                MessageBox.Show("非法连接信息，请按照以下格式之一输入:\n• 本地账户名（如: RpaUser_1）\n• 远程目标: IP\\用户名（如: 192.168.1.10\\Administrator）", "格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 密码读取逻辑（如果在界面没有输入，则尝试去 Credential Manager 获取）
            if (string.IsNullOrEmpty(password))
            {
                if (CredentialHelper.GetCredential($"RDPManager:{username}", out _, out string savedPwd))
                {
                    password = savedPwd;
                }
                else
                {
                    MessageBox.Show($"未找到本地账户 '{username}' 的保存密码，请在上方手动输入。", "请输入密码");
                    return;
                }
            }
            else
            {
                // 输入了密码，对其进行持久化更新
                CredentialHelper.SaveCredential($"RDPManager:{username}", username, password);
            }

            if (string.IsNullOrEmpty(friendlyName))
            {
                friendlyName = $"{username} ({server})";
            }

            int desktopWidth = 0;
            int desktopHeight = 0;
            if (ResolutionCombo.SelectedItem is ComboBoxItem resItem && resItem.Tag is string resTag)
            {
                if (resTag != "0x0" && resTag.Contains("x"))
                {
                    var parts = resTag.Split('x');
                    int.TryParse(parts[0], out desktopWidth);
                    int.TryParse(parts[1], out desktopHeight);
                }
            }

            int scaleFactor = 100;
            if (ScaleFactorCombo.IsEnabled && ScaleFactorCombo.SelectedItem is ComboBoxItem scaleItem && scaleItem.Tag is string scaleTag)
            {
                int.TryParse(scaleTag, out scaleFactor);
            }

            var newItem = new ConnectionItem
            {
                Id = Guid.NewGuid().ToString(),
                FriendlyName = friendlyName,
                Server = server,
                Username = username,
                Password = password,
                DesktopWidth = desktopWidth,
                DesktopHeight = desktopHeight,
                DesktopScaleFactor = scaleFactor
            };

            _connections.Add(newItem);
            SaveConnections();
            NewConnectionOverlay.Visibility = Visibility.Collapsed;
        }

        // ======================= 隔离账户管理 =======================

        private async void CreateAccount_Click(object sender, RoutedEventArgs e)
        {
            string username = NewUserTxt.Text.Trim();
            string password = NewPassTxt.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("用户名和密码不能为空。");
                return;
            }

            ShowLoading($"正在创建隔离账号 '{username}'...");
            bool success = false;
            string error = string.Empty;

            try
            {
                var result = await System.Threading.Tasks.Task.Run(() =>
                {
                    bool createResult = AccountHelper.CreateRobotAccount(username, password, out string err);
                    if (createResult)
                    {
                        CredentialHelper.SaveCredential($"RDPManager:{username}", username, password);
                    }
                    return new { Success = createResult, Error = err };
                });

                success = result.Success;
                error = result.Error;
            }
            catch (Exception ex)
            {
                success = false;
                error = ex.Message;
                Logger.LogError($"创建隔离账号 '{username}' 出现未处理异常", ex);
            }
            finally
            {
                HideLoading();
            }

            if (success)
            {
                MessageBox.Show($"隔离账号 '{username}' 已创建成功，自动完成管理员特权分配与环境首登优化！", "创建成功", MessageBoxButton.OK, MessageBoxImage.Information);
                NewUserTxt.Text = string.Empty;
                NewPassTxt.Password = string.Empty;
                LoadAccountsAsync(true);
            }
            else
            {
                MessageBox.Show($"账户创建失败: {error}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string username)
            {
                var result = MessageBox.Show($"警告：您即将删除本地系统账户 '{username}'。删除后该账户所有会话、数据、桌面缓存均将被清空！是否继续？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Stop);
                if (result == MessageBoxResult.Yes)
                {
                    ShowLoading($"正在删除账户 '{username}'...");
                    bool success = false;
                    string error = string.Empty;

                    try
                    {
                        var deleteResult = await System.Threading.Tasks.Task.Run(() =>
                        {
                            return AccountHelper.DeleteRobotAccount(username, out string err)
                                ? new { Success = true, Error = string.Empty }
                                : new { Success = false, Error = err };
                        });

                        success = deleteResult.Success;
                        error = deleteResult.Error;
                    }
                    catch (Exception ex)
                    {
                        success = false;
                        error = ex.Message;
                        Logger.LogError($"删除账号 '{username}' 时出现异常", ex);
                    }
                    finally
                    {
                        HideLoading();
                    }

                    if (success)
                    {
                        MessageBox.Show($"账户 '{username}' 已成功删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadAccountsAsync(true);
                    }
                    else
                    {
                        MessageBox.Show($"删除失败: {error}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // ======================= 系统设置选项 =======================

        private async void DeployPatch_Click(object sender, RoutedEventArgs e)
        {
            DeployPatchBtn.IsEnabled = false;
            ShowLoading("正在部署 TermWrap 补丁，这可能需要几十秒并断开所有活跃的远程连接，请耐心等待...");
            bool success = false;
            string error = string.Empty;

            try
            {
                var result = await System.Threading.Tasks.Task.Run(() =>
                {
                    bool deployResult = TermWrapDeployer.DeployPatch(out string err);
                    return new { Success = deployResult, Error = err };
                });
                success = result.Success;
                error = result.Error;
            }
            catch (Exception ex)
            {
                success = false;
                error = ex.Message;
                Logger.LogError("执行 DeployPatch 发生异常", ex);
            }
            finally
            {
                HideLoading();
                DeployPatchBtn.IsEnabled = true;
                RefreshTermWrapStatus();
            }

            if (success)
            {
                MessageBox.Show("TermWrap 多路并发 RDP 补丁部署成功！\n系统已解除多路限制，外设摄像头重定向等底层策略已激活。", "激活成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"部署失败: {error}\n请确认已排除安全软件拦截，或重启电脑后再试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void UninstallPatch_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("还原补丁将使 Windows 远程桌面多会话并发功能恢复为系统原始出厂配置。是否继续？", "确认还原", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            UninstallPatchBtn.IsEnabled = false;
            ShowLoading("正在还原系统默认配置，这可能会断开活跃的远程连接，请耐心等待...");
            bool success = false;
            string error = string.Empty;

            try
            {
                var runResult = await System.Threading.Tasks.Task.Run(() =>
                {
                    bool uninstallResult = TermWrapDeployer.UninstallPatch(out string err);
                    return new { Success = uninstallResult, Error = err };
                });
                success = runResult.Success;
                error = runResult.Error;
            }
            catch (Exception ex)
            {
                success = false;
                error = ex.Message;
                Logger.LogError("执行 UninstallPatch 发生异常", ex);
            }
            finally
            {
                HideLoading();
                UninstallPatchBtn.IsEnabled = true;
                RefreshTermWrapStatus();
            }

            if (success)
            {
                if (string.IsNullOrEmpty(error))
                {
                    MessageBox.Show("TermWrap 补丁卸载成功，远程桌面控制服务已恢复出厂配置。", "卸载成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(error, "卸载提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                MessageBox.Show($"卸载失败: {error}", "还原错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeNotifyIcon()
        {
            try
            {
                _notifyIcon = new System.Windows.Forms.NotifyIcon();
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                _notifyIcon.Text = "RDP Manager - RPA 隔离桌面管理器";
                _notifyIcon.Visible = true;
                _notifyIcon.DoubleClick += (s, args) =>
                {
                    ShowMainWindow();
                };

                var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                contextMenu.Items.Add("显示主窗口", null, (s, args) => ShowMainWindow());
                contextMenu.Items.Add("退出程序", null, (s, args) => ExitApplication());
                _notifyIcon.ContextMenuStrip = contextMenu;
            }
            catch (Exception ex)
            {
                Logger.LogError("初始化系统托盘图标失败", ex);
            }
        }

        private void ShowMainWindow()
        {
            Dispatcher.Invoke(() =>
            {
                this.Show();
                if (this.WindowState == WindowState.Minimized)
                {
                    this.WindowState = WindowState.Normal;
                }
                this.Activate();
            });
        }

        private void ExitApplication()
        {
            _isExplicitExit = true;
            _notifyIcon?.Dispose();
            
            // 安全断开所有连接
            foreach (var conn in _connections)
            {
                try
                {
                    conn.RdpControl?.Disconnect();
                }
                catch { }
            }
            
            Application.Current.Shutdown();
        }

        // ======================= 连接持久化与删除 =======================
        private static readonly string ConnectionsFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connections.json");

        private void SaveConnections()
        {
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                string json = System.Text.Json.JsonSerializer.Serialize(_connections, options);
                System.IO.File.WriteAllText(ConnectionsFilePath, json, System.Text.Encoding.UTF8);
                Logger.LogInfo("成功持久化连接配置到 connections.json");
            }
            catch (Exception ex)
            {
                Logger.LogError("保存连接配置到 JSON 失败", ex);
            }
        }

        private void LoadConnections()
        {
            try
            {
                if (System.IO.File.Exists(ConnectionsFilePath))
                {
                    string json = System.IO.File.ReadAllText(ConnectionsFilePath, System.Text.Encoding.UTF8);
                    var items = System.Text.Json.JsonSerializer.Deserialize<List<ConnectionItem>>(json);
                    if (items != null)
                    {
                        _connections.Clear();
                        foreach (var item in items)
                        {
                            _connections.Add(item);
                        }
                        Logger.LogInfo($"成功从 connections.json 加载了 {items.Count} 个连接项");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("从 JSON 加载连接配置失败", ex);
            }
        }

        private void DeleteConnection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string connId)
            {
                var connItem = _connections.FirstOrDefault(c => c.Id == connId);
                if (connItem == null) return;

                var result = MessageBox.Show($"确定要删除连接设备 '{connItem.FriendlyName}' 吗？此操作不会影响本地系统账户，但会清除该连接的保存配置。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    Logger.LogInfo($"开始主动删除连接设备: {connItem.FriendlyName}");
                    connItem.IsBeingDeleted = true;

                    // 清理凭据
                    try
                    {
                        var cmdkeyPsi = new System.Diagnostics.ProcessStartInfo("cmdkey")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        cmdkeyPsi.ArgumentList.Add($"/delete:TERMSRV/{connItem.Server}");
                        using (var proc = System.Diagnostics.Process.Start(cmdkeyPsi))
                        {
                            proc?.WaitForExit(3000);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"删除被删除连接 '{connItem.FriendlyName}' 的凭据失败: {ex.Message}");
                    }

                    // 1. 从 WorkspaceTabs 移除对应的 Tab
                    TabItem? tabToRemove = null;
                    for (int i = 0; i < WorkspaceTabs.Items.Count; i++)
                    {
                        if (WorkspaceTabs.Items[i] is TabItem t && t.Tag == connItem)
                        {
                            tabToRemove = t;
                            break;
                        }
                    }
                    if (tabToRemove != null)
                    {
                        WorkspaceTabs.Items.Remove(tabToRemove);
                        if (WorkspaceTabs.Items.Count > 0)
                        {
                            WorkspaceTabs.SelectedIndex = WorkspaceTabs.Items.Count - 1;
                        }
                        else
                        {
                            WorkspaceTabs.SelectedIndex = -1;
                            UpdateNavButtons(NavDashboardBtn);
                        }
                    }

                    // 2. 从视觉容器中移除控件，断开连接
                    if (connItem.RdpControl != null)
                    {
                        var rdpControl = connItem.RdpControl;
                        ActiveRdpContainer.Children.Remove(rdpControl);
                        connItem.RdpControl = null;
                        
                        // 异步执行断开
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            try
                            {
                                rdpControl.Disconnect();
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning($"断开被删除连接 '{connItem.FriendlyName}' 时发生异常: {ex.Message}");
                            }
                        });
                    }

                    // 3. 从列表中移除并保存
                    _connections.Remove(connItem);
                    SaveConnections();
                }
            }
        }

        private void EditConnection_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("连接设置功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_isExplicitExit)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true; // 拦截默认关闭动作

            var confirmWin = new CloseConfirmWindow
            {
                Owner = this
            };
            confirmWin.ShowDialog();

            if (confirmWin.Result == CloseConfirmResult.Exit)
            {
                ExitApplication();
            }
            else if (confirmWin.Result == CloseConfirmResult.Minimize)
            {
                this.Hide();
                try
                {
                    _notifyIcon?.ShowBalloonTip(3000, "RDP Manager 已最小化", "程序已最小化到系统托盘，双击托盘图标可重新打开主界面。", System.Windows.Forms.ToolTipIcon.Info);
                }
                catch { }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _notifyIcon?.Dispose();
            base.OnClosed(e);
        }
    }

    // ======================= 连接项数据实体 =======================

    public class ConnectionItem : INotifyPropertyChanged
    {
        private string _statusText = "未连接";
        private Brush _statusBrush = Brushes.Gray;
        private BitmapSource? _thumbnail;
        private Visibility _placeholderVisibility = Visibility.Visible;
        private Visibility _activeActionsVisibility = Visibility.Collapsed;

        public string Id { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string Server { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;

        public int DesktopWidth { get; set; } = 0;
        public int DesktopHeight { get; set; } = 0;
        public int DesktopScaleFactor { get; set; } = 100;

        [System.Text.Json.Serialization.JsonIgnore]
        public string Password { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonIgnore]
        public RdpClientControl? RdpControl { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsBeingDeleted { get; set; } = false;

        [System.Text.Json.Serialization.JsonIgnore]
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public Brush StatusBrush
        {
            get => _statusBrush;
            set { _statusBrush = value; OnPropertyChanged(); }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; OnPropertyChanged(); }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public Visibility PlaceholderVisibility
        {
            get => _placeholderVisibility;
            set { _placeholderVisibility = value; OnPropertyChanged(); }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public Visibility ActiveActionsVisibility
        {
            get => _activeActionsVisibility;
            set { _activeActionsVisibility = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
