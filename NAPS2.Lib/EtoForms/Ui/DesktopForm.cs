using System.Collections.Immutable;
using System.ComponentModel;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using Eto.Drawing;
using Eto.Forms;
using HarmonyLib;
using NAPS2.EtoForms.Desktop;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.ImportExport.Images;
using NAPS2.Scan;
using NAPS2.Scan.Batch;
using Newtonsoft.Json;
using PureWebSockets;
namespace NAPS2.EtoForms.Ui;

public class SockMessage
{
    public string? code { get; set; }
    public string? message { get; set; }
    public string? base64img { get; set; }
}

[HarmonyPatch(typeof(MessageBox))]
[HarmonyPatch(nameof(MessageBox.Show), typeof(Control), typeof(string), typeof(string), typeof(MessageBoxButtons), typeof(MessageBoxType), typeof(MessageBoxDefaultButton))]
public class HarmonyPatchForMessageBox
{
    //MessageBoxType _type = MessageBoxType.Information;
    static bool Prefix(object __instance, Control parent, string text, string caption, MessageBoxButtons buttons, MessageBoxType type)
    {
        string code = "4000";
        switch(type)
        {
            case MessageBoxType.Error:
                code = "4003";
                break;
            case MessageBoxType.Warning:
                code = "4002";
                break;
            case MessageBoxType.Question:
                code = "4001";
                break;
            case MessageBoxType.Information:
            default:
                break;
        }
        SockMessage message = new SockMessage() { message = text, code = code };
        if (DesktopForm.sock != null && DesktopForm.sock.State == WebSocketState.Open)
        {
            DesktopForm.sock.Send(JsonConvert.SerializeObject(message));
            return false;
        }
        else {
            return true;
        }
    }
    static void Postfix(ref DialogResult __result, MessageBoxType type, MessageBoxButtons buttons)
    {
        if (DesktopForm.sock != null && DesktopForm.sock.State == WebSocketState.Open)
        {
            __result = (buttons == MessageBoxButtons.YesNo || buttons == MessageBoxButtons.YesNoCancel) ? DialogResult.Yes : DialogResult.Ok;
        }
    }
};

public abstract class DesktopForm : EtoFormBase
{
    private readonly DesktopKeyboardShortcuts _keyboardShortcuts;
    private readonly INotificationManager _notify;
    private readonly CultureHelper _cultureHelper;
    protected readonly ColorScheme _colorScheme;
    private readonly IProfileManager _profileManager;
    private readonly ImageTransfer _imageTransfer;
    protected readonly ThumbnailController _thumbnailController;
    private readonly UiThumbnailProvider _thumbnailProvider;
    protected readonly DesktopController _desktopController;
    private readonly IDesktopScanController _desktopScanController;
    private readonly ImageListActions _imageListActions;
    private readonly DesktopFormProvider _desktopFormProvider;
    private readonly IDesktopSubFormController _desktopSubFormController;

    protected readonly ListProvider<Command> _scanMenuCommands = new();
    private readonly ListProvider<Command> _languageMenuCommands = new();
    private readonly ContextMenu _contextMenu = new();

    protected IListView<UiImage> _listView;
    private ImageListSyncer? _imageListSyncer;
    public static PureWebSocket? sock = null;
    private static Harmony? harmony = null;

