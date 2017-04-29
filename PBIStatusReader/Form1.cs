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
using System.Collections.Specialized;

namespace PBIStatusReader
{
    public partial class Form1 : Form
    {
        int n = 10; // число ресиверов
        settings ap;
        public System.Timers.Timer timer1;
        public System.Timers.Timer timermidnight;
        public bool appstarted; // флаг-индикатор того, что программа запущена, но данные ещё не считывались
        Label[] recvnamelabels;
        PictureBox[,] pictureBoxes = new PictureBox[10,5];
        //static HttpWebRequest webRequest;
        public Label[,] dynamicparamls;
        public LogManager logger;
        public bool isSettingsForm; // форма настроек создана и активна
        public bool isParamsForm; // форма настроек имен параметров или их шаблонов открыта и активна
        public bool isFormatLogForm; // форма настроек формата логов создана и активна

        public string[] oldparams = new string[10]; // буферы для хранения задаваемых через отдельные формы настроек
        public string[] oldregexps = new string[10];

        public string oldpatternLogOK = "%timestamp%\t%devicename%\tINPUT\t%paramname%\t%msg%\t%url%\t%regexp%";
        public string oldpatternLogConFail = "%timestamp%\t%devicename%\tINPUT\t%paramname%\tERROR\t%url%\t%regexp%\t%msg%";
        public string oldpatternDecoderOK = "%timestamp%\t%devicename%\tDECODER\t%paramname%\tSET\t%url%\t%msg%";
        public string oldpatternDecoderConFail = "%timestamp%\t%devicename%\tDECODER\tURL\tERROR\t%url%\t%msg%";
        public string oldpatternGeneralConFail = "%timestamp%\t%devicename%\tCONNECTION\tERROR\t%url%\t%msg%";
		
		// мьютекс для файлов
		static ReaderWriterLock locker = new ReaderWriterLock();
		static ReaderWriterLock conlocker = new ReaderWriterLock();
        
        public Form1()
        {
            ap = new settings(this); // инициализация настроек приложения
            InitializeComponent();

            bool ifread = ap.ReadSettings(); // считываем записанные ранее настройки
            isSettingsForm = false;
            isParamsForm = false;
            isFormatLogForm = false;
            timer1 = new System.Timers.Timer();
            timer1.Elapsed += new System.Timers.ElapsedEventHandler(timer1_Tick);
            timermidnight = new System.Timers.Timer();
			timermidnight.AutoReset = false;
            timermidnight.Elapsed += (sender, args) =>
            {
                MidNightScan();
            };
            SetTimerMidnight();
            appstarted = true;
            if (!ifread)
            {
                // настройки из файла не загрузились - ставим дефолтные
                ap.SetGlobalDefaultSettings();
                ap.setdefaults(ap.ap.receiverecs[0]);
                // не удалось прочитать настройки
            }
            else
            {
                for (int i = 0; i < ap.ap.n; i++)
                {
                    ap.ap.receiverecs[i].parameters.RemoveAll(item => item == null); // при десериализации в поле параметров появляются лишние null, этот код их убирает
                    ap.ap.receiverecs[i].regexps.RemoveAll(item => item == null);
                }
            }

            if (ap.ap.patternLogOK == null)
                ap.ap.patternLogOK = oldpatternLogOK;
            if (ap.ap.patternLogConFail == null)
                ap.ap.patternLogConFail = oldpatternLogConFail;
            if (ap.ap.patternDecoderConFail == null)
                ap.ap.patternDecoderConFail = oldpatternDecoderConFail;
            if (ap.ap.patternGeneralConFail == null)
                ap.ap.patternGeneralConFail = oldpatternGeneralConFail;
            logger = new LogManager(ap);
            for (int i = 0; i < ap.ap.n; i++)
            {
                // пишем в лог о старте программы
                logger.rawWrite(i, logger.getTimeStamp() + "\t" + "APP\tSTART");
            }

            n = ap.ap.n;
                
            // рисуем форму
            DrawMainForm();
        }

        public void MidNightScan()
        {
            // midnight - new day, we need to recreate log files
            timer1.Stop(); // don't check the data for a while
			logger.reopenlogfiles(); 
            Task.Factory.StartNew(() => midnightsetValues()).ContinueWith(t => midnightgetSelectedInputs());
            timer1.Interval = 1000 * (int)ap.ap.period;
            Thread.Sleep(5000);
            timer1.Start();
			SetTimerMidnight();
        }

        public void SetTimerMidnight()
        {
			timermidnight.Stop();
            DateTime nowTime = DateTime.Now; // e.g. 21.04.2017 20:53:27
            DateTime time = DateTime.Today; // e.g. 21.04.2017 00:00:00
            if (nowTime.Day == time.Day)
                time = time.AddDays(1);
            var span = time - nowTime; // how many milliseconds till the next midnight
            //MessageBox.Show(span.TotalHours.ToString());
            timermidnight.Interval = span.TotalMilliseconds + 5000; // +5 secs
            timermidnight.AutoReset = false;

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
            //this.InitializeComponent(); // этот метод не нужно вызывать вне конструктора, иначе он повторно создает обработчики событий
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
            this.Location = new Point(ap.ap.previousLocationX, ap.ap.previousLocationY);
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
            timer1.AutoReset = true;
            timer1.Enabled = true;
            this.Text += " Загрузка данных...";
        }

