using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

using SharpAvi;
using SharpAvi.Codecs;
using SharpAvi.Output;



namespace GrabThatDesktop
{
    public partial class Form1 : Form
    {
        private bool m_isRecording;
        private List<ToolStripMenuItem> screens;
        private int m_selectedScreenIndex;
        private DimmedForm m_dimmed;
        private Captura.Recorder m_recorder;
        private String m_tempFile;

        public Form1()
        {
            InitializeComponent();
        }

        private void updateContextStrip()
        {
            if (m_isRecording)
            {
                contextMenuStrip1.Items[2].Enabled = true;
                contextMenuStrip1.Items[3].Enabled = false;
            }
            else
            {
                contextMenuStrip1.Items[2].Enabled = false;
                contextMenuStrip1.Items[3].Enabled = true;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            screens = new List<ToolStripMenuItem>();

            Size = new Size(0, 0);
            Visible = false;
            m_isRecording = false;
            m_selectedScreenIndex = -1;

            desktopSelectorStrip.Items.Clear();
            foreach (Screen screen in Screen.AllScreens)
            {
                string display_number = Regex.Match(screen.DeviceName, @"\d+").Value;
                ToolStripMenuItem item = (ToolStripMenuItem) desktopSelectorStrip.Items.Add(display_number);
                screens.Add(item);
                if (screen.Primary)
                {
                    item.Checked = true;
                    m_selectedScreenIndex = screens.Count - 1;
                }
            }

            updateContextStrip();

            m_dimmed = new DimmedForm();
        }

        private void ShowSelectedScreen()
        {
            if (m_selectedScreenIndex < 0 || m_selectedScreenIndex > screens.Count - 1)
            {
                return;
            }

            m_dimmed.Hide();

            m_dimmed.Bounds = Screen.AllScreens[m_selectedScreenIndex].Bounds;
            m_dimmed.Location = Screen.AllScreens[m_selectedScreenIndex].WorkingArea.Location;
            m_dimmed.Show();
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            Hide();
            ShowSelectedScreen();
        }

        private void desktopSelectorStrip_Opening(object sender, CancelEventArgs e)
        {

        }

        private void StartRecording()
        {
            m_isRecording = true;
            updateContextStrip();
            m_dimmed.Hide();

            if (m_recorder != null)
            {
                m_recorder.Dispose();
                m_recorder = null;
            }

            m_tempFile = Path.GetTempFileName();

            m_recorder = new Captura.Recorder(new Captura.RecorderParams(m_tempFile, 10, SharpAvi.KnownFourCCs.Codecs.X264, 100, m_selectedScreenIndex));

        }

        private void StopRecording()
        {
            m_isRecording = false;
            updateContextStrip();
            m_dimmed.Show();

            m_recorder.Dispose();
            m_recorder = null;

            saveFileDialog1.ShowDialog();
        }

        private void contextMenuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem == exitMenuItem)
            {
                System.Windows.Forms.Application.Exit();
            }

            if (e.ClickedItem == startRecording)
            {
                StartRecording();
                return;
            }

            if (e.ClickedItem == stopRecording)
            {
                StopRecording();
                return;
            }
        }

        private void desktopSelectorStrip_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            ToolStripMenuItem selected = screens[0];
            foreach(ToolStripMenuItem item in screens)
            {
                item.Checked = false;
                if (e.ClickedItem == item)
                {
                    selected = item;
                }
            }

            selected.Checked = true;
            Int32.TryParse(selected.Text.Trim(), out m_selectedScreenIndex);
            m_selectedScreenIndex--;

