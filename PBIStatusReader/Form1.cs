using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace PBIStatusReader
{
    public partial class Form1 : Form
    {
        int n = 10; // число ресиверов
        settings ap;
        public System.Windows.Forms.Timer timer1;
        public System.Timers.Timer timermidnight;
        public bool writing; // флаг-индикатор записи
        public bool writingset; // флаг-индикатор записи смены входа
        Label[] recvnamelabels;
        PictureBox[,] pictureBoxes = new PictureBox[10,5];
        //static HttpWebRequest webRequest;
        static object locker = new object();
        public Label[,] dynamicparamls;

        public bool isSettingsForm; // форма настроек создана и активна

        public string[] oldparams = new string[10]; // буферы для хранения задаваемых настроек
        public string[] oldregexps = new string[10]; 
        
        public Form1()
        {
            ap = new settings(this); // инициализация настроек приложения
            InitializeComponent();
            bool ifread = ap.ReadSettings(); // считываем записанные ранее настройки
            isSettingsForm = false;
            writing = false;
            writingset = false;
            timer1 = new System.Windows.Forms.Timer();
            timermidnight = new System.Timers.Timer();
            SetTimerMidnight();

            if (!ifread)
            {
                // настройки из файла не загрузились - ставим дефолтные
                ap.SetGlobalDefaultSettings();
                ap.setdefaults(ap.ap.receiverecs[0]);
            }
            else
            {
                for (int i = 0; i < ap.ap.n; i++)
                {
                    ap.ap.receiverecs[i].parameters.RemoveAll(item => item == null); // при десериализации в поле параметров появляются лишние null, этот код их убирает
                    ap.ap.receiverecs[i].regexps.RemoveAll(item => item == null);
                }
            }
            n = ap.ap.n;
                
            // рисуем форму
            DrawMainForm();
        }

        public void MidNightScan()
        {
            timer1.Stop();
            writing = true;
            writingset = true;
            List<Task> tasklist = new List<Task>();
            for (int i = 0; i < ap.ap.n; i++)
            {
                int tempI = i;
                tasklist.Add(Task.Factory.StartNew(() => setValues(tempI)));
            }
            Task.WaitAll(tasklist.ToArray());
            writing = false;
            timer1.Start();
        }

        public void SetTimerMidnight()
        {
            DateTime nowTime = DateTime.Now;
            DateTime time = new DateTime(nowTime.Year, nowTime.Month, nowTime.Day, 0, 0, 0, 0);
            if (nowTime > time)
                time = time.AddDays(1);
            var span = time - DateTime.Now;
            timermidnight.Interval = span.TotalMilliseconds;
            timermidnight.AutoReset = false;
            
            timermidnight.Elapsed += (sender, args) => {
                MidNightScan();
                SetTimerMidnight();
            };
            timermidnight.Start();
        }

        public void InitializeOldArray(int n, string[] oldstrs, string newstrs)
        {
            for (int i = 0; i < n; i++)
            {
                oldstrs[i] = newstrs;
            }
        }

        public void ClearAllMainControls()
        {
            this.Controls.Clear();
            this.InitializeComponent();
        }

        public bool PictboxExists(int i, int j)
        {
            if (i < pictureBoxes.GetLength(0))
                if (j < pictureBoxes.GetLength(1))
                    return true;
            return false;
        }

        public void DrawMainForm()
        {
            recvnamelabels = new Label[10];
            this.Size = new Size((int)ap.ap.formwidth, (int)ap.ap.formheight);

            for (int i = 0; i < ap.ap.n; i++)
            {
                recvnamelabels[i] = new Label();
                recvnamelabels[i].Size = new Size(180, 24);
                recvnamelabels[i].Font = new Font("Microsoft Sans Serif", 12);
                recvnamelabels[i].Text = ap.ap.receiverecs[i].name;
                recvnamelabels[i].Size = TextRenderer.MeasureText(recvnamelabels[i].Text, recvnamelabels[i].Font);
                if (recvnamelabels[i].Text == "")
                {
                    //hide the line
                    recvnamelabels[i].Hide();
                }
            }

            // fit form according to the labels
            int maxwidth = 0;
            for (int i = 0; i < ap.ap.n; i++)
            {
                if (recvnamelabels[i].Width > maxwidth)
                    maxwidth = recvnamelabels[i].Width;
            }

            dynamicparamls = new Label[10, 5];
            for (int j = 0; j < 5; j++)
            {
                dynamicparamls[0, j] = new Label();
                dynamicparamls[0, j].Font = new Font("Microsoft Sans Serif", 12);
                dynamicparamls[0, j].TextAlign = ContentAlignment.TopLeft;
                dynamicparamls[0, j].Width = 60;
            }

            dynamicparamls[0, 0].Location = new Point(maxwidth + 50, 9);
            dynamicparamls[0, 1].Location = new Point(maxwidth + 50 + 66, 9);
            dynamicparamls[0, 2].Location = new Point(maxwidth + 50 + 66 + 79, 9);
            dynamicparamls[0, 3].Location = new Point(maxwidth + 50 + 66 + 79 + 82, 9);
            dynamicparamls[0, 4].Location = new Point(maxwidth + 50 + 66 + 79 + 82 + 88, 9);

            for (int j = 0; j < ap.ap.receiverecs[0].m; j++)
            {
                dynamicparamls[0, j].Text = ap.ap.receiverecs[0].parameters[j];
                this.Controls.Add(dynamicparamls[0, j]);
            }

            int tempstep = 80;

            for (int i = 0; i < ap.ap.n; i++)
            {
                // выстраиваем лейблы для названий ресиверов
                if (i == 0)
                {
                    recvnamelabels[i].Location = new Point(12, 50);
                }
                else
                {
                    recvnamelabels[i].Location = new Point(12, recvnamelabels[i - 1].Location.Y + tempstep);
                }
                this.Controls.Add(recvnamelabels[i]);



                // выстраиваем матрицу лампочек-индикаторов

                int firstX = dynamicparamls[0, 0].Location.X - 10;
                int firstY = recvnamelabels[i].Location.Y - 10;
                for (int j = 0; j < ap.ap.receiverecs[i].m; j++)
                {
                    pictureBoxes[i, j] = new PictureBox();
                    pictureBoxes[i, j].Location = new Point(firstX, firstY);
                    pictureBoxes[i, j].Size = new Size(38, 35);
                    pictureBoxes[i, j].SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
                    if (ap.ap.receiverecs[i].parameters[j] != "")
                        this.Controls.Add(pictureBoxes[i, j]);
                    setColorOfPictureBox(pictureBoxes[i, j], 2);

                    if (i > 0)
                    {
                        // if we have the next row
                        if (n > i)
                        {
                            dynamicparamls[i, j] = new Label();
                            dynamicparamls[i, j].Location = new Point(dynamicparamls[0, j].Location.X, dynamicparamls[0, j].Location.Y + 80 * i);
                            dynamicparamls[i, j].Font = new Font("Microsoft Sans Serif", 12);
                            dynamicparamls[i, j].Width = 60;
                            dynamicparamls[i, j].Text = ap.ap.receiverecs[i].parameters[j];
                            this.Controls.Add(dynamicparamls[i, j]);
                        }
                    }

                    firstX += 80;
                }
            }

            timer1.Interval = 1000 * Convert.ToInt32(ap.ap.period);
            timer1.Tick += new EventHandler(timer1_Tick);
            timer1.Enabled = true;
            this.Text += " Загрузка данных...";

        }

        // полная перерисовка формы (для отображения изменения настроек на форме)
        public void RedrawForm()
        {
            ClearAllMainControls();
            DrawMainForm();
            this.Size = new Size((int)ap.ap.formwidth, (int)ap.ap.formheight);
        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        // получение данных со всех устройств
        void globalScan()
        {
            for (int i = 0; i < ap.ap.n; i++)
            {
                // проверяем, готова ли настройка этого ресивера
                if (i < recvnamelabels.Count(s => s != null))
                {
                    //MessageBox.Show("timer1_Tick " + i.ToString());
                    if (recvnamelabels[i].Visible && recvnamelabels[i].Text != "")
                    {
                        int tempI = i;
                        Task.Factory.StartNew(() => setValues(tempI));
                        //sets[i] = new Thread(setValues);
                        //sets[i].Name = i.ToString();
                        //sets[i].Start(i);
                        //sets[i].Join();
                    }
                }
            }
        }

        void timer1_Tick(object sender, EventArgs e)
        {
            timer1.Stop();
            globalScan();
            timer1.Start();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            System.Windows.Forms.Control.CheckForIllegalCrossThreadCalls = false;
        }

        private string geturlbyid(int id)
        {
            return ap.ap.receiverecs[id].url + "/cgi-bin/input_status.cgi";
        }

        private Label getlabelbyid(int id)
        {
            return recvnamelabels[id];
        }

        public delegate void ChangeLabel(Label obj, string text);
        public void Changelab(Label obj, string text)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new ChangeLabel(Changelab), obj, text);
            }
            else
            {
                obj.Text = text;
            }
        }


        // status: 0 - параметр в состоянии Off, 1 - параметр в состоянии On, 2 - нет связи (не удалось получить)
        void setColorOfPictureBox(PictureBox tmp, int status)
        {
            if (status == 1)
                tmp.Image = PBIStatusReader.Properties.Resources.aquaballgreen_9399;
            else if (status == 0)
                tmp.Image = PBIStatusReader.Properties.Resources.aquaballred_8531;
            else if (status == 2)
                tmp.Image = PBIStatusReader.Properties.Resources.circle_yellow_9383;
        }

        string intToStatus(int st)
        {
            if (st == 1)
                return "On";
            if (st == 0)
                return "Off";
            if (st == 2)
                return "Error";
            else
                return "Undefined";
        }

        // процедура записи лога состояний подключения
        void WriteToConnectionLog(int i, string message)
        {
            if (!ap.ap.writetofile)
                return;
            string curpath = ap.ap.mainlogpath;
            DateTime nw = DateTime.Now;

            string subpath = "";
            if (ap.ap.nestedpath)
                subpath = nw.ToString("yyyy") + "\\" + nw.ToString("MM") + "\\" + nw.ToString("dd");
            
            if (curpath.EndsWith("\\"))
                curpath = curpath + subpath;
            else
                curpath = curpath + "\\" + subpath;
            try
            {
                if (!Directory.Exists(curpath))
                {
                    Directory.CreateDirectory(curpath);
                }
                curpath = curpath + "\\" + ap.ap.receiverecs[i].name + "_" + nw.ToString("yyyy-MM-dd") + ".log";
                string tofile = nw.ToString("yyyy/MM/dd") + "\t" + nw.ToString("HH:mm:ss") + "\t" + ap.ap.receiverecs[i].name + "\tCONNECT\tMESSAGE\t" + ap.ap.receiverecs[i].url + "\t" + message;
                //string tofile = DateTime.Now.ToString("HH:mm:ss") + "\t" + ap.ap.receiverecs[i].name + "\t" + ap.ap.receiverecs[i].url + "\t" + message;
                using (var str = new StreamWriter(File.Open(curpath, FileMode.Append), Encoding.UTF8))
                {
                    str.WriteLine(tofile);
                }  
            }
            catch (System.IO.IOException e)
            {
                MessageBox.Show(e.Message);
            }
            catch (System.UnauthorizedAccessException e)
            {
                MessageBox.Show(e.Message);
            }

        }
        
        // процедура записи лога
        void WriteToLog(int i, string message)
        {
            if (!ap.ap.writetofile)
                return;
            // формируем путь
            string curpath = ap.ap.mainlogpath;
            DateTime nw = DateTime.Now;
            string subpath = "";
            if (ap.ap.nestedpath)
                subpath = nw.ToString("yyyy") + "\\" + nw.ToString("MM") + "\\" + nw.ToString("dd");
            if (curpath.EndsWith("\\"))
                curpath = curpath + subpath;
            else
                curpath = curpath + "\\" + subpath;
            try
            {
                if (!Directory.Exists(curpath))
                    Directory.CreateDirectory(curpath);
                curpath = curpath + "\\" + ap.ap.receiverecs[i].name + "_" + nw.ToString("yyyy-MM-dd") + ".log";
                string tofile = nw.ToString("yyyy/MM/dd") + "\t" + nw.ToString("HH:mm:ss") + "\t" + ap.ap.receiverecs[i].name + "\t" + message;

                using (var str = new StreamWriter(File.Open(curpath, FileMode.Append), Encoding.UTF8))
                {
                    str.WriteLine(tofile);
                }
            }
            catch (System.IO.IOException e)
            {
                MessageBox.Show(e.Message);
            }
            catch (System.UnauthorizedAccessException e)
            {
                MessageBox.Show(e.Message);
            }
        }

        void getSelectedInput(int i)
        {
            HttpWebResponse resp = null;
            HttpWebRequest webRequest;
            try
            {
                webRequest = (HttpWebRequest)WebRequest.Create(ap.ap.receiverecs[i].urlinput);
            }
            catch (System.UriFormatException)
            {
                return;
            }
            webRequest.Method = "GET";
            webRequest.Timeout = 5000; // 5 секунд таймаут
            webRequest.Headers.Clear();
            byte[] authData = System.Text.Encoding.UTF8.GetBytes(ap.ap.receiverecs[i].login + ":" + ap.ap.receiverecs[i].pass);
            string authHeader = "Authorization: Basic " + Convert.ToBase64String(authData) + "\r\n";
            webRequest.Headers.Add(authHeader);
            try
            {
                resp = (HttpWebResponse)webRequest.GetResponse();
                if (HttpStatusCode.OK == resp.StatusCode)
                {
                    Stream ReceiveStream = resp.GetResponseStream();
                    Encoding encode = System.Text.Encoding.GetEncoding("utf-8");
                    StreamReader readStream = new StreamReader(ReceiveStream, encode);
                    String str = "";
                    str = readStream.ReadToEnd();
                    string pattern = @"<option value=""\d""\sselected>(.+?)\s</option>";
                    Regex newReg = new Regex(pattern);
                    Match matches = newReg.Match(str);
                    if (matches.Groups[1].Success)
                    {
                        //try to make actual input bold...
                        string bolditem = matches.Groups[1].Value.Replace("Input", "");
                        int keypos = -1;
                        if (bolditem != "")
                        {
                            for (int j = 0; j < ap.ap.m; j++)
                            {
                                if (bolditem.IndexOf(ap.ap.receiverecs[i].parameters[j]) != -1)
                                {
                                    keypos = j;
                                    if ((ap.ap.receiverecs[i].lastactiveinput != ap.ap.receiverecs[i].parameters[j]) || (writingset))
                                    {
                                            // изменился
                                            string tolog = ap.ap.receiverecs[i].parameters[j] + "\tSET\t" + ap.ap.receiverecs[i].urlinput;
                                            WriteToLog(i, tolog);
                                            if (writingset)
                                                writingset = false;
                                    }
                                    ap.ap.receiverecs[i].lastactiveinput = ap.ap.receiverecs[i].parameters[j]; // помечаем активный вход как последний
                                    dynamicparamls[i, j].Font = new Font(dynamicparamls[i, j].Font, FontStyle.Bold | FontStyle.Underline);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch {
            }
        }

        void makeregulardynamiclabels(int i)
        {
            for (int j = 0; j < ap.ap.receiverecs[i].m; j++)
            {
                dynamicparamls[i, j].Font = new Font(dynamicparamls[i, j].Font, FontStyle.Regular);
            }
        }

        void setValues(object id)
        {
                TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
                int secondsSinceEpoch = (int)t.TotalSeconds;
                //Console.Write(secondsSinceEpoch);
                int idn = (int)id;
                makeregulardynamiclabels(idn);
                HttpWebResponse resp = null;
                HttpWebRequest webRequest;
                //webRequest = (HttpWebRequest)WebRequest.Create(geturlbyid(idn) + "/cgi-bin/input_status.cgi?cur_time=" + secondsSinceEpoch.ToString());
                try
                {
                    webRequest = (HttpWebRequest)WebRequest.Create(geturlbyid(idn));
                }
                catch (System.UriFormatException)
                {
                    for (int j = 0; j < ap.ap.receiverecs[idn].m; j++)
                    {
                        setColorOfPictureBox(pictureBoxes[idn, j], 2);
                    }

                    string tmplogmsg = "В настройках задан неверный адрес.";
                    if (ap.ap.receiverecs[idn].lastlogmsg[0] != tmplogmsg)
                    {
                        ap.ap.receiverecs[idn].lastlogmsg[0] = tmplogmsg;
                        ap.ap.receiverecs[idn].laststatus[0] = 2;
                        WriteToConnectionLog(idn, tmplogmsg);
                    }
                    return;
                }
                
                webRequest.Method = "GET";
                webRequest.Timeout = 5000; // 5 секунд таймаут
                webRequest.Headers.Clear();
                byte[] authData = System.Text.Encoding.UTF8.GetBytes(ap.ap.receiverecs[idn].login + ":" + ap.ap.receiverecs[idn].pass);
                string authHeader = "Authorization: Basic " + Convert.ToBase64String(authData) + "\r\n";
                webRequest.Headers.Add(authHeader);

                this.Text = "PBI Status Reader";
                try
                {
                    resp = (HttpWebResponse)webRequest.GetResponse();
                    if (HttpStatusCode.NotFound == resp.StatusCode)
                    {
                        // not found
                        for (int j = 0; j < ap.ap.receiverecs[idn].m; j++)
                        {
                            setColorOfPictureBox(pictureBoxes[idn, j], 2);
                        }

                        string tmplogmsg = "Адрес страницы не найден";
                        if (ap.ap.receiverecs[idn].lastlogmsg[0] != tmplogmsg)
                        {
                            ap.ap.receiverecs[idn].lastlogmsg[0] = tmplogmsg;
                            ap.ap.receiverecs[idn].laststatus[0] = 2;
                            WriteToConnectionLog(idn, tmplogmsg);
                        }
                        return;
                    }
                    else if (HttpStatusCode.OK == resp.StatusCode)
                    {
                        //Console.Write("Connection... OK");
                        Changelab(getlabelbyid(idn), ap.ap.receiverecs[idn].name);
                        Stream ReceiveStream = resp.GetResponseStream();
                        Encoding encode = System.Text.Encoding.GetEncoding("utf-8");
                        StreamReader readStream = new StreamReader(ReceiveStream, encode);
                        String str = "";
                        str = readStream.ReadToEnd();
                        // если страница пустая, но коннект состоялся, лампочки будут жёлтыми
                        if (str.Length == 0)
                        {
                            for (int j = 0; j < ap.ap.receiverecs[idn].m; j++)
                            {
                                setColorOfPictureBox(pictureBoxes[idn, j], 2);
                            }
                            string tmplogmsg = "Пустая страница. Возможно, произошло зависание устройства.";
                            if (ap.ap.receiverecs[idn].lastlogmsg[0] != tmplogmsg)
                            {
                                ap.ap.receiverecs[idn].lastlogmsg[0] = tmplogmsg;
                                ap.ap.receiverecs[idn].laststatus[0] = 2;
                                WriteToConnectionLog(idn, tmplogmsg);
                            }
                            Task.Factory.StartNew(() => getSelectedInput(idn));
                            return;
                        }
                        //Console.WriteLine(String.Format("Response: {0}", str));
                        Task.Factory.StartNew(() => getSelectedInput(idn));
                        for (int j = 0; j < ap.ap.receiverecs[idn].m; j++)
                        {
                            Console.WriteLine(j.ToString());
                            string pattern = ap.ap.receiverecs[idn].regexps[j].ToString();
                            Regex newReg = new Regex(pattern);
                            Match matches = newReg.Match(str);
                            int currentstate = 0;
                            if (matches.Groups[1].Value == "1")  // if (matches.Groups[1].Success)
                            {
                                //Console.WriteLine(matches.Groups[1].Value);
                                setColorOfPictureBox(pictureBoxes[idn, j], 1);
                                currentstate = 1;
                            }
                            else
                            {
                                setColorOfPictureBox(pictureBoxes[idn, j], 0);
                                currentstate = 0;
                            }

                            if (ap.ap.writetofile == true)
                            {
                                // если полночь и данные уже записываются                             
                                // блок для безусловной записи в определенное время
                                if (writing == true)
                                {
                                    Console.WriteLine("write parameter " + j.ToString() + " " + ap.ap.receiverecs[idn].parameters[j]);
                                    string tmpmsg = ap.ap.receiverecs[idn].parameters[j] + "\t" + intToStatus(currentstate) + "\t" + ap.ap.receiverecs[idn].url;
                                    Thread.Sleep(100);
                                    WriteToLog(idn, tmpmsg);
                                    ap.ap.receiverecs[idn].lastlogmsg[j] = tmpmsg;
                                    ap.ap.receiverecs[idn].laststatus[j] = currentstate;
                                    if (ap.ap.receiverecs[idn].m == j)
                                        return;
                                    continue;
                                }
                                // блок для обычной записи
                                else
                                {
                                    // если состояние хоть одного параметра изменилось - пишем
                                    if (ap.ap.receiverecs[idn].laststatus[j] != currentstate)
                                    {
                                        string tmpmsg = ap.ap.receiverecs[idn].parameters[j] + "\t" + intToStatus(currentstate) + "\t" + ap.ap.receiverecs[idn].url;
                                        Thread.Sleep(100);
                                        WriteToLog(idn, tmpmsg);
                                        ap.ap.receiverecs[idn].lastlogmsg[j] = tmpmsg;
                                    }
                                }
                            }
                            ap.ap.receiverecs[idn].laststatus[j] = currentstate;
                        }
                    }
                }
                catch (System.ArgumentOutOfRangeException e)
                {
                    MessageBox.Show("new exception: " + e.Message);
                }
                catch (Exception e)
                {
                    if (!ap.ap.writetofile)
                        return;
                    Label tmp = getlabelbyid(idn);

                    // увеличиваем счетчик неудачных подключений
                    ap.ap.receiverecs[idn].counter = (ap.ap.receiverecs[idn].counter + 1) % ap.ap.logconnectlimit; // счетчик циклический, меняется в диапазоне от 0 до ap.ap.logconnectlimit - 1
                    // если исключение появляется уже в (ap.ap.logconnectlimit - 1) раз
                    if (ap.ap.receiverecs[idn].counter == ap.ap.logconnectlimit - 1)
                    {

                        // прошлое значение не было связано с проблемами подключения
                        if (ap.ap.receiverecs[idn].laststatus[0] != 2)
                        {
                            for (int j = 0; j < ap.ap.receiverecs[idn].m; j++)
                            {
                                // задаем статус ответа
                                ap.ap.receiverecs[idn].laststatus[j] = 2;
                                // подсветить все лампочки желтым
                                setColorOfPictureBox(pictureBoxes[idn, j], 2);
                            }

                            // пишем в лог только если предыдущее сообщение отличается от текущего (не пишем повторы)
                            if (ap.ap.receiverecs[idn].lastlogmsg[0] != e.Message)
                            {
                                var st = new StackTrace(e, true);
                                var frame = st.GetFrame(0);
                                var line = frame.GetFileLineNumber();
                                WriteToConnectionLog(idn, e.Message + " " + line.ToString());
                            }
                        }
                    }
                }
        }

        private void настройкиToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!isSettingsForm)
            {
                ap.CreateSettingsForm();
                isSettingsForm = true;
            }
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void Form1_ResizeEnd(object sender, EventArgs e)
        {
            ap.ap.formheight = (uint)this.Height;
            ap.ap.formwidth = (uint)this.Width;
            ap.WriteSettings();
        }
    }

    public class ReceiverRecord
    {
        public string name;
        public string url;
        public string urlinput;
        public string login;
        public string pass;

        [XmlIgnore]
        public List<int> laststatus; // значение последних считанных параметров (статусы), по 1 на параметр
        [XmlIgnore]
        public List<string> lastlogmsg; // последние записанные в лог сообщения, по 1 на параметр
        [XmlIgnore]
        public string lastactiveinput; // предыдущий активный вход
        public int counter; // счетчик перебоев с соединением

        public List<string> parameters;
        public List<string> regexps;
        public int m;

        public ReceiverRecord()
        {
            m = 5;
            counter = 0;
            parameters = new List<string>(new string[m]);
            regexps = new List<string>(new string[m]);
            laststatus = new List<int>(new int[m]);
            lastactiveinput = ""; // предыдущий активный вход
            for (int j = 0; j < m; j++)
            {
                laststatus[j] = 4;
            }
            lastlogmsg = new List<string>(new string[m]);
        }

        public ReceiverRecord(int inm)
        {
            m = inm;
            counter = 0;
            parameters = new List<string>(new string[m]);
            regexps = new List<string>(new string[m]);
            laststatus = new List<int>(new int[m]);
            lastactiveinput = ""; // предыдущий активный вход
            for (int j = 0; j < m; j++)
            {
                laststatus[j] = 4;
            }
            lastlogmsg = new List<string>(new string[m]);
        }

        public void Resize(int newm)
        {
            m = newm;
            int countr = parameters.Count;
            if (newm < countr)
            {
                parameters.RemoveRange(newm, countr - newm);
                regexps.RemoveRange(newm, countr - newm);
                laststatus.RemoveRange(newm, countr - newm);
                lastlogmsg.RemoveRange(newm, countr - newm);
            }
            else if (newm > countr)
            {
                if (newm > parameters.Capacity)
                {
                    parameters.AddRange(new List<string>(new string[newm - countr]));
                    regexps.AddRange(new List<string>(new string[newm - countr]));
                    laststatus.AddRange(new List<int>(new int[newm - countr]));
                    lastlogmsg.AddRange(new List<string>(new string[newm - countr]));
                }
            }
        }
    }

    public class settings
    {
        // main settings
        public class setstruct
        {
            public int n = 1;
            public int m = 5;
            public string path = "MySettings.xml"; // main conf file
            public uint period;
            public bool writetofile;
            public bool contype; // true = http, false = snmp
            public uint formwidth;
            public uint formheight;
            public string mainlogpath;
            public bool nestedpath;
            public List<ReceiverRecord> receiverecs = new List<ReceiverRecord>();
            public int logconnectlimit; // число неудачных попыток подключения перед записью в лог
        }
        public setstruct ap;
        public XmlSerializer x;
        Form1 formobj;

        // form specific objects and variables
        Form settingsform;
        NumericUpDown receivescnt;
        TextBox[] tnames;
        TextBox[] turls;
        TextBox[] turlset;
        Label[] captions;
        NumericUpDown height;
        NumericUpDown width;
        // // кнопки для настройки параметров
        Button[] paramitems;
        Label captionlogdir;
        TextBox logmaindir;
        
        // для настройки регулярных выражений
        Button[] regexps;
        // для настройки логинов
        TextBox[] logins;
        // и паролей
        TextBox[] passwords;
        CheckBox twritetofile;
        CheckBox tnestedpath;
        Label inform;
        NumericUpDown tperiod;
        Button okbutton;
        Button cancelbutton;
        //

        public settings(Form1 fobj)
        {
            ap = new setstruct();
            formobj = fobj;
            ap.n = 1;
            setvars();
        }
        public settings(Form1 fobj, int inn = 1)
        {
            ap = new setstruct();
            formobj = fobj;
            ap.n = inn;
            setvars();
        }
        // Initialize receivers
        public void setvars()
        {
            x = new XmlSerializer(typeof(setstruct));
            for (int i = 0; i < ap.n; i++)
            {
                ap.receiverecs.Add(new ReceiverRecord());
            }
        }

        public void SetGlobalDefaultSettings()
        {
            ap.n = 1;
            ap.m = 5;
            ap.period = 10;
            ap.logconnectlimit = 3;
            ap.writetofile = true;
            ap.contype = true;
            ap.nestedpath = true;
            ap.formwidth = 606;
            ap.formheight = 567;
            ap.mainlogpath = "C:\\";
        }
        // Set default settings (useful if there is no settings file yet)
        public void setdefaults(ReceiverRecord rr)
        {
            rr.login = "root";
            rr.pass = "12345";
            rr.name = "36 ТНТ";
            rr.url = "http://192.168.4.231/cgi-bin/input_status.cgi";
            rr.urlinput = "http://192.168.4.231/cgi-bin/decoder_config.cgi";
            rr.parameters[0] = "IP";
            rr.parameters[1] = "ASI1";
            rr.parameters[2] = "ASI2";
            rr.parameters[3] = "Tuner";
            rr.parameters[4] = "CI";
            rr.laststatus[0] = 4;
            rr.laststatus[1] = 4;
            rr.laststatus[2] = 4;
            rr.laststatus[3] = 4;
            rr.laststatus[4] = 4;
            rr.regexps[0] = "<ip value=\"\\d\">\\W+<lock value=\"(\\d+?)\">";
            rr.regexps[1] = "<asi1 value=\"\\d\">\\W+<lock value=\"(\\d+?)\">";
            rr.regexps[2] = "<asi2 value=\"\\d\">\\W+<lock value=\"(\\d+?)\">";
            rr.regexps[3] = "<tuner value=\"\\d\">\\W+<lock value=\"(\\d+?)\">";
            rr.regexps[4] = "<ci value=\"\\d\">\\W+<lock value=\"(\\d+?)\">";
        }

        // Change the number of receivers that we monitor (via the settings)
        public void Resize(int newn)
        {
            int countr = ap.receiverecs.Count;
            if (newn > countr)
            {
                // Add new receivers
                if (newn > ap.receiverecs.Capacity)
                {
                    ap.receiverecs.AddRange(new List<ReceiverRecord>(new ReceiverRecord[newn - countr]));
                }
            }
            else if (newn < countr)
            {
                // Delete unnecesary items
                ap.receiverecs.RemoveRange(newn, countr - newn);
            }
        }

        // определяет, активирован ли ресивер (то есть отрисованы ли его индикаторы - нажата ли кнопка OK настроек)
        bool isrecvActive(int i, int j)
        {
            return formobj.PictboxExists(i, j);
        }

        // настройка параметров относящихся к данному ресиверу, i - индекс ресивера, type - тип параметров (true - список параметров, false - регулярные выражения)
        public void SetParameters(int i, bool type)
        {
            Form setparamsform = new Form();
            setparamsform.TopMost = true;
            TextBox textboxparams = new TextBox();
            textboxparams.Multiline = true;
            textboxparams.SetBounds(setparamsform.Location.X, setparamsform.Location.Y, setparamsform.Width, 80);

            if (type)
                setparamsform.Text = "Настройка параметров для считывания";
            else
                setparamsform.Text = "Настройка регулярных выражений для поиска параметров";

            Button okbtn = new Button();
            okbtn.Text = "OK";
            okbtn.Click += (s, e) =>
            {
                // save params to struct
                using (System.IO.StringReader reader = new System.IO.StringReader(textboxparams.Text))
                {
                    if (type)
                        formobj.oldparams[i] = reader.ReadToEnd();
                    else
                        formobj.oldregexps[i] = reader.ReadToEnd();
                }
                setparamsform.Close();
            };
            okbtn.Location = new Point(textboxparams.Location.X + 10, textboxparams.Location.Y + textboxparams.Height + 20);
            Button cancelbtn = new Button();
            cancelbtn.Click += (s, e) => {
                setparamsform.Close();
            };
            cancelbtn.Text = "Cancel";
            cancelbtn.Location = new Point(okbtn.Location.X + 80, okbtn.Location.Y);
            setparamsform.Controls.Add(textboxparams);
            setparamsform.Controls.Add(okbtn);
            setparamsform.Controls.Add(cancelbtn);
            setparamsform.Show();
            // put current params to the form
            textboxparams.Clear();
            if (type)
                textboxparams.AppendText(string.Join("\r\n", formobj.oldparams[i].Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)));
            else
                textboxparams.AppendText(string.Join("\r\n", formobj.oldregexps[i].Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)));
        }

        void param_Click(object sender, EventArgs e)
        {
            var cur = sender as Button;
            SetParameters(Int32.Parse(cur.Name),true);
        }

        void regexps_Click(object sender, EventArgs e)
        {
            var cur = sender as Button;
            SetParameters(Int32.Parse(cur.Name), false);
        }

        void updown_Changed(object sender, EventArgs e)
        {
            
            int newvalue = (int)receivescnt.Value;
            int oldvalue = ap.n;
            // изменилось количество ресиверов
            ap.n = newvalue;
            Resize(newvalue);

            // двигаем нижние контролы на фиксированный шаг
            int tempstep = 20;

            if (newvalue > oldvalue)
            {
                // увеличилось
                // добавляем нужное количество контролов
                for (int i = oldvalue; i < newvalue; ++i)
                {
                    Array.Resize(ref tnames, newvalue);
                    Array.Resize(ref turls, newvalue);
                    Array.Resize(ref turlset, newvalue);
                    Array.Resize(ref paramitems, newvalue);
                    Array.Resize(ref regexps, newvalue);
                    Array.Resize(ref logins, newvalue);
                    Array.Resize(ref passwords, newvalue);
                    Array.Resize(ref captions, newvalue);

                    tnames[i] = new TextBox();
                    turls[i] = new TextBox();
                    turlset[i] = new TextBox();
                    turlset[i].Width = 170;
                    paramitems[i] = new Button();
                    regexps[i] = new Button();
                    logins[i] = new TextBox();
                    passwords[i] = new TextBox();
                    captions[i] = new Label();
                    tnames[i].Location = new Point(tnames[i - 1].Location.X, tnames[i - 1].Location.Y + 40);
                    settingsform.Controls.Add(tnames[i]);
                    captions[i].Location = new Point(tnames[i - 1].Location.X, tnames[i - 1].Location.Y + 20);
                    settingsform.Controls.Add(captions[i]);
                    turls[i].Location = new Point(tnames[i].Location.X + 100, tnames[i].Location.Y);
                    turls[i].Width = 120;
                    settingsform.Controls.Add(turls[i]);
                    paramitems[i].Location = new Point(turls[i].Location.X + 120, turls[i].Location.Y);
                    paramitems[i].Width = 150;
                    paramitems[i].Text = "Настройка параметров";
                    paramitems[i].Click += new EventHandler(param_Click);
                    paramitems[i].Name = i.ToString();
                    settingsform.Controls.Add(paramitems[i]);
                    regexps[i].Location = new Point(paramitems[i].Location.X + 150, paramitems[i].Location.Y);
                    regexps[i].Width = 200;
                    regexps[i].Text = "Настройка рег.выражений";
                    regexps[i].Click += new EventHandler(regexps_Click);
                    regexps[i].Name = i.ToString();
                    settingsform.Controls.Add(regexps[i]);
                    logins[i].Location = new Point(regexps[i].Location.X + 200, regexps[i].Location.Y);
                    settingsform.Controls.Add(logins[i]);
                    passwords[i].Location = new Point(logins[i].Location.X + 100, logins[i].Location.Y);
                    settingsform.Controls.Add(passwords[i]);
                    turlset[i].Location = new Point(passwords[i].Location.X + 100, passwords[i].Location.Y);
                    settingsform.Controls.Add(turlset[i]);
                    captions[i].Text = String.Format("Имя {0} ресивера", i + 1);

                    // двигаем нижние контролы вниз, не выходя за границы
                    twritetofile.Location = new Point(tnames[i].Location.X, tnames[i].Location.Y + 30);
                    inform.Location = new Point(inform.Location.X, twritetofile.Location.Y + tempstep); ;
                    tperiod.Location = new Point(tperiod.Location.X, inform.Location.Y + tempstep);
                    okbutton.Location = new Point(okbutton.Location.X, tperiod.Location.Y + tempstep);
                    cancelbutton.Location = new Point(cancelbutton.Location.X, okbutton.Location.Y);
                    captionlogdir.Location = new Point(captionlogdir.Location.X, inform.Location.Y);
                    logmaindir.Location = new Point(logmaindir.Location.X, captionlogdir.Location.Y);
                    tnestedpath.Location = new Point(tnestedpath.Location.X, logmaindir.Location.Y);

                    // создаем объект по умолчанию для нового ресивера
                    ReceiverRecord tempobj = new ReceiverRecord();
                    ap.receiverecs.Add(tempobj);
                    setdefaults(tempobj);

                    ap.receiverecs.RemoveAll(item => item == null); // если в поле параметров появляются лишние null, этот код их убирает
                    ap.receiverecs.RemoveAll(item => item == null);              

                    // даем полям значения по умолчанию
                    logins[i].Text = "root";
                    passwords[i].Text = "12345";
                    // адрес копируем из предыдущего поля
                    turls[i].Text = turls[i - 1].Text;
                    turlset[i].Text = turlset[i - 1].Text;


                    formobj.oldparams[i] = string.Join("\r\n", ap.receiverecs[i].parameters.ToArray(), 0, ap.receiverecs[i].m);
                    formobj.oldregexps[i] = string.Join("\r\n", ap.receiverecs[i].regexps.ToArray(), 0, ap.receiverecs[i].m);
                }
            }
            else if (newvalue < oldvalue)
            {
                // уменьшилось
                // скрываем ненужные контролы
                for (int i = oldvalue; i > newvalue; i--)
                {
                    tnames[i - 1].Hide();
                    settingsform.Controls.Remove(tnames[i - 1]);
                    tnames[i - 1].Dispose();


                    turls[i - 1].Hide();
                    settingsform.Controls.Remove(turls[i - 1]);
                    turls[i - 1].Dispose();


                    paramitems[i - 1].Hide();
                    settingsform.Controls.Remove(paramitems[i - 1]);
                    paramitems[i - 1].Dispose();

                    regexps[i - 1].Hide();
                    settingsform.Controls.Remove(regexps[i - 1]);
                    regexps[i - 1].Dispose();

                    logins[i - 1].Hide();
                    settingsform.Controls.Remove(logins[i - 1]);
                    logins[i - 1].Dispose();

                    passwords[i - 1].Hide();
                    settingsform.Controls.Remove(passwords[i - 1]);
                    passwords[i - 1].Dispose();

                    turlset[i - 1].Hide();
                    settingsform.Controls.Remove(turlset[i - 1]);
                    turlset[i - 1].Dispose();

                    captions[i - 1].Hide();
                    settingsform.Controls.Remove(captions[i - 1]);
                    captions[i - 1].Dispose();
                }


                Array.Resize(ref tnames, newvalue);
                Array.Resize(ref turls, newvalue);
                Array.Resize(ref turlset, newvalue);
                Array.Resize(ref paramitems, newvalue);
                Array.Resize(ref regexps, newvalue);
                Array.Resize(ref logins, newvalue);
                Array.Resize(ref passwords, newvalue);
                Array.Resize(ref turlset, newvalue);
                Array.Resize(ref captions, newvalue);

                //{
                // поднимаем нижние контролы на фиксированный шаг вверх, не выходя за границы
                twritetofile.Location = new Point(tnames[newvalue - 1].Location.X, tnames[newvalue - 1].Location.Y + 30);
                inform.Location = new Point(inform.Location.X, twritetofile.Location.Y + tempstep);
                tperiod.Location = new Point(tperiod.Location.X, inform.Location.Y + tempstep);
                okbutton.Location = new Point(okbutton.Location.X, tperiod.Location.Y + tempstep);
                cancelbutton.Location = new Point(cancelbutton.Location.X, okbutton.Location.Y);
                captionlogdir.Location = new Point(captionlogdir.Location.X, inform.Location.Y);
                logmaindir.Location = new Point(logmaindir.Location.X, inform.Location.Y);
                tnestedpath.Location = new Point(tnestedpath.Location.X, logmaindir.Location.Y);
                //}
            }
        }

        void label_DragEnter(object sender, DragEventArgs e)
        {
            int newX = e.X;
            int newY = e.Y;
            // formobj.GetChildAtPoint()
            Control tempControl = formobj.GetChildAtPoint(Cursor.Position);
            for (int j = 0; j < ap.m; j++)
            {
                if (tempControl == formobj.dynamicparamls[0, j])
                {
                    // swap controls
                }
            }

        }

        void label_DragDrop(object sender, DragEventArgs e)
        {
        }

        // создание формы настроек
        public void CreateSettingsForm()
        {
            formobj.timer1.Stop();
            settingsform = new Form();
            receivescnt = new NumericUpDown();
            tnames = new TextBox[ap.n];
            turls = new TextBox[ap.n];
            captions = new Label[ap.n];
            // для задания проверяемых параметров
            paramitems = new Button[ap.n];
            // для настройки регулярных выражений
            regexps = new Button[ap.n];
            // для настройки логинов
            logins = new TextBox[ap.n];
            // и паролей
            passwords = new TextBox[ap.n];
            turlset = new TextBox[ap.n];

            settingsform.Disposed += (s, e) =>
                {
                    formobj.isSettingsForm = false;
                };

            formobj.dynamicparamls[0, 0].AllowDrop = true;
            formobj.dynamicparamls[0, 0].DragEnter += new DragEventHandler(label_DragEnter);
            formobj.dynamicparamls[0, 0].DragDrop +=new DragEventHandler(label_DragDrop);

            receivescnt.Value = ap.n;
            receivescnt.Width = 40;
            receivescnt.Minimum = 1;
            receivescnt.Maximum = 10;
            receivescnt.Location = new Point(130, 5);

            for (int i = 0; i < ap.n; i++)
            {
                //MessageBox.Show(string.Join("\r\n", ap.receiverecs[i].parameters.ToArray(), 0, ap.receiverecs[i].m));
                formobj.oldparams[i] = string.Join("\r\n", ap.receiverecs[i].parameters.ToArray(), 0, ap.receiverecs[i].m);

                formobj.oldregexps[i] = string.Join("\r\n", ap.receiverecs[i].regexps.ToArray(), 0, ap.receiverecs[i].m);
            }

            //MessageBox.Show(formobj.oldparams[0].Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Count().ToString());
            //MessageBox.Show("Инициализация формы настроек прошла. Параметры в oldparams\n" + formobj.oldparams);
            for (int i = 0; i < ap.n; i++)
            {
                tnames[i] = new TextBox();
                turls[i] = new TextBox();
                captions[i] = new Label();
                paramitems[i] = new Button();
                paramitems[i].Text = "Настройка параметров";
                paramitems[i].Click += new EventHandler(param_Click);
                paramitems[i].Name = i.ToString();
                regexps[i] = new Button();
                regexps[i].Text = "Настройка рег.выражений";
                regexps[i].Click += new EventHandler(regexps_Click);
                regexps[i].Name = i.ToString();
                logins[i] = new TextBox();
                passwords[i] = new TextBox();
                turlset[i] = new TextBox();
                // размещение элементов: с каждым индексом ордината ниже на фиксированный шаг
                if (i == 0)
                {
                    settingsform.Controls.Add(receivescnt); // numerica updown for receivers
                    captions[i].Location = new Point(5, 35);
                    settingsform.Controls.Add(captions[i]);
                    // потом поля ввода
                    // левые
                    tnames[i].Location = new Point(captions[i].Location.X, captions[i].Location.Y + 20);
                    settingsform.Controls.Add(tnames[i]);
                    // правые
                    turls[i].Location = new Point(tnames[i].Location.X + 100, tnames[i].Location.Y);
                    turls[i].Width = 120;
                    settingsform.Controls.Add(turls[i]);
                    // поле ввода параметров
                    paramitems[i].Location = new Point(turls[i].Location.X + 120, turls[i].Location.Y);
                    paramitems[i].Width = 150;
 
                    settingsform.Controls.Add(paramitems[i]);
                    // поле ввода регулярного выражения
                    regexps[i].Location = new Point(paramitems[i].Location.X + 150, paramitems[i].Location.Y);
                    regexps[i].Width = 200;
                    settingsform.Controls.Add(regexps[i]);
                    // поле имени пользователя
                    logins[i].Location = new Point(regexps[i].Location.X + 200, regexps[i].Location.Y);
                    settingsform.Controls.Add(logins[i]);
                    // поле ввода пароля
                    passwords[i].Location = new Point(logins[i].Location.X + 100, logins[i].Location.Y);
                    // поле ввода адреса выбора входа
                    turlset[i].Width = 170;
                    turlset[i].Location = new Point(passwords[i].Location.X + 100, passwords[i].Location.Y);
                    settingsform.Controls.Add(passwords[i]);
                    settingsform.Controls.Add(turlset[i]);
                }
                if (i > 0)
                {
                    // сначала подписи к полям
                    captions[i].Location = new Point(tnames[i - 1].Location.X, tnames[i - 1].Location.Y + 20);
                    settingsform.Controls.Add(captions[i]);
                    // потом поля ввода
                    // левые
                    tnames[i].Location = new Point(tnames[i - 1].Location.X, tnames[i - 1].Location.Y + 40);
                    settingsform.Controls.Add(tnames[i]);
                    // правые
                    turls[i].Location = new Point(tnames[i].Location.X + 100, tnames[i].Location.Y);
                    turls[i].Width = 120;
                    settingsform.Controls.Add(turls[i]);
                    // поля ввода параметров
                    paramitems[i].Location = new Point(turls[i].Location.X + 120, turls[i].Location.Y);
                    paramitems[i].Width = 150;

                    settingsform.Controls.Add(paramitems[i]);
                    // поля ввода регулярного выражения
                    regexps[i].Location = new Point(paramitems[i].Location.X + 150, paramitems[i].Location.Y);
                    regexps[i].Width = 200;
                    settingsform.Controls.Add(regexps[i]);
                    // поля имени пользователя
                    logins[i].Location = new Point(regexps[i].Location.X + 200, regexps[i].Location.Y);
                    settingsform.Controls.Add(logins[i]);
                    // поля ввода пароля
                    passwords[i].Location = new Point(logins[i].Location.X + 100, logins[i].Location.Y);
                    settingsform.Controls.Add(passwords[i]);
                    turlset[i].Width = 170;
                    turlset[i].Location = new Point(passwords[i].Location.X + 100, passwords[i].Location.Y);
                    settingsform.Controls.Add(turlset[i]);
                }

                captions[i].Text = String.Format("Имя {0} ресивера", i + 1);
            }

            Label captionupdown = new Label();
            captionupdown.Text = "Количество ресиверов";
            captionupdown.Width = 130;
            captionupdown.Location = new Point(5, 7);
            settingsform.Controls.Add(captionupdown);

            Label captionformsize = new Label();
            captionformsize.Text = "Размеры формы по умолчанию";
            captionformsize.Location = new Point(200, 7);
            captionformsize.Width = 200;
            settingsform.Controls.Add(captionformsize);

            width = new NumericUpDown();
            width.Minimum = 10;
            width.Maximum = 9999;
            width.Width = 60;
            width.Value = ap.formwidth;
            width.Location = new Point(captionformsize.Location.X + 220, captionformsize.Location.Y);
            settingsform.Controls.Add(width);

            height = new NumericUpDown();
            height.Minimum = 10;
            height.Maximum = 9999;
            height.Width = 60;
            height.Value = ap.formheight;
            height.Location = new Point(width.Location.X + 70, width.Location.Y);
            settingsform.Controls.Add(height);

            Label captionaddr = new Label();
            captionaddr.Text = "Адрес";
            captionaddr.Location = new Point(captions[0].Location.X + 100, captions[0].Location.Y);
            settingsform.Controls.Add(captionaddr);

            Label captionparams = new Label();
            captionparams.Text = "Считываемые параметры";
            captionparams.Width = 150;
            captionparams.Location = new Point(captionaddr.Location.X + 120, captionaddr.Location.Y);
            settingsform.Controls.Add(captionparams);

            Label captionregex = new Label();
            captionregex.Text = "Регулярное выражение для поиска";
            captionregex.Width = 200;
            captionregex.Location = new Point(captionparams.Location.X + 150, captionparams.Location.Y);
            settingsform.Controls.Add(captionregex);

            Label captionlogin = new Label();
            captionlogin.Text = "Логин";
            captionlogin.Location = new Point(captionregex.Location.X + 200, captionregex.Location.Y);
            settingsform.Controls.Add(captionlogin);

            Label captionpass = new Label();
            captionpass.Text = "Пароль";
            captionpass.Location = new Point(captionlogin.Location.X + 100, captionlogin.Location.Y);
            settingsform.Controls.Add(captionpass);

            Label turlsetcap = new Label(); // контролы для адреса страницы выбора текущего входа
            turlsetcap.Text = "Адрес страницы выбора входа";
            turlsetcap.Width = 180;
            turlsetcap.Location = new Point(captionpass.Location.X + 110, captionpass.Location.Y);
            settingsform.Controls.Add(turlsetcap);

            twritetofile = new CheckBox(); // чекбокс ведение журнала?
            inform = new Label(); // лейбл периодичность опроса ресивера
            tperiod = new NumericUpDown(); // периодичность
            tperiod.Minimum = 1;
            tperiod.Maximum = 99999;
            settingsform.Text = "Program Settings";
            
            settingsform.Size = new System.Drawing.Size(990, 600);
            
            okbutton = new Button();
            okbutton.Text = "OK";
            okbutton.Click += new EventHandler(okbutton_Click);
            okbutton.Location = new Point(turls[ap.n-1].Location.X - 40, turls[ap.n-1].Location.Y + 90);
            settingsform.Controls.Add(okbutton);

            cancelbutton = new Button();
            cancelbutton.Text = "Отмена";
            cancelbutton.Click += (s, e) =>
                {
                    // нажата кнопка отмены
                    settingsform.Close(); // форма закрывается
                    formobj.isSettingsForm = false; // флаг помечается
                    // старые настройки снова считываются из файла
                    ReadSettings();
                    for (int i = 0; i < ap.n; i++)
                    {
                        ap.receiverecs[i].parameters.RemoveAll(item => item == null); // при десериализации в поле параметров появляются лишние null, этот код их убирает
                        ap.receiverecs[i].regexps.RemoveAll(item => item == null);
                    }
          
                    // таймер запускается снова
                    formobj.timer1.Start();
                };
            cancelbutton.Location = new Point(okbutton.Location.X + 80, okbutton.Location.Y);
            settingsform.Controls.Add(cancelbutton);

            twritetofile.Size = new Size(200, 20);
            twritetofile.Text = "Ведение журналов показаний";
            twritetofile.Location = new Point(tnames[ap.n-1].Location.X, tnames[ap.n-1].Location.Y + 20);
            settingsform.Controls.Add(twritetofile);
            inform.Size = new Size(300, 20);
            inform.Text = "Периодичность опроса ресивера, сек.";
            inform.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 20);
            settingsform.Controls.Add(inform);

            captionlogdir = new Label();
            captionlogdir.Location = new Point(inform.Location.X + 300, inform.Location.Y);
            captionlogdir.Text = "Директория для логов";
            captionlogdir.Width = 130;
            settingsform.Controls.Add(captionlogdir);
            logmaindir = new TextBox();
            logmaindir.Width = 150;
            logmaindir.Location = new Point(captionlogdir.Location.X + 130, captionlogdir.Location.Y);
            settingsform.Controls.Add(logmaindir);

            tnestedpath = new CheckBox();
            tnestedpath.Text = "Поддиректории по датам";
            tnestedpath.Width = 200;
            tnestedpath.Location = new Point(logmaindir.Location.X + 160, logmaindir.Location.Y);
            settingsform.Controls.Add(tnestedpath);

            tperiod.Size = new Size(200, 20);
            tperiod.Location = new Point(inform.Location.X, inform.Location.Y + 20);
            settingsform.Controls.Add(tperiod);
            settingsform.TopMost = true;

            // при изменении числа ресиверов через форму настроек
            receivescnt.ValueChanged += new EventHandler(updown_Changed);

            settingsform.Show();
            ApplySettingsFromStruct();
        }

        // write the settings to XML (serialize)
        public bool WriteSettings()
        {
            try
            {
                StreamWriter writer = new StreamWriter(ap.path);
                x.Serialize(writer, ap);
                writer.Close();
                writer.Dispose();
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return false;
            }
        }

        // read the settings from XML (deserialize)
        public bool ReadSettings()
        {
            if (!File.Exists(ap.path))
                return false;
            try
            {
                StreamReader reader = new StreamReader(ap.path);
                ap = (setstruct)x.Deserialize(reader);
                reader.Close();
                reader.Dispose();
                // ap.logconnectlimit can't be 0
                if (ap.logconnectlimit == 0)
                    ap.logconnectlimit = 1;
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return false;
            }
        }
        
        // set settings form controls according to the struct
        private void ApplySettingsFromStruct()
        {
            for (int i = 0; i < ap.n; i++)
            {
                tnames[i].Text = ap.receiverecs[i].name;
                turls[i].Text = ap.receiverecs[i].url;
                // params and regexps we should set in buttons handlers
                logins[i].Text = ap.receiverecs[i].login;
                passwords[i].Text = ap.receiverecs[i].pass;
                turlset[i].Text = ap.receiverecs[i].urlinput;
            }
            logmaindir.Text = ap.mainlogpath;
            tnestedpath.Checked = ap.nestedpath;
            twritetofile.Checked = ap.writetofile;
            tperiod.Value = ap.period;
        }

        // update the struct after changing the settings via controls
        private void ApplySettingsToStruct()
        {
            for (int i = 0; i < ap.n; i++)
            {
                ap.receiverecs[i].name = tnames[i].Text;
                ap.receiverecs[i].url = turls[i].Text;
                ap.receiverecs[i].login = logins[i].Text;
                ap.receiverecs[i].pass = passwords[i].Text;
                ap.receiverecs[i].urlinput = turlset[i].Text;
            }
            ap.writetofile = twritetofile.Checked;
            ap.period = Convert.ToUInt32(tperiod.Value);
            ap.mainlogpath = logmaindir.Text;
            ap.nestedpath = tnestedpath.Checked;
            ap.formwidth = Convert.ToUInt32(width.Value);
            ap.formheight = Convert.ToUInt32(height.Value);
        }

        void okbutton_Click(object sender, EventArgs e)
        {
            int tempn = (int)receivescnt.Value;
            // проверка полей на заполненность
            for (int i = 0; i < tempn; i++)
            {
                if (turls[i].Visible && turls[i].Text == "")
                {
                    MessageBox.Show("Внимание! Одно из полей адреса не задано.");
                    return;
                }
                if (tnames[i].Visible && tnames[i].Text == "")
                {
                    MessageBox.Show("Внимание! Одно из полей имени не задано.");
                    return;
                }
                if (logins[i].Visible && logins[i].Text == "")
                {
                    MessageBox.Show("Внимание! Одно из полей логина не задано.");
                    return;
                }
                if (passwords[i].Visible && passwords[i].Text == "")
                {
                    MessageBox.Show("Внимание! Одно из полей пароля не задано.");
                    return;
                }
            }

            // применяем настройки параметров и шаблонов к структуре
            for (int i = 0; i < ap.n; i++)
            {
                // проверка количества строк параметров и шаблонов на совпадение
                int n1 = formobj.oldparams[i].Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Count();
                int n2 = formobj.oldregexps[i].Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Count();
                if (n1 != n2)
                {
                    MessageBox.Show("Внимание! Количество строк-параметров не совпадает с количеством строк-шаблонов. Они должны соответстовать друг другу.");
                    return;
                }
                ap.receiverecs[i].m = n1;
                for (int j = 0; j < ap.receiverecs[i].m; j++)
                {
                    //MessageBox.Show(formobj.oldparams[i].Split(new[] { "\r\n" }, StringSplitOptions.None)[j]);
                    ap.receiverecs[i].parameters[j] = formobj.oldparams[i].Split(new[] { "\r\n" }, StringSplitOptions.None)[j];
                    ap.receiverecs[i].regexps[j] = formobj.oldregexps[i].Split(new[] { "\r\n" }, StringSplitOptions.None)[j];
                }
            }

            formobj.isSettingsForm = false; // форма настроек закрыта
            ap.n = tempn;

            ApplySettingsToStruct();
            WriteSettings();
            settingsform.Close();

            // timer 
            formobj.timer1.Interval = 1000*(int)ap.period;

            formobj.RedrawForm();
        }
    }
}
