using Accord.Audio;
using Accord.Audio.Windows;
using MsgPack.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using System.Data.SQLite;

namespace SpectrumChart
{
    public class AccWave
    {
        public string id;
        public string type;
        public float[] data;

        public AccWave(string id, string type, float[] data)
        {
            this.id = id;
            this.type = type;
            this.data = data;
        }
    }

    class VibrationACT12x
    {
        private string ip;
        private int udpPort;
        private ConcurrentQueue<float[]> dataQueue;
        private ConcurrentQueue<float[]> uiQueue;
        private BackgroundWorker backgroundWorkerProcessData;
        private BackgroundWorker backgroundWorkerUpdateUI;
        private BackgroundWorker backgroundWorkerReceiveData;
        private UdpClient udpClient;
        private UdpClient udpSendWave;
        private TextBox textBoxLog;
        private IWindow window;
        private const int WindowSize = 512;
        private Chart chart1;
        private string deviceType;
        private Dictionary<int, string> vibrateChannels;

        private string basePath;
        private string database;
        private string deviceId;

        private bool isUpdateChart;

        public VibrationACT12x(string id,string ip,int port,Chart chart,string deviceType,string path,string database,TextBox tb)
        {
            dataQueue = new ConcurrentQueue<float[]>();
            uiQueue = new ConcurrentQueue<float[]>();
            backgroundWorkerProcessData = new BackgroundWorker();
            backgroundWorkerReceiveData = new BackgroundWorker();
            backgroundWorkerUpdateUI = new BackgroundWorker();
            backgroundWorkerProcessData.WorkerSupportsCancellation = true;
            backgroundWorkerProcessData.DoWork += BackgroundWorkerProcessData_DoWork;

            backgroundWorkerUpdateUI.WorkerSupportsCancellation = true;
            backgroundWorkerUpdateUI.DoWork += BackgroundWorkerUpdateUI_DoWork;

            backgroundWorkerReceiveData.WorkerSupportsCancellation = true;
            backgroundWorkerReceiveData.DoWork += BackgroundWorkerReceiveData_DoWork;

            window = RaisedCosineWindow.Hann(WindowSize);

            this.ip = ip;
            this.deviceId = id;
            this.deviceType = deviceType;
            this.udpPort = port;
            this.chart1 = chart;
            this.textBoxLog = tb;
            this.basePath = path;
            this.database = database;
            this.isUpdateChart = false;
            this.vibrateChannels = new Dictionary<int, string>();

            LoadChannels();
        }

        private void InitializeChannels()
        {
            
            for(int i = 0; i < 8; i++)
            {
                this.vibrateChannels[i+1] = "560000171500"+(i+1);
            }
        }