    public DesktopForm(
        Naps2Config config,
        DesktopKeyboardShortcuts keyboardShortcuts,
        INotificationManager notify,
        CultureHelper cultureHelper,
        ColorScheme colorScheme,
        IProfileManager profileManager,
        UiImageList imageList,
        ImageTransfer imageTransfer,
        ThumbnailController thumbnailController,
        UiThumbnailProvider thumbnailProvider,
        DesktopController desktopController,
        IDesktopScanController desktopScanController,
        ImageListActions imageListActions,
        ImageListViewBehavior imageListViewBehavior,
        DesktopFormProvider desktopFormProvider,
        IDesktopSubFormController desktopSubFormController,
        DesktopCommands commands) : base(config)
    {
        harmony = new Harmony("naps2");
        harmony.PatchAll();

        _keyboardShortcuts = keyboardShortcuts;
        _notify = notify;
        _cultureHelper = cultureHelper;
        _colorScheme = colorScheme;
        _profileManager = profileManager;
        ImageList = imageList;
        _imageTransfer = imageTransfer;
        _thumbnailController = thumbnailController;
        _thumbnailProvider = thumbnailProvider;
        _desktopController = desktopController;
        _desktopScanController = desktopScanController;
        _imageListActions = imageListActions;
        _desktopFormProvider = desktopFormProvider;
        _desktopSubFormController = desktopSubFormController;
        Commands = commands;

        // PostInitializeComponent();
        //
        CreateToolbarsAndMenus();
        UpdateScanButton();
        InitLanguageDropdown();

        _listView = EtoPlatform.Current.CreateListView(imageListViewBehavior);
        _listView.Selection = ImageList.Selection;
        _listView.ItemClicked += ListViewItemClicked;
        _listView.Drop += ListViewDrop;
        _listView.SelectionChanged += ListViewSelectionChanged;
        _listView.ImageSize = _thumbnailController.VisibleSize;
        _listView.ContextMenu = _contextMenu;

        // TODO: Fix Eto so that we don't need to set an item here (otherwise the first time we right click nothing happens)
        _contextMenu.Items.Add(Commands.SelectAll);
        _contextMenu.Opening += OpeningContextMenu;
        if (!EtoPlatform.Current.IsMac)
        {
            // For Mac the menu shortcuts work without needing manual hooks
            // Maybe at some point we can support custom assignment on Mac, though we'll need to fix Ctrl vs Command
            _keyboardShortcuts.Assign(Commands);
        }
        KeyDown += OnKeyDown;
        _listView.Control.KeyDown += OnKeyDown;
        _listView.Control.MouseWheel += ListViewMouseWheel;

        //
        // Shown += FDesktop_Shown;
        // Closing += FDesktop_Closing;
        // Closed += FDesktop_Closed;
        _desktopFormProvider.DesktopForm = this;
        _thumbnailController.ListView = _listView;
        _thumbnailController.ThumbnailSizeChanged += ThumbnailController_ThumbnailSizeChanged;
        SetThumbnailSpacing(_thumbnailController.VisibleSize);
        ImageList.SelectionChanged += ImageList_SelectionChanged;
        ImageList.ImagesUpdated += ImageList_ImagesUpdated;
        _profileManager.ProfilesUpdated += ProfileManager_ProfilesUpdated;
        StartServer();
    }

