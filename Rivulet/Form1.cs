using System;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using System.Threading;
using NAudio.Wave;
using System.Timers;

namespace Rivulet
{
    public partial class MainWindow : Form
    {

        Thread udpWorker;
        IPAddress mIP;

        bool listen = false;
        int lastPacketID = -2;

        bool playing = false;
        BufferedWaveProvider soundBuffer;
        WaveOut sound;

        int packetCount = 0;
        int dropCount = 0;

        System.Timers.Timer countDisplayTimer;
        

        public MainWindow()
        {
            InitializeComponent();

            setupAudio();
            setupStream();

            // Set up display timer
            countDisplayTimer = new System.Timers.Timer(100);
            countDisplayTimer.Elapsed += new ElapsedEventHandler(displayCounts);
            countDisplayTimer.Start();

            this.FormClosed += new FormClosedEventHandler(cleanExit);

        }

        private void cleanExit(object sender, FormClosedEventArgs e)
        {
            //Console.WriteLine("Exiting!");
            packupStream();
            Environment.Exit(0);
        }

        private void displayCounts(object sender, ElapsedEventArgs args)
        {
            if(!listen) { return; }
            SafeSet(packetCount.ToString(), packets);
            SafeSet(dropCount.ToString(), dropped);
        }

        private void setupAudio()
        {
            soundBuffer = new BufferedWaveProvider(new WaveFormat(48000, 16, 2));
            sound = new WaveOut();
            sound.Init(soundBuffer);
        }

        private void stream_TextChanged(object sender, EventArgs e)
        {
            setupStream();
        }

        private void setupStream()
        {

            packupStream();

            // Be patient with users that haven't typed anything yet
            if(stream.Text.Length < 1)
            {
                stream.BackColor = Color.Yellow;
                return;
            }

            int streamNo;

            // Check that user has entered a number
            if(!Int32.TryParse(stream.Text, out streamNo))
            {
                stream.BackColor = Color.Red;
                return;
            }

            // Check that number is in range
            if(streamNo < 1 || streamNo > 32766)
            {
                stream.BackColor = Color.Red;
                return;
            }

            // All good
            stream.BackColor = Color.White;

            // Convert number to multicast address
            String mAddress = streamToAddress(streamNo);
            address.Text = mAddress + ":50003";
            mIP = IPAddress.Parse(mAddress);

            /*** Set up the stream ***/
            listen = true;
            udpWorker = new Thread(UdpWorker);
            udpWorker.Priority = ThreadPriority.Highest;
            udpWorker.Start();

        }

        private string streamToAddress(int streamNo)
        {
            if (streamNo < 256)
            {
                return "239.255.165." + streamNo.ToString();
            }

            if (streamNo > 9471 && streamNo < 9728)
            {
                return "239.255.128." + (streamNo % 256).ToString();
            }

            return "239.255." + (128 + streamNo / 256).ToString() + "." + (streamNo % 256).ToString();

        }

        delegate void StringArgReturningVoidDelegate(string text, Control c);

        private void TextSetter(string text, Control c)
        {
            c.Text = text;
        }

        private void SafeSet(string text, Control c)
        {
            this.Invoke(
                new StringArgReturningVoidDelegate(TextSetter),
                new object[] { text, c }
            );
        }

        private void processPacket(byte[] receivedBytes)
        {
            packetCount++;

            // Check header
            String header = System.Text.Encoding.Default.GetString(receivedBytes.Skip(2).Take(4).ToArray());
            if(!header.Equals("SVSI")) { return; }

            // Get stream ID
            int streamID = receivedBytes[6] + (receivedBytes[10] * 256);
            SafeSet(streamID.ToString(), id);

            // Check sequence IDs
            if(lastPacketID != -2) {
                int diff = receivedBytes[9] - lastPacketID;
                if(diff != 1)
                {
                    dropCount++;
                    Console.WriteLine("glitch (" + diff.ToString() + "), " + receivedBytes[9] + " " + lastPacketID);
                }
            }
            if(receivedBytes[9] == 255)
            {
                lastPacketID = -1;
            } else
            {
                lastPacketID = receivedBytes[9];
            }

            // Load audio samples into buffer
            if (playing)
            {
                byte[] audio = receivedBytes.Skip(14).ToArray();
                
                // Fix endianness
                for(int i = 0; i < audio.Length; i += 2)
                {
                    byte t = audio[i];
                    audio[i] = audio[i + 1];
                    audio[i + 1] = t;
                }

                soundBuffer.AddSamples(audio, 0, audio.Length);
            }

        }

        private void UdpWorker()
        {
            UdpClient listener = new UdpClient();
            listener.Client.ReceiveBufferSize = 128 * 1024;
            listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.ExclusiveAddressUse = false;
            IPEndPoint ipep = new IPEndPoint(IPAddress.Any, 50003);
            listener.Client.Bind(ipep);
            listener.JoinMulticastGroup(mIP);
            while(listen)
            {
                byte[] receivedBytes = listener.Receive(ref ipep);
                processPacket(receivedBytes);
            }
        }

        private void packupStream()
        {
            listen = false;
            packets.Text = "";
            dropped.Text = "";
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if(!playing)
            {
                soundBuffer.ClearBuffer();
                Thread.Sleep(250); // Little bit of buffer time (?)
                sound.Play();
            } else
            {
                sound.Stop();
            }
            playing = !playing;
            button2.Text = playing ? "Stop" : "Play";
            button2.BackColor = playing ? Color.Red : Color.Lime;
        }

        private void MainWindow_Load(object sender, EventArgs e)
        {
            // nah
        }

    }
}
