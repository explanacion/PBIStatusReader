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
using SnmpSharpNet;
using System.Web;
using System.Runtime.Serialization.Formatters.Binary;

namespace PBIStatusReader
{
    public partial class Form1 : Form
    {
        int n = 15; // число ресиверов
        public settings ap;
        public System.Timers.Timer timer1; // для режима web
        public System.Timers.Timer timer2; // для режима snmp
        public System.Timers.Timer timermidnight;
        public bool appstarted; // флаг-индикатор того, что программа запущена, но данные ещё не считывались
        
        Label[] recvnamelabels;
        PictureBox[,] pictureBoxes = new PictureBox[15,5];
        //static HttpWebRequest webRequest;
        public Label[,] dynamicparamls;
        public LogManager logger;
        public bool isSettingsForm; // форма настроек создана и активна
        public bool isParamsForm; // форма настроек имен параметров или их шаблонов открыта и активна

        public bool isFormatLogForm; // форма настроек формата логов создана и активна

        public settings oldap;  // копия настроек 
		
		// мьютекс для файлов
		static ReaderWriterLock locker = new ReaderWriterLock();
		static ReaderWriterLock conlocker = new ReaderWriterLock();


        // временные переменные отслеживающие количество устройств при их изменении через форму настроек (нужны для возможной отмены настроек)
        public int tempoldn = 0;
        public int tempnewn = 0;
        
        public Form1()
        {
            ap = new settings(this); // инициализация настроек приложения
            oldap = new settings(this);
            InitializeComponent();
            bool ifread = ap.ReadSettings(); // считываем записанные ранее настройки
            oldap = (settings)ap.Clone();
            isSettingsForm = false;
            isParamsForm = false;
            isFormatLogForm = false;
            timer1 = new System.Timers.Timer();
            timer2 = new System.Timers.Timer();
            timer1.Elapsed += new System.Timers.ElapsedEventHandler(timer1_Tick);
            timer2.Elapsed += new System.Timers.ElapsedEventHandler(timer2_Tick);
            timermidnight = new System.Timers.Timer();
			timermidnight.AutoReset = false;
            timermidnight.Elapsed += (sender, args) =>
            {
                MidNightScan();
            };
            SetTimerMidnight();
            GlobalVars.firstscan = true;
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
                    ap.ap.receiverecs[i].matchvalues.RemoveAll(item => item == null);
                    ap.ap.receiverecs[i].snmpaddrs.RemoveAll(item => item == null);
                    ap.ap.receiverecs[i].snmpid.RemoveAll(item => item == null);
                    ap.ap.receiverecs[i].paramsaliases.RemoveAll(item => item == null);

                    oldap.ap.receiverecs[i].parameters.RemoveAll(item => item == null);
                    oldap.ap.receiverecs[i].regexps.RemoveAll(item => item == null);
                    oldap.ap.receiverecs[i].matchvalues.RemoveAll(item => item == null);
                    oldap.ap.receiverecs[i].snmpaddrs.RemoveAll(item => item == null);
                    oldap.ap.receiverecs[i].snmpid.RemoveAll(item => item == null);
                    oldap.ap.receiverecs[i].paramsaliases.RemoveAll(item => item == null);
                }
            }

            if (ap.ap.patternLogOK == null) 
                ap.ap.patternLogOK = oldap.ap.patternLogOK;
            if (ap.ap.patternLogConFail == null)
                ap.ap.patternLogConFail = oldap.ap.patternLogConFail;
            if (ap.ap.patternDecoderConFail == null)
                ap.ap.patternDecoderConFail = oldap.ap.patternDecoderConFail;
            if (ap.ap.patternGeneralConFail == null)
                ap.ap.patternGeneralConFail = oldap.ap.patternGeneralConFail;
            if (ap.ap.patternPaused == null)
                ap.ap.patternPaused = oldap.ap.patternPaused;
            if (ap.ap.patternResumed == null)
                ap.ap.patternResumed = oldap.ap.patternResumed;
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
            timer2.Stop();
			logger.reopenlogfiles();

