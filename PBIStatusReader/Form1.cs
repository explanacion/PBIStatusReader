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

namespace PBIStatusReader
{
    public partial class Form1 : Form
    {
        int n = 10; // число ресиверов
        settings ap;
        public System.Windows.Forms.Timer timer1;
        Label[] recvnamelabels;
        PictureBox[,] pictureBoxes = new PictureBox[10,5];
        //static HttpWebRequest webRequest;
        static object locker = new object();
        Label[,] dynamicparamls;

        // здесь будем хранить последние считанные параметры для каждого! устройства в виде статусов
        int[,] lastcheckedparams = new int[10, 5];
        
        public Form1()
        {
            ap = new settings(this); // инициализация настроек приложения
            InitializeComponent();
            bool ifread = ap.ReadSettings(); // считываем записанные ранее настройки

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
            timer1 = new System.Windows.Forms.Timer();

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
                    setColorOfPictureBox(pictureBoxes[i, j], 0);

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

        void timer1_Tick(object sender, EventArgs e)
        {
            timer1.Stop();
            Thread[] sets = new Thread[10];

            for (int i = 0; i < ap.ap.n; i++)
            {
                // проверяем, готова ли настройка этого ресивера
                if (i < recvnamelabels.Count(s => s != null))
                {
                    if (recvnamelabels[i].Visible && recvnamelabels[i].Text != "")
                    {
                        sets[i] = new Thread(setValues);
                        sets[i].Start(i);
                        //sets[i].Join(7000);
                    }
                }
            }

            timer1.Start();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            System.Windows.Forms.Control.CheckForIllegalCrossThreadCalls = false;

            // последние сохраненные параметры
            for (int i = 0; i < ap.ap.n; i++)
            {
                for (int j = 0; j < ap.ap.receiverecs[i].m; j++)
                {
                    lastcheckedparams[i, j] = new int();
                    lastcheckedparams[i, j] = 4;
                }
            }

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
                string tofile = nw.ToString("dd-MM-yyyy") + "\t" + nw.ToString("HH:mm:ss") + "\t" + ap.ap.receiverecs[i].name + "\t" + ap.ap.receiverecs[i].url + "\t" + message;
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
                string tofile = nw.ToString("dd-MM-yyyy") + "\t" + nw.ToString("HH:mm:ss") + "\t" + ap.ap.receiverecs[i].name + "\t" + ap.ap.receiverecs[i].url + "\t" + message;

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
            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(ap.ap.receiverecs[i].url + "/cgi-bin/decoder_config.cgi");
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
                                    dynamicparamls[i, j].Font = new Font(dynamicparamls[i, j].Font, FontStyle.Bold);
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
                //webRequest = (HttpWebRequest)WebRequest.Create(geturlbyid(idn) + "/cgi-bin/input_status.cgi?cur_time=" + secondsSinceEpoch.ToString());
                HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(geturlbyid(idn));
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
                    if (HttpStatusCode.OK == resp.StatusCode)
                    {
                        //Console.Write("Connection... OK");
                        Changelab(getlabelbyid(idn), ap.ap.receiverecs[idn].name);
                        Stream ReceiveStream = resp.GetResponseStream();
                        Encoding encode = System.Text.Encoding.GetEncoding("utf-8");
                        StreamReader readStream = new StreamReader(ReceiveStream, encode);
                        String str = "";
                        str = readStream.ReadToEnd();
                        //Console.WriteLine(String.Format("Response: {0}", str));
                        Task.Factory.StartNew(() => getSelectedInput(idn));
                        for (int j = 0; j < ap.ap.receiverecs[idn].m; j++)
                        {
                            string pattern = ap.ap.receiverecs[idn].regexps[j].ToString();
                            Regex newReg = new Regex(pattern);
                            Match matches = newReg.Match(str);
                            int currentstate = 0;

                            if (matches.Groups[1].Value == "1")  // if (matches.Groups[1].Success)
                            {
                                //Console.WriteLine(matches.Groups[1].Value);
                                setColorOfPictureBox(pictureBoxes[idn,j],1);
                                currentstate = 1;
                            }
                            else
                            {
                                setColorOfPictureBox(pictureBoxes[idn, j], 0);
                                currentstate = 0;
                            }


                            if (ap.ap.writetofile == true)
                            {
                                // если состояние хоть одного параметра изменилось - пишем
                                if (lastcheckedparams[idn,j] != currentstate) {
                                    if (lastcheckedparams[idn,j] != 4)
                                    {
                                        WriteToLog(idn, ap.ap.receiverecs[idn].parameters[j] + "\t" + intToStatus(currentstate));
                                    }
                                }
                                
                            }
                            lastcheckedparams[idn, j] = currentstate;
                        }
                    }
                }
                catch (Exception e)
                {
                    if (!ap.ap.writetofile)
                        return;
                    //MessageBox.Show(e.ToString());
                    Label tmp = getlabelbyid(idn);
                    //Changelab(tmp, "Not connected");
                    //MessageBox.Show(e.Message);
             
                    // прошлое значение не было связано с проблемами подключения
                    if (lastcheckedparams[idn, 0] != 2)
                    {
                        for (int j = 0; j < ap.ap.receiverecs[idn].m; j++)
                        {
                            lastcheckedparams[idn, j] = 2;
                            setColorOfPictureBox(pictureBoxes[idn, j], 2);
                        }
                        WriteToConnectionLog(idn, "CONNECT" + "\t" + e.Message);
                    }
                }
        }

