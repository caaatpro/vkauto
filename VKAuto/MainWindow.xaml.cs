using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using VkNet;
using VkNet.Enums.Filters;
using VkNet.Exception;
using xNet;
using HttpRequest = xNet.HttpRequest;

namespace VKAuto
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private List<string> _accounts = new List<string>();
        private readonly Settings _scope = Settings.All;
        private string _parameters = "";
        private int _appid = Properties.Settings.Default.APPID;
        private int _interval = Properties.Settings.Default.interval*1000;
        private bool _captchaBox = Properties.Settings.Default.captchaBox;
        private string _groupId; // и группы
        private string _mess = ""; // соббщение
        private Thread _t; // поток
        private string _captchaSid = ""; // ид капчи
        private string _captchaText = ""; // текст капчи
        private int _accauntI = -1;
        private VkApi vk = new VkApi();
        private static ProxyClient proxy;

        public MainWindow()
        {
            InitializeComponent();

            Uri proxyURI = new Uri("http://218.49.74.233:8080");
            WebRequest.DefaultWebProxy = new WebProxy(proxyURI);
            proxy = new HttpProxyClient("218.49.74.233", 8080);

            idapp.Text = Properties.Settings.Default.APPID.ToString();
            interval.Text = Properties.Settings.Default.interval.ToString();
            captchaKey.Text = Properties.Settings.Default.captchaKey;
            captchaBox.IsChecked = Properties.Settings.Default.captchaBox;

            string res = "";
            var req = new HttpRequest();

            try
            {
                res = req.Get($"http://94.103.80.182/vk.txt").ToString();

            }
            catch (Exception)
            {
                Application.Current.Shutdown();
            }

            if (res != "1")
            {
                Application.Current.Shutdown();
            }
        }

        // Авторизация
        private void Autorizetion()
        {
            vk.AccessToken = null;
            
 
            if (_appid <= 0)
            {
                Logg(null, "Ошибка: APP ID не указан!");
                EnabledDisabled("", true);
                return;
            }

            if (_accounts.Count == 0)
            {
                Logg(null, "Чтение файла accounts.txt");
                // Чтение файла списка аккаунтов
                var file = new StreamReader(@"accounts.txt", Encoding.GetEncoding("windows-1251"));
                string line;

                // Перебор аккаунтов
                while ((line = file.ReadLine()) != null)
                {
                    _accounts.Add(line);
                }
                file.Close();
            }
            if (_accounts.Count == 0)
            {
                Logg(null, "Аккаунты не найдены");
                return;
            }

            // Перебор аккаунтов
            while (_accauntI < _accounts.Count)
            {
                // Ожидание капчи
                if (_captchaSid != "" && _captchaText == "") continue;

                _accauntI++;

                // Разделение аккаунта и пароля
                var ac = _accounts[_accauntI].Split(new[] { ':' }, 2);

                
                Logg(null, "Авторизация: " + ac[0]);
                try
                {
                    if (_captchaSid != "" && _captchaText != "")
                    {
                        vk.Authorize(_appid, ac[0], ac[1], _scope, (long) Convert.ToInt64(_captchaSid), _captchaText);
                        _captchaSid = "";
                        _captchaText = "";
                    }
                    else
                    {
                        vk.Authorize(_appid, ac[0], ac[1], _scope);
                    }

                }
                catch (VkApiAuthorizationException)
                {
                    Logg(null, "Неверный логин или пароль");
                    using (var f = File.AppendText("bad_accounts.txt"))
                    {
                        f.WriteLine(ac[0]+":"+ac[1]);
                    }
                    continue;
                }
                catch (CaptchaNeededException e)
                {
                    Logg(null, "Распознавание капчи");
                    _captchaSid = e.Sid.ToString();

                    // Использовать сервис распознования
                    if (_captchaBox)
                    {
                        _captchaText = AntiGate.Recognize(e.Img.ToString());
                    }
                    else
                    {
                        messageСaptchaImage.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            var img = CaptchaImage(e.Img.ToString());
                            messageСaptchaImage.Source = img;
                            searchСaptchaImage.Source = img;
                            friendsСaptchaImage.Source = img;
                            invitationСaptchaImage.Source = img;
                        }));
                    }
                    _accauntI--;
                    continue;
                }
                catch (VkApiException e)
                {
                    Logg(null, "Неизвестная ошибка авторизации 1" + e);
                    continue;
                }

                using (var f = File.AppendText("good_accounts.txt"))
                {
                    f.WriteLine(ac[0] + ":" + ac[1]);
                }

                return;
            }

            Logg(null, "Аккаунты закончились");
            EnabledDisabled("", true);
            vk.AccessToken = null;
            _accauntI = -1;
            _t.Abort();
        }
       

        // Поиск пользователей метод
        private void GetSearch()
        {
            var openMessage = 0;
            var closeMessage = 0;
            _accauntI = -1;
            while (true)
            {
                Autorizetion();
                if (vk.AccessToken == null) continue;

                const int count = 1000;
                var offset = 0;

                while (true)
                {
                    Logg(null, "Получение списка пользователей");

                    var r = VkApi("users.search",
                        _parameters + "&count=" + count + "&offset=" + offset + "&fields=can_write_private_message",
                        vk.AccessToken);
                    
                    if (r == "") { Logg(null, "Неизвестная ошибка 1"); continue;}

                    dynamic rArray = JsonConvert.DeserializeObject(r);

                    if (rArray.response != null)
                    {

                            Logg(null, "Распределение пользователей");

                            // Распределяем пользователей по файлам

                                int rrCount = rArray.response.Count;
                                EditElem(search_total, rArray.response[0].ToString());

                                for (var j = 1; j < rrCount; j++)
                                {
                                    if (rArray.response[j].can_write_private_message == "1")
                                    {
                                        openMessage++;
                                        using (var t = File.AppendText("open_message.txt"))
                                        {
                                            t.WriteLine(rArray.response[j].first_name + "|" +
                                                        rArray.response[j].last_name +
                                                        "|" + rArray.response[j].uid);
                                        }
                                    }
                                    else
                                    {
                                        closeMessage++;
                                        using (var t = File.AppendText("close_message.txt"))
                                        {
                                            t.WriteLine(rArray.response[j].first_name + "|" +
                                                        rArray.response[j].last_name +
                                                        "|" + rArray.response[j].uid);
                                        }
                                    }

                                    // Разрешено писать сообщения
                                    EditElem(search_open_message, openMessage.ToString());

                                    // Запрещено писать сообщения
                                    EditElem(search_close_message, closeMessage.ToString());
                                }
                        }
                        if (rArray.error != null)
                        {
                            Logg(null, "Error: " + rArray.error.error_code + " " + rArray.error.error_msg);
                        }

                    break;

                }

                break;
            }


            Logg(null, "Готово!");
            EnabledDisabled("", true);
        }
        
        // Рассылка сообщений метод
        private void Send()
        {
            string line;
            var users = new List<string>();
            var users2 = new List<string>();
            var usersIgnore = new List<string>();
            var userI = 0;
            var userS = 0;
            var userF = 0;


            Logg(null, "Чтение файла open_message_ignore.txt");

            // Чтение файла списка аккаунтов
            var file3 = new StreamReader(@"open_message_ignore.txt", Encoding.GetEncoding("windows-1251"));

            // Перебор аккаунтов
            while ((line = file3.ReadLine()) != null)
            {
                usersIgnore.Add(line);
            }
            file3.Close();

            Logg(null, "Чтение файла open_message.txt");

            // Чтение файла списка аккаунтов
            var file1 = new StreamReader(@"open_message.txt", Encoding.GetEncoding("windows-1251"));

            // Чтение списка пользователей
            while ((line = file1.ReadLine()) != null)
            {
                var ac = line.Split(new[] { '|' }, 3);
                if (usersIgnore.IndexOf(ac[2]) == -1)
                {
                    users.Add(line);
                }
            }
            file1.Close();


            Logg(null, "Чтение файла close_message_ignore.txt");

            // Чтение файла списка аккаунтов
            var file2 = new StreamReader(@"close_message_ignore.txt", Encoding.GetEncoding("windows-1251"));

            // Перебор аккаунтов
            while ((line = file2.ReadLine()) != null)
            {
                usersIgnore.Add(line);
            }
            file2.Close();

            Logg(null, "Чтение файла close_message.txt");

            // Чтение файла списка аккаунтов
            var file4 = new StreamReader(@"close_message.txt", Encoding.GetEncoding("windows-1251"));

            // Чтение списка пользователей
            while ((line = file4.ReadLine()) != null)
            {
                var ac = line.Split(new[] { '|' }, 3);
                if (usersIgnore.IndexOf(ac[2]) == -1)
                {
                    users2.Add(line);
                }
            }
            file4.Close();

            _accauntI = -1;
            // Перебор аккаунтов
            while (true)
            {
                Autorizetion();
                if (vk.AccessToken == null) continue;

                if (userI >= users.Count)
                {
                    break;
                }

                // Перебор первых 20 пользователей
                // i - счётчик отправленных сообщений с одного пользователя
                // user_i - всего пользователей
                var i = 0;
                while (userI < users.Count)
                {
                    if (i > 20) break;

                    // Ожидание капчи
                    if (_captchaSid != "" && _captchaText == "") continue;

                    var user = users[userI].Split('|');
                    var m = _mess;
                    m = m.Replace("%fname%", user[0]);
                    m = m.Replace("%lname%", user[1]);
                    m = HttpUtility.UrlEncode(m);

                    Logg(null, "Отправляем сообщение: " + user[2]);

                    // Дополнительные параметры запроса
                    var p = "";

                    if (_captchaText != "")
                    {
                        p += "&captcha_sid=" + _captchaSid + "&captcha_key=" + _captchaText;

                        // Обнуляем значения
                        _captchaSid = "";
                        _captchaText = "";
                        messageСaptchaText.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            messageСaptchaText.Text = "";
                        }));

                        Logg(null, "Повторный запрос с капчой");
                    }

                    var r = VkApi("messages.send", "user_id=" + user[2] + "&message=" + m + p, vk.AccessToken);
                    
                    if (r == "") { Logg(null, "Неизвестная ошибка 2"); continue;}
                    dynamic rArray = JsonConvert.DeserializeObject(r);


                    if (rArray.error != null)
                    {
                        Logg(null, "Error: " + rArray.error.error_code + " " + rArray.error.error_msg);

                        // Капча
                        if (rArray.error.error_code == 14)
                        {
                            _captchaSid = rArray.error.captcha_sid.ToString();
                            
                            // Использовать сервис распознования
                            if (_captchaBox)
                            {
                                Logg(null, "Распознавание капчи");

                                _captchaText = AntiGate.Recognize(rArray.error.captcha_img.ToString());
                            }
                            else
                            {
                                messageСaptchaImage.Dispatcher.BeginInvoke(new Action(delegate
                                {
                                    messageСaptchaImage.Source = CaptchaImage(rArray.error.captcha_img.ToString());
                                }));
                            }

                            continue;
                        }
                        userF++;
                        EditElem(messageFail, userF.ToString());
                        break;
                    }

                    using (var t = File.AppendText("open_message_ignore.txt"))
                    {
                        t.WriteLine(user[2]);
                    }

                    userS++;
                    EditElem(messageSuccess, userS.ToString());
                    i++;
                    userI++;

                    Thread.Sleep(_interval);
                }

                // Перебор первых 50 пользователей
                // i - счётчик отправленных сообщений с одного пользователя
                // user_i - всего пользователей
                i = 0;
                userI = 0;
                while (userI < users2.Count)
                {
                    if (i > 50) break;

                    // Ожидание капчи
                    if (_captchaSid != "" && _captchaText == "") continue;

                    var user = users2[userI].Split('|');
                    var m = _mess;
                    m = m.Replace("%fname%", user[0]);
                    m = m.Replace("%lname%", user[1]);
                    m = HttpUtility.UrlEncode(m);

                    Logg(null, "Добавляем в друзья: " + user[2]);

                    // Дополнительные параметры запроса
                    var p = "";

                    if (_captchaText != "")
                    {
                        p += "&captcha_sid=" + _captchaSid + "&captcha_key=" + _captchaText;

                        // Обнуляем значения
                        _captchaSid = "";
                        _captchaText = "";
                        messageСaptchaText.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            messageСaptchaText.Text = "";
                        }));

                        Logg(null, "Повторный запрос с капчой");
                    }

                    var r = VkApi("friends.add", "user_id=" + user[2] + "&text=" + m + p, vk.AccessToken);
                    
                    if (r == "") { Logg(null, "Неизвестная ошибка 3"); continue;}
                    dynamic rArray = JsonConvert.DeserializeObject(r);

                    if (rArray.error != null)
                    {
                        Logg(null, "Error: " + rArray.error.error_code + " " + rArray.error.error_msg);

                        // привышен лимит
                        if (rArray.error.error_code == 103)
                        {
                            break;
                        }

                        // Капча
                        if (rArray.error.error_code == 14)
                        {
                            _captchaSid = rArray.error.captcha_sid.ToString();

                            // Использовать сервис распознования
                            if (_captchaBox)
                            {
                                Logg(null, "Распознавание капчи");

                                _captchaText = AntiGate.Recognize(rArray.error.captcha_img.ToString());
                            }
                            else
                            {
                                messageСaptchaImage.Dispatcher.BeginInvoke(new Action(delegate
                                {
                                    messageСaptchaImage.Source = CaptchaImage(rArray.error.captcha_img.ToString());
                                }));
                            }

                            continue;
                        }
                        userF++;
                        EditElem(messageFail, userF.ToString());
                        break;
                    }

                    using (var t = File.AppendText("close_message_ignore.txt"))
                    {
                        t.WriteLine(user[2]);
                    }

                    userS++;
                    EditElem(messageSuccess, userS.ToString());
                    i++;
                    userI++;

                    Thread.Sleep(_interval);
                }
            }

            Logg(null, "Рассылка завершена");
            EnabledDisabled("", true);
        }

        // Рассылка сообщений метод
        private void Friends()
        {
            string line;
            var users = new List<string>();
            var usersIgnore = new List<string>();
            var userS = 0;
            var userF = 0;

            Logg(null, "Чтение файла close_message_ignore.txt");

            // Чтение файла списка аккаунтов
            var file = new StreamReader(@"close_message_ignore.txt", Encoding.GetEncoding("windows-1251"));

            // Перебор аккаунтов
            while ((line = file.ReadLine()) != null)
            {
                usersIgnore.Add(line);
            }
            file.Close();
            
                    _accauntI = -1;
            // Перебор аккаунтов
            while (true)
            {
                Autorizetion();
                if (vk.AccessToken == null) continue;

                var count = 1000;
                var offset = 0;

                Logg(null, "Получение списка пользователей");

                var r = VkApi("users.search",
                        _parameters + "&count=" + count + "&offset=" + offset + "&fields=can_write_private_message",
                        vk.AccessToken);

                if (r == "") { Logg(null, "Неизвестная ошибка 4"); continue; }
                dynamic rArray = JsonConvert.DeserializeObject(r);

                if (rArray.response != null)
                {
                    int rrCount = rArray.response.Count;
                    EditElem(friendsUsers, rArray.response[0].ToString());

                    for (var j = 1; j < rrCount; j++)
                    {
                        if (usersIgnore.IndexOf(rArray.response[j].uid.ToString()) == -1)
                        {
                            users.Add(rArray.response[j].first_name.ToString() + "|" +
                                          rArray.response[j].last_name.ToString() +
                                          "|" + rArray.response[j].uid.ToString());
                        }
                    }
                }
                if (rArray.error != null)
                {
                    Logg(null, "Error: " + rArray.error.error_code + " " + rArray.error.error_msg);
                }

                break;
            }
            
            if (users.Count > 0)
            {
                Logg(null, "Добавление в друзья");

                _accauntI = -1;

                // Перебор аккаунтов
                while (true)
                {
                    Autorizetion();
                    if (vk.AccessToken == null) continue;

                    // Перебор первых 50 пользователей
                    // i - счётчик отправленных сообщений с одного пользователя
                    // user_i - всего пользователей
                    var i = 0;
                    var userI = 0;

                    while (userI < users.Count)
                    {
                        if (i > 50) break;

                        // Ожидание капчи
                        if (_captchaSid != "" && _captchaText == "") continue;

                        var user = users[userI].Split('|');
                        var m = _mess;
                        m = m.Replace("%fname%", user[0]);
                        m = m.Replace("%lname%", user[1]);
                        m = HttpUtility.UrlEncode(m);

                        Logg(null, "Добавляем в друзья: " + user[2]);

                        // Дополнительные параметры запроса
                        var p = "";

                        if (_captchaText != "")
                        {
                            p += "&captcha_sid=" + _captchaSid + "&captcha_key=" + _captchaText;

                            // Обнуляем значения
                            _captchaSid = "";
                            _captchaText = "";
                            friendsСaptchaText.Dispatcher.BeginInvoke(new Action(delegate
                            {
                                friendsСaptchaText.Text = "";
                            }));

                            Logg(null, "Повторный запрос с капчой");
                        }

                        var r = VkApi("friends.add", "user_id=" + user[2] + "&text=" + m + p, vk.AccessToken);
                        
                        if (r == "") { Logg(null, "Неизвестная ошибка 5"); continue;}
                        dynamic rArray = JsonConvert.DeserializeObject(r);

                        if (rArray.error != null)
                        {
                            Logg(null, "Error: " + rArray.error.error_code + " " + rArray.error.error_msg);


                            // привышен лимит
                            if (rArray.error.error_code == 103)
                            {
                                break;
                            }

                            // Капча
                            if (rArray.error.error_code == 14)
                            {
                                _captchaSid = rArray.error.captcha_sid.ToString();

                                // Использовать сервис распознования
                                if (_captchaBox)
                                {
                                    Logg(null, "Распознавание капчи");

                                    _captchaText = AntiGate.Recognize(rArray.error.captcha_img.ToString());
                                }
                                else
                                {
                                    friendsСaptchaImage.Dispatcher.BeginInvoke(new Action(delegate
                                    {
                                        friendsСaptchaImage.Source = CaptchaImage(rArray.error.captcha_img.ToString());
                                    }));
                                }

                                continue;
                            }
                            userF++;
                            EditElem(friendsFail, userF.ToString());
                            break;
                        }

                        using (var t = File.AppendText("close_message_ignore.txt"))
                        {
                            t.WriteLine(user[2]);
                        }

                        userS++;
                        EditElem(friendsSuccess, userS.ToString());
                        i++;
                        userI++;

                        Thread.Sleep(_interval);
                    }
                }
            }

            Logg(null, "Рассылка завершена");
            EnabledDisabled("", true);
        }

        // Рассылка приглашений метод
        private void Invite()
        {
            string line;
            var usersIgnore = new List<string>();
            var userS = 0;
            var userF = 0;


            Logg(null, "Чтение файла invitation_ignore.txt");

            // Чтение файла списка аккаунтов
            var file3 = new StreamReader(@"invitation_ignore.txt", Encoding.GetEncoding("windows-1251"));

            // Перебор аккаунтов
            while ((line = file3.ReadLine()) != null)
            {
                usersIgnore.Add(line);
            }
            file3.Close();

            _accauntI = -1;
            // Перебор аккаунтов
            while (true)
            {
                Autorizetion();
                if (vk.AccessToken == null) continue;

                Logg(null, "Получение друзей");

                var r = VkApi("friends.get", "", vk.AccessToken);

                if (r == "")
                {
                    Logg(null, "Неизвестная ошибка 6");
                    continue;
                }
                dynamic rArray = JsonConvert.DeserializeObject(r);

                if (rArray.error != null)
                {
                    Logg(null, "Error: " + rArray.error.error_code + " " + rArray.error.error_msg);
                    break;
                }


                // Перебор первых 40 пользователей
                // i - счётчик отправленных сообщений с одного пользователя
                // user_i - всего пользователей
                var i = 0;
                var userI = 0;
                EditElem(invitationUsers, rArray.response.Count.ToString());
                while (userI < rArray.response.Count)
                {
                    if (i > 40) break; 

                    var userId = rArray.response[userI].ToString();

                    if (usersIgnore.IndexOf(userId) != -1)
                    {
                        userI++;
                        continue;
                    }

                    // Ожидание капчи
                    if (_captchaSid != "" && _captchaText == "") continue;

                    Logg(null, "Приглашаем: " + userId);

                    // Дополнительные параметры запроса
                    var p = "";

                    if (_captchaText != "")
                    {
                        p += "&captcha_sid=" + _captchaSid + "&captcha_key=" + _captchaText;

                        // Обнуляем значения
                        _captchaSid = "";
                        _captchaText = "";
                        invitationСaptchaText.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            invitationСaptchaText.Text = "";
                        }));

                        Logg(null, "Повторный запрос с капчой");
                    }

                    var rr = VkApi("groups.invite", "user_id=" + userId + "&group_id=" + _groupId + p, vk.AccessToken);
                    
                    if (r == "") { Logg(null, "Неизвестная ошибка 7"); continue;}
                    dynamic rrArray = JsonConvert.DeserializeObject(rr);

                    if (rrArray.error != null)
                    {
                        Logg(null, "Error: " + rrArray.error.error_code + " " + rrArray.error.error_msg);

                        // привышен лимит
                        if (rrArray.error.error_code == 103)
                        {
                            break;
                        }

                        // Капча
                        if (rrArray.error.error_code == 14)
                        {
                            _captchaSid = rrArray.error.captcha_sid.ToString();

                            // Использовать сервис распознования
                            if (_captchaBox)
                            {
                                Logg(null, "Распознавание капчи");

                                _captchaText = AntiGate.Recognize(rrArray.error.captcha_img.ToString());
                            }
                            else
                            {
                                invitationСaptchaImage.Dispatcher.BeginInvoke(new Action(delegate
                                {
                                    invitationСaptchaImage.Source = CaptchaImage(rrArray.error.captcha_img.ToString());
                                }));
                            }

                            continue;
                        }

                        userF++;
                        EditElem(invitationFail, userF.ToString());
                        i++;
                        userI++;
                        continue;
                    }

                    using (var t = File.AppendText("invitation_ignore.txt"))
                    {
                        t.WriteLine(userId);
                    }

                    userS++;
                    EditElem(invitationSuccess, userS.ToString());
                    i++;
                    userI++;

                    Thread.Sleep(_interval);
                }
            }

            Logg(null, "Приглашение завершено");
            EnabledDisabled("", true);
        }


        // Рассылка сообщений
        private void message_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            EnabledDisabled("message", false);

            _mess = messageText.Text;

            try
            {
                _t = new Thread(Send);
                _t.Start();
            }
            catch (Exception exception)
            {
                Logg(null, exception.ToString());
            }
        }

        // Сбор пользователей
        private void searchButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            EnabledDisabled("search", false);

            var url = searchUrl.Text;

            if (url == "")
            {
                Logg(null, "Ошибка: ссылка не указана!");
                EnabledDisabled("", true);
                return;
            }

            Logg(null, "Разбор поискового запроса");

            url = url.Replace("%5B", "[");
            url = url.Replace("%5D", "]");
            url = url.Replace("https://vk.com/search?", "");
            url = url.Replace("c[", "");
            url = url.Replace("]", "");

            var strData = url.Split('&');

            // Параметры поиска
            _parameters = string.Join("&", strData, 0, strData.Length);

            try
            {
                _t = new Thread(GetSearch);
                _t.Start();
            }
            catch (Exception exception)
            {
                Logg(null, exception.ToString());
            }
        }

        // Рассылка приглашений
        private void invitation_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            EnabledDisabled("invitation", false);

            _groupId = invitationID.Text;

            if (_groupId == "")
            {
                Logg(null, "Ошибка: не указан ID группы/встречи");
                EnabledDisabled("", true);
                return;
            }

            try
            {
                _t = new Thread(Invite);
                _t.Start();
            }
            catch (Exception exception)
            {
                Logg(null, exception.ToString());
            }
        }

        // Приглашение в друзья
        private void friends_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            EnabledDisabled("friends", false);

            _mess = friendsText.Text;
            var url = friendsUrl.Text;

            if (url == "")
            {
                Logg(null, "Ошибка: ссылка не указана!");
                EnabledDisabled("", true);
                return;
            }

            Logg(null, "Разбор поискового запроса");

            url = url.Replace("%5B", "[");
            url = url.Replace("%5D", "]");
            url = url.Replace("https://vk.com/search?", "");
            url = url.Replace("c[", "");
            url = url.Replace("]", "");

            var strData = url.Split('&');

            // Параметры поиска
            _parameters = string.Join("&", strData, 0, strData.Length);


            try
            {
                _t = new Thread(Friends);
                _t.Start();
            }
            catch (Exception exception)
            {
                Logg(null, exception.ToString());
            }
        }

        // Кнопка остановки задачи
        private void stopButton_Click(object sender, RoutedEventArgs e)
        {
            _captchaSid = "";
            _captchaText = "";
            _accauntI = -1;
            EnabledDisabled("", true);

            Logg(null, "Остановлено пользователем");

            // останавливаем выполнение
            _t.Abort();
        }

        // Отправка капчи
        private void captchaText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key.Equals(Key.Return))
            {
                e.Handled = true;
                
                _captchaText = ((TextBox)sender).Text;
            }
        }


        // Блокировка кнопок
        private void EnabledDisabled(string b, bool t)
        {
            // Разблокировать
            if (t)
            {
                searchStart.Dispatcher.BeginInvoke(new Action(delegate {
                    searchStart.IsEnabled = true;
                }));
                searchStop.Dispatcher.BeginInvoke(new Action(delegate {
                    searchStop.IsEnabled = false;
                }));

                messageStart.Dispatcher.BeginInvoke(new Action(delegate {
                    messageStart.IsEnabled = true;
                }));
                messageStop.Dispatcher.BeginInvoke(new Action(delegate {
                    messageStop.IsEnabled = false;
                }));

                friendsStart.Dispatcher.BeginInvoke(new Action(delegate {
                    friendsStart.IsEnabled = true;
                }));
                friendsStop.Dispatcher.BeginInvoke(new Action(delegate {
                    friendsStop.IsEnabled = false;
                }));

                invitationStart.Dispatcher.BeginInvoke(new Action(delegate {
                    invitationStart.IsEnabled = true;
                }));
                invitationStop.Dispatcher.BeginInvoke(new Action(delegate {
                    invitationStop.IsEnabled = false;
                }));
            }
            // Заблокировать
            else
            {
                searchStart.Dispatcher.BeginInvoke(new Action(delegate {
                    searchStart.IsEnabled = false;
                }));
                searchStop.Dispatcher.BeginInvoke(new Action(delegate {
                    searchStop.IsEnabled = false;
                }));

                messageStart.Dispatcher.BeginInvoke(new Action(delegate {
                    messageStart.IsEnabled = false;
                }));
                messageStop.Dispatcher.BeginInvoke(new Action(delegate {
                    messageStop.IsEnabled = false;
                }));

                friendsStart.Dispatcher.BeginInvoke(new Action(delegate {
                    friendsStart.IsEnabled = false;
                }));
                friendsStop.Dispatcher.BeginInvoke(new Action(delegate {
                    friendsStop.IsEnabled = false;
                }));

                invitationStart.Dispatcher.BeginInvoke(new Action(delegate {
                    invitationStart.IsEnabled = false;
                }));
                invitationStop.Dispatcher.BeginInvoke(new Action(delegate {
                    invitationStop.IsEnabled = false;
                }));

                if (b == "search")
                {
                    searchStop.Dispatcher.BeginInvoke(new Action(delegate {
                        searchStop.IsEnabled = true;
                    }));
                }
                else if (b == "message")
                {
                    messageStop.Dispatcher.BeginInvoke(new Action(delegate {
                        messageStop.IsEnabled = true;
                    }));
                }
                else if (b == "friends")
                {
                    friendsStop.Dispatcher.BeginInvoke(new Action(delegate {
                        friendsStop.IsEnabled = true;
                    }));
                }
                else if (b == "invitation")
                {
                    invitationStop.Dispatcher.BeginInvoke(new Action(delegate {
                        invitationStop.IsEnabled = true;
                    }));
                }
            }
        }


        // VK API методы
        public static string VkApi(string methodName, string parameters, string token)
        {
            var res = "";

            var req = new HttpRequest();
            req.Proxy = proxy;
            try
            {
                res = req.Get($"https://api.vk.com/method/{methodName}?{parameters}&access_token={token}").ToString();

            }
            catch (Exception)
            {
                // ignored
            }

            return res;
        }

        // Лог
        private void Logg(ContentControl l, string text)
        {
            if (l != null)
            {
                l.Dispatcher.BeginInvoke(new Action(delegate {
                    l.Content = text;
                }));
            }

            listBoxLog.Dispatcher.BeginInvoke(new Action(delegate {
                listBoxLog.Items.Add(DateTime.Now.ToString("t", CultureInfo.CreateSpecificCulture("es-ES")) + " " + text);
            }));

            using (var f = File.AppendText("Logg.txt"))
            {
                f.WriteLine(DateTime.Now.ToString("t", CultureInfo.CreateSpecificCulture("es-ES")) + " " + text);
            }
        }

        // Редактирование элемента
        private void EditElem(ContentControl e, string text)
        {
            e.Dispatcher.BeginInvoke(new Action(delegate {
                e.Content = text;
            }));
        }

        // Метод сохранения настроек
        private void SaveSettings()
        {
            _appid = Convert.ToInt32(idapp.Text);
            _interval = Convert.ToInt32(interval.Text);
            AntiGate.AntiGateKey = captchaKey.Text;
            if (captchaBox.IsChecked != null) _captchaBox = (bool)captchaBox.IsChecked;
            Properties.Settings.Default.APPID = Convert.ToInt32(idapp.Text);
            Properties.Settings.Default.interval = Convert.ToInt32(interval.Text);
            Properties.Settings.Default.captchaKey = captchaKey.Text;
            if (captchaBox.IsChecked != null) Properties.Settings.Default.captchaBox = (bool)captchaBox.IsChecked;
            Properties.Settings.Default.Save();
            _interval = _interval * 1000;
        }

        // Картинка капчи
        private static BitmapImage CaptchaImage(string url)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(url, UriKind.Absolute);
            bitmap.EndInit();

            return bitmap;
        }

    }
}