    public static void StopSock()
    {
        if (sock != null)
        {
            sock.Disconnect();
            sock.Dispose();
        }
    }
    void StartServer()
    {
        var socketOptions = new PureWebSocketOptions
        {
            DebugMode = false, // set this to true to see a ton O' logging
            SendDelay = 100, // the delay in ms between sending messages
            IgnoreCertErrors = true,
            MyReconnectStrategy = new ReconnectStrategy(2000, 4000, 20) // automatic reconnect if connection is lost
        };

        if (sock == null)
        {
            sock = new PureWebSocket("ws://127.0.0.1:1488", socketOptions);
            sock.OnOpened += (object sender) => {
                SockMessage message = new SockMessage() { code = "2211" };
                sock.Send(JsonConvert.SerializeObject(message));
            };
            sock.OnMessage += async (object? sender, string message) =>
            {
                var msgObj = JsonConvert.DeserializeObject<SockMessage>(message);
                //MessageBox.Show(message);
                if (msgObj != null)
                {
                    switch (msgObj.code)
                    {
                        case "1100":
                            await _desktopScanController.ScanDefault();
                            _desktopController.Cleanup();
                            _desktopController.Suspend();
                            Close();
                            break;
                        case "1101":
                            await _desktopScanController.ScanDefault();
                            break;
                        case "1103":
                            ShowBatch();
                            //new BatchScanPerformer().PerformBatchScan(new BatchSettings() { });
                            break;
                        case "2101":
                            var msgOut = new SockMessage() { code = "2201", message = $"NAPS2 {string.Format(MiscResources.Version, AssemblyHelper.Version)}" };
                            sock.Send(JsonConvert.SerializeObject(msgOut));
                            break;
                        case "3001":
                            _desktopController.Cleanup();
                            _desktopController.Suspend();
                            StopSock();
                            Close();
                            break;
                        case "3002":
                            Visible = false;
                            break;
                        case "3003":
                            Visible = false;
                            ShowInTaskbar = false;
                            break;
                        case "3004":
                            //Visible = true;
                            ShowInTaskbar = true;
                            Show();
                            break;
                        case "3005":
                            ShowInTaskbar = true;
                            break;
                        default:
                            break;
                    }
                }
            };
            sock.OnSendFailed += (object sender, byte[] data, Exception ex) =>
            {
                Console.WriteLine($"{DateTime.Now} {((PureWebSocket)sender).InstanceName} Send Failed: {ex.Message}\r\n", ConsoleColor.Red);
            };
        }

        ScanController.OnProcess += ShareImage;
        ScanController.OnStart += async (object sender, EventArgs args) =>
        {
            SockMessage message = new SockMessage() { code = "0220" };
            await sock.SendAsync(JsonConvert.SerializeObject(message));
        };
        ScanController.OnEnd += async (object sender, EventArgs args) =>
        {
            SockMessage message = new SockMessage() { code = "0222" };
            await sock.SendAsync(JsonConvert.SerializeObject(message));
        };
        ScanController.OnError += async (object sender, string msg) =>
        {
            SockMessage message = new SockMessage() { code = "0300", message = msg };
            await sock.SendAsync(JsonConvert.SerializeObject(message));
        };
        //sock.OnClosed += (object sender, WebSocketCloseStatus reason) => {
        //    isConnected = false;
        //};

        sock.ConnectAsync();
        //sock.OnStateChanged += (object sender, WebSocketState newState, WebSocketState prevState) => { 
        //    if (newState === WebSocketState.Open)
        //};
        //if (server == null)
        //{
        //    server = new WebSocketServer("ws://0.0.0.0:8181");
        //    if (allSockets == null)
        //    {
        //        allSockets = new List<IWebSocketConnection>();
        //    }
        //    server.Start(socket =>
        //    {
        //        socket.OnOpen = () =>
        //        {
        //            Debug.WriteLine("Open!");
        //            allSockets.Add(socket);
        //        };
        //        socket.OnClose = () =>
        //        {
        //            Debug.WriteLine("Close!");
        //            allSockets.Remove(socket);
        //        };
        //        socket.OnMessage = message =>
        //        {
        //            Debug.WriteLine($"{message}");
        //        };
        //    });
        //}
    }

    public static async void ShareImage(object? sender, ProcessedImage image)
    {
        if (sock != null && sock.State == WebSocketState.Open)
        {
            var toSend = image.Clone();
            //sock.Send("0211");
            var base64img = Convert.ToBase64String(toSend.Render().SaveToMemoryStream(ImageFileFormat.Png).ToArray());
            SockMessage message = new SockMessage() { code = "0211", base64img = base64img };
            await sock.SendAsync(JsonConvert.SerializeObject(message));
            toSend.Dispose();
        }
    }