        private void настройкиToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ap.CreateSettingsForm();
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

//    1) добавить параметры в настройку:

//задать считываемые параметры для каждого тюнера (эдитбокс через запятую), 
//вынести регулярное выражение в настройку, но сделать его по умолчанию неактивным, чекбокс настроить
//пользователь - пароль
//тип соединения - http, snmp

//2) журнал-лог сделать в формате
//Дата<TAB>Время<TAB>IP<TAB>Tuner<TAB>Off 
//Дата<TAB>Время<TAB>IP<TAB>ASI-1<TAB>On 
//Дата<TAB>Время<TAB>IP<TAB>ASI-2<TAB>Off 
//Дата<TAB>Время<TAB>IP<TAB>CI<TAB>Off 
//Дата<TAB>Время<TAB>IP<TAB>IP<TAB>Off 

//3) 
    public class ReceiverRecord
    {
        public string name;
        public string url;
        public string login;
        public string pass;
        public List<string> parameters;
        public List<string> regexps;
        public int m;

        public ReceiverRecord()
        {
            m = 5;
            parameters = new List<string>(new string[m]);
            regexps = new List<string>(new string[m]);
        }

        public ReceiverRecord(int inm)
        {
            m = inm;
            parameters = new List<string>(new string[m]);
            regexps = new List<string>(new string[m]);
        }

        public void Resize(int newm)
        {
            m = newm;
            int countr = parameters.Count;
            if (newm < countr)
            {
                parameters.RemoveRange(newm, countr - newm);
                regexps.RemoveRange(newm, countr - newm);
            }
            else if (newm > countr)
            {
                if (newm > parameters.Capacity)
                {
                    parameters.AddRange(new List<string>(new string[newm - countr]));
                    regexps.AddRange(new List<string>(new string[newm - countr]));
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

        }
        public setstruct ap;
        public XmlSerializer x;
        Form1 formobj;

        // form specific objects and variables
        Form settingsform;
        NumericUpDown receivescnt;
        TextBox[] tnames;
        TextBox[] turls;
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
                ap.receiverecs[i].m = textboxparams.Lines.Length;
                using (System.IO.StringReader reader = new System.IO.StringReader(textboxparams.Text))
                {
                    for (int j = 0; j < textboxparams.Lines.Length; j++)
                    {
                        if (type)
                            ap.receiverecs[i].parameters[j] = reader.ReadLine();
                        else
                            ap.receiverecs[i].regexps[j] = reader.ReadLine();
                    }
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
            for (int j = 0; j < ap.receiverecs[i].m; j++)
            {
                if (ap.receiverecs[i].parameters[j] != "")
                {
                    
                    if (j > 0)
                    {
                        textboxparams.AppendText("\r\n");
                    }
                    if (type)
                        textboxparams.AppendText(ap.receiverecs[i].parameters[j]);
                    else
                        textboxparams.AppendText(ap.receiverecs[i].regexps[j]);
                }
            }
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
                    Array.Resize(ref paramitems, newvalue);
                    Array.Resize(ref regexps, newvalue);
                    Array.Resize(ref logins, newvalue);
                    Array.Resize(ref passwords, newvalue);
                    Array.Resize(ref captions, newvalue);

                    tnames[i] = new TextBox();
                    turls[i] = new TextBox();
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

                    captions[i - 1].Hide();
                    settingsform.Controls.Remove(captions[i - 1]);
                    captions[i - 1].Dispose();
                }


                Array.Resize(ref tnames, newvalue);
                Array.Resize(ref turls, newvalue);
                Array.Resize(ref paramitems, newvalue);
                Array.Resize(ref regexps, newvalue);
                Array.Resize(ref logins, newvalue);
                Array.Resize(ref passwords, newvalue);
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

            receivescnt.Value = ap.n;
            receivescnt.Width = 40;
            receivescnt.Minimum = 1;
            receivescnt.Maximum = 10;
            receivescnt.Location = new Point(130, 5);

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
                    settingsform.Controls.Add(passwords[i]);
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

            twritetofile = new CheckBox(); // чекбокс ведение журнала?
            inform = new Label(); // лейбл периодичность опроса ресивера
            tperiod = new NumericUpDown(); // периодичность
            tperiod.Maximum = 99999;
            settingsform.Text = "Program Settings";
            
            settingsform.Size = new System.Drawing.Size(850, 600);
            
            okbutton = new Button();
            okbutton.Text = "OK";
            okbutton.Click += new EventHandler(okbutton_Click);
            okbutton.Location = new Point(turls[ap.n-1].Location.X - 40, turls[ap.n-1].Location.Y + 90);
            settingsform.Controls.Add(okbutton);

            cancelbutton = new Button();
            cancelbutton.Text = "Отмена";
            cancelbutton.Click += (s, e) =>
                {
                    settingsform.Close();
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
            ap.n = tempn;

            ApplySettingsToStruct();
            WriteSettings();
            settingsform.Close();

            // timer 
            formobj.timer1.Interval = 1000*(int)ap.period;
            formobj.timer1.Start();

            formobj.RedrawForm();
        }
    }
}
