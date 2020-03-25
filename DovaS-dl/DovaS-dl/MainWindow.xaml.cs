using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace DovaS_dl
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private Thread ProcessThread { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            ServicePointManager.DefaultConnectionLimit = int.MaxValue;

            this.Loaded += MainWindow_Loaded;
            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            HttpWebRequest bgmPageRequest = (HttpWebRequest)WebRequest.Create(string.Format("https://dova-s.jp/bgm/"));
            bgmPageRequest.ReadWriteTimeout = 5000;
            HttpWebResponse bgmPageResponse = (HttpWebResponse)bgmPageRequest.GetResponse();
            using (StreamReader streamReader = new StreamReader(bgmPageResponse.GetResponseStream()))
            {
                string result = streamReader.ReadToEnd();
                Match match = Regex.Match(result, "/bgm/play(?<Id>[0-9]+)\\.html");
                if (match.Success)
                    EndIdBox.Text = match.Groups["Id"].Value;
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            Abort();
        }

        private void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            StartIdBox.IsEnabled = false;
            EndIdBox.IsEnabled = false;
            StartBtn.IsEnabled = false;
            AbortBtn.IsEnabled = true;

            CurrentIdBox.Text = "-";

            uint startId = uint.Parse(StartIdBox.Text);
            uint endId = uint.Parse(EndIdBox.Text);


            ProcessThread = new Thread(() =>
            {
                uint i = startId;
                while (i <= endId)
                {
                    Dispatcher.Invoke(() =>
                    {
                        CurrentIdBox.Text = i.ToString();
                    });

                    try
                    {
                        List<TrackFileDownloader> trackFileDownloaders = GetDownloadOptions(i);
                        if (trackFileDownloaders != null)
                        {
                            foreach (TrackFileDownloader trackFileDownloader in trackFileDownloaders)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    CurrentIdBox.Text = string.Format("{0} - {1} Downloading file", i, trackFileDownloader.TrackId);
                                });
                                trackFileDownloader.Download("Downloads");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Not exists: {0}", i);
                        }

                        i++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        Thread.Sleep(5000);
                    }
                }
                Dispatcher.Invoke(() =>
                {
                    StartIdBox.IsEnabled = true;
                    EndIdBox.IsEnabled = true;
                    StartBtn.IsEnabled = true;
                    AbortBtn.IsEnabled = false;
                    CurrentIdBox.Text = "-";
                });
            });
            ProcessThread.Start();
        }

        private void AbortBtn_Click(object sender, RoutedEventArgs e)
        {
            Abort();
            StartIdBox.IsEnabled = true;
            EndIdBox.IsEnabled = true;
            StartBtn.IsEnabled = true;
            AbortBtn.IsEnabled = false;
        }

        private void Abort()
        {
            if (ProcessThread != null)
            {
                ProcessThread.Abort();
                ProcessThread.Join();
            }

        }

        private List<TrackFileDownloader> GetDownloadOptions(uint id)
        {
            Dispatcher.Invoke(() =>
            {
                CurrentIdBox.Text = string.Format("{0} Requesting playing page", id);
            });
            // Request preview page
            HttpWebRequest playPageRequest = (HttpWebRequest)WebRequest.Create(string.Format("https://dova-s.jp/bgm/play{0}.html", id));
            playPageRequest.ReadWriteTimeout = 5000;
            HttpWebResponse playPageResponse = (HttpWebResponse)playPageRequest.GetResponse();

            if (playPageResponse.ResponseUri.AbsoluteUri == "https://dova-s.jp/_contents/_error/404.html")
                return null;

            string playPageResult;
            using (Stream stream = playPageResponse.GetResponseStream())
            {
                using (StreamReader streamReader = new StreamReader(stream))
                {
                    playPageResult = streamReader.ReadToEnd();
                }
                stream.Close();
            }


            // Parse preview page
            Match downloadFormMatch = Regex.Match(playPageResult, string.Format("<form action=\"download{0}\\.html\" method=\"post\">[\\S\\s]+?</form>", id));
            MatchCollection downloadFormMatchCollection = Regex.Matches(downloadFormMatch.Value, "<input type=\"([^\\\"\\\\]|(\\\\.))+\" name=\"(?<Name>([^\\\"\\\\]|(\\\\.))+)\" value=\"(?<Value>([^\\\"\\\\]|(\\\\.))+)\">");

            List<string> downloadQueries = new List<string>();
            foreach (Match m in downloadFormMatchCollection)
            {
                downloadQueries.Add(string.Format("{0}={1}", HttpUtility.UrlEncode(m.Groups["Name"].Value), HttpUtility.UrlEncode(m.Groups["Value"].Value)));
            }
            string downloadQuery = string.Join("&", downloadQueries);

            playPageResponse.Close();


            Dispatcher.Invoke(() =>
            {
                CurrentIdBox.Text = string.Format("{0} Requesting downloading page", id);
            });

            // Request download page
            HttpWebRequest downloadPageRequest = (HttpWebRequest)WebRequest.Create(string.Format("https://dova-s.jp/bgm/download{0}.html", id));
            downloadPageRequest.ReadWriteTimeout = 5000;
            downloadPageRequest.Method = "POST";
            downloadPageRequest.ContentType = "application/x-www-form-urlencoded";
            downloadPageRequest.CookieContainer = new CookieContainer();

            using (Stream stream = downloadPageRequest.GetRequestStream())
            {
                using (StreamWriter streamWriter = new StreamWriter(stream))
                {
                    streamWriter.WriteLine(downloadQuery);
                }
                stream.Close();
            }

            HttpWebResponse downloadPageResponse = (HttpWebResponse)downloadPageRequest.GetResponse();
            string downloadPageResult;
            using (Stream stream = downloadPageResponse.GetResponseStream())
            {
                using (StreamReader streamReader = new StreamReader(stream))
                {
                    downloadPageResult = streamReader.ReadToEnd();
                }
                stream.Close();
            }


            // Parse download page
            Match fileFormMatch = Regex.Match(downloadPageResult, "<form name=\"toFileMp3\" action=\"inc/file\\.html\" method=\"post\">[\\S\\s]+?</form>");
            MatchCollection fileFormMatchCollection = Regex.Matches(fileFormMatch.Value, "<input type=\"([^\\\"\\\\]|(\\\\.))+\" name=\"(?<Name>([^\\\"\\\\]|(\\\\.))+)\" value=\"(?<Value>([^\\\"\\\\]|(\\\\.))+)\">");

            List<string> fileQueries = new List<string>();
            foreach (Match m in fileFormMatchCollection)
            {
                fileQueries.Add(string.Format("{0}={1}", HttpUtility.UrlEncode(m.Groups["Name"].Value), HttpUtility.UrlEncode(m.Groups["Value"].Value)));
            }
            string fileQuery = string.Join("&", fileQueries);


            // Parse avaliable trackes
            Match tracksOptionMatch = Regex.Match(downloadPageResult, "<select name=\"tracks\" size=\"[0-9]+\" id=\"trackSelect\">[\\S\\s]+?</select>");
            MatchCollection tracksOptionMatchCollection = Regex.Matches(tracksOptionMatch.Value, "<option value=\"(?<Id>[0-9]+)\"( selected=\"selected\")?>(?<Name>.+?)</option>");

            List<TrackFileDownloader> trackFileDownloaders = new List<TrackFileDownloader>();
            foreach (Match m in tracksOptionMatchCollection)
            {
                trackFileDownloaders.Add(new TrackFileDownloader(fileQuery, id, uint.Parse(m.Groups["Id"].Value), downloadPageResponse.Cookies));
            }

            downloadPageResponse.Close();

            return trackFileDownloaders;
        }

        private class TrackFileDownloader
        {
            public uint FileId { get; private set; }
            public uint TrackId { get; private set; }
            public string FileQuery { get; private set; }
            public CookieCollection DownloadCookies { get; private set; }

            private string TrackQuery { get; set; }
            private FileStream DownloadFileStream { get; set; }

            public TrackFileDownloader(string fileQuery, uint fileId, uint trackId, CookieCollection cookieCollection)
            {
                FileId = fileId;
                TrackId = trackId;
                FileQuery = fileQuery;
                TrackQuery = string.Format("tracks={0}&{1}", trackId, fileQuery);
                DownloadCookies = cookieCollection;
            }

            public void Download(string downloadDirectory)
            {
                HttpWebRequest request = MakeDownloadRequest();
                using (TcpClient client = new TcpClient())
                {
                    client.SendTimeout = request.ReadWriteTimeout;
                    client.ReceiveTimeout = request.ReadWriteTimeout;
                    client.Connect(request.Host, 443);
                    Stream tcpStream = client.GetStream();
                    using (SslStream sslStream = new SslStream(tcpStream))
                    {
                        sslStream.AuthenticateAsClient(request.Host);
                        using (StreamWriter streamWriter = new StreamWriter(sslStream))
                        {
                            using (StreamReader streamReader = new StreamReader(sslStream))
                            {
                                // Write request
                                streamWriter.WriteLine(string.Format("{0} {1} HTTP/{2}", request.Method, request.Address.AbsoluteUri, request.ProtocolVersion));
                                streamWriter.WriteLine(string.Format("Content-Length: {0}", request.ContentLength));
                                streamWriter.Write(request.Headers.ToString());
                                streamWriter.WriteLine(TrackQuery);
                                streamWriter.Flush();

                                // Read response
                                // Read response line and headers
                                string responseLine = streamReader.ReadLine();
                                Dictionary<string, string> headers = new Dictionary<string, string>();
                                while (true)
                                {
                                    string header = streamReader.ReadLine();
                                    if (header == string.Empty)
                                        break;
                                    string[] keyValue = header.Split(new string[] { ": " }, 2, StringSplitOptions.RemoveEmptyEntries);
                                    headers.Add(keyValue[0], keyValue[1]);
                                }

                                // Find filename
                                Match match = Regex.Match(headers["Content-Disposition"], "attachment; filename=\"(?<Filename>.+?)\"");
                                string filename = string.Format("{0} - {1}", FileId, match.Groups["Filename"].Value);

                                // Prepare directory
                                string fullFilename = Path.Combine(downloadDirectory, MakeValidFileName(filename));
                                if (!Directory.Exists(downloadDirectory))
                                    Directory.CreateDirectory(downloadDirectory);
                                
                                // Save content
                                using (DownloadFileStream = new FileStream(fullFilename, FileMode.Create))
                                {
                                    byte b;
                                    List<byte> lengthBuffer = new List<byte>();
                                    while (true)
                                    {
                                        // Read part length
                                        lengthBuffer.Clear();
                                        while (true)
                                        {
                                            b = (byte)sslStream.ReadByte();
                                            if (b != 0x0D)
                                                lengthBuffer.Add(b);
                                            else
                                            {
                                                b = (byte)sslStream.ReadByte();
                                                if (b == 0x0A)
                                                    break;
                                                else
                                                    new Exception();
                                            }
                                        }
                                        // Parse part length
                                        string hexLength = Encoding.ASCII.GetString(lengthBuffer.ToArray());
                                        int length = Convert.ToInt32(hexLength, 16);

                                        // Eof then break
                                        if (length == 0)
                                            break;

                                        // Save part
                                        int position = 0;
                                        byte[] buffer = new byte[length];
                                        while (position != length)
                                        {
                                            int rlength = sslStream.Read(buffer, position, buffer.Length - position);
                                            position += rlength;
                                        }
                                        DownloadFileStream.Write(buffer, 0, length);

                                        // Part end
                                        b = (byte)sslStream.ReadByte();
                                        if (b != 0x0D)
                                            new Exception();
                                        else
                                        {
                                            b = (byte)sslStream.ReadByte();
                                            if (b != 0x0A)
                                                new Exception();
                                        }
                                    }

                                }
                            }
                        }
                    }

                }
            }
            
            private HttpWebRequest MakeDownloadRequest()
            {
                HttpWebRequest fileRequest = (HttpWebRequest)WebRequest.Create("https://dova-s.jp/bgm/inc/file.html");
                fileRequest.ReadWriteTimeout = 5000;
                fileRequest.Method = "POST";
                fileRequest.ContentType = "application/x-www-form-urlencoded";
                fileRequest.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/80.0.3987.149 Safari/537.36";

                CookieContainer cookieContainer = new CookieContainer();
                cookieContainer.Add(DownloadCookies);
                fileRequest.CookieContainer = cookieContainer;
                fileRequest.Referer = string.Format("https://dova-s.jp/bgm/download{0}.html", FileId);

                using (Stream stream = fileRequest.GetRequestStream())
                {
                    using (StreamWriter streamWriter = new StreamWriter(stream))
                    {
                        streamWriter.WriteLine(TrackQuery);
                    }
                    stream.Close();
                }

                return fileRequest;
            }

            private static string MakeValidFileName(string text, string replacement = "_")
            {
                StringBuilder str = new StringBuilder();
                var invalidFileNameChars = Path.GetInvalidFileNameChars();
                foreach (var c in text)
                {
                    if (invalidFileNameChars.Contains(c))
                    {
                        str.Append(replacement ?? "");
                    }
                    else
                    {
                        str.Append(c);
                    }
                }

                return str.ToString();
            }

        }

    }

}