    public void ShowBatch ()
    {
        Application.Instance.Invoke(Commands.BatchScan.Execute);
    }
    protected override void BuildLayout()
    {
        Icon = Icons.favicon.ToEtoIcon();

        FormStateController.AutoLayoutSize = false;
        FormStateController.DefaultClientSize = new Size(1210, 600);

        LayoutController.RootPadding = 0;
        LayoutController.Content = L.Overlay(
            GetMainContent(),
            L.Column(
                C.Filler(),
                L.Row(GetZoomButtons(), C.Filler())
            ).Padding(10)
        );

        UpdateColors();
        // TODO: Memory leak?
        _colorScheme.ColorSchemeChanged += (_, _) => UpdateColors();
    }

    protected virtual void UpdateColors()
    {
    }

    private void OpeningContextMenu(object? sender, EventArgs e)
    {
        _contextMenu.Items.Clear();
        if (!EtoPlatform.Current.IsMac)
        {
            // TODO: Can't do this on Mac yet as it disables the menu item indefinitely
            Commands.Paste.Enabled = _imageTransfer.IsInClipboard();
        }
        if (ImageList.Selection.Any())
        {
            // TODO: Remove icon from delete command somehow
            // TODO: Is this memory leaking (because of event handlers) when commands are converted to menuitems?
            _contextMenu.Items.AddRange(new List<MenuItem>
            {
                Commands.ViewImage,
                new SeparatorMenuItem(),
                Commands.SelectAll,
                Commands.Copy,
                Commands.Paste,
                new SeparatorMenuItem(),
                Commands.Delete
            });
        }
        else
        {
            _contextMenu.Items.AddRange(new List<MenuItem>
            {
                Commands.SelectAll,
                Commands.Paste
            });
        }
    }

    private void ImageList_SelectionChanged(object? sender, EventArgs e)
    {
        Invoker.Current.Invoke(() =>
        {
            UpdateToolbar();
            _listView!.Selection = ImageList.Selection;
        });
    }

    private void ImageList_ImagesUpdated(object? sender, ImageListEventArgs e)
    {
        Invoker.Current.Invoke(UpdateToolbar);
    }

    private void ProfileManager_ProfilesUpdated(object? sender, EventArgs e)
    {
        UpdateScanButton();
    }

    private void ThumbnailController_ThumbnailSizeChanged(object? sender, EventArgs e)
    {
        SetThumbnailSpacing(_thumbnailController.VisibleSize);
        UpdateToolbar();
    }

