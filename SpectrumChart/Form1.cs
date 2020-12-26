using Accord.Audio;
using Accord.Audio.Windows;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using MsgPack.Serialization;
using System.Data.SQLite;

namespace SpectrumChart
{
    public partial class Form1 : Form
    {
        private ConcurrentQueue<float[]> dataQueue;
        private ConcurrentQueue<float[]> uiQueue;
        private BackgroundWorker backgroundWorkerProcessData;
        private BackgroundWorker backgroundWorkerUpdateUI;
        private BackgroundWorker backgroundWorkerReceiveData;
        private float[] signalBuffer;
        private UdpClient udpClient;
        private IWindow window;
        private const int WindowSize = 512;
        private VibrationACT12x vact;
        private string database = "DataSource = Vibrate.db";

        private Dictionary<string,VibrationACT12x> deviceList;

        public Form1()
        {
            InitializeComponent();

            deviceList = new Dictionary<string, VibrationACT12x>();

            for (int x = 0; x < checkedListBoxChannel.Items.Count; x++)
            {
                this.checkedListBoxChannel.SetItemChecked(x, false);
            }

            dataQueue = new ConcurrentQueue<float[]>();
            uiQueue = new ConcurrentQueue<float[]>();
            backgroundWorkerProcessData = new BackgroundWorker();
            backgroundWorkerReceiveData = new BackgroundWorker();
            backgroundWorkerUpdateUI = new BackgroundWorker();
            signalBuffer = new float[WindowSize];
            backgroundWorkerProcessData.WorkerSupportsCancellation = true;
            backgroundWorkerProcessData.DoWork += BackgroundWorkerProcessData_DoWork;

            backgroundWorkerUpdateUI.WorkerSupportsCancellation = true;
            backgroundWorkerUpdateUI.DoWork += BackgroundWorkerUpdateUI_DoWork;

            backgroundWorkerReceiveData.WorkerSupportsCancellation = true;
            backgroundWorkerReceiveData.DoWork += BackgroundWorkerReceiveData_DoWork;

            window = RaisedCosineWindow.Hann(WindowSize);

            LoadDevices();

            vact = null;

            //Set series chart type
            //chart1.Series["Series1"].ChartType = SeriesChartType.Line;
            //chart2.Series["Series1"].ChartType = SeriesChartType.Line;
            //chart1.Series["Series1"].IsValueShownAsLabel = true;
        }


