using Fleck;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Net.Sockets;

namespace NAPS2.Agent
{
    public enum SockMessages {
        StartNAPS2 = 0111,
        OpenSettings = 0112,
        JustScan = 1100,
        ScanAndWait = 1101,
        JustOpen = 1102,
        BatchScan = 1103,
        SendBitmap = 0211,
        IAMNAPS = 2211,
        GetVer = 2101,
        MyVer = 2201,
        GetConnectedNAPS = 3000,
        CloseNAPS = 3001,
        HideNAPS = 3002,
        HideWithTaskBarNAPS = 3003,
        ShowNAPS = 3004,
        ShowInTaskBarNAPS = 3005,
        NAPSError = 4003,
        NAPSWarn = 4002,
        NAPSQuestion = 4001,
        NAPSInfo = 4000,
        ScanStart = 0220,
        ScanEnd = 0222,
        OnError = 0300,
    };

    public class SockMessage
    {
        public SockMessages code { get; set; }
        public string? message { get; set; }
        public string? base64img { get; set; }
    }

    public static class TaskEx
    {
        /// <summary>
        /// Blocks while condition is true or timeout occurs.
        /// </summary>
        /// <param name="condition">The condition that will perpetuate the block.</param>
        /// <param name="frequency">The frequency at which the condition will be check, in milliseconds.</param>
        /// <param name="timeout">Timeout in milliseconds.</param>
        /// <exception cref="TimeoutException"></exception>
        /// <returns></returns>
        public static async Task WaitWhile(Func<bool> condition, int frequency = 25, int timeout = -1)
        {
            var waitTask = Task.Run(async () =>
            {
                while (condition()) await Task.Delay(frequency);
            });

            if (waitTask != await Task.WhenAny(waitTask, Task.Delay(timeout)))
                throw new TimeoutException();
        }

        /// <summary>
        /// Blocks until condition is true or timeout occurs.
        /// </summary>
        /// <param name="condition">The break condition.</param>
        /// <param name="frequency">The frequency at which the condition will be checked.</param>
        /// <param name="timeout">The timeout in milliseconds.</param>
        /// <returns></returns>
        public static async Task WaitUntil(Func<bool> condition, int frequency = 25, int timeout = -1)
        {
            var waitTask = Task.Run(async () =>
            {
                while (!condition()) await Task.Delay(frequency);
            });

            if (waitTask != await Task.WhenAny(waitTask,
                    Task.Delay(timeout)))
                throw new TimeoutException();
        }
    }

    internal static class Program
    {
        static WebSocketServer? server;
        static List<IWebSocketConnection>? allSocketsClients;
        static IWebSocketConnection? NAPS;
        static MemoryStream? memImage;
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            ToolStripMenuItem quit = new ToolStripMenuItem("Quit", null, (sender, e) => Application.Exit());
            ToolStripMenuItem[] items = new ToolStripMenuItem[] { quit };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.AddRange(items);

            NotifyIcon trayIcon = new NotifyIcon()
            {
                Icon = Icons.favicon,
                ContextMenuStrip = menu,
                Visible = true
            };

            Application.ApplicationExit += (object sender, EventArgs e) =>
            {
                if (NAPS != null)
                {
                    SockMessage msg = new SockMessage() { code = SockMessages.CloseNAPS };
                    NAPS.Send(JsonConvert.SerializeObject(msg));
                    NAPS.Close();
                }
                trayIcon.Dispose();
            };

            Thread d = new Thread(StartServer);
            d.Start();
            Application.Run();
        }

        public static byte[] ImageToByte2(Image img)
        {
            using (var stream = new MemoryStream())
            {
                img.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return stream.ToArray();
            }
        }