            ShowSelectedScreen();
        }

        private void saveFileDialog1_FileOk(object sender, CancelEventArgs e)
        {
            File.Copy(m_tempFile, saveFileDialog1.FileName, true);
            File.Delete(m_tempFile);
        }
    }

    namespace Captura
    {
        // Used to Configure the Recorder
        public class RecorderParams
        {
            public RecorderParams(string filename, int FrameRate, FourCC Encoder, int Quality, int ScreenIndex)
            {
                FileName = filename;
                FramesPerSecond = FrameRate;
                Codec = Encoder;
                this.Quality = Quality;

                Location = Screen.AllScreens[ScreenIndex].Bounds.Location;
                Height = Screen.AllScreens[ScreenIndex].Bounds.Height;
                Width = Screen.AllScreens[ScreenIndex].Bounds.Width;
            }

            string FileName;
            public int FramesPerSecond, Quality;
            FourCC Codec;

            public Point Location { get; private set; }
            public int Height { get; private set; }
            public int Width { get; private set; }

            public AviWriter CreateAviWriter()
            {
                return new AviWriter(FileName)
                {
                    FramesPerSecond = FramesPerSecond,
                    EmitIndex1 = true,
                };
            }

            public IAviVideoStream CreateVideoStream(AviWriter writer)
            {
                // Select encoder type based on FOURCC of codec
                if (Codec == KnownFourCCs.Codecs.Uncompressed)
                    return writer.AddUncompressedVideoStream(Width, Height);
                else if (Codec == KnownFourCCs.Codecs.MotionJpeg)
                    return writer.AddMotionJpegVideoStream(Width, Height, Quality);
                else
                {
                    return writer.AddMpeg4VideoStream(Width, Height, (double)writer.FramesPerSecond,
                        // It seems that all tested MPEG-4 VfW codecs ignore the quality affecting parameters passed through VfW API
                        // They only respect the settings from their own configuration dialogs, and Mpeg4VideoEncoder currently has no support for this
                        quality: Quality,
                        codec: Codec,
                        // Most of VfW codecs expect single-threaded use, so we wrap this encoder to special wrapper
                        // Thus all calls to the encoder (including its instantiation) will be invoked on a single thread although encoding (and writing) is performed asynchronously
                        forceSingleThreadedAccess: true);
                }
            }
        }

        public class Recorder : IDisposable
        {
            #region Fields
            AviWriter writer;
            RecorderParams Params;
            IAviVideoStream videoStream;
            Thread screenThread;
            ManualResetEvent stopThread = new ManualResetEvent(false);
            #endregion

            public Recorder(RecorderParams Params)
            {
                this.Params = Params;

                // Create AVI writer and specify FPS
                writer = Params.CreateAviWriter();

                // Create video stream
                videoStream = Params.CreateVideoStream(writer);
                // Set only name. Other properties were when creating stream, 
                // either explicitly by arguments or implicitly by the encoder used
                videoStream.Name = "Captura";

                screenThread = new Thread(RecordScreen)
                {
                    Name = typeof(Recorder).Name + ".RecordScreen",
                    IsBackground = true
                };

                screenThread.Start();
            }

            public void Dispose()
            {
                stopThread.Set();
                screenThread.Join();

                // Close writer: the remaining data is written to a file and file is closed
                writer.Close();

                stopThread.Dispose();
            }

            void RecordScreen()
            {
                var frameInterval = TimeSpan.FromSeconds(1 / (double)writer.FramesPerSecond);
                var buffer = new byte[Params.Width * Params.Height * 4];
                Task videoWriteTask = null;
                var timeTillNextFrame = TimeSpan.Zero;

                while (!stopThread.WaitOne(timeTillNextFrame))
                {
                    var timestamp = DateTime.Now;

                    Screenshot(buffer);

                    // Wait for the previous frame is written
                    videoWriteTask?.Wait();

                    // Start asynchronous (encoding and) writing of the new frame
                    videoWriteTask = videoStream.WriteFrameAsync(true, buffer, 0, buffer.Length);

                    timeTillNextFrame = timestamp + frameInterval - DateTime.Now;
                    if (timeTillNextFrame < TimeSpan.Zero)
                        timeTillNextFrame = TimeSpan.Zero;
                }

                // Wait for the last frame is written
                videoWriteTask?.Wait();
            }

            public void Screenshot(byte[] Buffer)
            {
                using (var BMP = new Bitmap(Params.Width, Params.Height))
                {
                    using (var g = Graphics.FromImage(BMP))
                    {
                        g.CopyFromScreen(Params.Location, new Point(0,0), new Size(Params.Width, Params.Height), CopyPixelOperation.SourceCopy);

                        g.Flush();

                        var bits = BMP.LockBits(new Rectangle(0, 0, Params.Width, Params.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                        Marshal.Copy(bits.Scan0, Buffer, 0, Buffer.Length);
                        BMP.UnlockBits(bits);
                    }
                }
            }
        }
    }

}