        private void LoadDevices()
        {
            this.deviceList.Clear();
            using (SQLiteConnection connection = new SQLiteConnection(database))
            {
                connection.Open();
                //string deviceType = "ACT12816";
                string strainStatement = "select RemoteIP,LocalPort,DeviceId,Type,Desc,Path from SensorInfo";
                SQLiteCommand command2 = new SQLiteCommand(strainStatement, connection);
                using (SQLiteDataReader reader = command2.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string remoteIP = reader.GetString(0);
                        int localPort = reader.GetInt32(1);
                        string deviceId = reader.GetString(2);         
                        string type = reader.GetString(3);
                        string description = reader.GetString(4);
                        string path = reader.GetString(5);

                        //int index = this.dataGridView1.Rows.Add();

                        string[] itemString = { description, type, deviceId, remoteIP, localPort.ToString(),path };
                        ListViewItem item = new ListViewItem(itemString);

                        listView1.Items.Add(item);

                         //config = new SerialACT4238Config(portName, baudrate, timeout, deviceId, type);

                        VibrationACT12x device = null;

                        device = new VibrationACT12x(deviceId,remoteIP,localPort,chart1,type,path,this.database,textBoxLog);

                        if (device != null)
                        {
                            this.deviceList.Add(deviceId, device);
                        }
                    }
                }
            }
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listView1.SelectedIndices != null && listView1.SelectedIndices.Count > 0)
            {
                ListView.SelectedIndexCollection c = listView1.SelectedIndices;
                //textBoxId.Text = listView1.Items[c[0]].SubItems[3].Text;
                //textBoxPort.Text = listView1.Items[c[0]].SubItems[4].Text;

                chart1.Titles[0].Text = listView1.Items[c[0]].SubItems[0].Text;
                //chart1.Titles.Add(listView1.Items[c[0]].SubItems[0].Text);

                string key = listView1.Items[c[0]].SubItems[2].Text;

                if (deviceList.ContainsKey(key))
                {
                    //textBoxLog.AppendText("deviceList contains key:" + key + "\r\n");
                    if (vact == null)
                    {
                        vact = deviceList[key];
                        vact.SetUpdateChart(true);
                        //textBoxLog.AppendText("vact is null\r\n");
                    }
                    else
                    {
                        vact.SetUpdateChart(false);
                        vact = deviceList[key];
                        vact.SetUpdateChart(true);
                        //textBoxLog.AppendText("vact is not null\r\n");
                    }
                }

            }
        }

        private void BackgroundWorkerUpdateUI_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bgWorker = sender as BackgroundWorker;

            int numberOfPointsInChart = 200;
            MessagePackSerializer serializer = MessagePackSerializer.Get<AccWave>();
            UdpClient udpClient = null;
            IPAddress remoteIp = IPAddress.Parse("192.168.100.31");
            try
            {
                udpClient = new UdpClient();
                udpClient.Connect("192.168.100.31", 26660);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            while (true)
            {
                try
                {
                    float[] data;
                    bool success = uiQueue.TryDequeue(out data);

                    if (success)
                    {
                        AccWave aw = new AccWave("5600001715001", "016", data);
                        byte[] result = serializer.PackSingleObject(aw);
                        udpClient.Send(result, result.Length);

                        chart1.BeginInvoke(new MethodInvoker(() => {

                            for (int i = 0; i < 8; i++)
                            {
                                chart1.Series[i].Points.AddY(data[i]);

                                if (chart1.Series[i].Points.Count > numberOfPointsInChart)
                                {
                                    chart1.Series[i].Points.RemoveAt(0);
                                }
                            }

                            // Adjust Y & X axis scale
                            chart1.ResetAutoValues();

                            // Invalidate chart
                            chart1.Invalidate();

                        }));
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                    
                    if (bgWorker.CancellationPending == true)
                    {
                        e.Cancel = true;
                        udpClient.Close();
                        break;
                    }
                }
                catch (Exception ex)
                {
                    //Console.WriteLine(ex.ToString());
                    if (bgWorker.CancellationPending == true)
                    {
                        e.Cancel = true;
                        udpClient.Close();
                        break;
                    }
                }
            }
        }

        private float ExtractChannel(byte higher,byte lower)
        {
            int data = (higher & 0x7f) * 256 + lower;

            if ((higher & 0x80) == 0x80)
            {
                data = -data;
            }

            float channel = (float)(data / 10000.0);
            return channel;
        }

        private void BackgroundWorkerReceiveData_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bgWorker = sender as BackgroundWorker;

            while (true)
            {
                try
                {
                    //IPEndPoint object will allow us to read datagrams sent from any source.
                    IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);

                    // Blocks until a message returns on this socket from a remote host.
                    Byte[] receiveBytes = udpClient.Receive(ref RemoteIpEndPoint);

                    float[] data = new float[8];

                    for(int i = 0; i < 8; i++)
                    {
                        data[i] = ExtractChannel(receiveBytes[i*2 + 4], receiveBytes[i*2 + 5]);
                    }

                    dataQueue.Enqueue(data);
                    uiQueue.Enqueue(data);

                    if (bgWorker.CancellationPending == true)
                    {
                        e.Cancel = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    //Console.WriteLine(ex.ToString());ss
                    if (bgWorker.CancellationPending == true)
                    {
                        e.Cancel = true;
                        break;
                    }
                }

                Thread.Sleep(10);
            }
        }

        private void BackgroundWorkerProcessData_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bgWorker = sender as BackgroundWorker;

            while (true)
            {
                try {
                    int dataCount = dataQueue.Count;

                    if (dataCount > WindowSize)
                    {

                        float[,] channels = new float[WindowSize,8];

                        for (int i = 0; i < WindowSize; i++)
                        {
                            float[] line;

                            bool success = dataQueue.TryDequeue(out line);
                            if (success)
                            {
                                for (int j = 0; j < 8; j++)
                                {
                                    channels[i, j] = line[j];
                                }
                            }
                        }
                        ProcessFrame(channels);

                        //ProcessSingleFrame(channels);
                    }
                } catch (Exception ex) {
                    using (StreamWriter sw = new StreamWriter(@"ErrLog.txt", true))
                    {
                        sw.WriteLine(ex.Message+" \r\n" + ex.StackTrace.ToString());
                        sw.WriteLine("---------------------------------------------------------");
                        sw.Close();
                    }
                }

                

                if (bgWorker.CancellationPending == true)
                {
                    e.Cancel = true;
                    break;
                }

                Thread.Sleep(3000);
            }
        }

        /// <summary>
        ///   This method will be called whenever there is a new audio
        ///   frame to be processed.
        /// </summary>
        /// 
        void ProcessSingleFrameTest(float[,] channels)
        {
            // We can start by converting the audio frame to a complex signal
            //ComplexSignal signal = ComplexSignal.FromSignal(eventArgs.Signal);
            //Signal realSignal = Signal.FromArray(channels, WindowSize, 8, 50, SampleFormat.Format32BitIeeeFloat);
            //ComplexSignal signal = ComplexSignal.FromSignal(realSignal);
            
            ComplexSignal signal = ComplexSignal.FromArray(channels, 50);

            // If its needed,
            if (window != null)
            {
                // Apply the chosen audio window
                signal = window.Apply(signal, 0);
            }

            // Transform to the complex domain
            signal.ForwardFourierTransform();

            // Now we can get the power spectrum output and its
            // related frequency vector to plot our spectrometer.

            double[] freqv = Tools.GetFrequencyVector(signal.Length, signal.SampleRate);

            //double[][] power = new double[8][];

            Complex[] channel0 = signal.GetChannel(0);
            Complex[] channel1 = signal.GetChannel(1);
            Complex[] channel2 = signal.GetChannel(2);

            double[] g0 = Tools.GetPowerSpectrum(channel0);
            double[] g1 = Tools.GetPowerSpectrum(channel1);
            double[] g2 = Tools.GetPowerSpectrum(channel2);

            //for(int i = 0; i < 8; i++)
            //{
            //    Complex[] channel = signal.GetChannel(i);
            //    power[i] = Tools.GetPowerSpectrum(channel);
            //    // zero DC
            //    power[i][0] = 0;
            //}

            if (chart1.InvokeRequired)
            {
                chart1.BeginInvoke(new MethodInvoker(() =>
                {
                    chart1.Series[4].Points.Clear();
                    chart1.Series[5].Points.Clear();
                    chart1.Series[6].Points.Clear();
                    for (int i = 0; i < g0.Length; i++)
                    {
                        chart1.Series[4].Points.AddXY(freqv[i], g0[i]);
                        chart1.Series[5].Points.AddXY(freqv[i], g1[i]);
                        chart1.Series[6].Points.AddXY(freqv[i], g2[i]);

                    }
                    chart1.Invalidate();
                }));
            }
            else
            {
                chart1.Series[1].Points.Clear();
                for (int i = 0; i < g0.Length; i++)
                {
                    chart1.Series[1].Points.AddXY(freqv[i], g0[i]);

                }
                chart1.Invalidate();
            }
        }

        /// <summary>
        ///   This method will be called whenever there is a new audio
        ///   frame to be processed.
        /// </summary>
        /// 
        void ProcessSingleFrame(float[,] channels)
        {
            //float[] data = new float[WindowSize];
            //// We can start by converting the audio frame to a complex signal
            //for(int i = 0; i < WindowSize; i++)
            //{
            //    data[i] = channels[1, i];
            //}
            //Signal realSignal = Signal.FromArray(data, 50, SampleFormat.Format32BitIeeeFloat);
            //ComplexSignal signal = ComplexSignal.FromSignal(realSignal);

            ComplexSignal signal = ComplexSignal.FromArray(channels,50);

            // If its needed,
            if (window != null)
            {
                // Apply the chosen audio window
                signal = window.Apply(signal, 0);
            }

            // Transform to the complex domain
            signal.ForwardFourierTransform();

            // Now we can get the power spectrum output and its
            // related frequency vector to plot our spectrometer.

            double[] freqv = Tools.GetFrequencyVector(signal.Length, signal.SampleRate);

            //double[][] power = new double[8][];

            Complex[] channel0 = signal.GetChannel(0);
            Complex[] channel1 = signal.GetChannel(1);
            Complex[] channel2 = signal.GetChannel(2);
            Complex[] channel3 = signal.GetChannel(3);

            double[] g0 = Tools.GetPowerSpectrum(channel0);
            double[] g1 = Tools.GetPowerSpectrum(channel1);
            double[] g2 = Tools.GetPowerSpectrum(channel2);
            double[] g3 = Tools.GetPowerSpectrum(channel3);

            g0[0] = 0;
            g1[0] = 0;
            g2[0] = 0;
            g3[0] = 0;

            //for(int i = 0; i < 8; i++)
            //{
            //    Complex[] channel = signal.GetChannel(i);
            //    power[i] = Tools.GetPowerSpectrum(channel);
            //    // zero DC
            //    power[i][0] = 0;
            //}

            if (chart1.InvokeRequired)
            {
                chart1.BeginInvoke(new MethodInvoker(() =>
                {
                    chart1.Series[4].Points.Clear();
                    chart1.Series[5].Points.Clear();
                    chart1.Series[6].Points.Clear();
                    chart1.Series[7].Points.Clear();
                    for (int i = 0; i < g0.Length; i++)
                    {
                        chart1.Series[4].Points.AddXY(freqv[i], g0[i]);
                        chart1.Series[5].Points.AddXY(freqv[i], g1[i]);
                        chart1.Series[6].Points.AddXY(freqv[i], g2[i]);
                        chart1.Series[7].Points.AddXY(freqv[i], g3[i]);
                    }
                    chart1.Invalidate();
                }));
            }
            else
            {
                chart1.Series[4].Points.Clear();
                for (int i = 0; i < g0.Length; i++)
                {
                    chart1.Series[4].Points.AddXY(freqv[i], g0[i]);

                }
                chart1.Invalidate();
            }
        }

        /// <summary>
        ///   This method will be called whenever there is a new audio
        ///   frame to be processed.
        /// </summary>
        /// 
        void ProcessFrame(float[,] channels)
        {
            // We can start by converting the audio frame to a complex signal

            //Signal realSignal = Signal.FromArray(channels,WindowSize,8, 50, SampleFormat.Format32BitIeeeFloat);
            //ComplexSignal signal = ComplexSignal.FromSignal(realSignal);
            ComplexSignal signal = ComplexSignal.FromArray(channels, 50);

            // If its needed,
            if (window != null)
            {
                // Apply the chosen audio window
                signal = window.Apply(signal, 0);
            }

            // Transform to the complex domain
            signal.ForwardFourierTransform();

            // Now we can get the power spectrum output and its
            // related frequency vector to plot our spectrometer.

            double[] freqv = Tools.GetFrequencyVector(signal.Length, signal.SampleRate);

            double[][] power = new double[8][];
            Complex[][] complexChannels = new Complex[8][];

            //Complex[] channel = signal.GetChannel(0);

            //double[] g = Tools.GetPowerSpectrum(channel);

            int[][] peaksIndex1 = new int[signal.Channels][];
            int[][] peaksIndex2 = new int[signal.Channels][];

            for (int i = 0; i < 8; i++)
            {
                //complexChannels[i] = signal.GetChannel(i);
                power[i] = Tools.GetPowerSpectrum(signal.GetChannel(i));
                // zero DC
                power[i][0] = 0;
                peaksIndex1[i] = power[i].FindPeaks();

                double[] peaks2 = new double[peaksIndex1[i].Length];
                for (int j = 0; j < peaksIndex1[i].Length; j++)
                {
                    peaks2[j] = power[i][peaksIndex1[i][j]];
                }
                int[] index = peaks2.FindPeaks();
                int[] rawIndex = new int[index.Length];
                for (int k = 0; k < index.Length; k++)
                {
                    rawIndex[k] = peaksIndex1[i][index[k]];
                }
                peaksIndex2[i] = rawIndex;
            }

            //for (int j = 0; j < 8; j++)
            //{
            //    chart1.Series[j + 16].Points.Clear();
            //    for (int i = 0; i < freqv.Length; i++)
            //    {
            //        chart1.Series[j + 16].Points.AddXY(freqv[i], power[j][i]);
            //    }
            //}
            for (int j = 0; j < 8; j++)
            {
                chart1.Series[j].Points.Clear();
                for (int i = 0; i < peaksIndex1[j].Length; i++)
                {
                    int frequencyIndex = peaksIndex1[j][i];
                    int powerIndex = peaksIndex1[j][i];
                    chart1.Series[j].Points.AddXY(freqv[frequencyIndex], power[j][powerIndex]);
                }

                chart1.Series[j + 16].Points.Clear();
                for (int i = 0; i < peaksIndex2[j].Length; i++)
                {
                    int frequencyIndex = peaksIndex2[j][i];
                    int powerIndex = peaksIndex2[j][i];
                    chart1.Series[j + 16].Points.AddXY(freqv[frequencyIndex], power[j][powerIndex]);
                }
            }
            chart1.Invalidate();
        }

        /// <summary>
        /// 写记录
        /// </summary>
        /// <param name="str"></param>
        private void AppendRecord(string str)
        {
            //if (!Directory.Exists("ErrLog"))
            //{
            //    Directory.CreateDirectory("ErrLog");
            //}
            string currentDate = DateTime.Now.ToString("yyyy-MM-dd") + ".txt";

            string pathString = Path.Combine(@"D:\vibrate", currentDate);

            using (StreamWriter sw = new StreamWriter(pathString, true))
            {
                sw.WriteLine(str);
                sw.Close();
            }
        }

        public void Start()
        {
            buttonStart.Enabled = false;
            buttonStop.Enabled = true;

            foreach(VibrationACT12x va in deviceList.Values)
            {
                va.Start();
            }
        }

        public void Stop()
        {
            buttonStart.Enabled = true;
            buttonStop.Enabled = false;
            foreach (VibrationACT12x va in deviceList.Values)
            {
                va.Stop();
            }
        }

        private void buttonStart_Click(object sender, EventArgs e)
        {
            Start();
            //AppendRecord("hello world");
            //return;
            //string str = DateTime.Now.ToShortDateString();
            //MessageBox.Show(str);
            //return;
            //AccWave aw = new AccWave("123", "acc", new double[] { 12.1, 12.5, 44.6 });
            //var serializer = MessagePackSerializer.Get<AccWave>();
            //byte[] result = serializer.PackSingleObject(aw);
            ////serializer.Pack(stream, aw);
            ////stream.Position = 0;
            //var value = serializer.UnpackSingleObject(result);

            //MessageBox.Show(result.Length+"\r\n"+value.id + " " + value.type + " " + value.data[0].ToString());
            //vact = new VibrationACT12x("1",int.Parse(textBoxPort.Text), chart1, "ACT1228",@"E:\vibrate",this.database,this.textBoxLog);
            //vact.Start();
            //buttonStart.Enabled = false;
            //buttonStop.Enabled = true;
            //return;

            //int port;

            //bool success = Int32.TryParse(textBoxPort.Text, out port);

            //if (!success)
            //{
            //    MessageBox.Show("端口错误");
            //    return;
            //}

            //buttonStart.Enabled = false;
            //buttonStop.Enabled = true;

            //udpClient = new UdpClient(port);
            //backgroundWorkerUpdateUI.RunWorkerAsync();
            //backgroundWorkerProcessData.RunWorkerAsync();
            //backgroundWorkerReceiveData.RunWorkerAsync();
        }

        private void buttonStop_Click(object sender, EventArgs e)
        {
            Stop();
            //vact.Stop();
            //buttonStart.Enabled = true;
            //buttonStop.Enabled = false;
            //return;
            //try
            //{
            //    backgroundWorkerUpdateUI.CancelAsync();
            //    backgroundWorkerProcessData.CancelAsync();
            //    backgroundWorkerReceiveData.CancelAsync();
            //    udpClient.Close();
            //}
            //catch(Exception ex)
            //{
            //    MessageBox.Show(ex.Message);
            //}
            
        }

        private void checkedListBoxChannel_SelectedIndexChanged(object sender, EventArgs e)
        {
            int index = checkedListBoxChannel.SelectedIndex;
            if(chart1.Series[index] !=null && chart1.Series[index + 16] != null)
            {
                bool selected = checkedListBoxChannel.GetItemChecked(index);
                chart1.Series[index].Enabled = selected;
                chart1.Series[index + 16].Enabled = selected;
            }
            //MessageBox.Show("Index: "+index+" "+checkedListBoxChannel.SelectedItem.ToString()+ " : " + checkedListBoxChannel.GetItemChecked(index).ToString());
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 注意判断关闭事件reason来源于窗体按钮，否则用菜单退出时无法退出!
            if (e.CloseReason == CloseReason.UserClosing)
            {
                //取消"关闭窗口"事件
                e.Cancel = true; // 取消关闭窗体 

                //使关闭时窗口向右下角缩小的效果
                this.WindowState = FormWindowState.Minimized;
                this.notifyIcon1.Visible = true;
                this.Hide();
                return;
            }
        }



        private void RestoreToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Visible = true;
            this.WindowState = FormWindowState.Normal;
            this.notifyIcon1.Visible = true;
            this.Show();
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定要退出？", "系统提示", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
            {
                this.notifyIcon1.Visible = false;
                this.Close();
                this.Dispose();
            }
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (this.Visible)
            {
                this.WindowState = FormWindowState.Minimized;
                this.notifyIcon1.Visible = true;
                this.Hide();
            }
            else
            {
                this.Visible = true;
                this.WindowState = FormWindowState.Normal;
                this.Activate();
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            UdpClient udpClient = null;
            IPAddress remoteIp = IPAddress.Parse("112.112.16.144");
            try
            {
                udpClient = new UdpClient();
                udpClient.Connect(remoteIp, 26668);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            MessagePackSerializer serializer = MessagePackSerializer.Get<AccWave>();

            AccWave aw = new AccWave("123", "acc", new float[] { 12.1F, 12.5F, 44.6F });
            byte[] result = serializer.PackSingleObject(aw);
            udpClient.Send(result, result.Length);

            MessageBox.Show("Finished");

        }

        private void buttonTest_Click(object sender, EventArgs e)
        {

            StreamReader sr = new StreamReader("2raw.csv", Encoding.UTF8);
            String line;

            float[,] channels = new float[WindowSize, 8];

            int index = 0;
            char[] chs = { ',' };
            while ((line = sr.ReadLine()) != null)
            {
                string[] items = line.Split(chs);

                for (int j = 0; j < 8; j++)
                {
                    channels[index, j] = float.Parse(items[j]);
                }
                index++;
            }
            sr.Close();

            ProcessFrame(channels);
        }
    }
}