    protected UiImageList ImageList { get; }
    protected DesktopCommands Commands { get; }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _imageListSyncer = new ImageListSyncer(ImageList, _listView.ApplyDiffs, SynchronizationContext.Current!);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateToolbar();
        await _desktopController.Initialize();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_desktopController.PrepareForClosing(true))
        {
            e.Cancel = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _desktopController.Cleanup();

        // TODO: Make sure we don't have any remaining memory leaks (toolbars? commands?)
        _thumbnailController.ThumbnailSizeChanged -= ThumbnailController_ThumbnailSizeChanged;
        ImageList.SelectionChanged -= ImageList_SelectionChanged;
        ImageList.ImagesUpdated -= ImageList_ImagesUpdated;
        _profileManager.ProfilesUpdated -= ProfileManager_ProfilesUpdated;
        _imageListSyncer?.Dispose();
        StopSock();
    }

    protected virtual void CreateToolbarsAndMenus()
    {
        ToolBar = new ToolBar();
        ConfigureToolbar();

        var hiddenButtons = Config.Get(c => c.HiddenButtons);

        if (!hiddenButtons.HasFlag(ToolbarButtons.Scan))
            CreateToolbarButtonWithMenu(Commands.Scan, DesktopToolbarMenuType.Scan, new MenuProvider()
                .Dynamic(_scanMenuCommands)
                .Separator()
                .Append(Commands.NewProfile)
                .Append(Commands.BatchScan));
        if (!hiddenButtons.HasFlag(ToolbarButtons.Profiles))
            CreateToolbarButton(Commands.Profiles);
        if (!hiddenButtons.HasFlag(ToolbarButtons.Ocr))
            CreateToolbarButton(Commands.Ocr);
        if (!hiddenButtons.HasFlag(ToolbarButtons.Import))
            CreateToolbarButton(Commands.Import);
        CreateToolbarSeparator();
        if (!hiddenButtons.HasFlag(ToolbarButtons.SavePdf))
            CreateToolbarButtonWithMenu(Commands.SavePdf, DesktopToolbarMenuType.SavePdf, new MenuProvider()
                .Append(Commands.SaveAllPdf)
                .Append(Commands.SaveSelectedPdf)
                .Separator()
                .Append(Commands.PdfSettings));
        if (!hiddenButtons.HasFlag(ToolbarButtons.SaveImages))
            CreateToolbarButtonWithMenu(Commands.SaveImages, DesktopToolbarMenuType.SaveImages, new MenuProvider()
                .Append(Commands.SaveAllImages)
                .Append(Commands.SaveSelectedImages)
                .Separator()
                .Append(Commands.ImageSettings));
        if (!hiddenButtons.HasFlag(ToolbarButtons.EmailPdf) && PlatformCompat.System.CanEmail)
            CreateToolbarButtonWithMenu(Commands.EmailPdf, DesktopToolbarMenuType.EmailPdf, new MenuProvider()
                .Append(Commands.EmailAll)
                .Append(Commands.EmailSelected)
                .Separator()
                .Append(Commands.EmailSettings)
                .Append(Commands.PdfSettings));
        if (!hiddenButtons.HasFlag(ToolbarButtons.Print) && PlatformCompat.System.CanPrint)
            CreateToolbarButton(Commands.Print);
        CreateToolbarSeparator();
        if (!hiddenButtons.HasFlag(ToolbarButtons.Image))
            CreateToolbarMenu(Commands.ImageMenu, new MenuProvider()
                .Append(Commands.ViewImage)
                .Separator()
                .Append(Commands.Crop)
                .Append(Commands.BrightCont)
                .Append(Commands.HueSat)
                .Append(Commands.BlackWhite)
                .Append(Commands.Sharpen)
                .Append(Commands.DocumentCorrection)
                .Separator()
                .Append(Commands.ResetImage));
        if (!hiddenButtons.HasFlag(ToolbarButtons.Rotate))
            CreateToolbarMenu(Commands.RotateMenu, GetRotateMenuProvider());
        if (!hiddenButtons.HasFlag(ToolbarButtons.Move))
            CreateToolbarStackedButtons(Commands.MoveUp, Commands.MoveDown);
        if (!hiddenButtons.HasFlag(ToolbarButtons.Reorder))
            CreateToolbarMenu(Commands.ReorderMenu, new MenuProvider()
                .Append(Commands.Interleave)
                .Append(Commands.Deinterleave)
                .Separator()
                .Append(Commands.AltInterleave)
                .Append(Commands.AltDeinterleave)
                .Separator()
                .SubMenu(Commands.ReverseMenu, new MenuProvider()
                    .Append(Commands.ReverseAll)
                    .Append(Commands.ReverseSelected)));
        CreateToolbarSeparator();
        if (!hiddenButtons.HasFlag(ToolbarButtons.Delete))
            CreateToolbarButton(Commands.Delete);
        if (!hiddenButtons.HasFlag(ToolbarButtons.Clear))
            CreateToolbarButton(Commands.ClearAll);
        CreateToolbarSeparator();
        if (!hiddenButtons.HasFlag(ToolbarButtons.Language))
            CreateToolbarMenu(Commands.LanguageMenu, GetLanguageMenuProvider());
        if (!hiddenButtons.HasFlag(ToolbarButtons.About))
            CreateToolbarButton(Commands.About);
    }

    public virtual void ShowToolbarMenu(DesktopToolbarMenuType menuType)
    {
    }

    protected MenuProvider GetRotateMenuProvider() =>
        new MenuProvider()
            .Append(Commands.RotateLeft)
            .Append(Commands.RotateRight)
            .Append(Commands.Flip)
            .Append(Commands.Deskew)
            .Append(Commands.CustomRotate);

    protected MenuProvider GetLanguageMenuProvider()
    {
        return new MenuProvider().Dynamic(_languageMenuCommands);
    }

    protected virtual void ConfigureToolbar()
    {
    }

    protected virtual void CreateToolbarButton(Command command) => throw new InvalidOperationException();

    protected virtual void CreateToolbarButtonWithMenu(Command command, DesktopToolbarMenuType menuType,
        MenuProvider menu) =>
        throw new InvalidOperationException();

    protected virtual void CreateToolbarMenu(Command command, MenuProvider menu) =>
        throw new InvalidOperationException();

    protected virtual void CreateToolbarStackedButtons(Command command1, Command command2) =>
        throw new InvalidOperationException();

    protected virtual void CreateToolbarSeparator() => throw new InvalidOperationException();

    // TODO: Can we generalize this kind of logic?
    protected SubMenuItem CreateSubMenu(Command menuCommand, MenuProvider menuProvider)
    {
        var menuItem = new SubMenuItem
        {
            Text = menuCommand.MenuText,
            Image = menuCommand.Image
        };
        menuProvider.Handle(subItems =>
        {
            menuItem.Items.Clear();
            foreach (var subItem in subItems)
            {
                switch (subItem)
                {
                    case MenuProvider.CommandItem { Command: var command }:
                        menuItem.Items.Add(new ButtonMenuItem(command));
                        break;
                    case MenuProvider.SeparatorItem:
                        menuItem.Items.Add(new SeparatorMenuItem());
                        break;
                    case MenuProvider.SubMenuItem:
                        throw new NotImplementedException();
                }
            }
        });
        return menuItem;
    }

    protected virtual LayoutElement GetMainContent() => _listView.Control;

    protected virtual LayoutElement GetZoomButtons()
    {
        var zoomIn = C.ImageButton(Commands.ZoomIn);
        EtoPlatform.Current.ConfigureZoomButton(zoomIn);
        var zoomOut = C.ImageButton(Commands.ZoomOut);
        EtoPlatform.Current.ConfigureZoomButton(zoomOut);
        return L.Row(zoomOut, zoomIn).Spacing(-1);
    }

    private void InitLanguageDropdown()
    {
        _languageMenuCommands.Value = _cultureHelper.GetAvailableCultures().Select(x =>
            new ActionCommand(() => SetCulture(x.langCode))
            {
                MenuText = x.langName
            }).ToImmutableList<Command>();
    }

    protected virtual void SetCulture(string cultureId)
    {
        _desktopController.Suspend();
        try
        {
            Config.User.Set(c => c.Culture, cultureId);
            _cultureHelper.SetCulturesFromConfig();
            FormStateController.DoSaveFormState();
            var newDesktop = FormFactory.Create<DesktopForm>();
            newDesktop.Show();
            SetMainForm(newDesktop);
            Close();
        }
        finally
        {
            _desktopController.Resume();
        }
        // TODO: If we make any other forms non-modal, we will need to refresh them too
    }

    protected virtual void SetMainForm(Form newMainForm)
    {
        Application.Instance.MainForm = newMainForm;
    }

    protected virtual void UpdateToolbar()
    {
        // Top-level toolbar items
        Commands.ImageMenu.Enabled =
            Commands.RotateMenu.Enabled = Commands.MoveUp.Enabled = Commands.MoveDown.Enabled =
                Commands.Delete.Enabled = ImageList.Selection.Any();
        Commands.SavePdf.Enabled = Commands.SaveImages.Enabled = Commands.ClearAll.Enabled =
            Commands.ReorderMenu.Enabled =
                Commands.EmailPdf.Enabled = Commands.Print.Enabled = ImageList.Images.Any();

        // "All" dropdown items
        Commands.SaveAllPdf.Text = Commands.SaveAllImages.Text = Commands.EmailAll.Text =
            Commands.ReverseAll.Text = string.Format(MiscResources.AllCount, ImageList.Images.Count);
        Commands.SaveAllPdf.Enabled = Commands.SaveAllImages.Enabled = Commands.EmailAll.Enabled =
            Commands.ReverseAll.Enabled = ImageList.Images.Any();

        // "Selected" dropdown items
        Commands.SaveSelectedPdf.Text = Commands.SaveSelectedImages.Text = Commands.EmailSelected.Text =
            Commands.ReverseSelected.Text = string.Format(MiscResources.SelectedCount, ImageList.Selection.Count);
        Commands.SaveSelectedPdf.Enabled = Commands.SaveSelectedImages.Enabled = Commands.EmailSelected.Enabled =
            Commands.ReverseSelected.Enabled = ImageList.Selection.Any();

        // Other
        Commands.SelectAll.Enabled = ImageList.Images.Any();
        Commands.ZoomIn.Enabled = ImageList.Images.Any() && _thumbnailController.VisibleSize < ThumbnailSizes.MAX_SIZE;
        Commands.ZoomOut.Enabled = ImageList.Images.Any() && _thumbnailController.VisibleSize > ThumbnailSizes.MIN_SIZE;
        Commands.NewProfile.Enabled =
            !(Config.Get(c => c.NoUserProfiles) && _profileManager.Profiles.Any(x => x.IsLocked));
    }

    private void UpdateScanButton()
    {
        var defaultProfile = _profileManager.DefaultProfile;
        UpdateTitle(defaultProfile);
        var commandList = _profileManager.Profiles.Select(profile =>
                new ActionCommand(() => _desktopScanController.ScanWithProfile(profile))
                {
                    // TODO: Does this need to change on non-WinForms?
                    MenuText = profile.DisplayName.Replace("&", "&&"),
                    Image = profile == defaultProfile ? Icons.accept_small.ToEtoImage() : null
                })
            .ToImmutableList<Command>();
        for (int i = 0; i < commandList.Count; i++)
        {
            _keyboardShortcuts.AssignProfileShortcut(i + 1, commandList[i]);
        }
        _scanMenuCommands.Value = commandList;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = _keyboardShortcuts.Perform(e.KeyData);
    }

    protected virtual void UpdateTitle(ScanProfile? defaultProfile)
    {
        Title = string.Format(UiStrings.Naps2TitleFormat, defaultProfile?.DisplayName ?? UiStrings.Naps2FullName);
    }

    private void ListViewMouseWheel(object? sender, MouseEventArgs e)
    {
        if (e.Modifiers.HasFlag(Keys.Control))
        {
            _thumbnailController.StepSize(e.Delta.Height); //  / (double) SystemInformation.MouseWheelScrollDelta
        }
    }

    protected virtual void SetThumbnailSpacing(int thumbnailSize)
    {
    }

    private void ListViewItemClicked(object? sender, EventArgs e) => _desktopSubFormController.ShowViewerForm();

    private void ListViewSelectionChanged(object? sender, EventArgs e)
    {
        ImageList.UpdateSelection(_listView.Selection);
        UpdateToolbar();
    }

    private void ListViewDrop(object? sender, DropEventArgs args)
    {
        if (args.CustomData != null)
        {
            var data = _imageTransfer.FromBinaryData(args.CustomData);
            if (data.ProcessId == Process.GetCurrentProcess().Id)
            {
                DragMoveImages(args.Position);
            }
            else
            {
                _desktopController.ImportDirect(data, false);
            }
        }
        else if (args.FilePaths != null)
        {
            _desktopController.ImportFiles(args.FilePaths);
        }
    }

    private void DragMoveImages(int position)
    {
        if (!ImageList.Selection.Any())
        {
            return;
        }
        if (position != -1)
        {
            _imageListActions.MoveTo(position);
        }
    }
}