            Task.Factory.StartNew(() => midnightsetValues()).ContinueWith(t => midnightgetSelectedInputs());
            timer1.Interval = 1000 * (int)ap.ap.period;
            timer2.Interval = 1000 * (int)ap.ap.periodsnmp;
            Thread.Sleep(5000);
            timer1.Start();
            timer2.Start();
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
            recvnamelabels = new Label[15];
            this.Size = new Size((int)ap.ap.formwidth, (int)ap.ap.formheight);
            // проверяем, не выходит ли расположение формы за пределы экрана
            Rectangle resolution = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            int maxX = resolution.Width - this.Size.Width;
            int maxY = resolution.Height - this.Size.Height;
            if (ap.ap.previousLocationX < 0)
                ap.ap.previousLocationX = 0;
            if (ap.ap.previousLocationX > maxX)
                ap.ap.previousLocationX = maxX;
            if (ap.ap.previousLocationY > maxY)
                ap.ap.previousLocationY = maxY;
            if (ap.ap.previousLocationY < 0)
                ap.ap.previousLocationY = 0;
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
                    //recvnamelabels[i].Hide();
                }
            }

            // fit form according to the labels
            int maxwidth = 0;
            for (int i = 0; i < ap.ap.n; i++)
            {
                if (recvnamelabels[i].Width > maxwidth)
                    maxwidth = recvnamelabels[i].Width;
            }

            dynamicparamls = new Label[15, 5];
            for (int j = 0; j < 5; j++)
            {
                dynamicparamls[0, j] = new Label();
                dynamicparamls[0, j].Font = new Font("Microsoft Sans Serif", 12);
                dynamicparamls[0, j].TextAlign = ContentAlignment.TopLeft;
                dynamicparamls[0, j].Width = 60;
            }
            // выстраиваем первый ряд
            dynamicparamls[0, 0].Location = new Point(maxwidth + 50, 9);
            dynamicparamls[0, 1].Location = new Point(maxwidth + 50 + 66, 9);
            dynamicparamls[0, 2].Location = new Point(maxwidth + 50 + 66 + 79, 9);
            dynamicparamls[0, 3].Location = new Point(maxwidth + 50 + 66 + 79 + 82, 9);
            dynamicparamls[0, 4].Location = new Point(maxwidth + 50 + 66 + 79 + 82 + 88, 9);

            for (int j = 0; j < ap.ap.receiverecs[0].m; j++)
            {
                dynamicparamls[0, j].Text = ap.ap.receiverecs[0].parameters[j];
                if (ap.ap.receiverecs[0].isActive)
                {
                    this.Controls.Add(dynamicparamls[0, j]);
                }
            }

            int tempstep = 70;

            for (int i = 0; i < ap.ap.n; i++)
            {
                // выстраиваем лейблы для названий ресиверов
                if (i == 0)
                {
                    if (ap.ap.receiverecs[i].isActive)
                    {
                        recvnamelabels[i].Location = new Point(12, 50);
                    }
                    else
                    {
                        recvnamelabels[i].Location = new Point(12, 50 - tempstep);
                    }
                }
                else
                {
                    if (ap.ap.receiverecs[i].isActive)
                    {
                        recvnamelabels[i].Location = new Point(12, recvnamelabels[i - 1].Location.Y + tempstep);
                    }
                    else
                    {
                        // если устройство отключено в настройках - на форме его не отображаем (скрываем)
                        recvnamelabels[i].Location = new Point(12, recvnamelabels[i - 1].Location.Y);
                        continue;
                    }
                }
                if (!ap.ap.receiverecs[i].isActive)
                    continue;
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
                            dynamicparamls[i, j].Location = new Point(dynamicparamls[0, j].Location.X, dynamicparamls[0, j].Location.Y + tempstep * i);
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
            timer2.Interval = 1000 * Convert.ToInt32(ap.ap.periodsnmp);
            timer1.AutoReset = true;
            timer1.Enabled = true;
            timer2.AutoReset = true;
            timer2.Enabled = true;
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
        
        void globalScanSNMP()
        {
            // если форма настроек открыта
            if (isSettingsForm)
                return;
            Task.Factory.StartNew(() => setValuesSNMP()).ContinueWith(t => getSelectedInputsSNMP());
        }

        void timer1_Tick(object sender, EventArgs e)
        {
            if (appstarted)
                appstarted = false;
            timer1.Stop();
            globalScan();
            timer1.Start();
        }
        
        // snmp mode timer
        void timer2_Tick(object sender, EventArgs e)
        {
            if (appstarted)
                appstarted = false;
            timer2.Stop();
            globalScanSNMP();
            timer2.Start();
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

        void setLastMsgs(int i, string msg)
        {
            for (int j = 0; j < ap.ap.receiverecs[i].m; j++)
            {
                ap.ap.receiverecs[i].lastlogmsg[j] = msg;
            }
        }

        int getTypeLogStatus(int type)
        {
            if (type == 0)
                return 0;
            else if (type == 1)
                return 2;
            return -1;
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
                    // не сбрасываем статус до 3 попыток, чтобы соответствовало записи в логе
					//ap.ap.receiverecs[i].lastactiveinput = "NoConnect";
                    logger.WriteToLog(0, i, 0, e.Message);
                }
                else if (type == 1)
                {
                    // decoder page
                    // ap.ap.receiverecs[i].lastactiveinput = "NoConnect";
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
                        logger.WriteToLog(getTypeLogStatus(type), i, 0, "Empty page. Perhaps the device is frozen up");
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
                {

                    logger.WriteToLog(getTypeLogStatus(type), i, 0, e.Message);
                    return "";
                }
                if (type == 0)
                {
                    // status page
                    logger.WriteToLog(0, i, 0, e.Message);
                }
                else if (type == 1)
                {
                    // decoder page
                    logger.WriteToLog(2, i, 0, e.Status.ToString() + " " + e.Message);
                }
                
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Source.ToString() + " " + e.Message);
                if (type == 0)
                {
                    MakeAllLightsYellow(i);
                    //setLastMsgs(i, "Exception");
                }
                // пишем в лог
                logger.WriteToLog(4, i, 0, e.Message);
                return "";
            }
            return "";
        }

        // snmp mode
        void getSelectedInputSnmp(int i, bool writeanyway)
        {
            Console.WriteLine("getSelectedInputSnmp " + i.ToString());
            if (!ap.ap.receiverecs[i].isActive)
                return;
            string snmpValue = getValueSNMP(i, ap.ap.receiverecs[i].snmpinputsaddrs);
            int snmpIndex = ap.ap.receiverecs[i].snmpid.IndexOf(snmpValue);
            if (snmpIndex == -1)
            {
                //logger.WriteToLog(7, i, 0, "SnmpGetInputError");
                // ничего не пишем, т.к. ошибка уже пишется внутри функции getValueSNMP
                //logger.WriteToLog(3, i, j, "OFF");
                //ap.ap.receiverecs[i].lastactiveinput = "SnmpGetInputError";
                Console.WriteLine("значение не равно ожидаемому");
                return;
            }
            string activeInput = ap.ap.receiverecs[i].parameters[snmpIndex];
            if (activeInput == "")
            {
                Console.WriteLine("Error with getting active input via snmp");
                logger.WriteToLog(2, i, 0, "Couldn't get active input via snmp");
                //ap.ap.receiverecs[i].lastactiveinput = "SnmpGetInputError";
                return;
            }

            for (int j = 0; j < ap.ap.receiverecs[i].m; j++)
            {
                if (activeInput == ap.ap.receiverecs[i].parameters[j])
                {
                    // temp logging
                    if (ap.ap.isdebuglog)
                    {
                        try
                        {
                            File.AppendAllText(ap.ap.debuglogpath, "selected input " + activeInput + "\n");
                        }
                        catch (System.IO.FileNotFoundException ex)
                        {
                            MessageBox.Show("File not found: " + ex.FileName);
                        }
                    }
                    if (writeanyway)
                    {
                        if (ap.ap.writetofile)
                        {
                            string tmpmsg = logger.CreateLogMsg(3, i, j, "ON");
                            logger.rawWrite(i, tmpmsg);
                            ap.ap.receiverecs[i].lastactiveinput = ap.ap.receiverecs[i].parameters[j]; // помечаем активный вход как последний
                            dynamicparamls[i, j].Font = new Font(dynamicparamls[i, j].Font, FontStyle.Bold | FontStyle.Underline);
                            return;
                        }
                    }

                    if (ap.ap.receiverecs[i].lastactiveinput != ap.ap.receiverecs[i].parameters[j])
                    {
                        // изменился
                        logger.WriteToLog(3, i, j, "ON");
                    }
                    ap.ap.receiverecs[i].lastactiveinput = ap.ap.receiverecs[i].parameters[j]; // помечаем активный вход как последний
                    dynamicparamls[i, j].Font = new Font(dynamicparamls[i, j].Font, FontStyle.Bold | FontStyle.Underline);

                }
                else
                {
                    // шаблон не найден
                    //logger.WriteToLog(2, i, 0, "Шаблон поиска активного входа не найден");
                    //ap.ap.receiverecs[i].lastactiveinput = "PatternError";
                }
            }
        }

        void getSelectedInput(int i, bool writeanyway)
        {
            if (!ap.ap.receiverecs[i].isActive)
                return;
            String str = getHtmlcode(i, ap.ap.receiverecs[i].urlinput, 1);
            if (str == "")
            {
                Console.WriteLine("Error with getting the html code of decoder page");
                //ap.ap.receiverecs[i].lastactiveinput = "EmptyPage";
                return;
            }
            string pattern = ap.ap.receiverecs[i].regexpactiveinput; //@"<option value=""\d""\sselected>(.+?)\s</option>";
            Regex newReg = new Regex(pattern);
            Match matches = newReg.Match(str);
            if (matches.Groups[1].Success)
            {
                //try to make actual input bold...
                string bolditem = matches.Groups[1].Value.Replace("Input", "");
                // temp logging
                if (ap.ap.isdebuglog)
                {
                    try
                    {
                        File.AppendAllText(ap.ap.debuglogpath, "selected input " + bolditem + "\n");
                    }
                    catch (System.IO.FileNotFoundException ex)
                    {
                        MessageBox.Show("File not found: " + ex.FileName);
                    }
                }
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
                            //dynamicparamls[i, j].AutoSize = true;
                            break;
                        }
                    }
                }
            }
            else
            {
                // шаблон не найден
                logger.WriteToLog(2, i, 0, "The pattern for finding an active input not exists");
                //ap.ap.receiverecs[i].lastactiveinput = "PatternError";
            }
        }
		
		// перед новым циклом считывания убрать подчеркнутый шрифт у всех входов
        void makeregulardynamiclabels()
        {
            // если форма настроек открыта
            if (isSettingsForm)
                return;
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
                if (matches.Groups[1].Value == ap.ap.receiverecs[i].matchvalues[j])
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
                logger.WriteToLog(0, i, j, "No parameters were found using the template. Perhaps it's not correct.");
                return 2;
            }

        }

        // snmp mode
        string getValueSNMP(int i, string oid)
        {
            Console.WriteLine("getValueSNMP " + i.ToString());
            OctetString community = new OctetString("public");
            AgentParameters param = new AgentParameters(community);
            param.Version = SnmpVersion.Ver1;
            System.Uri myUri;
            bool isUrlValid = false;
            string ip = ap.ap.receiverecs[i].snmpipaddress;
            try
            {
                if (ap.ap.receiverecs[i].workmode == "Web")
                    myUri = new Uri(ap.ap.receiverecs[i].url);
                else
                    myUri = new Uri("snmp://" + ap.ap.receiverecs[i].snmpipaddress);
                isUrlValid = true;
                if (ip == "")
                    ip = myUri.Host;
                //Console.WriteLine(ip);
            }
            catch (System.UriFormatException ex)
            {
                isUrlValid = false;
            }

            if (!isUrlValid)
            {
                ip = ap.ap.receiverecs[i].url;
                // добавить желтую лампочку
                return "";
            }
            IpAddress agent;
            try
            {
                agent = new IpAddress(ip);
            }
            catch (ArgumentException ex)
            {
                // некорректный ip
                return "";
            }
            UdpTarget target = new UdpTarget((IPAddress)agent, 161, 2000, 1);
            Pdu pdu = new Pdu(PduType.Get);
            pdu.VbList.Add(oid);
            //Console.WriteLine("test line " + i.ToString() + " " + oid);
            SnmpV1Packet result = new SnmpV1Packet();
            try
            {
                result = (SnmpV1Packet)target.Request(pdu, param);
            }
            catch (SnmpSharpNet.SnmpException e)
            {
                int logId = 4;
                if (oid == ap.ap.receiverecs[i].snmpinputsaddrs)
                    logId = 2;
                logger.WriteToLog(logId, i, 0, e.Message);
                //Console.WriteLine("WARNING! EXCEPTION!!!");
                return "";
            }
            //Console.WriteLine("result = " + result);
            if (result != null)
            {
                if (result.Pdu.ErrorStatus != 0)
                {
                    Console.WriteLine("error: " + result.Pdu.ErrorStatus);
                    return "";
                }
                else
                {
                    string snmpValue = result.Pdu.VbList[0].Value.ToString();
                    return snmpValue;

                }
            }
            return "";
        }

        // получаем данные по snmp и анализируем
        void setValueSnmp(int i, bool writeanyway)
        {
            Console.WriteLine("setValueSnmp " + i.ToString());
            if (!ap.ap.receiverecs[i].isActive)
                return;
            // считываем по одному параметру в цикле
            for (int j = 0; j < ap.ap.receiverecs[i].m; j++)
            {
                string param = getValueSNMP(i, ap.ap.receiverecs[i].snmpaddrs[j]).Trim();
                //Console.WriteLine("i=" + i.ToString() + " " + "name = " + ap.ap.receiverecs[i].name);
                //Console.WriteLine("ip = " + ap.ap.receiverecs[i].snmpipaddress);
                //Console.WriteLine((param == ap.ap.receiverecs[i].matchvalues[j]).ToString());
                //Console.WriteLine("getValueSNMP = " + param + ", expected value=" + ap.ap.receiverecs[i].matchvalues[j]);
                if (param == "")
                {
                    //logger.WriteToLog(0, i, 0, "SnmpError");
                    setColorOfPictureBox(pictureBoxes[i, j], 2);
                    continue;
                }
                int currentstate = 2; // ERROR

                if (param == ap.ap.receiverecs[i].matchvalues[j])
                {
                    setColorOfPictureBox(pictureBoxes[i, j], 1);
                    // параметр совпадает с ожидаемым
                    currentstate = 1; // ON
                    if (ap.ap.writetofile)
                    {
                        string tmpmsg = logger.CreateLogMsg(1, i, j, "ON");
                        string templastmessage = string.Join("\t", tmpmsg.Split(new char[] { '\t' }).Skip(2).ToArray());
                        if (ap.ap.isdebuglog)
                        {
                            try
                            {
                                File.AppendAllText(ap.ap.debuglogpath, logger.getTimeStamp() + " getValueSNMP = " + param + ", i = " + i.ToString() + ", expected value=" + ap.ap.receiverecs[i].matchvalues[j] + "\r\n");
                                File.AppendAllText(ap.ap.debuglogpath, logger.getTimeStamp() + " name: " + ap.ap.receiverecs[i].name + " param: " + ap.ap.receiverecs[i].parameters[j] + " lastlogmsg: " + ap.ap.receiverecs[i].lastlogmsg[j] + " lastactiveinput " + ap.ap.receiverecs[i].lastactiveinput + " got snmp value: " + param + "\r\n");
                                File.AppendAllText(ap.ap.debuglogpath, templastmessage);
                                File.AppendAllText(ap.ap.debuglogpath, Environment.NewLine);
                            }
                            catch (System.IO.FileNotFoundException ex)
                            {
                                MessageBox.Show("Не найден файл " + ex.FileName);
                            }
                        }
                        if (writeanyway)
                        {
                            // безусловная запись
                            logger.rawWrite(i, tmpmsg);
                            ap.ap.receiverecs[i].lastlogmsg[j] = templastmessage;
                            continue;
                        }
                        // если состояние хоть одного параметра изменилось - пишем
                        if (ap.ap.receiverecs[i].lastlogmsg[j] != templastmessage)
                        {
                            logger.WriteToLog(1, i, j, intToStatus(currentstate));

                            // запоминаем полученное состояние
                            ap.ap.receiverecs[i].lastlogmsg[j] = templastmessage;
                        }
                    }
                }
                else
                {
                    // значение параметра не совпадает с ожидаемым значением
                    currentstate = 0;
                    setColorOfPictureBox(pictureBoxes[i, j], 0);
                    //logger.WriteToLog(1, i, j, intToStatus(currentstate));
                    if (ap.ap.writetofile)
                    {
                        string tmpmsg = logger.CreateLogMsg(1, i, j, "OFF");
                        string templastmessage = string.Join("\t", tmpmsg.Split(new char[] { '\t' }).Skip(2).ToArray());
                        if (ap.ap.isdebuglog)
                        {
                            try
                            {
                                File.AppendAllText(ap.ap.debuglogpath, logger.getTimeStamp() + " getValueSNMP = " + param + ", i = " + i.ToString() + ", expected value=" + ap.ap.receiverecs[i].matchvalues[j] + "\r\n");
                                File.AppendAllText(ap.ap.debuglogpath, logger.getTimeStamp() + " name: " + ap.ap.receiverecs[i].name + " param: " + ap.ap.receiverecs[i].parameters[j] + " lastlogmsg: " + ap.ap.receiverecs[i].lastlogmsg[j] + " lastactiveinput " + ap.ap.receiverecs[i].lastactiveinput + " got snmp value: " + param + "\r\n");
                                File.AppendAllText(ap.ap.debuglogpath, templastmessage);
                                File.AppendAllText(ap.ap.debuglogpath, Environment.NewLine);
                            }
                            catch (System.IO.FileNotFoundException ex)
                            {
                                MessageBox.Show("Не найден файл " + ex.FileName);
                            }
                        }

                        if (writeanyway)
                        {
                            // безусловная запись
                            logger.rawWrite(i, tmpmsg);
                            ap.ap.receiverecs[i].lastlogmsg[j] = templastmessage;
                            continue;
                        }
                        // если состояние хоть одного параметра изменилось - пишем
                        if (ap.ap.receiverecs[i].lastlogmsg[j] != templastmessage)
                        {
                            logger.WriteToLog(1, i, j, intToStatus(currentstate));

                            // запоминаем полученное состояние
                            ap.ap.receiverecs[i].lastlogmsg[j] = templastmessage;
                        }
                    }
                }
            }
        }

        // ----

        // web mode
        void setValue(int i, bool writeanyway)
		{
            if (!ap.ap.receiverecs[i].isActive)
                return;
			String str = getHtmlcode(i,geturlbyid(i),0);
            
            if (str == "")
            {
                // EmptyPage
                logger.WriteToLog(0, i, 0, "EmptyPage");
                // Console.WriteLine("Error with getting the html code of parameters");
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
                    // temp logging
                    if (ap.ap.isdebuglog)
                    {
                        try
                        {

                            File.AppendAllText(ap.ap.debuglogpath, logger.getTimeStamp() + " getValue (Web), status= " + intToStatus(currentstate) + " , i = " + i.ToString() + "\r\n");
                            File.AppendAllText(ap.ap.debuglogpath, logger.getTimeStamp() + " name: " + ap.ap.receiverecs[i].name + " param: " + ap.ap.receiverecs[i].parameters[j] + " lastlogmsg: " + ap.ap.receiverecs[i].lastlogmsg[j] + " lastactiveinput " + ap.ap.receiverecs[i].lastactiveinput + " curstatus: " + intToStatus(currentstate) + "\r\n");
                            File.AppendAllText(ap.ap.debuglogpath, templastmessage);
                            File.AppendAllText(ap.ap.debuglogpath, Environment.NewLine);
                        }
                        catch (System.IO.FileNotFoundException ex)
                        {
                            MessageBox.Show("Не найден файл " + ex.FileName);
                        }
                    }

					//Console.WriteLine("templastmessage = " + templastmessage);
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
						// запоминаем полученное состояние
						ap.ap.receiverecs[i].lastlogmsg[j] = templastmessage;
                    }
                }
            }
		}

        void midnightsetValues()
        {
            for (int i = 0; i < ap.ap.n; i++)
            {
                if (ap.ap.receiverecs[i].workmode == "Web")
                    setValue(i, true);
                else
                    setValueSnmp(i, true);
            }
        }

        void midnightgetSelectedInputs()
        {
            makeregulardynamiclabels();
            for (int i = 0; i < ap.ap.n; i++)
            {
                if (ap.ap.receiverecs[i].workmode == "Web")
                {
                    getSelectedInput(i, true);
                }
                else
                {
                    getSelectedInputSnmp(i, true);
                }
            }
        }

        void setValues()
        {
			for (int i = 0; i<ap.ap.n; i++)
			{
                if (ap.ap.receiverecs[i].workmode == "Web")
                    setValue(i, false);
			}
        }
        
        void setValuesSNMP()
        {
            Console.WriteLine("setValuesSNMP");
			for (int i = 0; i<ap.ap.n; i++)
			{
                if (ap.ap.receiverecs[i].workmode == "SNMP")
                    setValueSnmp(i, false);
			}
            if (GlobalVars.firstscan)
                GlobalVars.firstscan = false;
        }
        
        void getSelectedInputsSNMP()
        {
            Console.WriteLine("getSelectedInputsSNMP");
            makeregulardynamiclabels();
            for (int i = 0; i<ap.ap.n; i++)
            {
                if (ap.ap.receiverecs[i].workmode == "SNMP")
                    getSelectedInputSnmp(i, false);
            }
            if (GlobalVars.firstscan)
                GlobalVars.firstscan = false;
        }
		
		void getSelectedInputs()
		{
            makeregulardynamiclabels();
			for (int i = 0; i<ap.ap.n; i++)
			{
                if (ap.ap.receiverecs[i].workmode == "Web")
                    getSelectedInput(i, false);
			}
		}

        private void настройкиToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!isSettingsForm)
            {
                ap.CreateSettingsForm();
                // write to the log that program is paused
                for (int i = 0; i < ap.ap.n; i++)
                {
                    logger.rawWrite(i, logger.CreateLogMsg(5, i, 0, ""));
                }
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

        private void оПрограммеToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form about = new Form();
            about.Text = "О программе";
            about.Size = new System.Drawing.Size(400, 300);
            Label abText = new Label();
            abText.Width = about.Width;
            abText.Height = about.Height - 70;
            abText.Font = new System.Drawing.Font(FontFamily.GenericSerif, 14);
            abText.Text = "PBI Status Reader v2.1\n\nПрограмма для мониторинга состояния входов тюнеров PBI\nи других совместимых устройств через веб-интерфейс или snmp\n\nАвтор: Неклюдов Константин, odexed@mail.ru\nООО \"Красноярский ППЦ\" 2017-2019 гг.";
            abText.Location = about.Location;
            Button ok = new Button();
            ok.Text = "Закрыть";
            ok.Location = new Point(about.Location.X + 50, about.Location.Y + abText.Height);
            ok.Click += (s, ev) =>
                {
                    about.Close();
                };

            about.Controls.Add(abText);
            about.Controls.Add(ok);
            about.Show();
        }
    }

    public class GlobalVars
    {
        public static bool firstscan; // флаг-индикатор того, что опрос устройств произошел пока только 1 раз
    }

    public class LogManager
    {
        string filepath;
        settings apobj;
        int currentIndex;
        IDictionary<int, int> connectionfailcnt;
        System.Collections.Generic.Dictionary<int,string>[] lastlogmsg; // буфер для хранения последних сообщений
        System.Collections.Generic.Dictionary<int, string>[] lastlogdecodermsg; // буфер для хранения последних сообщений декодера (значений активного входа)
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
            connectionfailcnt = new Dictionary<int, int>(apobj.ap.maxn);
            lastlogmsg = new Dictionary<int, string>[apobj.ap.maxn];
            lastlogdecodermsg = new Dictionary<int, string>[apobj.ap.maxn];
            for (int i = 0; i < apobj.ap.n; i++)
            {
                if (i < apobj.ap.n)
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
		
        // type - тип счетчика: 0 - общий, 1 - декодера (активный вход)
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
                // счетчик декодера (активный вход)
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
                    if (i >= logwriters.Count(s => s != null))
                    {
                        // new devices
                        try
                        {
                            logwriters[i] = new StreamWriter(getLogPath(i), true, Encoding.UTF8);
                            lastlogmsg[i] = new Dictionary<int, string>();
                            lastlogdecodermsg[i] = new Dictionary<int, string>();
                            unsetLastLogMsgs(i, apobj);
                        }
                        catch (System.IO.IOException e)
                        {
                            MessageBox.Show("ResizeLoggers: " + e.Message);
                        }
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
                    if (i >= apobj.ap.n)
                        return;
                    if (i < logwriters.Count(s => s != null) && logwriters[i] != null)
                    {
                        string curpath = ((FileStream)(logwriters[i].BaseStream)).Name;
                        if (curpath.IndexOf(Path.GetFileName(getLogPath(i))) == -1)
                        {
                            // filename has been changed
                            try
                            {
                                logwriters[i].Close();
                                logwriters[i] = new StreamWriter(getLogPath(i), true, Encoding.UTF8);
                                unsetLastLogMsgs(i, apobj);
                            }
                            catch (System.IO.IOException e)
                            {
                                MessageBox.Show("ResizeLoggers: " + e.Message);
                            }
                        }
                        else
                        {
                        }
                    }
                }
            }
            oldsize = newsize;
        }

        // проверка на дублирующее сообщение
        public bool isNewLastMsg(int i, int j, string currentmsg, int type)
        {
            // если добавили новый параметр в настройках, а в массиве его не было
            if (lastlogdecodermsg[i].Count <= j)
                lastlogdecodermsg[i].Add(j, "");
            if (lastlogmsg[i].Count <= j)
                lastlogmsg[i].Add(j, "");
            if (lastlogmsg[i][j] == "" && lastlogdecodermsg[i][j] == "")
                return true;
			//Console.WriteLine("dupl msg check: " + lastlogmsg[i][j] + " " + currentmsg);
            // параметр
            if (type == 0 || type == 1 || type == 4)
            {
                if (currentmsg != "" && lastlogmsg[i][j].IndexOf(currentmsg) != -1)
                    return false;
            }
            if (type == 2 || type == 3)
            {
                if (currentmsg != "" && lastlogdecodermsg[i][j].IndexOf(currentmsg) != -1)
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
            if (i >= apobj.ap.n)
                return "";
            // connection main log
            if (type == 0)
            {
                string tofile;
                if (apobj.ap.receiverecs[i].workmode == "Web")
                    tofile = apobj.ap.patternLogConFail.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%paramname%", apobj.ap.receiverecs[i].parameters[j]).Replace("%url%", apobj.ap.receiverecs[i].url).Replace("%regexp%", apobj.ap.receiverecs[i].regexps[j]).Replace("%msg%", msg).Replace("\\t", "\t");
                else
                    tofile = apobj.ap.patternLogConFail.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%paramname%", apobj.ap.receiverecs[i].parameters[j]).Replace("%url%", apobj.ap.receiverecs[i].snmpipaddress).Replace("%regexp%", apobj.ap.receiverecs[i].snmpaddrs[j]).Replace("%msg%", msg).Replace("\\t", "\t");
                // если %regexp% пустой (не подставился), убираем его
                tofile = tofile.Replace("%regexp%", "").Replace("%paramname%","");
                return tofile;
            }
            // normal log
            if (type == 1)
            {
                string tofile;
                // check last message
                if (apobj.ap.receiverecs[i].workmode == "Web")
                    tofile = apobj.ap.patternLogOK.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%paramname%", apobj.ap.receiverecs[i].parameters[j]).Replace("%url%", apobj.ap.receiverecs[i].url).Replace("%regexp%", apobj.ap.receiverecs[i].regexps[j]).Replace("%msg%", msg).Replace("\\t","\t");
                else
                    tofile = apobj.ap.patternLogOK.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%paramname%", apobj.ap.receiverecs[i].parameters[j]).Replace("%url%", apobj.ap.receiverecs[i].snmpipaddress).Replace("%regexp%", apobj.ap.receiverecs[i].snmpaddrs[j]).Replace("%msg%", msg).Replace("\\t", "\t");
                tofile = tofile.Replace("%regexp%", "").Replace("%paramname%", "");
                return tofile;
            }
            // connection decoder log
            if (type == 2)
            {
                string tofile = "";
                if (apobj.ap.receiverecs[i].workmode == "Web")
                    tofile = apobj.ap.patternDecoderConFail.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%paramname%", apobj.ap.receiverecs[i].parameters[j]).Replace("%url%", apobj.ap.receiverecs[i].urlinput).Replace("%msg%", msg).Replace("%regexp%", apobj.ap.receiverecs[i].regexps[j]).Replace("\\t", "\t");
                else
                    tofile = apobj.ap.patternDecoderConFail.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%paramname%", apobj.ap.receiverecs[i].parameters[j]).Replace("%url%", apobj.ap.receiverecs[i].snmpipaddress).Replace("%msg%", msg).Replace("%regexp%", apobj.ap.receiverecs[i].snmpinputsaddrs).Replace("\\t", "\t");
                tofile = tofile.Replace("%regexp%", "").Replace("%paramname%", "");
                return tofile;
            }
            // decoder normal log
            if (type == 3)
            {
                string tofile = "";
                if (apobj.ap.receiverecs[i].workmode == "Web")
                    tofile = apobj.ap.patternDecoderOK.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%paramname%", apobj.ap.receiverecs[i].parameters[j]).Replace("%url%", apobj.ap.receiverecs[i].urlinput).Replace("%msg%", msg).Replace("%regexp%", apobj.ap.receiverecs[i].regexps[j]).Replace("\\t", "\t");
                else
                    tofile = apobj.ap.patternDecoderOK.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%paramname%", apobj.ap.receiverecs[i].parameters[j]).Replace("%url%", apobj.ap.receiverecs[i].snmpipaddress).Replace("%msg%", msg).Replace("%regexp%", apobj.ap.receiverecs[i].snmpinputsaddrs).Replace("\\t", "\t");
                tofile = tofile.Replace("%regexp%", "").Replace("%paramname%", "");
                return tofile;
            }
            
            // general connection fail
            if (type == 4)
            {
                string tofile = "";
                if (apobj.ap.receiverecs[i].workmode == "Web")
                    tofile = apobj.ap.patternGeneralConFail.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%url%", apobj.ap.receiverecs[i].url).Replace("%msg%", msg);
                else
                    tofile = apobj.ap.patternGeneralConFail.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%url%", apobj.ap.receiverecs[i].snmpipaddress).Replace("%msg%", msg);
                tofile = tofile.Replace("%regexp%", "").Replace("%paramname%", "");
                return tofile;
            }

            // paused
            if (type == 5)
            {
                string tofile = apobj.ap.patternPaused.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%url%", apobj.ap.receiverecs[i].url).Replace("%msg%", msg);
                return tofile;
            }
            // resumed
            if (type == 6)
            {
                string tofile = apobj.ap.patternResumed.Replace("%timestamp%", getTimeStamp()).Replace("%devicename%", apobj.ap.receiverecs[i].name).Replace("%url%", apobj.ap.receiverecs[i].url).Replace("%msg%", msg);
                return tofile;
            }
            return "";
        }

        public void WriteToLog(int type, int i, int j, string msg)
        {
            if (!isNewLastMsg(i, j, msg, type))
                return;
            string tofile = CreateLogMsg(type, i, j, msg);
            // если задан триггер - запускаем его
            if (apobj.ap.isTrigger && apobj.ap.triggerCmd != "")
            {
                if (File.Exists(apobj.ap.triggerCmd))
                {
                    if (!GlobalVars.firstscan)
                    {
                        //  не первый запрос параметров
                        Process ExternalProcess = new Process();
                        ExternalProcess.StartInfo.FileName = apobj.ap.triggerCmd;
                        ExternalProcess.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
                        ExternalProcess.StartInfo.UseShellExecute = true;
                        ExternalProcess.StartInfo.Arguments = tofile;
                        ExternalProcess.Start();
                    }
                    else
                    {
                        if (apobj.ap.isdebuglog)
                        {
                            try
                            {
                                File.AppendAllText(apobj.ap.debuglogpath, " GlobalVars.firstscan = " + GlobalVars.firstscan.ToString() + "\n");
                            }
                            catch (System.IO.FileNotFoundException ex)
                            {
                                MessageBox.Show("File not found: " + ex.FileName);
                            }
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Ошибка! В настройках задан запуск стороннего приложения при аварии, но задан некорректный путь к файлу.");
                }
            }

            //MessageBox.Show(i.ToString() + " " + j.ToString());
            if (!apobj.ap.writetofile)
                return;

            // финт ушами, если ttype изменится - значит, нужно ждать перед записью (пишем наверняка, при повторении ошибки несколько раз)
            int ttype = -1; 
            if (type == 0)
            {
                ttype = 0; // тип ошибки соединения - основная страница
            }
			if (type == 2) {
                ttype = 1; // тип ошибки соединения - страница декодера (активный вход)
            }
            if (ttype != -1)
            {
                if (!incrementCnt(ttype, i))
                    return;
            }
			
			if (type == 4)
				apobj.ap.receiverecs[i].lastactiveinput = "NoConnect";
			if (type == 2)
				apobj.ap.receiverecs[i].lastactiveinput = "NoDecoderConnect";
			if (type == 0)
				apobj.ap.receiverecs[i].lastactiveinput = "NoConnect";
            string skipped = string.Join("\t",tofile.Split(new char[] { '\t' }).Skip(2).ToArray());
            SetLastMsg(type, i, j, skipped);
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

                MessageBox.Show("rawWrite " + e.Message);
            }
            catch (System.UnauthorizedAccessException e)
            {
                MessageBox.Show(e.Message);
            }
            catch (Exception e)
            {
                MessageBox.Show("Error: " + e.Message);
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
        public string snmpipaddress;
        public string url;
        public string urlinput;
        public string login;
        public string pass;
        public string workmode; // web/snmp
        public bool isActive; // отключенное или включенное устройство

        [XmlIgnore]
        public List<string> lastlogmsg; // последние записанные в лог сообщения, по 1 на параметр
        [XmlIgnore]
        public string lastactiveinput; // предыдущий активный вход
        [XmlIgnore]
        public int counter; // счетчик перебоев с соединением

        public List<string> parameters; // имена параметров
        public List<string> regexps;
        public List<string> snmpaddrs;
        public List<string> snmpid;
        public List<string> matchvalues;
        public List<string> paramsaliases; // удобные названия параметров на русском языке
        public string regexpactiveinput;
        public string snmpinputsaddrs;

        public int m;

        public ReceiverRecord()
        {
            m = 5;
            counter = 0;
            parameters = new List<string>(new string[m]);
            regexps = new List<string>(new string[m]);
            snmpaddrs = new List<string>(new string[m]);
            matchvalues = new List<string>(new string[m]);
            paramsaliases = new List<string>(new string[m]);
            lastactiveinput = ""; // предыдущий активный вход
            lastlogmsg = new List<string>(new string[m]);
            snmpid = new List<string>(new string[m]);
        }

        public ReceiverRecord(int inm)
        {
            m = inm;
            counter = 0;
            parameters = new List<string>(new string[m]);
            regexps = new List<string>(new string[m]);
            snmpaddrs = new List<string>(new string[m]);
            matchvalues = new List<string>(new string[m]);
            paramsaliases = new List<string>(new string[m]);
            lastactiveinput = ""; // предыдущий активный вход
            lastlogmsg = new List<string>(new string[m]);
            snmpid = new List<string>(new string[m]);
        }

        public void Resize(int newm)
        {
            m = newm;
            int countr = parameters.Count;
            if (newm < countr)
            {
                parameters.RemoveRange(newm, countr - newm);
                regexps.RemoveRange(newm, countr - newm);
                matchvalues.RemoveRange(newm, countr - newm);
                snmpaddrs.RemoveRange(newm, countr - newm);
                lastlogmsg.RemoveRange(newm, countr - newm);
                snmpid.RemoveRange(newm, countr - newm);
                paramsaliases.RemoveRange(newm, countr - newm);
            }
            else if (newm > countr)
            {
                if (newm > parameters.Capacity)
                {
                    parameters.AddRange(new List<string>(new string[newm - countr]));
                    regexps.AddRange(new List<string>(new string[newm - countr]));
                    matchvalues.AddRange(new List<string>(new string[newm - countr]));
                    snmpaddrs.AddRange(new List<string>(new string[newm - countr]));
                    lastlogmsg.AddRange(new List<string>(new string[newm - countr]));
                    snmpid.AddRange(new List<string>(new string[newm - countr]));
                    paramsaliases.AddRange(new List<string>(new string[newm - countr]));
                }
            }
        }
    }

    
    public class settings : ICloneable
    {
        // main settings
        public class setstruct
        {
            public int n = 1;
            public int m = 5;
            public int maxn = 15; // максимальное число ресиверов
            public string path = "MySettings.xml"; // main conf file
            public uint period;
            public uint periodsnmp;
            public string snmppassword = "community"; // пароль (community)
            public bool writetofile;
            public bool isdebuglog; // ведение отладочного лога
            public bool checkactiveinputs; // проверять ли активный вход
            public string debuglogpath = "diagnostics.txt";
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
            public string patternPaused;
            public string patternResumed;
            public List<ReceiverRecord> receiverecs = new List<ReceiverRecord>();
            public int logconnectlimit; // число неудачных попыток подключения перед записью в лог
            public bool isTrigger;
            public string triggerCmd; // действие при аварии
        }
        public setstruct ap;
        public XmlSerializer x;
        Form1 formobj;

        // form specific objects and variables
        Form settingsform;
        NumericUpDown receivescnt;
        TextBox[] tnames;
        CheckBox[] isActiveChkboxes;
        TextBox[] turls;
        Label[] captions;
        NumericUpDown height;
        NumericUpDown width;
        // выпадающее меню с выбором режима опроса
        ComboBox[] combomode;
        // // кнопки для настройки параметров
        Button[] paramitems;
        Label captionlogdir;
        TextBox logmaindir;
        
        // для настройки регулярных выражений
        Button[] regexps;

        CheckBox twritetofile;
        CheckBox tnestedpath;
        Button setlogpatterns;

        CheckBox debuglog;
        Label captiondebuglogdir;
        TextBox debuglogdir;

        Label inform;
        Label informsnmp;
		Label lconnfaillimit;
        CheckBox triggerCheckBox;
        TextBox triggerAction;
		NumericUpDown tconnfaillimit;
        NumericUpDown tperiod;
        NumericUpDown tsnmperiod;
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

        public object Clone()
        {
            return this.MemberwiseClone();
        }

        // clone the most important fields
        public void MakeCopy(settings oldap)
        {
            // сохраняем параметры каждого устройства
            for (int i = 0; i < ap.n; i++)
            {
                // копируем настройки второй формы
                // название проверяемых параметров (multiline)
                //Console.WriteLine(i.ToString() + "/" + ap.n.ToString());
                //Console.WriteLine(ap.receiverecs[i].parameters.ToList());
                oldap.ap.receiverecs[i].parameters = ap.receiverecs[i].parameters.ToList();

                // номер параметра в snmp (multiline)
                oldap.ap.receiverecs[i].snmpid = ap.receiverecs[i].parameters.ToList();

                // адрес snmp для получения нужного значения (multiline)
                oldap.ap.receiverecs[i].snmpipaddress = ap.receiverecs[i].snmpipaddress;
                // ожидаемое значение (multiline)

                // адрес snmp для получения активного входа
                oldap.ap.receiverecs[i].snmpinputsaddrs = ap.receiverecs[i].snmpinputsaddrs;
                // искать активный вход
                oldap.ap.checkactiveinputs = ap.checkactiveinputs;
                // пароль snmp
                oldap.ap.snmppassword = ap.snmppassword;
            }
        }

        public void SetGlobalDefaultSettings()
        {
            ap.n = 1;
            ap.m = 5;
            ap.maxn = 15;
            ap.period = 10;
            ap.periodsnmp = 10;
            ap.checkactiveinputs = true;
            ap.snmppassword = "community";
            ap.logconnectlimit = 3;
            ap.isTrigger = false;
            ap.triggerCmd = "";
            ap.writetofile = true;
            ap.isdebuglog = false;
            ap.debuglogpath = "diagnostics.txt";
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
            // шаблон, по которому в лог пишется о приостановке опроса устройств
            ap.patternPaused = "%timestamp%\tPAUSED";
            // шаблон, по которому в лог пишется о возобновлении опроса устройств
            ap.patternResumed = "%timestamp%\tRESUMED";

        }
        // Set default settings (useful if there is no settings file yet)
        public void setdefaults(ReceiverRecord rr)
        {
            rr.login = "root";
            rr.pass = "12345";
            rr.name = "36 ТНТ";
            rr.url = "http://192.168.4.231/cgi-bin/input_status.cgi";
            rr.urlinput = "http://192.168.4.231/cgi-bin/decoder_config.cgi";
            rr.snmpipaddress = "192.168.4.231";
            rr.workmode = "Web";
            rr.isActive = true;
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

            rr.matchvalues[0] = "1";
            rr.matchvalues[1] = "1";
            rr.matchvalues[2] = "1";
            rr.matchvalues[3] = "1";
            rr.matchvalues[4] = "1";

            rr.paramsaliases[0] = "Параметр1";
            rr.paramsaliases[1] = "Параметр2";
            rr.paramsaliases[2] = "Параметр3";
            rr.paramsaliases[3] = "Параметр4";
            rr.paramsaliases[4] = "Параметр5";

            rr.regexpactiveinput = @"<option value=""\d""\sselected>(.+?)\s</option>";
            rr.snmpinputsaddrs = ".1.3.6.1.4.1.38295.44.1.1.1.1.0";
            rr.snmpid[0] = "1";
            rr.snmpid[1] = "2";
            rr.snmpid[2] = "3";
            rr.snmpid[3] = "4";
            rr.snmpid[4] = "5";

            rr.snmpaddrs[0] = ".1.3.6.1.4.1.38295.44.1.1.1.1.0";
            rr.snmpaddrs[1] = ".1.3.6.1.4.1.38295.44.1.1.1.1.0";
            rr.snmpaddrs[2] = ".1.3.6.1.4.1.38295.44.1.1.1.1.0";
            rr.snmpaddrs[3] = ".1.3.6.1.4.1.38295.44.1.1.1.1.0";
            rr.snmpaddrs[4] = ".1.3.6.1.4.1.38295.44.1.1.1.1.0";

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
                    //Console.WriteLine(ap.receiverecs.Count);
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

        // функция вызова окна с настройками мониторинга для тюнера
        public void SetTemplateMatch(int i, bool type)
        {
            // форма настройки параметров
            Form setparamsform = new Form();
            setparamsform.FormClosed += (s, e) =>
            {
                formobj.isParamsForm = false;
            };
            setparamsform.TopMost = true;
            setparamsform.Size = new Size(900, 400);
            // название проверяемого параметра
            TextBox textboxparams = new TextBox();
            textboxparams.Multiline = true;
            int endblock1 = 210;
            int endblock2 = 380;
            int endblock3 = 650;
            Label capblock1 = new Label();
            capblock1.Width = endblock1;
            capblock1.Text = "Название проверяемого параметра";
            Label capblock2 = new Label();
            capblock2.Text = "Ожидаемое значение при\nпоиске активного входа";
            capblock2.Width = endblock2 - endblock1;
            capblock2.Height = 30;
            capblock1.Location = new Point(setparamsform.Location.X, setparamsform.Location.Y);
            capblock2.Location = new Point(endblock1, setparamsform.Location.Y);

            textboxparams.SetBounds(setparamsform.Location.X, capblock1.Location.Y + 35, endblock1, 80);
            // Номер параметра в SNMP
            TextBox textboxmatch = new TextBox();
            textboxmatch.Multiline = true;
            textboxmatch.SetBounds(endblock1, capblock2.Location.Y + 35, endblock2 - endblock1, 80);
            setparamsform.Controls.Add(capblock1);
            setparamsform.Controls.Add(capblock2);
            setparamsform.Controls.Add(textboxparams);
            setparamsform.Controls.Add(textboxmatch);


            Label capblock3 = new Label();
            capblock3.Width = endblock3 - endblock2;
            capblock3.Text = "Адрес snmp для получения нужного значения";
            capblock3.Location = new Point(endblock2, setparamsform.Location.Y);
            // Адрес snmp для получения нужного значения
            TextBox textboxdetails = new TextBox();
            textboxdetails.Multiline = true;
            // Ожидаемое значение (для зеленой лампочки)
            TextBox textboxdetailsmatch = new TextBox();
            textboxdetailsmatch.Multiline = true;
            Label capblock4 = new Label();
            capblock4.Text = "Ожидаемое значение (для зеленой лампочки)";
            capblock4.Location = new Point(endblock3, setparamsform.Location.Y);
            capblock4.Width = setparamsform.Width - endblock3;
            textboxdetails.SetBounds(endblock2, capblock2.Location.Y + 35, endblock3 - endblock2, 80);
            textboxdetailsmatch.SetBounds(endblock3, capblock3.Location.Y + 35, setparamsform.Width - endblock3, 80);
            setparamsform.Controls.Add(capblock3);
            setparamsform.Controls.Add(capblock4);
            setparamsform.Controls.Add(textboxdetails);
            setparamsform.Controls.Add(textboxdetailsmatch);

            Label capblock5 = new Label();
            capblock5.Text = "Адрес snmp для получения активного входа";
            capblock5.Location = new Point(textboxparams.Location.X, textboxparams.Location.Y + textboxparams.Height + 20);
            capblock5.Height = 20;
            capblock5.Width = 280;
            setparamsform.Controls.Add(capblock5);
            // Адрес snmp для получения активного входа
            TextBox turl = new TextBox();
            turl.Multiline = false;
            turl.SetBounds(textboxparams.Location.X, capblock5.Location.Y + 20, 280, 50);
            setparamsform.Controls.Add(turl);
            // Искать активный выход
            CheckBox lookForActiveinput = new CheckBox();
            lookForActiveinput.Text = "Искать активный выход";
            lookForActiveinput.Checked = ap.checkactiveinputs;
            lookForActiveinput.Location = new Point(turl.Location.X + turl.Width + 10, turl.Location.Y);
            lookForActiveinput.Width = 200;
            setparamsform.Controls.Add(lookForActiveinput);

            Label capblock6 = new Label();
            capblock6.Text = "Пароль SNMP (community string)";
            capblock6.Location = new Point(lookForActiveinput.Location.X + lookForActiveinput.Width + 10, capblock5.Location.Y);
            capblock6.Height = 20;
            capblock6.Width = 300;
            setparamsform.Controls.Add(capblock6);
            // Пароль SNMP (community string)
            TextBox tpass = new TextBox();
            tpass.Multiline = false;
            tpass.SetBounds(capblock6.Location.X, turl.Location.Y, 280, 50);
            setparamsform.Controls.Add(tpass);

            setparamsform.Text = "Настройка параметров для считывания";


            Button okbtn = new Button();
            okbtn.Text = "ON";
            okbtn.Click += (s, e) =>
            {
                // save params to struct
                using (System.IO.StringReader reader = new System.IO.StringReader(textboxparams.Text))
                {
                    // // название проверяемого параметра
                    //Console.WriteLine(reader.ReadToEnd());
                    formobj.oldap.ap.receiverecs[i].parameters = reader.ReadToEnd().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }

                // snmp id of parameters
                using (System.IO.StringReader reader2 = new System.IO.StringReader(textboxmatch.Text))
                {
                    // Номер параметра в SNMP
                    formobj.oldap.ap.receiverecs[i].snmpid = reader2.ReadToEnd().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }

                // snmp addresses
                using (System.IO.StringReader reader3 = new System.IO.StringReader(textboxdetails.Text))
                {
                    // Адрес snmp для получения нужного значения
                    formobj.oldap.ap.receiverecs[i].snmpaddrs = SplitParamsString(reader3.ReadToEnd());

                    //formobj.oldap.ap.receiverecs[i].snmpaddrs = reader3.ReadToEnd().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }

                // match values (Ожидаемое значение)
                using (System.IO.StringReader reader4 = new System.IO.StringReader(textboxdetailsmatch.Text))
                {
                    formobj.oldap.ap.receiverecs[i].matchvalues = reader4.ReadToEnd().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }


                // save active input address
                if (type)
                    formobj.oldap.ap.receiverecs[i].regexpactiveinput = turl.Text;
                else
                    formobj.oldap.ap.receiverecs[i].snmpinputsaddrs = turl.Text;

                // snmp password
                formobj.oldap.ap.snmppassword = tpass.Text;
                
                // should we look for an active input
                formobj.oldap.ap.checkactiveinputs = lookForActiveinput.Checked;

                // количество параметров для данного устройства
                formobj.oldap.ap.receiverecs[i].m = formobj.oldap.ap.receiverecs[i].parameters.Count;

                formobj.isParamsForm = false;
                setparamsform.Close();
            };
            okbtn.Location = new Point(textboxparams.Location.X + 10, capblock5.Location.Y + capblock5.Height + 40);
            //Console.WriteLine(okbtn.Location);
            Button cancelbtn = new Button();
            cancelbtn.Click += (s, e) =>
            {
                formobj.isParamsForm = false;
                setparamsform.Close();
            };
            cancelbtn.Text = "Отмена";
            cancelbtn.Location = new Point(okbtn.Location.X + 80, okbtn.Location.Y);
            setparamsform.Controls.Add(okbtn);
            setparamsform.Controls.Add(cancelbtn);
            setparamsform.Show();
            // инициализируем форму предварительными значениями
            textboxparams.Clear();
            // имена параметров
            if (formobj.oldap.ap.receiverecs[i].parameters.Count(s => s != null) > 0)
                textboxparams.AppendText(string.Join("\r\n", formobj.oldap.ap.receiverecs[i].parameters));
            // номера snmp параметров
            if (formobj.oldap.ap.receiverecs[i].snmpid.Count(s => s != null) > 0)
                textboxmatch.AppendText(string.Join("\r\n", formobj.oldap.ap.receiverecs[i].snmpid));
            // snmp адреса (oid)
            if (formobj.oldap.ap.receiverecs[i].snmpaddrs.Count(s => s != null) > 0)
                textboxdetails.AppendText(string.Join("\r\n", formobj.oldap.ap.receiverecs[i].snmpaddrs));
            // snmp ожидаемые значения
            if (formobj.oldap.ap.receiverecs[i].matchvalues.Count(s => s != null) > 0)
                textboxdetailsmatch.AppendText(string.Join("\r\n", formobj.oldap.ap.receiverecs[i].matchvalues));

            // snmp-адрес для получения активного входа
            if (formobj.oldap.ap.receiverecs[i].snmpinputsaddrs.Length > 0)
                turl.Text = formobj.oldap.ap.receiverecs[i].snmpinputsaddrs;

            // искать активный вход
            lookForActiveinput.Checked = formobj.oldap.ap.checkactiveinputs;
            // пароль

            if (formobj.oldap.ap.snmppassword.Length > 0)
                tpass.AppendText(formobj.oldap.ap.snmppassword);

        }

        public List<string> SplitParamsString(string paramslist)
        {
            List<string> testsplit = paramslist.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (testsplit.Count == 0)
                testsplit = paramslist.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
            return testsplit;
        }

        // настройка параметров относящихся к данному ресиверу, i - индекс ресивера, type - режим (true - Web, false - snmp)
        public void SetParameters(int i, bool type)
        {
            Form setparamsform = new Form();
            setparamsform.Size = new Size(900, 400);
            setparamsform.FormClosed += (s, e) =>
                {
                    formobj.isParamsForm = false;
                };
            setparamsform.TopMost = true;


            TextBox textboxparams = new TextBox();
            textboxparams.Multiline = true;
            int endblock1 = 210;
            int endblock2 = 380;
            int endblock3 = 650;
            Label capblock1 = new Label();
            capblock1.Width = endblock1;
            capblock1.Text = "Название проверяемого параметра";
            Label capblock2 = new Label();

            capblock2.Text = "Алиас для имени параметра";
            capblock2.Width = endblock2 - endblock1;
            capblock1.Location = new Point(setparamsform.Location.X, setparamsform.Location.Y);
            capblock2.Location = new Point(endblock1, setparamsform.Location.Y);

            textboxparams.SetBounds(setparamsform.Location.X, capblock1.Location.Y + 25, endblock1, 80);
            TextBox textboxmatch = new TextBox();
            textboxmatch.Multiline = true;
            textboxmatch.SetBounds(endblock1, capblock2.Location.Y + 25, endblock2 - endblock1, 80);
            setparamsform.Controls.Add(capblock1);
            setparamsform.Controls.Add(capblock2);
            setparamsform.Controls.Add(textboxmatch);


            Label capblock3 = new Label();
            capblock3.Width = endblock3 - endblock2;
            capblock3.Text = "Регулярное выражение для поиска значения параметра";
            capblock3.Location = new Point(endblock2, setparamsform.Location.Y);
            TextBox textboxdetails = new TextBox();
            textboxdetails.Multiline = true;
            TextBox textboxdetailsmatch = new TextBox();
            textboxdetailsmatch.Multiline = true;
            Label capblock4 = new Label();
            capblock4.Text = "Ожидаемое значение (для зеленой лампочки)";
            capblock4.Location = new Point(endblock3, setparamsform.Location.Y);
            capblock4.Width = setparamsform.Width - endblock3;
            textboxdetails.SetBounds(endblock2, capblock2.Location.Y + 25, endblock3 - endblock2, 80);
            textboxdetailsmatch.SetBounds(endblock3, capblock3.Location.Y + 25, setparamsform.Width - endblock3, 80);
            setparamsform.Controls.Add(capblock3);
            setparamsform.Controls.Add(capblock4);
            setparamsform.Controls.Add(textboxdetails);
            setparamsform.Controls.Add(textboxdetailsmatch);

            Label capblock5 = new Label();
            capblock5.Text = "Адрес страницы проверки основных параметров";
            capblock5.Location = new Point(textboxparams.Location.X, textboxparams.Location.Y + textboxparams.Height + 20);
            capblock5.Height = 20;
            capblock5.Width = 280;
            setparamsform.Controls.Add(capblock5);
            TextBox turl = new TextBox();
            turl.Multiline = false;
            turl.SetBounds(textboxparams.Location.X, capblock5.Location.Y + 20, 280, 50);
            setparamsform.Controls.Add(turl);

            CheckBox lookForActiveinput = new CheckBox();
            lookForActiveinput.Text = "Искать активный выход";
            lookForActiveinput.Checked = ap.checkactiveinputs;
            lookForActiveinput.Location = new Point(turl.Location.X + turl.Width + 10, turl.Location.Y);
            lookForActiveinput.Width = 200;
            setparamsform.Controls.Add(lookForActiveinput);

            Label capblock6 = new Label();
            capblock6.Text = "Адрес страницы поиска активного входа";
            capblock6.Location = new Point(lookForActiveinput.Location.X + lookForActiveinput.Width + 10, capblock5.Location.Y);
            capblock6.Height = 20;
            capblock6.Width = 300;
            setparamsform.Controls.Add(capblock6);
            TextBox textboxurlinput = new TextBox();
            textboxurlinput.Location = new Point(capblock6.Location.X, capblock6.Location.Y + 20);
            textboxurlinput.Width = 300;
            textboxurlinput.Multiline = false;
            setparamsform.Controls.Add(textboxurlinput);

            Label capblock7 = new Label();
            capblock7.Text = "Регулярное выражение для поиска активного входа";
            capblock7.Location = new Point(capblock5.Location.X, capblock5.Location.Y + 50);
            capblock7.Height = 20;
            capblock7.Width = 300;
            setparamsform.Controls.Add(capblock7);
            TextBox textboxregexinput = new TextBox();
            textboxregexinput.Location = new Point(capblock7.Location.X, capblock7.Location.Y + 20);
            textboxregexinput.Width = 300;
            textboxregexinput.Multiline = false;
            setparamsform.Controls.Add(textboxregexinput);

            Label capblock8 = new Label();
            capblock8.Text = "Логин";
            capblock8.Height = 20;
            capblock8.Location = new Point(textboxregexinput.Location.X, textboxregexinput.Location.Y + textboxregexinput.Height + 20);
            setparamsform.Controls.Add(capblock8);
            TextBox textboxlogin = new TextBox();
            textboxlogin.Location = new Point(capblock8.Location.X, capblock8.Location.Y + 20);
            setparamsform.Controls.Add(textboxlogin);

            Label capblock9 = new Label();
            capblock9.Text = "Пароль";
            capblock9.Height = 20;
            capblock9.Location = new Point(capblock8.Location.X + textboxlogin.Width + 10, capblock8.Location.Y);
            setparamsform.Controls.Add(capblock9);
            TextBox textboxpass = new TextBox();
            textboxpass.Location = new Point(capblock9.Location.X, capblock9.Location.Y + 20);
            setparamsform.Controls.Add(textboxpass);
 
            setparamsform.Text = "Настройка параметров для считывания";

            Button okbtn = new Button();
            okbtn.Text = "OK";
            okbtn.Click += (s, e) =>
            {
                // save params to struct
                using (System.IO.StringReader reader = new System.IO.StringReader(textboxparams.Text))
                {
                    formobj.oldap.ap.receiverecs[i].parameters[i] = reader.ReadToEnd();
                }

                using (System.IO.StringReader reader = new System.IO.StringReader(textboxmatch.Text))
                {
                    formobj.oldap.ap.receiverecs[i].snmpid[i] = reader.ReadToEnd();
                }

                formobj.isParamsForm = false;
                setparamsform.Close();
            };
            okbtn.Location = new Point(textboxparams.Location.X + 10, textboxparams.Location.Y + textboxparams.Height + 200);
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
            // заполняем форму
            // для режима snmp
            if (true)
            {
                for (int j = 0; j < formobj.oldap.ap.receiverecs[i].m; j++)
                {
                    // имена параметров
                    textboxparams.AppendText(formobj.oldap.ap.receiverecs[i].parameters[j] + "\r\n");
                    // snmp номера, соответствующие параметрам
                    if (formobj.oldap.ap.receiverecs[i].snmpid.ElementAt(j) != null)
                        textboxmatch.AppendText(formobj.oldap.ap.receiverecs[i].snmpid[j] + "\r\n");
                    // адрес snmp (oid)
                    //textboxdetails.AppendText(string.Join("\r\n", formobj.oldsnmpaddrs[i].Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)));
                    // ожидаемое значение

                    // Адрес snmp для получения активного входа
                    //textboxurlinput.AppendText(string.Join("\r\n", formobj.oldsnmpinputs[i].Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)));
                }
            }
        }

        void param_Click(object sender, EventArgs e)
        {
            var cur = sender as Button;
            if (formobj.isParamsForm)
                return;
            formobj.isParamsForm = true;
            bool getmode = (combomode[Int32.Parse(cur.Name)].Text == "Web" ? true : false);
            if (getmode)
                SetParameters(Int32.Parse(cur.Name), getmode);
            else
                SetTemplateMatch(Int32.Parse(cur.Name), getmode);

        }

        void setlogpatterns_Click(object sender, EventArgs e)
        {
            if (formobj.isFormatLogForm)
                return;
            formobj.isFormatLogForm = true;
            // draw a small form that allows us to set new log formats
            Form setlogformat = new Form();
            setlogformat.Text = "Формат логов";
            setlogformat.Size = new Size(1000, 400);
            setlogformat.TopMost = true;

            Label hint = new Label();
            hint.Width = 890;
            hint.Height = 50;
            hint.Font = new Font(hint.Font, FontStyle.Bold);

            hint.Text = "Переменные подстановки:\r\n%timestamp% - дата и время, %devicename% - название устройства, %paramname% - имя параметра (не для всех логов),\r\n %url% - адрес для подключения, %msg% - подробности или системное сообщение, %regexp% - шаблон для поиска данных/snmp адрес";
            hint.Location = new Point(setlogformat.Location.X, setlogformat.Location.Y + 10);
            setlogformat.Controls.Add(hint);
            Label pattern1cap = new Label();
            pattern1cap.Text = "Изменение состояния входов";
            pattern1cap.Width = 370;
            pattern1cap.Location = new Point(setlogformat.Location.X, setlogformat.Location.Y + 60);
            setlogformat.Controls.Add(pattern1cap);
            TextBox pattern1box = new TextBox();
            pattern1box.Width = 600;
            pattern1box.Location = new Point(pattern1cap.Location.X + 380, pattern1cap.Location.Y);
            pattern1box.Text = ap.patternLogOK;
            setlogformat.Controls.Add(pattern1box);

            Label pattern2cap = new Label();
            pattern2cap.Text = "Ошибки и проблемы с получением данных о входах";
            pattern2cap.Width = 290;
            pattern2cap.Location = new Point(pattern1cap.Location.X, pattern1cap.Location.Y + 30);
            setlogformat.Controls.Add(pattern2cap);
            TextBox pattern2box = new TextBox();
            pattern2box.Width = 600;
            pattern2box.Text = ap.patternLogConFail;
            pattern2box.Location = new Point(pattern2cap.Location.X + 380, pattern2cap.Location.Y);
            setlogformat.Controls.Add(pattern2box);

            Label pattern3cap = new Label();
            pattern3cap.Text = "Изменение активного входа";
            pattern3cap.Width = 290;
            pattern3cap.Location = new Point(pattern2cap.Location.X, pattern2cap.Location.Y + 30);
            setlogformat.Controls.Add(pattern3cap);
            TextBox pattern3box = new TextBox();
            pattern3box.Width = 600;
            pattern3box.Location = new Point(pattern3cap.Location.X + 380, pattern3cap.Location.Y);
            pattern3box.Text = ap.patternDecoderOK;
            setlogformat.Controls.Add(pattern3box);
            Label pattern4cap = new Label();
            pattern4cap.Text = "Ошибка получения активного входа";
            pattern4cap.Width = 290;
            pattern4cap.Location = new Point(pattern3cap.Location.X, pattern3cap.Location.Y + 30);
            setlogformat.Controls.Add(pattern4cap);
            TextBox pattern4box = new TextBox();
            pattern4box.Width = 600;
            pattern4box.Text = ap.patternDecoderConFail;
            pattern4box.Location = new Point(pattern4cap.Location.X + 380, pattern4cap.Location.Y);
            setlogformat.Controls.Add(pattern4box);

            Label pattern5cap = new Label();
            pattern5cap.Text = "Другие ошибки";
            pattern5cap.Width = 290;
            pattern5cap.Location = new Point(pattern4cap.Location.X, pattern4cap.Location.Y + 30);
            setlogformat.Controls.Add(pattern5cap);
            TextBox pattern5box = new TextBox();
            pattern5box.Width = 600;
            pattern5box.Text = ap.patternGeneralConFail;
            pattern5box.Location = new Point(pattern5cap.Location.X + 380, pattern5cap.Location.Y);
            setlogformat.Controls.Add(pattern5box);

            Label pattern6cap = new Label();
            pattern6cap.Text = "Приостановка работы программы";
            pattern6cap.Width = 290;
            pattern6cap.Location = new Point(pattern5cap.Location.X, pattern5cap.Location.Y + 30);
            setlogformat.Controls.Add(pattern6cap);
            TextBox pattern6box = new TextBox();
            pattern6box.Width = 600;
            pattern6box.Location = new Point(pattern6cap.Location.X + 380, pattern6cap.Location.Y);
            pattern6box.Text = ap.patternPaused;
            setlogformat.Controls.Add(pattern6box);

            Label pattern7cap = new Label();
            pattern7cap.Text = "Возобновление работы программы";
            pattern7cap.Width = 300;
            pattern7cap.Location = new Point(pattern6cap.Location.X, pattern6cap.Location.Y + 30);
            setlogformat.Controls.Add(pattern7cap);
            TextBox pattern7box = new TextBox();
            pattern7box.Width = 600;
            pattern7box.Location = new Point(pattern7cap.Location.X + 380, pattern7cap.Location.Y);
            pattern7box.Text = ap.patternResumed;
            setlogformat.Controls.Add(pattern7box);

            Button okbtn = new Button();
            okbtn.Text = "OK";
            okbtn.Location = new Point(pattern7cap.Location.X, pattern7cap.Location.Y + 30);
            okbtn.Click += (s, ee) =>
                {
                    // replace our patterns
                    formobj.oldap.ap.patternLogOK = pattern1box.Text;
                    formobj.oldap.ap.patternLogConFail = pattern2box.Text;
                    formobj.oldap.ap.patternDecoderOK = pattern3box.Text;
                    formobj.oldap.ap.patternDecoderConFail = pattern4box.Text;
                    formobj.oldap.ap.patternGeneralConFail = pattern5box.Text;
                    formobj.oldap.ap.patternPaused = pattern6box.Text;
                    formobj.oldap.ap.patternResumed = pattern7box.Text;
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
            // new in 2018
            formobj.tempoldn = oldvalue;
            formobj.tempnewn = newvalue;
            //

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
                    Array.Resize(ref isActiveChkboxes, newvalue);
                    Array.Resize(ref turls, newvalue);
                    Array.Resize(ref combomode, newvalue);
                    Array.Resize(ref paramitems, newvalue);
                    Array.Resize(ref regexps, newvalue);
                    Array.Resize(ref captions, newvalue);

                    tnames[i] = new TextBox();
                    turls[i] = new TextBox();
                    isActiveChkboxes[i] = new CheckBox();
                    combomode[i] = new ComboBox();
                    combomode[i].Items.Add("Web");
                    combomode[i].Items.Add("SNMP");
                    combomode[i].SelectedIndex = 0;
                    
                    paramitems[i] = new Button();
                    regexps[i] = new Button();
                    captions[i] = new Label();
                    //isActiveChkboxes[i].Location = new Point
                    tnames[i].Location = new Point(tnames[i - 1].Location.X, tnames[i - 1].Location.Y + 40);
                    isActiveChkboxes[i].Location = new Point(5, tnames[i].Location.Y);
                    settingsform.Controls.Add(tnames[i]);
                    settingsform.Controls.Add(isActiveChkboxes[i]);
                    captions[i].Location = new Point(tnames[i - 1].Location.X, tnames[i - 1].Location.Y + 20);
                    settingsform.Controls.Add(captions[i]);
                    turls[i].Location = new Point(tnames[i].Location.X + 100, tnames[i].Location.Y);

                    turls[i].Width = 120;
                    settingsform.Controls.Add(turls[i]);

                    combomode[i].Location = new Point(turls[i].Location.X + 120, turls[i].Location.Y);
                    combomode[i].Name = i.ToString();
                    combomode[i].Width = 85;
                    settingsform.Controls.Add(combomode[i]);

                    paramitems[i].Location = new Point(combomode[i].Location.X + 85, combomode[i].Location.Y);
                    paramitems[i].Width = 150;
                    paramitems[i].Text = "Настройка параметров";
                    paramitems[i].Click += new EventHandler(param_Click);
                    paramitems[i].Name = i.ToString();
                    settingsform.Controls.Add(paramitems[i]);

                    captions[i].Text = String.Format("Имя {0} ресивера", i + 1);

                    // двигаем нижние контролы вниз, не выходя за границы
                    twritetofile.Location = new Point(tnames[i].Location.X, tnames[i].Location.Y + 30);
					lconnfaillimit.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 50);
                    triggerCheckBox.Location = new Point(lconnfaillimit.Location.X, lconnfaillimit.Location.Y + 30);
                    triggerAction.Location = new Point(triggerAction.Location.X, triggerCheckBox.Location.Y);
					tconnfaillimit.Location = new Point(tconnfaillimit.Location.X, lconnfaillimit.Location.Y);
                    inform.Location = new Point(twritetofile.Location.X, triggerCheckBox.Location.Y + 30);
                    informsnmp.Location = new Point(inform.Location.X + inform.Width + 10, inform.Location.Y);

                    tperiod.Location = new Point(tperiod.Location.X, inform.Location.Y + tempstep);
                    tsnmperiod.Location = new Point(tsnmperiod.Location.X, inform.Location.Y + tempstep);

                    okbutton.Location = new Point(okbutton.Location.X, tperiod.Location.Y + tempstep);
                    cancelbutton.Location = new Point(cancelbutton.Location.X, okbutton.Location.Y);
                    captionlogdir.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 25);
                    logmaindir.Location = new Point(logmaindir.Location.X, captionlogdir.Location.Y);
                    tnestedpath.Location = new Point(tnestedpath.Location.X, logmaindir.Location.Y);
                    setlogpatterns.Location = new Point(setlogpatterns.Location.X, tnestedpath.Location.Y);
                    debuglog.Location = new Point(debuglog.Location.X, setlogpatterns.Location.Y);
                    captiondebuglogdir.Location = new Point(captiondebuglogdir.Location.X, debuglog.Location.Y + 5);
                    debuglogdir.Location = new Point(debuglogdir.Location.X, captiondebuglogdir.Location.Y);

                    settingsform.Size = new Size(settingsform.Size.Width, settingsform.Size.Height + 2*tempstep);

                    // создаем объект по умолчанию для нового ресивера
                    ReceiverRecord tempobj = new ReceiverRecord();
                    
                    if (i >= ap.receiverecs.Count)
                    {
                        ap.receiverecs.AddRange(new List<ReceiverRecord>(new ReceiverRecord[newvalue - oldvalue]));
                    }
                    
                    ap.receiverecs[i] = tempobj;
                    //if (ap.receiverecs.Capacity < newvalue)
                    //{
                    //    formobj.ap.ap.receiverecs.Add(tempobj);
                    //    formobj.ap.ap.receiverecs[i].Resize(newvalue);
                    //    ap.receiverecs[i].Resize(newvalue);
                    //    formobj.oldap.ap.receiverecs[i].Resize(newvalue);
                    //}
                    //ap.receiverecs.RemoveAll(item => item == null); // если в поле параметров появляются лишние null, этот код их убирает
              

                    // даем полям значения по умолчанию

                    // адрес копируем из предыдущего поля
                    turls[i].Text = turls[i - 1].Text;
                    if (i < ap.receiverecs.Count - 1)
                    {
                        formobj.oldap.ap.receiverecs[i].parameters[i] = string.Join("\r\n", ap.receiverecs[i].parameters.ToArray(), 0, ap.receiverecs[i].m);
                        formobj.oldap.ap.receiverecs[i].regexps[i] = string.Join("\r\n", ap.receiverecs[i].regexps.ToArray(), 0, ap.receiverecs[i].m);
                        formobj.oldap.ap.receiverecs[i].matchvalues[i] = string.Join("\r\n", ap.receiverecs[i].matchvalues.ToArray(), 0, ap.receiverecs[i].m);
                        formobj.oldap.ap.receiverecs[i].paramsaliases[i] = string.Join("\r\n", ap.receiverecs[i].paramsaliases.ToArray(), 0, ap.receiverecs[i].m);
                        formobj.oldap.ap.receiverecs[i].snmpaddrs[i] = string.Join("\r\n", ap.receiverecs[i].snmpaddrs.ToArray(), 0, ap.receiverecs[i].m);
                        formobj.oldap.ap.receiverecs[i].snmpinputsaddrs = ap.receiverecs[i].snmpinputsaddrs;
                        formobj.oldap.ap.receiverecs[i].regexpactiveinput = ap.receiverecs[i].regexpactiveinput;
                        formobj.oldap.ap.receiverecs[i].snmpid[i] = string.Join("\r\n", ap.receiverecs[i].snmpid.ToArray(), 0, ap.receiverecs[i].m);
                    }
                    else
                    {
                        formobj.oldap.setdefaults(formobj.oldap.ap.receiverecs[i]);
                    }

                    // увеличиваем высоту формы, если нужно
                    // Console.WriteLine(settingsform.Size.Height.ToString() + " " + (40 * i + 274).ToString());
                    if (40 * i + 274 > settingsform.Size.Height)
                    {
                        settingsform.Size = new Size(settingsform.Size.Width, settingsform.Size.Height + 40);
                    }
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

                    isActiveChkboxes[i - 1].Hide();
                    settingsform.Controls.Remove(isActiveChkboxes[i - 1]);
                    isActiveChkboxes[i - 1].Dispose();

                    turls[i - 1].Hide();
                    settingsform.Controls.Remove(turls[i - 1]);
                    turls[i - 1].Dispose();

                    combomode[i - 1].Hide();
                    settingsform.Controls.Remove(combomode[i - 1]);
                    combomode[i - 1].Dispose();

                    paramitems[i - 1].Hide();
                    settingsform.Controls.Remove(paramitems[i - 1]);
                    paramitems[i - 1].Dispose();

                    if (regexps != null && regexps.Count(s => s != null) > 0)
                    {
                        if (i <= regexps.Count(s => s != null))
                        {
                            regexps[i - 1].Hide();
                            settingsform.Controls.Remove(regexps[i - 1]);
                            regexps[i - 1].Dispose();
                        }
                    }

                    captions[i - 1].Hide();
                    settingsform.Controls.Remove(captions[i - 1]);
                    captions[i - 1].Dispose();
                }


                Array.Resize(ref tnames, newvalue);
                Array.Resize(ref turls, newvalue);
                Array.Resize(ref combomode, newvalue);
                Array.Resize(ref paramitems, newvalue);
                Array.Resize(ref regexps, newvalue);
                Array.Resize(ref captions, newvalue);

                //{
                // поднимаем нижние контролы на фиксированный шаг вверх, не выходя за границы
                twritetofile.Location = new Point(tnames[newvalue - 1].Location.X, tnames[newvalue - 1].Location.Y + 30);
				lconnfaillimit.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 50);
				tconnfaillimit.Location = new Point(tconnfaillimit.Location.X, lconnfaillimit.Location.Y);
                triggerCheckBox.Location = new Point(triggerCheckBox.Location.X, lconnfaillimit.Location.Y + 30);
                triggerAction.Location = new Point(triggerAction.Location.X, triggerCheckBox.Location.Y);
                inform.Location = new Point(inform.Location.X, triggerCheckBox.Location.Y + 30);
                informsnmp.Location = new Point(informsnmp.Location.X, inform.Location.Y);
                tperiod.Location = new Point(tperiod.Location.X, inform.Location.Y + tempstep);

                tsnmperiod.Location = new Point(tsnmperiod.Location.X, inform.Location.Y + tempstep);
                okbutton.Location = new Point(okbutton.Location.X, tperiod.Location.Y + tempstep);
                cancelbutton.Location = new Point(cancelbutton.Location.X, okbutton.Location.Y);
                captionlogdir.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 25);
                logmaindir.Location = new Point(captionlogdir.Location.X + 130, captionlogdir.Location.Y);
                tnestedpath.Location = new Point(tnestedpath.Location.X, logmaindir.Location.Y);
                setlogpatterns.Location = new Point(setlogpatterns.Location.X, tnestedpath.Location.Y);

                debuglog.Location = new Point(debuglog.Location.X, setlogpatterns.Location.Y);
                captiondebuglogdir.Location = new Point(captiondebuglogdir.Location.X, debuglog.Location.Y + 5);
                debuglogdir.Location = new Point(debuglogdir.Location.X, captiondebuglogdir.Location.Y);
                //}

                // уменьшаем высоту формы, если нужно

                if (40 * newvalue + 274 < settingsform.Size.Height)
                {
                    settingsform.Size = new Size(settingsform.Size.Width, settingsform.Size.Height - 40);
                }
            }
        }

        void label_DragEnter(object sender, DragEventArgs e)
        {
            //int newX = e.X;
            //int newY = e.Y;
            //// formobj.GetChildAtPoint()
            //Control tempControl = formobj.GetChildAtPoint(Cursor.Position);
            //for (int j = 0; j < ap.m; j++)
            //{
            //    if (tempControl == formobj.dynamicparamls[0, j])
            //    {
            //        // swap controls
            //    }
            //}

        }

        void label_DragDrop(object sender, DragEventArgs e)
        {
        }

        // если выбран режим snmp - блокируем поле изменения адреса как ненужную настройку и наоборот
        void settings_TextChanged(object sender, EventArgs e)
        {
            ComboBox tmpcmb = (ComboBox)sender;
            int i = Int32.Parse(tmpcmb.Name);
            bool getmode = (tmpcmb.Text == "Web" ? true : false); // true = web, false = snmp
            turls[i].Enabled = getmode;

            if (getmode)
            {
                paramitems[i].Text = "Настройки Web";
                ap.receiverecs[i].workmode = "Web";
            }
            else
            {
                paramitems[i].Text = "Настройки SNMP";
                ap.receiverecs[i].workmode = "SNMP";
            }
        }

        // создание формы настроек

// new settings in ReceiverRecord:

//1) public List<string> paramsaliases;  equals to public List<string> matchvalues;
//2) public string snmpipaddress; equals to public string url;

//new settings in setstruct:

//1) public bool checkactiveinputs; // проверять ли активный вход
//2) public uint periodsnmp;
//3) public string snmppassword; // community



        public void CreateSettingsForm()
        {
            // создаем копию настроек
            //formobj.ap.MakeCopy(formobj.oldap);
            formobj.ap = (settings)formobj.oldap.Clone();

            formobj.timer1.Stop();
            settingsform = new Form();
            receivescnt = new NumericUpDown();
            tnames = new TextBox[ap.n];
            isActiveChkboxes = new CheckBox[ap.n];
            turls = new TextBox[ap.n];
            combomode = new ComboBox[ap.n];
            
            captions = new Label[ap.n];
            // для задания проверяемых параметров
            paramitems = new Button[ap.n];

            settingsform.Disposed += (s, e) =>
                {

                    formobj.isSettingsForm = false;
                    formobj.logger.ResizeLoggers(formobj.logger.oldsize);
                    for (int i = 0; i < formobj.logger.oldsize; i++)
                    {
                        // если мы до этого увеличили количество устройств - надо всё сбросить назад
                        // ... apobj.ap.receiverecs[i].name
                        string resumemsg = formobj.logger.CreateLogMsg(6, i, 0, "");
                        formobj.logger.rawWrite(i, resumemsg);
                    }
                };

            //formobj.dynamicparamls[0, 0].AllowDrop = true;
            //formobj.dynamicparamls[0, 0].DragEnter += new DragEventHandler(label_DragEnter);
            //formobj.dynamicparamls[0, 0].DragDrop +=new DragEventHandler(label_DragDrop);

            receivescnt.Value = ap.n;
            receivescnt.Width = 40;
            receivescnt.Minimum = 1;
            receivescnt.Maximum = 15;
            receivescnt.Location = new Point(130, 5);

            // сохраняем старые настройки (до открытия формы детальных настроек)
            formobj.oldap = formobj.ap;

            //MessageBox.Show(formobj.oldparams[0].Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Count().ToString());
            //MessageBox.Show("Инициализация формы настроек прошла. Параметры в oldparams\n" + formobj.oldparams);
            for (int i = 0; i < ap.n; i++)
            {
                bool getmode = (ap.receiverecs[i].workmode == "Web" ? true : false);
                tnames[i] = new TextBox();
                isActiveChkboxes[i] = new CheckBox();
                isActiveChkboxes[i].Name = i.ToString();
                isActiveChkboxes[i].Size = new Size(20, 20);
                isActiveChkboxes[i].CheckedChanged += (s,e) => {
                    var state = s as CheckBox;
                    int index = Int32.Parse(state.Name);
                    ap.receiverecs[index].isActive = state.Checked;
                    tnames[index].Enabled = state.Checked;
                    turls[index].Enabled = state.Checked;
                    combomode[index].Enabled = state.Checked;
                    paramitems[index].Enabled = state.Checked;
                };
                turls[i] = new TextBox();
                combomode[i] = new ComboBox();
                combomode[i].Items.Add("Web");
                combomode[i].Items.Add("SNMP");
                combomode[i].SelectedIndex = 1;
                combomode[i].Name = i.ToString();
                combomode[i].TextChanged += new EventHandler(settings_TextChanged);
                captions[i] = new Label();
                paramitems[i] = new Button();
                paramitems[i].Text = "Настройка параметров";
                paramitems[i].Click += new EventHandler(param_Click);
                paramitems[i].Name = i.ToString();
                
                if (getmode)
                    paramitems[i].Text = "Настройки Web";
                else
                    paramitems[i].Text = "Настройки SNMP";

                // размещение элементов: с каждым индексом ордината ниже на фиксированный шаг
                if (i == 0)
                {
                    settingsform.Controls.Add(receivescnt); // numerica updown for receivers
                    captions[i].Location = new Point(25, 35);
                    settingsform.Controls.Add(captions[i]);
                    settingsform.Controls.Add(isActiveChkboxes[i]);
                    // потом поля ввода
                    // левые
                    tnames[i].Location = new Point(captions[i].Location.X, captions[i].Location.Y + 20);
                    isActiveChkboxes[i].Location = new Point(5, tnames[i].Location.Y);
                    settingsform.Controls.Add(tnames[i]);
                    // правые
                    turls[i].Location = new Point(tnames[i].Location.X + 100, tnames[i].Location.Y);
                    turls[i].Width = 120;
                    settingsform.Controls.Add(turls[i]);

                    combomode[i].Location = new Point(turls[i].Location.X + 120, turls[i].Location.Y);
                    combomode[i].Width = 85;
                    settingsform.Controls.Add(combomode[i]);

                    // поле ввода параметров
                    paramitems[i].Location = new Point(combomode[i].Location.X + 85, combomode[i].Location.Y);
                    paramitems[i].Width = 150;
 
                    settingsform.Controls.Add(paramitems[i]);
                }
                if (i > 0)
                {
                    // сначала подписи к полям
                    captions[i].Location = new Point(tnames[i - 1].Location.X, tnames[i - 1].Location.Y + 20);
                    isActiveChkboxes[i].Size = new Size(20, 20);
                    settingsform.Controls.Add(captions[i]);
                    // потом поля ввода
                    // левые
                    tnames[i].Location = new Point(tnames[i - 1].Location.X, tnames[i - 1].Location.Y + 40);
                    isActiveChkboxes[i].Location = new Point(5, tnames[i].Location.Y);
                    settingsform.Controls.Add(tnames[i]);
                    settingsform.Controls.Add(isActiveChkboxes[i]);
                    // правые
                    turls[i].Location = new Point(tnames[i].Location.X + 100, tnames[i].Location.Y);
                    turls[i].Width = 120;
                    settingsform.Controls.Add(turls[i]);

                    combomode[i].Location = new Point(turls[i].Location.X + 120, turls[i].Location.Y);
                    combomode[i].Width = 85;
                    settingsform.Controls.Add(combomode[i]);

                    // поля ввода параметров
                    paramitems[i].Location = new Point(combomode[i].Location.X + 85, combomode[i].Location.Y);
                    paramitems[i].Width = 150;

                    settingsform.Controls.Add(paramitems[i]);
                }
                captions[i].Height = 20;
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

            Label captioncombomode = new Label();
            captioncombomode.Text = "Режим опроса";
            captioncombomode.Width = 85;
            captioncombomode.Location = new Point(captionaddr.Location.X + 120, captionaddr.Location.Y);
            settingsform.Controls.Add(captioncombomode);

            Label captionparams = new Label();
            captionparams.Text = "Считываемые параметры";
            captionparams.Width = 150;
            captionparams.Location = new Point(captioncombomode.Location.X + 85, captioncombomode.Location.Y);
            settingsform.Controls.Add(captionparams);

            twritetofile = new CheckBox(); // чекбокс ведение журнала?
            inform = new Label(); // лейбл периодичность опроса ресивера
            informsnmp = new Label();
			lconnfaillimit = new Label(); // лейбл порога записи лога при перебоях сети
			tconnfaillimit = new NumericUpDown(); // порог для записи лога при неудачном соединении
			tconnfaillimit.Minimum = 1;
			tconnfaillimit.Maximum = 99999;

            triggerCheckBox = new CheckBox(); // действие при аварии
            triggerAction = new TextBox(); // путь к скрипту для запуска
            triggerAction.Click += (s, ev) =>
                {
                    OpenFileDialog openFileDialog1 = new OpenFileDialog();
                    if (openFileDialog1.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                            string fullPath = openFileDialog1.FileName;
                            string fileName = openFileDialog1.SafeFileName;
                            string path = fullPath.Replace(fileName, "");
                            triggerAction.Text = path + fileName;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }
                    }

                };

            tperiod = new NumericUpDown(); // периодичность
            tperiod.Minimum = 1;
            tperiod.Maximum = 99999;

            tsnmperiod = new NumericUpDown();
            tsnmperiod.Minimum = 1;
            tsnmperiod.Maximum = 99999;

            settingsform.Text = "Program Settings";
            
            settingsform.Size = new System.Drawing.Size(750, 60 * ap.n + 139 + 78);
            
            okbutton = new Button();
            okbutton.Text = "OK";
            okbutton.Click += new EventHandler(okbutton_Click);
            okbutton.Location = new Point(turls[ap.n-1].Location.X - 40, turls[ap.n-1].Location.Y + 160);
            settingsform.Controls.Add(okbutton);

            cancelbutton = new Button();
            cancelbutton.Text = "Отмена";
            cancelbutton.Click += (s, e) =>
                {
                    // нажата кнопка отмены
                    settingsform.Close(); // форма закрывается
                    formobj.isSettingsForm = false; // флаг помечается
                    // старые настройки возвращаются
                    //formobj.oldap.MakeCopy(formobj.ap);
                    formobj.oldap = (settings)formobj.ap.Clone();
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
            inform.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 100);

            informsnmp.Size = new Size(300, 20);
            informsnmp.Text = "Периодичность опроса ресивера в режиме SNMP, сек.";
            informsnmp.Location = new Point(inform.Location.X + inform.Width + 10, inform.Location.Y);
			
			lconnfaillimit.Size = new Size(350,20);
			lconnfaillimit.Text = "При проблемах с соединением писать в лог после X попыток";

            triggerCheckBox.Size = new Size(150, 20);
            triggerCheckBox.Text = "При аварии запускать...";
            triggerAction.Size = new Size(400, 20);

			lconnfaillimit.Location = new Point(twritetofile.Location.X, twritetofile.Location.Y + 50);
            triggerCheckBox.Location = new Point(lconnfaillimit.Location.X, lconnfaillimit.Location.Y + 30);
            triggerAction.Location = new Point(lconnfaillimit.Location.X + 150, triggerCheckBox.Location.Y);
			tconnfaillimit.Size = new Size(40,20);
			tconnfaillimit.Location = new Point(lconnfaillimit.Location.X + 350, lconnfaillimit.Location.Y);
            settingsform.Controls.Add(inform);
            settingsform.Controls.Add(informsnmp);
			settingsform.Controls.Add(lconnfaillimit);
            settingsform.Controls.Add(triggerCheckBox);
            settingsform.Controls.Add(triggerAction);
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
            tnestedpath.Width = 160;
            tnestedpath.Location = new Point(logmaindir.Location.X + 160, logmaindir.Location.Y);
            settingsform.Controls.Add(tnestedpath);

            setlogpatterns = new Button();
            setlogpatterns.Text = "Форматы логов";
            setlogpatterns.Width = 140;
            setlogpatterns.Location = new Point(tnestedpath.Location.X + 180, tnestedpath.Location.Y);
            setlogpatterns.Click += new EventHandler(setlogpatterns_Click);
            settingsform.Controls.Add(setlogpatterns);

            debuglog = new CheckBox();
            debuglog.Text = "Отладочный лог";
            debuglog.Width = 115;
            debuglog.CheckedChanged += new EventHandler(debuglog_Click);
            debuglog.Location = new Point(setlogpatterns.Location.X + 150, setlogpatterns.Location.Y);
            settingsform.Controls.Add(debuglog);

            captiondebuglogdir = new Label();
            captiondebuglogdir.Text = "Путь:";
            captiondebuglogdir.Width = 40;
            captiondebuglogdir.Location = new Point(debuglog.Location.X + 115, debuglog.Location.Y + 5);
            captiondebuglogdir.Hide();
            settingsform.Controls.Add(captiondebuglogdir);

            debuglogdir = new TextBox();
            debuglogdir.Text = ap.debuglogpath;
            debuglogdir.Width = 100;
            debuglogdir.Location = new Point(captiondebuglogdir.Location.X + 40, debuglog.Location.Y);
            debuglogdir.Hide();
            settingsform.Controls.Add(debuglogdir);

            tperiod.Size = new Size(200, 20);
            tperiod.Location = new Point(inform.Location.X, inform.Location.Y + 20);
            settingsform.Controls.Add(tperiod);

            tsnmperiod.Size = new Size(200, 20);
            tsnmperiod.Location = new Point(informsnmp.Location.X, informsnmp.Location.Y + 20);
            settingsform.Controls.Add(tsnmperiod);

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
                // ap.logconnectlimit can't be 0
                if (ap.logconnectlimit == 0)
                    ap.logconnectlimit = 1;
                if (ap.periodsnmp == 0)
                    ap.periodsnmp = 1;
                if (!formobj.appstarted)
                {
                    setstruct oldap = ap; // сделаем копию объекта, чтобы сохранить состояния последнего считывания
                    RestoreOldLastStatusFields(oldap, ap);
                }
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
                // если режим Web - показать весь адрес, если snmp - только ip
                if (ap.receiverecs[i].workmode == "Web")
                    turls[i].Text = ap.receiverecs[i].url;
                else
                    turls[i].Text = ap.receiverecs[i].snmpipaddress;
                // params and regexps we should set in buttons handlers
                // turlset[i].Text = ap.receiverecs[i].urlinput;
                combomode[i].Text = ap.receiverecs[i].workmode;
                isActiveChkboxes[i].Checked = ap.receiverecs[i].isActive;
            }
            logmaindir.Text = ap.mainlogpath;
            tnestedpath.Checked = ap.nestedpath;
            twritetofile.Checked = ap.writetofile;
            tperiod.Value = ap.period;
            tsnmperiod.Value = ap.periodsnmp;
            
            tconnfaillimit.Value = ap.logconnectlimit;
            triggerAction.Text = ap.triggerCmd;
            triggerCheckBox.Checked = ap.isTrigger;

            debuglog.Checked = ap.isdebuglog;
            debuglogdir.Text = ap.debuglogpath;
        }

        // update the struct after changing the settings via controls
        private void ApplySettingsToStruct()
        {
            for (int i = 0; i < ap.n; i++)
            {
                ap.receiverecs[i].name = tnames[i].Text;
                if (ap.receiverecs[i].workmode == "Web")
                    ap.receiverecs[i].url = turls[i].Text;
                else
                    ap.receiverecs[i].snmpipaddress = turls[i].Text;
                //ap.receiverecs[i].urlinput = turlset[i].Text;
                ap.receiverecs[i].workmode = combomode[i].Text;
                ap.receiverecs[i].isActive = isActiveChkboxes[i].Checked;
            }
            ap.writetofile = twritetofile.Checked;
            ap.period = Convert.ToUInt32(tperiod.Value);
            ap.periodsnmp = Convert.ToUInt32(tsnmperiod.Value);
            ap.logconnectlimit = Convert.ToInt32(tconnfaillimit.Value);
            ap.triggerCmd = triggerAction.Text;
            ap.isTrigger = triggerCheckBox.Checked;
            ap.mainlogpath = logmaindir.Text;
            ap.nestedpath = tnestedpath.Checked;
            ap.formwidth = Convert.ToUInt32(width.Value);
            ap.previousLocationX = formobj.Location.X;
            ap.previousLocationY = formobj.Location.Y;
            ap.formheight = Convert.ToUInt32(height.Value);
            ap.debuglogpath = debuglogdir.Text;
            ap.isdebuglog = debuglog.Checked;
        }

        void debuglog_Click(object sender, EventArgs e)
        {
            if (debuglog.Checked)
            {
                debuglogdir.Show();
                captiondebuglogdir.Show();
            }
            else
            {
                captiondebuglogdir.Hide();
                debuglogdir.Hide();
            }
        }

        void okbutton_Click(object sender, EventArgs e)
        {
            int tempn = (int)receivescnt.Value;
            // проверка полей на заполненность
            for (int i = 0; i < tempn; i++)
            {
                if (isActiveChkboxes[i].Checked && turls[i].Visible && turls[i].Text == "")
                {
                    MessageBox.Show("Внимание! Одно из полей IP-адреса не задано.");
                    return;
                }
                if (isActiveChkboxes[i].Checked &&  tnames[i].Visible && tnames[i].Text == "")
                {
                    MessageBox.Show("Внимание! Одно из полей имени устройства не задано.");
                    return;
                }
            }

            // применяем настройки параметров и шаблонов к структуре
            for (int i = 0; i < ap.n; i++)
            {
                // проверка количества строк параметров и шаблонов на совпадение
                int n1 = formobj.oldap.ap.receiverecs[i].parameters.Count(s => s != null);
                int n2 = formobj.oldap.ap.receiverecs[i].regexps.Count(s => s != null);
                int n3 = formobj.oldap.ap.receiverecs[i].matchvalues.Count(s => s != null);
                int n4 = formobj.oldap.ap.receiverecs[i].snmpaddrs.Count(s => s != null);
                int n5 = formobj.oldap.ap.receiverecs[i].snmpid.Count(s => s != null);

                bool getmode = (ap.receiverecs[i].workmode == "Web" ? true : false); // true = web, false = snmp

                //n1 = 5
                //n2 = 5
                //n3 = 20
                //n4 = 31
                //n5 = 1

                //Console.WriteLine("i = " + i);
                //Console.WriteLine("getmode = " + getmode.ToString());
                //Console.WriteLine("n1 = " + n1);
                //Console.WriteLine("n2 = " + n2);
                //Console.WriteLine("n3 = " + n3);
                //Console.WriteLine("n4 = " + n4);
                //Console.WriteLine("n5 = " + n5);
                 

                if (!getmode)
                {
                    // snmp режим
                    if (formobj.oldap.ap.receiverecs[i].snmpinputsaddrs == "")
                    {
                        MessageBox.Show("Внимание! Значение snmp-адреса для получения активного выхода не задано.");
                        return;
                    }
                    if (n1 == n4 && n3 == n4 && n4 == n5)
                    {
                        // число строк в настройках совпадает - можно применять эти настройки
                        formobj.ap = formobj.oldap;
                    }
                    else
                    {
                        MessageBox.Show("Внимание! Количество адресов snmp для " + ap.receiverecs[i].name + " не совпадает с количеством ожидаемых значений либо с количеством порядковых snmp-номеров. Они должны соответстовать друг другу.");
                        return;
                    }
                }
                else
                {
                    // web режим
                    if (n1 == n2 && n2 == n3 && n1 == n3)
                    {
                        formobj.ap = formobj.oldap;
                    }
                }

            }

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
            formobj.timer1.Interval = 1000 * (int)ap.period;
            formobj.timer2.Interval = 1000 * (int)ap.periodsnmp;
            // сам таймер запустится дальше внутри функции DrawMainForm
            formobj.RedrawForm();
        }
    }
}