        static void StartServer()
        {
            if (server == null)
            {
                server = new WebSocketServer("ws://0.0.0.0:1488");
                allSocketsClients = new List<IWebSocketConnection>();
            }
            server.Start(socket =>
            {
                socket.OnOpen = () =>
                {
                    //MessageBox.Show("Open!");
                    allSocketsClients.Add(socket);
                };
                socket.OnClose = () =>
                {
                    //MessageBox.Show("Close!");
                    if (allSocketsClients.Contains(socket))
                    {
                        allSocketsClients.Remove(socket);
                    }
                    if (NAPS == socket)
                    {
                        NAPS = null;
                    }
                };
                socket.OnBinary = mem =>
                {
                    //MessageBox.Show("I've got binary!");
                    if (socket != NAPS) {
                        MessageBox.Show("This client cannot share images");
                        return;
                    }
                    memImage = new MemoryStream();
                    memImage.Write(mem);
                    allSocketsClients?.ForEach(socket =>
                    {
                        socket.Send(memImage.ToArray());
                    });
                };
                socket.OnMessage = async message =>
                {
                    //MessageBox.Show($"{message}");
                    //MessageBox.Show(message, SockMessages.StartNAPS2.ToString());
                    var msgObj = JsonConvert.DeserializeObject<SockMessage>(message);
                    if (msgObj != null)
                    {
                        switch (msgObj.code)
                        {
                            case SockMessages.StartNAPS2:
                                Process.Start("NAPS2.exe");
                                break;
                            case SockMessages.IAMNAPS:
                                if (allSocketsClients != null && allSocketsClients.Contains(socket))
                                {
                                    allSocketsClients.Remove(socket);
                                }
                                if (NAPS == null)
                                {
                                    NAPS = socket;
                                    //NAPS.Send("1100");
                                }
                                else
                                {
                                    MessageBox.Show("Only one NAPS client may be active at once");
                                    socket.Close();
                                }
                                break;
                            case SockMessages.SendBitmap:
                                if (socket == NAPS)
                                {
                                    allSocketsClients.ForEach(cock => cock.Send(message));
                                    memImage = null;
                                    memImage = new MemoryStream();
                                }
                                else
                                {
                                    MessageBox.Show("This client cannot share images");
                                }
                                break;
                            case SockMessages.JustScan:
                            case SockMessages.ScanAndWait:
                            case SockMessages.BatchScan:
                            case SockMessages.JustOpen:
                            case SockMessages.CloseNAPS:
                            case SockMessages.HideNAPS:
                            case SockMessages.ShowNAPS:
                            case SockMessages.HideWithTaskBarNAPS:
                            case SockMessages.ShowInTaskBarNAPS:
                                if (socket != NAPS && NAPS != null)
                                {
                                    await NAPS.Send(message);
                                } else
                                {
                                    Process.Start("NAPS2.exe");
                                    await TaskEx.WaitUntil(() => NAPS != null);
                                    //MessageBox.Show("NAPS started and connected");
                                    await NAPS.Send(message);
                                }
                                break;
                            case SockMessages.GetConnectedNAPS:
                                if (socket != NAPS && NAPS != null)
                                {
                                    var msg2naps = new SockMessage() { code = SockMessages.GetVer };
                                    await NAPS.Send(JsonConvert.SerializeObject(msg2naps));
                                } else
                                {
                                    var msgAnswer = new SockMessage() { code = SockMessages.MyVer, message = "Not connected" };
                                    await socket.Send(JsonConvert.SerializeObject(msgAnswer));
                                }
                                break;
                            case SockMessages.MyVer:
                            case SockMessages.ScanStart:
                            case SockMessages.ScanEnd:
                            case SockMessages.OnError:
                            case SockMessages.NAPSError:
                            case SockMessages.NAPSWarn:
                            case SockMessages.NAPSQuestion:
                            case SockMessages.NAPSInfo:
                                allSocketsClients.ForEach(sock =>
                                {
                                    sock.Send(message);
                                });
                                break;
                            default:
                                break;
                        }

                    }
                };
            });
        }
    }
}