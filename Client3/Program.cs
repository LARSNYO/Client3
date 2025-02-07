using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
/// <summary>
/// Тут реализован клиент, с возможностью выбора роли, у каждой роли свои функции
/// Ошибки, которые могут возникать: я пока нашёл одну довольно неприятную ошибку: ни клиент, ни вещь не видят подключения друг друга, поэтому если после авторизации отключится вещь, а клиент после сделает запрос, то произойдет сбой. Я не знаю как реализовать правильно эту обработку исключения, ведь это UDP соединение, связи как бы нет, можно сделать какой-то постоянный запрос сервер->вещь, проверять есть ли ответ, если ответа нет, значит вещь отключена, но это выглядит как-то тупо, не буду ничего делать, пока что.
/// p.s. а вообще тут по хорошему бы порядок навести, мне не очень нравится структура, нужно всё разложить по полочкам.
/// </summary>
class UdpClientOrThing
{
    static async Task Main()
    {
        string serverIp = "127.0.0.1";
        int serverPort = 12000;
        IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);

        using (UdpClient udpClient = new UdpClient())
        {
            Console.WriteLine("Добро пожаловать");

            int maxAttempt = 5; // теперь 5 попыток авторизации
            int attempt = 0;
            bool authorized = false;
            string clientRole = "";

            // Цикл авторизации (до 5 попыток)
            while (attempt < maxAttempt && !authorized)
            {
                Console.Write("Введите роль (THING/CLIENT): ");
                clientRole = Console.ReadLine().Trim().ToUpper();

                // Отправляем сообщение авторизации
                string authMessage = $"AUTH iot {clientRole}";
                byte[] authBytes = Encoding.UTF8.GetBytes(authMessage);
                await udpClient.SendAsync(authBytes, authBytes.Length, serverEndpoint);
                Console.WriteLine("Отправлено сообщение авторизации. Ожидание ответа...");

                var authResponseTask = udpClient.ReceiveAsync();
                if (await Task.WhenAny(authResponseTask, Task.Delay(3000)) == authResponseTask)
                {
                    var result = authResponseTask.Result;
                    string response = Encoding.UTF8.GetString(result.Buffer);
                    Console.WriteLine($"AUTH response: {response}");
                    if (response.StartsWith("AUTH OK"))
                    {
                        authorized = true;
                        break;
                    }
                    else
                    {
                        attempt++;
                        Console.WriteLine($"Авторизация не удалась. Осталось попыток: {maxAttempt - attempt}");
                    }
                }
                else
                {
                    attempt++;
                    Console.WriteLine($"Таймаут ожидания авторизации. Осталось попыток: {maxAttempt - attempt}");
                }
            }

            if (!authorized)
            {
                Console.WriteLine("Превышено число попыток авторизации. Завершение программы.");
                return;
            }

            // Вызов функции в зависимости от выбранной роли
            if (clientRole == "CLIENT")
            {
                await RunClientAsync(udpClient, serverEndpoint);
            }
            else if (clientRole == "THING")
            {
                await RunThingAsync(udpClient, serverEndpoint);
            }
        }
    }

    // Реализация для CLIENT: ввод команд и приём сообщений
    static async Task RunClientAsync(UdpClient udpClient, IPEndPoint serverEndpoint)
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        Task receiveTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult result = await udpClient.ReceiveAsync();
                    string message = Encoding.UTF8.GetString(result.Buffer);
                    // Используем метод Print для вывода сообщений без нарушения строки ввода
                    Print($"Получено: {message}");
                }
                catch (Exception ex)
                {
                    if (!cts.Token.IsCancellationRequested)
                        Print("Ошибка получения: " + ex.Message);
                }
            }
        });

        while (true)
        {
            Console.Write("Введите команду (например, GET TEMP, START STREAM, STOP STREAM или exit для выхода): ");
            string command = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(command))
                continue;
            if (command.Trim().ToLower() == "exit")
            {
                Console.WriteLine("Выход из программы.");
                break;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(command);
            await udpClient.SendAsync(bytes, bytes.Length, serverEndpoint);
        }
        cts.Cancel();
        await receiveTask;
        udpClient.Close();
    }

    // Реализация для THING: обработка входящих команд и отправка ответов/обновлений
    static async Task RunThingAsync(UdpClient udpClient, IPEndPoint serverEndpoint)
    {
        bool streamingEnabled = false;
        DateTime lastStreamTime = DateTime.Now;
        Random rnd = new Random();
        CancellationTokenSource cts = new CancellationTokenSource();

        // Фоновая задача для приёма команд от CLIENT
        Task receiveTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult result = await udpClient.ReceiveAsync();
                    string message = Encoding.UTF8.GetString(result.Buffer);
                    Console.WriteLine($"Получено: {message}");
                    if (message.Equals("GET TEMP", StringComparison.OrdinalIgnoreCase))
                    {
                        string response = $"TEMP:{rnd.Next(20, 30)}C";
                        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                        await udpClient.SendAsync(responseBytes, responseBytes.Length, serverEndpoint);
                        Console.WriteLine($"Отправлено: {response}");
                    }
                    else if (message.Equals("START STREAM", StringComparison.OrdinalIgnoreCase))
                    {
                        streamingEnabled = true;
                        string ack = "STREAMING ENABLED";
                        byte[] ackBytes = Encoding.UTF8.GetBytes(ack);
                        await udpClient.SendAsync(ackBytes, ackBytes.Length, serverEndpoint);
                        Console.WriteLine("Режим потоковой передачи включен.");
                    }
                    else if (message.Equals("STOP STREAM", StringComparison.OrdinalIgnoreCase))
                    {
                        streamingEnabled = false;
                        string ack = "STREAMING DISABLED";
                        byte[] ackBytes = Encoding.UTF8.GetBytes(ack);
                        await udpClient.SendAsync(ackBytes, ackBytes.Length, serverEndpoint);
                        Console.WriteLine("Режим потоковой передачи отключен.");
                    }
                }
                catch (Exception ex)
                {
                    if (!cts.Token.IsCancellationRequested)
                        Console.WriteLine("Ошибка получения: " + ex.Message);
                }
            }
        });

        // Цикл для отправки периодических обновлений в режиме потоковой передачи
        while (true)
        {
            if (streamingEnabled && DateTime.Now - lastStreamTime >= TimeSpan.FromSeconds(5))
            {
                string update = $"UPDATE TEMP:{rnd.Next(20, 30)}C";
                byte[] updateBytes = Encoding.UTF8.GetBytes(update);
                await udpClient.SendAsync(updateBytes, updateBytes.Length, serverEndpoint);
                Print($"Отправлено: {update}");
                lastStreamTime = DateTime.Now;
            }
            await Task.Delay(100);
        }
    }
    static void Print(string message) //очень важная штука, без нее ничего не работает
    {
        if (OperatingSystem.IsWindows())    // если ОС Windows
        {
            var position = Console.GetCursorPosition(); // получаем текущую позицию курсора
            int left = position.Left;   // смещение в символах относительно левого края
            int top = position.Top;     // смещение в строках относительно верха
                                        // копируем ранее введенные символы в строке на следующую строку
            Console.MoveBufferArea(0, top, left, 1, 0, top + 1);
            // устанавливаем курсор в начало текущей строки
            Console.SetCursorPosition(0, top);
            // в текущей строке выводит полученное сообщение
            Console.WriteLine(message);
            // переносим курсор на следующую строку
            // и пользователь продолжает ввод уже на следующей строке
            Console.SetCursorPosition(left, top + 1);
        }
        else Console.WriteLine(message);
    }
}
