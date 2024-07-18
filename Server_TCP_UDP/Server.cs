using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

class Server
{
    private static readonly Dictionary<string, string> Users = new Dictionary<string, string>
    {
        { "user1", "password1" },
        { "user2", "password2" }
    };

    private static readonly ConcurrentDictionary<string, int> UserRequestCounts = new ConcurrentDictionary<string, int>();
    private static readonly ConcurrentDictionary<string, DateTime> UserBlockTimes = new ConcurrentDictionary<string, DateTime>();

    private static readonly int MaxRequestsPerUser = 5;
    private static readonly ConcurrentBag<TcpClient> Clients = new ConcurrentBag<TcpClient>();

    static async Task Main(string[] args)
    {
        int port = 11000;
        IPAddress ipAddress = IPAddress.Parse("192.168.0.103"); // Ваша IP-адреса
        TcpListener server = new TcpListener(ipAddress, port);

        server.Start();
        Console.WriteLine("Server started...");

        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();
            Clients.Add(client);
            _ = HandleClientAsync(client);
        }
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        string user = null;

        try
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            string[] loginDetails = Encoding.UTF8.GetString(buffer, 0, bytesRead).Split(':');

            if (loginDetails.Length != 2 || !Users.TryGetValue(loginDetails[0], out string password) || password != loginDetails[1])
            {
                await SendMessageAsync(stream, "Invalid username or password.");
                client.Close();
                return;
            }

            user = loginDetails[0];
            Console.WriteLine($"User {user} connected.");
            await SendMessageAsync(stream, "Login successful.");  // Надсилаємо підтвердження успішної авторизації

            while (true)
            {
                if (UserBlockTimes.TryGetValue(user, out DateTime blockTime) && blockTime > DateTime.Now)
                {
                    await SendMessageAsync(stream, $"Too many requests. Try again after {blockTime.ToShortTimeString()}.");
                    client.Close();
                    return;
                }

                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                if (!ProcessRequest(user, message, out string response))
                {
                    await SendMessageAsync(stream, response);
                    continue;
                }

                await SendMessageAsync(stream, response);
                Console.WriteLine($"User {user} requested {message}: {response}");

                UserRequestCounts.AddOrUpdate(user, 1, (key, count) => count + 1);
                if (UserRequestCounts[user] >= MaxRequestsPerUser)
                {
                    UserBlockTimes[user] = DateTime.Now.AddMinutes(1);
                    UserRequestCounts[user] = 0;
                    await SendMessageAsync(stream, "Too many requests. You are blocked for 1 minute.");
                    client.Close();
                    return;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Exception: {e.Message}");
        }
        finally
        {
            if (user != null) Clients.TryTake(out _);
            Console.WriteLine($"User {user} disconnected.");
        }
    }

    private static bool ProcessRequest(string user, string message, out string response)
    {
        string[] currencies = message.Split(' ');
        if (currencies.Length != 2)
        {
            response = "Invalid format. Use: [Currency1] [Currency2]";
            return false;
        }

        string currency1 = currencies[0].ToUpper();
        string currency2 = currencies[1].ToUpper();

        // Прикладова логіка для отримання курсу валют
        Dictionary<string, double> exchangeRates = new Dictionary<string, double>
        {
            { "USD", 1.0 },
            { "EUR", 0.85 },
            { "UAH", 27.2 }
        };

        if (!exchangeRates.ContainsKey(currency1) || !exchangeRates.ContainsKey(currency2))
        {
            response = "Unknown currency.";
            return false;
        }

        double rate = exchangeRates[currency2] / exchangeRates[currency1];
        response = $"Exchange rate {currency1}/{currency2}: {rate}";
        return true;
    }

    private static async Task SendMessageAsync(NetworkStream stream, string message)
    {
        byte[] msg = Encoding.UTF8.GetBytes(message);
        await stream.WriteAsync(msg, 0, msg.Length);
    }
}