        private void LoadChannels()
        {
            using (SQLiteConnection connection = new SQLiteConnection(this.database))
            {
                connection.Open();
                string strainStatement =  "select SensorId,ChannelNo from Channels where GroupNo ='" + this.deviceId + "'";
                SQLiteCommand command = new SQLiteCommand(strainStatement, connection);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        //string  groupId = reader.GetString(0);
                        string sensorId = reader.GetString(0);
                        int channelNo = reader.GetInt32(1);

                        vibrateChannels.Add(channelNo, sensorId);
                    }
                }
            }
        }

        public void Start()
        {
            udpClient = new UdpClient(udpPort);
            udpSendWave = new UdpClient("182.245.124.106", 9002);
            backgroundWorkerUpdateUI.RunWorkerAsync();
            backgroundWorkerProcessData.RunWorkerAsync();
            backgroundWorkerReceiveData.RunWorkerAsync();
        }

        public void Stop()
        {
            backgroundWorkerUpdateUI.CancelAsync();
            backgroundWorkerProcessData.CancelAsync();
            backgroundWorkerReceiveData.CancelAsync();
            udpClient.Close();
            udpSendWave.Close();
        }

        private void BackgroundWorkerUpdateUI_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bgWorker = sender as BackgroundWorker;

            

            MessagePackSerializer serializer = MessagePackSerializer.Get<AccWave>();

            //UdpClient udpClient = null;
            //IPAddress remoteIp = IPAddress.Parse("182.245.124.106");
            //try
            //{
            //    udpClient = new UdpClient();
            //    udpClient.Connect(remoteIp, 9002);
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine(ex.ToString());
            //    //MessageBox.Show(ex.ToString());
            //}

            if (textBoxLog.InvokeRequired)
            {
                textBoxLog.BeginInvoke(new MethodInvoker(() =>
                {
                    textBoxLog.AppendText("Start UI thread dequeue\r\n");
                }));
            }
            else
            {
                textBoxLog.AppendText("Start UI thread dequeue\r\n");
            }

            while (true)
            {
                try
                {
                    float[] data;
                    bool success = uiQueue.TryDequeue(out data);

                    if (success)
                    {
                        //AccWave aw = new AccWave("5600001715001", "016", data);
                        ////AccWave aw = new AccWave("123", "acc", new float[] { 12.1F, 12.5F, 44.6F });
                        //byte[] result = serializer.PackSingleObject(aw);
                        //udpClient.Send(result, result.Length);

                        //for (int i = 0; i < data.Length; i++)
                        //{
                        //    if (vibrateChannels.ContainsKey(i))
                        //    {

                        //        float[] arr = { data[i] };
                        //        AccWave awObject = new AccWave(vibrateChannels[i], "016", arr);
                        //        byte[] result = serializer.PackSingleObject(awObject);
                        //        udpSendWave.Send(result, result.Length);
                                
                        //    }
                        //}

                        UpdateAmplitudeChart(data);
                    }
                    else
                    {
                        Thread.Sleep(10);
                        //if (textBoxLog.InvokeRequired)
                        //{
                        //    textBoxLog.BeginInvoke(new MethodInvoker(() =>
                        //    {
                        //        textBoxLog.AppendText("UI thread dequeue failed \r\n");
                        //    }));
                        //}
                        //else
                        //{
                        //    textBoxLog.AppendText("UI thread dequeue failed \r\n");
                        //}
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
                    MessageBox.Show(ex.ToString());
                    if (bgWorker.CancellationPending == true)
                    {
                        udpClient.Close();
                        e.Cancel = true;
                        break;
                    }
                }
            }
        }

        private void UpdateAmplitudeChart(float[] data)
        {
            int numberOfPointsInChart = 200;
            if (isUpdateChart)
            {
                if (chart1.InvokeRequired)
                {
                    chart1.BeginInvoke(new MethodInvoker(() =>
                    {
                        for (int i = 0; i < data.Length; i++)
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
                    for (int i = 0; i < data.Length; i++)
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
                }
            }
        }

        private float ACT1228ExtractChannel(byte higher, byte lower)
        {
            int data = (higher & 0x7f) * 256 + lower;

            if ((higher & 0x80) == 0x80)
            {
                data = -data;
            }

            float channel = (float)(data / 10000.0);
            return channel;
        }

        private void AppendLog(string message)
        {
            if (textBoxLog.InvokeRequired)
            {
                textBoxLog.BeginInvoke(new MethodInvoker(() =>
                {
                    textBoxLog.AppendText(message + " \r\n");
                }));
            }
            else
            {
                textBoxLog.AppendText(message + " \r\n");
            }
        }

        private float ACT12816ExtractChannel(byte higher, byte lower)
        {
            int data = (higher & 0x7f) * 256 + lower;

            if ((higher & 0x80) == 0x80)
            {
                data = -data;
            }

            float channel = (float)(data / 1000.0);
            return channel;
        }

        private void BackgroundWorkerReceiveData_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bgWorker = sender as BackgroundWorker;

            //IPAddress ip = IPAddress.Parse();
            udpClient.Connect(IPAddress.Parse(ip), udpPort);

            byte[] start = { 0x00, 0x10, 0x00, 0xc8, 0x00, 0x01, 0x01, 0x01 };
            this.udpClient.Send(start, start.Length);

            while (true)
            {
                try
                {
                    //IPEndPoint object will allow us to read datagrams sent from any source.
                    IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);

                    //udpClient.Connect()

                    // Blocks until a message returns on this socket from a remote host.
                    Byte[] receiveBytes = udpClient.Receive(ref RemoteIpEndPoint);

                    float[] data;

                    if (this.deviceType == "ACT1228")
                    {
                        data = new float[8];
                        for (int i = 0; i < 8; i++)
                        {
                            data[i] = ACT1228ExtractChannel(receiveBytes[i * 2 + 4], receiveBytes[i * 2 + 5]);
                        }
                    }
                    else
                    {//ACT12816
                        data = new float[16];
                        for (int i = 0; i < 16; i++)
                        {
                            data[i] = ACT12816ExtractChannel(receiveBytes[i * 2 + 3], receiveBytes[i * 2 + 4]);
                        }
                    }

                    //string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                    //for (int i = 0; i < data.Length; i++)
                    //{
                        
                    //}

                    //float[] data = new float[8];

                    //for (int i = 0; i < 8; i++)
                    //{
                    //    data[i] = ExtractChannel(receiveBytes[i * 2 + 4], receiveBytes[i * 2 + 5]);
                    //}

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

            uint count = 0;

            while (true)
            {
                try
                {
                    int dataCount = dataQueue.Count;

                    if (dataCount > WindowSize)
                    {
                        /* funny
                        float[,] channels = new float[8, WindowSize];

                        for (int i = 0; i < WindowSize; i++)
                        {
                            float[] line;

                            bool success = dataQueue.TryDequeue(out line);
                            if (success)
                            {
                                for (int j = 0; j < 8; j++)
                                {
                                    channels[j, i] = line[j];
                                }
                            }
                        }
                        */
                        
                        int numberOfChannels = 0;
                        if (this.deviceType == "ACT1228")
                        {
                            numberOfChannels = 8;
                        }
                        else
                        {
                            numberOfChannels = 16;
                        }

                        float[,] channels = new float[WindowSize, numberOfChannels];

                        //StringBuilder sb = new StringBuilder(1024*16);
                        for (int i = 0; i < WindowSize; i++)
                        {
                            float[] line;

                            bool success = dataQueue.TryDequeue(out line);
                            if (success)
                            {
                                for (int j = 0; j < numberOfChannels; j++)
                                {
                                    channels[i, j] = line[j];
                                    //sb.Append(line[j] + ",");
                                }
                               // sb.Remove(sb.Length - 1, 1);
                               // sb.Append("\r\n");
                            }
                        }
                        //AppendRecord(sb, count + "raw");
                        count++;
                        ProcessFrame(channels);
                    }
                }
                catch (Exception ex)
                {
                    using (StreamWriter sw = new StreamWriter(@"ErrLog.txt", true))
                    {
                        sw.WriteLine(ex.Message + " \r\n" + ex.StackTrace.ToString());
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

            double[][] power = new double[signal.Channels][];
            FrequencyPoint[][] fps = new FrequencyPoint[8][];

            int[][] peaksIndex1 = new int[signal.Channels][];
            int[][] peaksIndex2 = new int[signal.Channels][];

            for (int i = 0; i < signal.Channels; i++)
            {
                //complexChannels[i] = signal.GetChannel(i);
                power[i] = Tools.GetPowerSpectrum(signal.GetChannel(i));
                // zero DC
                power[i][0] = 0;
                //fps[i] = GetFrequencyPoints(power[i],freqv);

                peaksIndex1[i] = power[i].FindPeaks();

                double[] peaks2 = new double[peaksIndex1[i].Length];
                for(int j=0; j < peaksIndex1[i].Length; j++)
                {
                    peaks2[j] = power[i][peaksIndex1[i][j]];
                }
                int[] index = peaks2.FindPeaks();
                int[] rawIndex = new int[index.Length];
                for(int k = 0; k < index.Length; k++)
                {
                    rawIndex[k] = peaksIndex1[i][index[k]];
                }
                peaksIndex2[i] = rawIndex;
            }

            //int[] peaksIndex = power[0].FindPeaks();

            //string content="";
            //foreach(int index in peaksIndex)
            //{
            //    content += (freqv[index] + " ");
            //}
            //content += "\r\n";
            //AppendLog(content);

            
            if (isUpdateChart)
            {
                if (chart1.InvokeRequired)
                {
                    chart1.BeginInvoke(new MethodInvoker(() =>
                    {
                        for (int j = 0; j < signal.Channels; j++)
                        {
                            chart1.Series[j + 16].Points.Clear();
                            //for (int i = 0; i < freqv.Length; i++)
                            for (int i = 0; i < peaksIndex2[j].Length; i++)
                            {

                                //chart1.Series[j + 16].Points.AddXY(freqv[i], power[j][i]);
                                chart1.Series[j + 16].Points.AddXY(freqv[peaksIndex2[j][i]], power[j][peaksIndex2[j][i]]);
                                //chart1.Series[j + 16].Points.AddXY(freqv[peaksIndex1[j][i]], power[j][peaksIndex1[j][i]]);
                                //chart1.Series[j + 16].Points[i].ToolTip = freqv[i].ToString();
                            }
                        }
                        chart1.Invalidate();
                    }));
                }
                else
                {
                    for (int j = 0; j < signal.Channels; j++)
                    {
                        chart1.Series[j + 16].Points.Clear();
                        for (int i = 0; i < freqv.Length; i++)
                        {
                            chart1.Series[j + 16].Points.AddXY(freqv[i], power[j][i]);
                            chart1.Series[j + 16].Points[i].ToolTip = freqv[i].ToString();
                        }
                    }
                    chart1.Invalidate();
                }
            }
            

            //保存频谱
            /*
            StringBuilder sb = new StringBuilder(2048);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            //sb.AppendLine(stamp + ",");
            for(int i=0;i< signal.Channels; i++)
            {
                if (vibrateChannels.ContainsKey(i + 1))
                {
                    sb.Append(vibrateChannels[i + 1] + "," + stamp + ",");

                    for (int j = 0; j < freqv.Length; j++)
                    {
                        sb.Append(power[i][j] + ",");
                    }
                    sb.Remove(sb.Length - 1, 1);
                    sb.Append("\r\n");
                }         
            }

            //AppendLog(sb.ToString());
            AppendRecord(sb);
            */
        }

        public void SetUpdateChart(bool state)
        {
            isUpdateChart = state;
            AppendLog(this.deviceId + " State:" + isUpdateChart);
        }

        /// <summary>
        /// 写记录
        /// </summary>
        /// <param name="str"></param>
        private void AppendRecord(StringBuilder sb,string fileName)
        {
            //if (!Directory.Exists("ErrLog"))
            //{
            //    Directory.CreateDirectory("ErrLog");
            //}
            //string currentDate = DateTime.Now.ToString("yyyy-MM-dd") + ".txt";

            string currentDate = fileName+".csv";

            string pathString = Path.Combine(this.basePath, currentDate);

            using (StreamWriter sw = new StreamWriter(pathString, true))
            {
                //StringBuilder sb = new StringBuilder(20);
                sw.Write(sb);
                //sw.WriteLine(sb);
                sw.Close();
            }
        }

        private FrequencyPoint[] GetFrequencyPoints(double[] power,double[] freqv)
        {
            FrequencyPoint[] fps = new FrequencyPoint[4];

            double[] powerCpy = (double[])power.Clone();
            double[] freqCpy = (double[])freqv.Clone();

            Array.Sort(powerCpy, freqCpy);

            fps[0] = new FrequencyPoint(freqCpy[freqCpy.Length - 1], powerCpy[freqCpy.Length - 1]);
            fps[1] = new FrequencyPoint(freqCpy[freqCpy.Length - 2], powerCpy[freqCpy.Length - 2]);
            fps[2] = new FrequencyPoint(freqCpy[freqCpy.Length - 3], powerCpy[freqCpy.Length - 3]);
            fps[3] = new FrequencyPoint(freqCpy[freqCpy.Length - 4], powerCpy[freqCpy.Length - 4]);

            return fps;
        }

        class FrequencyPoint
        {
            public double frequency;
            public double amplitude;
            public FrequencyPoint(double fre,double amplitude)
            {
                this.frequency = fre;
                this.amplitude = amplitude;
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
    }
}
