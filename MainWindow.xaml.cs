using NAudio.Wave;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace explore
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            DataContext = this;
            m_volume = Properties.Settings.Default.Volume;
            InitializeComponent();
        }

        // ED is keeping its logs open, so we need to open the file with the correct sharing permissions
        public string[] read_all_lines(string path)
        {
            FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            StreamReader reader = new StreamReader(stream);

            List<string> lines = new List<string>();
            while (!reader.EndOfStream)
            {
                lines.Add(reader.ReadLine());
            }

            return lines.ToArray();
        }

        void parse_log(string path, bool setup_only = false)
        {
            bool new_target = false;

            string[] lines = read_all_lines(path);
            foreach (string l in lines)
            {
                dynamic o = JsonConvert.DeserializeObject(l);
                string e = o["event"];

                switch (e)
                {
                    case "FSDJump":
                        string system = o.StarSystem;
                        m_known_systems.Add(system);
                        break;
                    case "FSDTarget":
                        string ts = o.timestamp;
                        string target = o.Name;
                        DateTime timestamp = DateTime.Parse(ts);
                        if (timestamp < m_last_timestamp) break;

                        new_target = (target != m_last_target);

                        m_last_target = target;
                        m_last_timestamp = timestamp;
                        break;
                }
            }

            if (setup_only || !new_target) return;

            bool known = m_known_systems.Contains(m_last_target);
            if (!known)
            {
                // Query EDSM
                try
                {
                    HttpWebRequest request = WebRequest.Create("https://www.edsm.net/api-v1/system?systemName=" + WebUtility.UrlEncode(m_last_target)) as HttpWebRequest;
                    HttpWebResponse response = request.GetResponse() as HttpWebResponse;
                    StreamReader reader = new StreamReader(response.GetResponseStream());
                    if (reader.ReadToEnd() != "[]")
                    {
                        known = true;
                    }
                }
                catch (Exception)
                {
                    Dispatcher.Invoke(() =>
                    {
                        m_system.Text = "Can't connect to EDSM";
                        m_system.Foreground = Brushes.Red;
                    });
                    return;
                }
            }
            Dispatcher.Invoke(() =>
            {
                m_system.Text = m_last_target + " is " + ((known) ? "known" : "unknown");
                m_system.Foreground = (known) ? Brushes.Red : Brushes.Green;
            });
            {
                WaveFileReader reader = new WaveFileReader((known) ? Properties.Resources.known : Properties.Resources.unknown);
                DirectSoundOut sound_out = new DirectSoundOut();
                WaveChannel32 provider = new WaveChannel32(reader);
                provider.Volume = m_volume;
                sound_out.Init(provider);
                sound_out.Play();
            }
        }

        void initialize(string path)
        {
            string[] files = Directory.GetFiles(path, "Journal.*.log");
            foreach (string f in files)
            {
                parse_log(f, true);
            }

            Dispatcher.Invoke(() =>
            {
                m_system.Text = "Ready";
            });
        }

        void update(object sender, FileSystemEventArgs e)
        {
            parse_log(e.FullPath);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                string save_path = Path.Combine(new string[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games\\Frontier Developments\\Elite Dangerous" });
                initialize(save_path);

                m_watcher = new FileSystemWatcher(save_path);
                m_watcher.NotifyFilter = NotifyFilters.LastWrite;
                m_watcher.Changed += update;
                m_watcher.Created += update;
                m_watcher.Filter = "Journal.*.log";
                m_watcher.EnableRaisingEvents = true;
            });
        }

        float _volume = 1.0f;
        public float m_volume
        {
            get { return _volume; }
            set
            {
                _volume = value;
                Properties.Settings.Default.Volume = _volume;
                Properties.Settings.Default.Save();
            }
        }

        HashSet<string> m_known_systems = new HashSet<string>();
        DateTime m_last_timestamp;
        string m_last_target;

        FileSystemWatcher m_watcher;
    }
}