        // полная перерисовка формы (для отображения изменения настроек на форме)
        public void RedrawForm()
        {
            ClearAllMainControls();
            DrawMainForm();
            this.Size = new Size((int)ap.ap.formwidth, (int)ap.ap.formheight);
            //this.Location = ap.ap.previousLocation;
        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        // получение данных со всех устройств
        void globalScan()
        {
            // если форма настроек открыта
            if (isSettingsForm)
                return;
            Task.Factory.StartNew(() => setValues()).ContinueWith(t => getSelectedInputs());
        }

        void timer1_Tick(object sender, EventArgs e)
        {
            if (appstarted)
                appstarted = false;
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
                return "ON";
            if (st == 0)
                return "OFF";
            if (st == 2)
                return "ERROR";
            else
                return "UNDEFINED";
        }

        // Get html code of web page, i - current device, type = 0 (values) or 1 (decoder page)
        string getHtmlcode(int i, string tmpurl, int type)
        {
            // authentification
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            int secondsSinceEpoch = (int)t.TotalSeconds;
            HttpWebResponse resp = null;
            HttpWebRequest webRequest;
            // webRequest = (HttpWebRequest)WebRequest.Create(geturlbyid(i) + "/cgi-bin/input_status.cgi?cur_time=" + secondsSinceEpoch.ToString());
            try
            {
                webRequest = (HttpWebRequest)WebRequest.Create(tmpurl);
            }
            catch (System.UriFormatException e)
            {
                if (type == 0)
                {
                    // status page
                    MakeAllLightsYellow(i);
                    logger.WriteToLog(0, i, 0, e.Message);
                }
                else if (type == 1)
                {
                    // decoder page
                    ap.ap.receiverecs[i].lastactiveinput = "NoConnect";
                    logger.WriteToLog(2, i, 0, e.Message);
                }
                return "";
            }

            webRequest.Method = "GET";
            webRequest.Timeout = 5000; // 5 секунд таймаут
            webRequest.Headers.Clear();
            byte[] authData = System.Text.Encoding.UTF8.GetBytes(ap.ap.receiverecs[i].login + ":" + ap.ap.receiverecs[i].pass);
            string authHeader = "Authorization: Basic " + Convert.ToBase64String(authData) + "\r\n";
            webRequest.Headers.Add(authHeader);
            this.Text = "PBI Status Reader";
            try
            {
                resp = (HttpWebResponse)webRequest.GetResponse();
                if (HttpStatusCode.NotFound == resp.StatusCode)
                {
                    if (type == 0)
                    {
                        // not found
                        if (type == 0)
                        {
                            MakeAllLightsYellow(i);
                            logger.WriteToLog(0, i, 0, resp.StatusCode + " " + resp.StatusDescription);
                        }
                        else
                        {
                            logger.WriteToLog(2, i, 0, resp.StatusCode + " " + resp.StatusDescription);
                        }
                        return "";
                    }
                }
                else if (HttpStatusCode.OK == resp.StatusCode)
                {
                    Stream ReceiveStream = resp.GetResponseStream();
                    Encoding encode = System.Text.Encoding.GetEncoding("utf-8");
                    StreamReader readStream = new StreamReader(ReceiveStream, encode);
                    String str = readStream.ReadToEnd();
                    // если страница пустая, но коннект состоялся, лампочки будут жёлтыми
                    if (str.Length == 0)
                    {
                        MakeAllLightsYellow(i);
                        if (type == 0)
                        {
                            logger.WriteToLog(0, i, 0, "Пустая страница. Возможно, произошло зависание устройства");
                        }
                        else if (type == 1)
                        {
                            logger.WriteToLog(2, i, 0, "Пустая страница. Возможно, произошло зависание устройства");
                        }
                        return "";
                    }
                    return str;
                }
            }
            catch (System.ArgumentOutOfRangeException e)
            {
                MessageBox.Show("ArgumentOutOfRangeException: " + e.Message);
                return "";
            }
            catch (System.Net.WebException e)
            {
                Console.WriteLine(e.Status.ToString() + " " + e.Message);
                if (e.Status.ToString() == "RequestCanceled")
                    return "";
                if (e.Status.ToString() == "Timeout")
                    return "";
                if (type == 0)
                {
                    // status page
                    logger.WriteToLog(0, i, 0, e.Status.ToString() + " " + e.Message);
                }
                else if (type == 1)
                {
                    // decoder page
                    logger.WriteToLog(2, i, 0, e.Status.ToString() + " " + e.Message);
                }
                
            }
            catch (Exception e)
            {
                if (type == 0)
                {
                    MakeAllLightsYellow(i);
                }
                // пишем в лог
                logger.WriteToLog(4, i, 0, e.Message);
                return "";
            }
            return "";
        }

        void getSelectedInput(int i, bool writeanyway)
        {
            String str = getHtmlcode(i, ap.ap.receiverecs[i].urlinput, 1);
            if (str == "")
            {
                Console.WriteLine("Error with getting the html code of decoder page");
                ap.ap.receiverecs[i].lastactiveinput = "EmptyPage";
                return;
            }
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
                            if (writeanyway)
                            {
                                if (ap.ap.writetofile)
                                {
                                    string tmpmsg = logger.CreateLogMsg(3, i, j, pattern);
                                    logger.rawWrite(i, tmpmsg);
                                    ap.ap.receiverecs[i].lastactiveinput = ap.ap.receiverecs[i].parameters[j]; // помечаем активный вход как последний
                                    dynamicparamls[i, j].Font = new Font(dynamicparamls[i, j].Font, FontStyle.Bold | FontStyle.Underline);
                                    return;
                                }
                            }
                            if (ap.ap.receiverecs[i].lastactiveinput != ap.ap.receiverecs[i].parameters[j])
                            {
                                // изменился
                                logger.WriteToLog(3, i, j, pattern);
                            }
                            ap.ap.receiverecs[i].lastactiveinput = ap.ap.receiverecs[i].parameters[j]; // помечаем активный вход как последний
                            dynamicparamls[i, j].Font = new Font(dynamicparamls[i, j].Font, FontStyle.Bold | FontStyle.Underline);
                            break;
                        }
                    }
                }
            }
            else
            {
                // шаблон не найден
                logger.WriteToLog(2, i, 0, "Шаблон поиска активного входа не найден");
                ap.ap.receiverecs[i].lastactiveinput = "PatternError";
            }
        }
		
		// перед новым циклом считывания убрать подчеркнутый шрифт у всех входов
        void makeregulardynamiclabels()
        {
			for (int i = 0; i < ap.ap.n; i++) {
				for (int j = 0; j < ap.ap.receiverecs[i].m; j++)
				{
					dynamicparamls[i, j].Font = new Font(dynamicparamls[i, j].Font, FontStyle.Regular);
				}
			}
        }
		
		void MakeAllLightsYellow(int i)
		{
			for (int j = 0; j < ap.ap.receiverecs[i].m; j++)
			{
				setColorOfPictureBox(pictureBoxes[i, j], 2);
			}
		}

        int analyzeHtmlInputs(string html, int i, int j)
        {
            //Console.WriteLine(j.ToString() + " " + ap.ap.receiverecs[i].m.ToString());
            string pattern = ap.ap.receiverecs[i].regexps[j].ToString();
            Regex newReg = new Regex(pattern);
            Match matches = newReg.Match(html);

            if (matches.Groups[1].Success) // шаблон найден
            {
                // параметр равен 1
                if (matches.Groups[1].Value == "1")
                {
                    //Console.WriteLine(matches.Groups[1].Value);
                    setColorOfPictureBox(pictureBoxes[i, j], 1);
                    return 1;
                }
                // параметр не равен 1 но равен другому числу
                else
                {
                    setColorOfPictureBox(pictureBoxes[i, j], 0);
                    return 0;
                }
            }
            else
            {
                // шаблон не найден
                setColorOfPictureBox(pictureBoxes[i, j], 2);
                logger.WriteToLog(0, i, j, "По шаблону для поиска параметров ничего не найдено. Возможно, он задан неверно.");
                return 2;
            }

        }
		
		void setValue(int i, bool writeanyway)
		{
			String str = getHtmlcode(i,geturlbyid(i),0);
            if (str == "")
            {
                Console.WriteLine("Error with getting the html code of parameters");
                return;
            }
            for (int j = 0; j < ap.ap.receiverecs[i].m; j++)
            {
                int currentstate = analyzeHtmlInputs(str, i, j);
				
                if (ap.ap.writetofile)
                {
                    string tmpmsg = logger.CreateLogMsg(1, i, j, intToStatus(currentstate));
                    // trim first two tokens (current datestamp) from the last message market
					string templastmessage = string.Join("\t",tmpmsg.Split(new char[] { '\t' }).Skip(2).ToArray());

                    if (writeanyway)
                    {
                        // безусловная запись
                        logger.rawWrite(i, tmpmsg + " midnight record ");
                        ap.ap.receiverecs[i].lastlogmsg[j] = templastmessage;
                        continue;
                    }
                    // если состояние хоть одного параметра изменилось - пишем
                    if (ap.ap.receiverecs[i].lastlogmsg[j] != templastmessage)
                    {
                        logger.WriteToLog(1, i, j, intToStatus(currentstate));
                    }
                    // запоминаем полученное состояние
                    ap.ap.receiverecs[i].lastlogmsg[j] = templastmessage;
                }
            }
		}

        void midnightsetValues()
        {
            for (int i = 0; i < ap.ap.n; i++)
            {
                setValue(i, true);
            }
        }

        void midnightgetSelectedInputs()
        {
            makeregulardynamiclabels();
            for (int i = 0; i < ap.ap.n; i++)
            {
                getSelectedInput(i, true);
            }
        }

        void setValues()
        {
			for (int i = 0; i<ap.ap.n; i++)
			{
				setValue(i, false);
			}
        }
		
		void getSelectedInputs()
		{
            makeregulardynamiclabels();
			for (int i = 0; i<ap.ap.n; i++)
			{
				getSelectedInput(i, false);
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

        private void Form1_LocationChanged(object sender, EventArgs e)
        {
            ap.ap.previousLocationX = this.Location.X;
            ap.ap.previousLocationY = this.Location.Y;
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Control && e.KeyCode == Keys.C))
            {
                MessageBox.Show("timer1 is " + timer1.Enabled.ToString() + "\r\n" + "timer1 inteval = " + timer1.Interval.ToString() + "\n"
                    + "timermidnight is " + timermidnight.Enabled.ToString() + "\r\ntimermidnight interval = " + timermidnight.Interval.ToString() + " (" + (timermidnight.Interval / 1000 / 3600) + ")" + "\n" + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "\n"
                    + "ap.ap.receiverecs[0].lastlogmsg[0] = " + ap.ap.receiverecs[0].lastlogmsg[0]
                    );
            }
        }
    }

    public class LogManager
    {
        string filepath;
        settings apobj;
        int currentIndex;
        IDictionary<int, int> connectionfailcnt;
        System.Collections.Generic.Dictionary<int,string>[] lastlogmsg; // буфер для хранения последних сообщений
        System.Collections.Generic.Dictionary<int, string>[] lastlogdecodermsg; // буфер для хранения последних сообщений декодера
        StreamWriter[] logwriters;
        public int oldsize;
        public LogManager(settings ap)
        {
            setap(ap);
            filepath = ap.ap.mainlogpath;
            oldsize = ap.ap.n;
            currentIndex = -1;
            if (!Directory.Exists(filepath))
                Directory.CreateDirectory(filepath);
            logwriters = new StreamWriter[apobj.ap.maxn];
			connectionfailcnt = new Dictionary<int, int>();
            lastlogmsg = new Dictionary<int, string>[apobj.ap.maxn];
            lastlogdecodermsg = new Dictionary<int, string>[apobj.ap.maxn];
            for (int i = 0; i < apobj.ap.n; i++)
            {
                if (!Directory.Exists(Path.GetDirectoryName(getLogPath(i))))
                    Directory.CreateDirectory(Path.GetDirectoryName(getLogPath(i)));
                logwriters[i] = new StreamWriter(getLogPath(i), true, Encoding.UTF8);
				connectionfailcnt[i] = 0;
                lastlogmsg[i] = new Dictionary<int, string>();
                lastlogdecodermsg[i] = new Dictionary<int, string>();
                unsetLastLogMsgs(i, apobj);
            }
        }

        public void reopenlogfiles()
        {
            for (int i = 0; i < apobj.ap.n; i++)
            {
                logwriters[i].Close();
                unsetLastLogMsgs(i, apobj);

                connectionfailcnt[i] = 0; // сбрасываем счетчики неудачных подключений, потому что новый день - новый лог
                apobj.ap.receiverecs[i].counter = 0;

				//Console.WriteLine(getLogPath(i));
                logwriters[i] = new StreamWriter(getLogPath(i), true, Encoding.UTF8);
            }
        }

        public void setap(settings newap)
        {
            this.apobj = newap;
        }

        public bool isFileLocked(int i)
        {
            try
            {
                using (Stream strm = new FileStream(getLogPath(i),FileMode.Open))
                {
                    return false;
                }
            }
            catch {
                return true;
            }
        }
		
        // type - тип счетчика: 0 - общий, 1 - декодера
		public bool incrementCnt(int type, int i)
		{
            if (type == 0)
            {
                // счетчик циклический, меняется в диапазоне от 0 до ap.ap.logconnectlimit - 1
                apobj.ap.receiverecs[i].counter = (apobj.ap.receiverecs[i].counter + 1) % apobj.ap.logconnectlimit;
                // Console.WriteLine(i.ToString() + " trigger " + apobj.ap.receiverecs[i].counter.ToString());

                if (apobj.ap.receiverecs[i].counter >= apobj.ap.logconnectlimit - 1)
                {
                    return true;
                }
                return false;
            }
            else
            {
                // счетчик декодера
                connectionfailcnt[i] = (connectionfailcnt[i] + 1) % apobj.ap.logconnectlimit;

                if (connectionfailcnt[i] >= apobj.ap.logconnectlimit - 1)
                {
                    return true;
                }
                return false;
            }
		}

        private void unsetLastLogMsgs(int i, settings ap)
        {
            for (int j = 0; j < ap.ap.receiverecs[i].m; j++)
            {
                if (lastlogmsg[i].ContainsKey(j))
                    lastlogmsg[i][j] = "";
                else
                    lastlogmsg[i].Add(j, "");
                if (lastlogdecodermsg[i].ContainsKey(j))
                    lastlogdecodermsg[i][j] = "";
                else
                    lastlogdecodermsg[i].Add(j, "");
            }
        }

        public void ResizeLoggers(int newsize)
        {
            if (newsize > oldsize)
            {
                for (int i = 0; i < newsize; i++)
                {
                    if (logwriters.ElementAt(i) == null)
                    {
                        // new devices
                        logwriters[i] = new StreamWriter(getLogPath(i), true, Encoding.UTF8);
                        lastlogmsg[i] = new Dictionary<int, string>();
                        lastlogdecodermsg[i] = new Dictionary<int, string>();
                        unsetLastLogMsgs(i, apobj);
                    }
                    else
                    {
                        string curpath = ((FileStream)(logwriters[i].BaseStream)).Name;
                        if (curpath.IndexOf(Path.GetFileName(getLogPath(i))) != -1)
                        {
                            // this file is already in use
                            continue;
                        }
                        else
                        {
                            if (isFileLocked(i))
                            {
                                logwriters[i].Close();
                                logwriters[i] = new StreamWriter(getLogPath(i), true, Encoding.UTF8);
                                unsetLastLogMsgs(i, apobj);
                            }
                        }
                    }
                }
            }
            else
            {
                // число устройств уменьшилось - просто проверяем все файлы - изменились ли имена
                for (int i = 0; i < newsize; i++)
                {

                    string curpath = ((FileStream)(logwriters[i].BaseStream)).Name;
                    if (curpath.IndexOf(Path.GetFileName(getLogPath(i))) == -1)
                    {
                        // filename has been changed
                        logwriters[i].Close();
                        logwriters[i] = new StreamWriter(getLogPath(i), true, Encoding.UTF8);
                        unsetLastLogMsgs(i, apobj);
                    }
                    else
                    {
                    }
                }
            }
        }

        // проверка на дублирующее сообщение
        public bool isNewLastMsg(int i, int j, string currentmsg)
        {
            if (lastlogmsg[i][j] == "" && lastlogdecodermsg[i][j] == "")
                return true;
			//Console.WriteLine("dupl msg check: " + lastlogmsg[i][j] + " " + currentmsg);
            if (lastlogmsg[i][j].IndexOf(currentmsg) != -1 || lastlogdecodermsg[i][j].IndexOf(currentmsg) != -1)
            {
                    return false;
            }
            return true;
        }

        public void SetLastMsg(int type, int i, int j, string currentmsg)
        {
            if (type == 0 || type == 1 || type == 4)
                lastlogmsg[i][j] = currentmsg;
            else
                lastlogdecodermsg[i][j] = currentmsg;
        }


        // generate new message to write to the log file
        public string CreateLogMsg(int type, int i, int j, string msg)
        {
            // connection main log
            if (type == 0)
            {
                string tofile = apobj.ap.patternLogConFail.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%paramname%", apobj.ap.receiverecs[i].parameters[j]).Replace("%url%", apobj.ap.receiverecs[i].url).Replace("%regexp%", apobj.ap.receiverecs[i].regexps[j]).Replace("%msg%", msg).Replace("\\t", "\t");
                return tofile;
            }
            // normal log
            if (type == 1)
            {
                // check last message
                string tofile = apobj.ap.patternLogOK.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%paramname%", apobj.ap.receiverecs[i].parameters[j]).Replace("%url%", apobj.ap.receiverecs[i].url).Replace("%regexp%", apobj.ap.receiverecs[i].regexps[j]).Replace("%msg%", msg).Replace("\\t","\t");
                return tofile;
            }
            // connection decoder log
            if (type == 2)
            {
                string tofile = apobj.ap.patternDecoderConFail.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%paramname%", apobj.ap.receiverecs[i].parameters[j]).Replace("%url%", apobj.ap.receiverecs[i].urlinput).Replace("%msg%", msg).Replace("\\t", "\t");
                return tofile;
            }
            // decoder normal log
            if (type == 3)
            {
                string tofile = apobj.ap.patternDecoderOK.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%paramname%", apobj.ap.receiverecs[i].parameters[j]).Replace("%url%", apobj.ap.receiverecs[i].urlinput).Replace("%msg%", msg).Replace("\\t", "\t");
                return tofile;
            }
            
            // general connection fail
            if (type == 4)
            {
                string tofile = apobj.ap.patternGeneralConFail.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%url%", apobj.ap.receiverecs[i].url).Replace("%msg%", msg);
                return tofile;
            }

            return "";
        }

        public void WriteToLog(int type, int i, int j, string msg)
        {
            //MessageBox.Show(i.ToString() + " " + j.ToString());
            if (!apobj.ap.writetofile)
                return;

            int ttype = -1; 
            if (type == 0)
            {
                ttype = 0; // тип ошибки соединения - основная страница
            }
			if (type == 2) {
                ttype = 1; // тип ошибки соединения - страница декодера
            }
            if (ttype != -1)
            {
                if (!incrementCnt(ttype, i))
                    return;
            }
            string tofile = CreateLogMsg(type, i, j, msg);
            if (!isNewLastMsg(i,j,msg))
                return;
            SetLastMsg(type, i, j, tofile);
            rawWrite(i, tofile);
        }

        public void rawWrite(int i, string msg)
        {
            if (msg == "")
                return;
            try
            {
                logwriters[i].WriteLine(msg.ToArray(),0,msg.Length);
                logwriters[i].AutoFlush = true;
                //logwriters[i].Flush();
            }
            catch (System.IO.IOException e)
            {
                MessageBox.Show(e.Message);
            }
            catch (System.UnauthorizedAccessException e)
            {
                MessageBox.Show(e.Message);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
            //finally
            //{
            //    locker.ReleaseWriterLock();
            //}
        }

        public void setLogPath()
        {
            filepath = getLogPath(currentIndex);
        }
        public string getLogPath(int index)
        {
            DateTime nw = DateTime.Now;
            string fpath = apobj.ap.mainlogpath;
            string subpath = "";
            if (apobj.ap.nestedpath)
            {
                subpath = nw.ToString("yyyy") + "\\" + nw.ToString("MM") + "\\" + nw.ToString("dd");
            }
            if (fpath.EndsWith("\\"))
            {
                fpath = fpath + subpath;
            }
            else
            {
                fpath = fpath + "\\" + subpath;
            }
            if (!Directory.Exists(fpath))
                Directory.CreateDirectory(fpath);
            // thread safety
            // locker.AcquireWriterLock(int.MaxValue);
            fpath = fpath + "\\" + apobj.ap.receiverecs[index].name + "_" + nw.ToString("yyyy-MM-dd") + ".log";
            return fpath;
        }

        public string getTimeStamp()
        {
            DateTime nw = DateTime.Now;
            return nw.ToString("yyyy/MM/dd") + "\t" + nw.ToString("HH:mm:ss");
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
        public List<string> lastlogmsg; // последние записанные в лог сообщения, по 1 на параметр
        [XmlIgnore]
        public string lastactiveinput; // предыдущий активный вход
        [XmlIgnore]
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
            lastactiveinput = ""; // предыдущий активный вход
            lastlogmsg = new List<string>(new string[m]);
        }

        public ReceiverRecord(int inm)
        {
            m = inm;
            counter = 0;
            parameters = new List<string>(new string[m]);
            regexps = new List<string>(new string[m]);
            lastactiveinput = ""; // предыдущий активный вход
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
                lastlogmsg.RemoveRange(newm, countr - newm);
            }
            else if (newm > countr)
            {
                if (newm > parameters.Capacity)
                {
                    parameters.AddRange(new List<string>(new string[newm - countr]));
                    regexps.AddRange(new List<string>(new string[newm - countr]));
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
            public int maxn = 10; // максимальное число ресиверов
            public string path = "MySettings.xml"; // main conf file
            public uint period;
            public bool writetofile;
            public bool contype; // true = http, false = snmp
            public uint formwidth;
            public uint formheight;
            public int previousLocationX;
            public int previousLocationY;
            public string mainlogpath;
            public bool nestedpath;
            public string patternLogOK;
            public string patternLogConFail;
            public string patternDecoderOK;
            public string patternDecoderConFail;
            public string patternGeneralConFail;
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
        Button setlogpatterns;
        Label inform;
		Label lconnfaillimit;
		NumericUpDown tconnfaillimit;
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
            ap.previousLocationX = 100;
            ap.previousLocationY = 100;
            ap.mainlogpath = ".\\LOG";
			// шаблон, используемый для записи в лог при изменении состояния входов - ON, OFF
			// общий вид - 2017.02.23	08:58:09    PTN INPUT   IP	ON  http://192.168.4.232/cgi-bin/input_status.cgi   <ip value="\d">\W+<lock value="(\d+?)">
            ap.patternLogOK = "%timestamp%\t%devicename%\tINPUT\t%paramname%\t%msg%\t%url%\t%regexp%";
			// шаблон, по которому в лог пишутся любые ошибки и проблемы с подключением и соединением
			// общий вид - 2017.02.23	08:58:38    PTN INPUT   ASI3                ERROR   http://        <asi3>
            ap.patternLogConFail = "%timestamp%\t%devicename%\tINPUT\t%paramname%\tERROR\t%url%\t%regexp%\t%msg%";
			// шаблон, по которому в лог пишется текущий активный вход, или его изменение
			// общий вид - 2017.02.23        11:53:04        TNT        DECODER        IP                SET                http://        selected>
			ap.patternDecoderOK = "%timestamp%\t%devicename%\tDECODER\t%paramname%\tSET\t%url%\t%msg%";
			// шаблон, по которому в лог пишется сообщение об ошибках при попытке получить активный вход
			// 2017.02.23        11:53:04        TNT        DECODER        URL                ERROR        http://        не могу найти страницу
			ap.patternDecoderConFail = "%timestamp%\t%devicename%\tDECODER\tURL\tERROR\t%url%\t%msg%";
			// шаблон, по которому в лог пишется сообщение об ошибках соединения безотносительно параметра
			// общий вид - 2017.02.23        11:53:04        TNT        CONNECTION  ERROR   http:// msg
            ap.patternGeneralConFail = "%timestamp%\t%devicename%\tCONNECTION\tERROR\t%url%\t%msg%";
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
                formobj.isParamsForm = false;
                setparamsform.Close();
            };
            okbtn.Location = new Point(textboxparams.Location.X + 10, textboxparams.Location.Y + textboxparams.Height + 20);
            Button cancelbtn = new Button();
            cancelbtn.Click += (s, e) => {
                formobj.isParamsForm = false;
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
            if (formobj.isParamsForm)
                return;
            formobj.isParamsForm = true;
            SetParameters(Int32.Parse(cur.Name),true);
        }

        void regexps_Click(object sender, EventArgs e)
        {
            var cur = sender as Button;
            if (formobj.isParamsForm)
                return;
            formobj.isParamsForm = true;
            SetParameters(Int32.Parse(cur.Name), false);
        }

        void setlogpatterns_Click(object sender, EventArgs e)
        {
            if (formobj.isFormatLogForm)
                return;
            formobj.isFormatLogForm = true;
            // draw a small form that allows us to set new log formats
            Form setlogformat = new Form();
            setlogformat.Text = "Формат логов";
            setlogformat.Size = new Size(900, 300);
            setlogformat.TopMost = true;

            Label hint = new Label();
            hint.Width = 890;
            hint.Height = 50;
            hint.Font = new Font(hint.Font, FontStyle.Bold);

            hint.Text = "Переменные подстановки:\r\n%timestamp% - дата и время, %devicename% - название устройства, %paramname% - имя параметра (не для всех логов),\r\n %url% - адрес для подключения, %msg% - подробности или системное сообщение, %regexp% - шаблон для поиска данных (не для всех логов)";
            hint.Location = new Point(setlogformat.Location.X, setlogformat.Location.Y + 10);
            setlogformat.Controls.Add(hint);
            Label pattern1cap = new Label();
            pattern1cap.Text = "Изменение состояния входов";
            pattern1cap.Width = 270;
            pattern1cap.Location = new Point(setlogformat.Location.X, setlogformat.Location.Y + 60);
            setlogformat.Controls.Add(pattern1cap);
            TextBox pattern1box = new TextBox();
            pattern1box.Width = 600;
            pattern1box.Location = new Point(pattern1cap.Location.X + 280, pattern1cap.Location.Y);
            pattern1box.Text = ap.patternLogOK;
            setlogformat.Controls.Add(pattern1box);

            Label pattern2cap = new Label();
            pattern2cap.Text = "Ошибки и проблемы с получением данных о входах";
            pattern2cap.Width = 270;
            pattern2cap.Location = new Point(pattern1cap.Location.X, pattern1cap.Location.Y + 30);
            setlogformat.Controls.Add(pattern2cap);
            TextBox pattern2box = new TextBox();
            pattern2box.Width = 600;
            pattern2box.Text = ap.patternLogConFail;
            pattern2box.Location = new Point(pattern2cap.Location.X + 280, pattern2cap.Location.Y);
            setlogformat.Controls.Add(pattern2box);

            Label pattern3cap = new Label();
            pattern3cap.Text = "Изменение активного входа";
            pattern3cap.Width = 270;
            pattern3cap.Location = new Point(pattern2cap.Location.X, pattern2cap.Location.Y + 30);
            setlogformat.Controls.Add(pattern3cap);
            TextBox pattern3box = new TextBox();
            pattern3box.Width = 600;
            pattern3box.Location = new Point(pattern3cap.Location.X + 280, pattern3cap.Location.Y);
            pattern3box.Text = ap.patternDecoderOK;
            setlogformat.Controls.Add(pattern3box);
            Label pattern4cap = new Label();
            pattern4cap.Text = "Ошибки и проблемы с подключением к странице декодера";
            pattern4cap.Width = 270;
            pattern4cap.Location = new Point(pattern3cap.Location.X, pattern3cap.Location.Y + 30);
            setlogformat.Controls.Add(pattern4cap);
            TextBox pattern4box = new TextBox();
            pattern4box.Width = 600;
            pattern4box.Text = ap.patternDecoderConFail;
            pattern4box.Location = new Point(pattern4cap.Location.X + 280, pattern4cap.Location.Y);
            setlogformat.Controls.Add(pattern4box);
            Label pattern5cap = new Label();
            pattern5cap.Text = "Другие ошибки";
            pattern5cap.Width = 270;
            pattern5cap.Location = new Point(pattern4cap.Location.X, pattern4cap.Location.Y + 30);
            setlogformat.Controls.Add(pattern5cap);
            TextBox pattern5box = new TextBox();
            pattern5box.Width = 600;
            pattern5box.Text = ap.patternGeneralConFail;
            pattern5box.Location = new Point(pattern5cap.Location.X + 280, pattern5cap.Location.Y);
            setlogformat.Controls.Add(pattern5box);

            Button okbtn = new Button();
            okbtn.Text = "OK";
            okbtn.Location = new Point(pattern5cap.Location.X, pattern5cap.Location.Y + 30);
            okbtn.Click += (s, ee) =>
                {
                    // replace our patterns
                    formobj.oldpatternLogOK = pattern1box.Text;
                    formobj.oldpatternLogConFail = pattern2box.Text;
                    formobj.oldpatternDecoderOK = pattern3box.Text;
                    formobj.oldpatternDecoderConFail = pattern4box.Text;
                    formobj.oldpatternGeneralConFail = pattern5box.Text;
                    formobj.isFormatLogForm = false;
                    setlogformat.Close();
                };
            Button cancelbtn = new Button();
            cancelbtn.Text = "Cancel";
            cancelbtn.Location = new Point(okbtn.Location.X + 80, okbtn.Location.Y);
            cancelbtn.Click += (s, ee) =>
                {
                    formobj.isFormatLogForm = false;
                    setlogformat.Close();
                };
            setlogformat.Controls.Add(okbtn);
            setlogformat.Controls.Add(cancelbtn);
            setlogformat.Show();
        }

        void updown_Changed(object sender, EventArgs e)
        {
            
            int newvalue = (int)receivescnt.Value;
            int oldvalue = ap.n;
            formobj.logger.oldsize = oldvalue;
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
					lconnfaillimit.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 50);
					tconnfaillimit.Location = new Point(tconnfaillimit.Location.X, lconnfaillimit.Location.Y);
                    inform.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 70);
                    tperiod.Location = new Point(tperiod.Location.X, inform.Location.Y + tempstep);
                    okbutton.Location = new Point(okbutton.Location.X, tperiod.Location.Y + tempstep);
                    cancelbutton.Location = new Point(cancelbutton.Location.X, okbutton.Location.Y);
                    captionlogdir.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 25);
                    logmaindir.Location = new Point(logmaindir.Location.X, captionlogdir.Location.Y);
                    tnestedpath.Location = new Point(tnestedpath.Location.X, logmaindir.Location.Y);
                    setlogpatterns.Location = new Point(setlogpatterns.Location.X, tnestedpath.Location.Y);

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
				lconnfaillimit.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 50);
				tconnfaillimit.Location = new Point(tconnfaillimit.Location.X, lconnfaillimit.Location.Y);
                inform.Location = new Point(inform.Location.X, twritetofile.Location.Y + 70);
                tperiod.Location = new Point(tperiod.Location.X, inform.Location.Y + tempstep);
                okbutton.Location = new Point(okbutton.Location.X, tperiod.Location.Y + tempstep);
                cancelbutton.Location = new Point(cancelbutton.Location.X, okbutton.Location.Y);
                captionlogdir.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 25);
                logmaindir.Location = new Point(captionlogdir.Location.X + 130, captionlogdir.Location.Y);
                tnestedpath.Location = new Point(tnestedpath.Location.X, logmaindir.Location.Y);
                setlogpatterns.Location = new Point(setlogpatterns.Location.X, tnestedpath.Location.Y);
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
			lconnfaillimit = new Label(); // лейбл порога записи лога при перебоях сети
			tconnfaillimit = new NumericUpDown(); // порог для записи лога при неудачном соединении
			tconnfaillimit.Minimum = 1;
			tconnfaillimit.Maximum = 99999;
            tperiod = new NumericUpDown(); // периодичность
            tperiod.Minimum = 1;
            tperiod.Maximum = 99999;
            settingsform.Text = "Program Settings";
            
            settingsform.Size = new System.Drawing.Size(990, 620);
            
            okbutton = new Button();
            okbutton.Text = "OK";
            okbutton.Click += new EventHandler(okbutton_Click);
            okbutton.Location = new Point(turls[ap.n-1].Location.X - 40, turls[ap.n-1].Location.Y + 130);
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

                    formobj.logger.oldsize = ap.n;
                    // таймер запускается снова
                    formobj.timer1.Start();
                };
            cancelbutton.Location = new Point(okbutton.Location.X + 80, okbutton.Location.Y);
            settingsform.Controls.Add(cancelbutton);

            twritetofile.Size = new Size(200, 20);
            twritetofile.Text = "Ведение логов";
            twritetofile.Location = new Point(tnames[ap.n-1].Location.X, tnames[ap.n-1].Location.Y + 20);
            settingsform.Controls.Add(twritetofile);
            inform.Size = new Size(300, 20);
            inform.Text = "Периодичность опроса ресивера, сек.";
            inform.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 70);
			
			lconnfaillimit.Size = new Size(350,20);
			lconnfaillimit.Text = "При проблемах с соединением писать в лог после X попыток";
			lconnfaillimit.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 50);
			tconnfaillimit.Size = new Size(40,20);
			tconnfaillimit.Location = new Point(lconnfaillimit.Location.X + 350, lconnfaillimit.Location.Y);
            settingsform.Controls.Add(inform);
			settingsform.Controls.Add(lconnfaillimit);
			settingsform.Controls.Add(tconnfaillimit);

            captionlogdir = new Label();
            captionlogdir.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 25);
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

            setlogpatterns = new Button();
            setlogpatterns.Text = "Форматы логов";
            setlogpatterns.Width = 140;
            setlogpatterns.Location = new Point(tnestedpath.Location.X + 210, tnestedpath.Location.Y);
            setlogpatterns.Click += new EventHandler(setlogpatterns_Click);
            settingsform.Controls.Add(setlogpatterns);

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

        // restore previous lastactiveinput fields
        public void RestoreOldLastStatusFields(setstruct oldap, setstruct newap)
        {
            for (int i = 0; i < newap.n; i++)
            {
                for (int j = 0; j < newap.receiverecs[i].m; j++)
                {
                    newap.receiverecs[i].lastactiveinput = oldap.receiverecs[i].lastactiveinput;
                }
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
                if (!formobj.appstarted)
                {
                    setstruct oldap = ap; // сделаем копию объекта, чтобы сохранить состояния последнего считывания
                    RestoreOldLastStatusFields(oldap, ap);
                }
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
            tconnfaillimit.Value = ap.logconnectlimit;
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
            ap.logconnectlimit = Convert.ToInt32(tconnfaillimit.Value);
            ap.mainlogpath = logmaindir.Text;
            ap.nestedpath = tnestedpath.Checked;
            ap.formwidth = Convert.ToUInt32(width.Value);
            ap.previousLocationX = formobj.Location.X;
            ap.previousLocationY = formobj.Location.Y;
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

            // применяем настройки формата логов к структуре
            // replace our patterns
            ap.patternLogOK = formobj.oldpatternLogOK;
            ap.patternLogConFail = formobj.oldpatternLogConFail;
            ap.patternDecoderOK = formobj.oldpatternDecoderOK;
            ap.patternDecoderConFail = formobj.oldpatternDecoderConFail;
            ap.patternGeneralConFail = formobj.oldpatternGeneralConFail;

            formobj.isSettingsForm = false; // форма настроек закрыта
            ap.n = tempn;

            ApplySettingsToStruct();
            WriteSettings();
            settingsform.Close();
			
			// logger update
            // обновляем данные для логгера
            formobj.logger.setap(this);
            formobj.logger.ResizeLoggers(ap.n);

            // timer 
            formobj.timer1.Interval = 1000*(int)ap.period;
            // сам таймер запустится дальше внутри функции DrawMainForm
            formobj.RedrawForm();
        }
    }
}
